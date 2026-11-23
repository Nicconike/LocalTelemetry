using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LocalTelemetry.Core.Diagnostics;

namespace LocalTelemetry.Core.Hardware;

/// <summary>
/// Queries system-wide GPU utilisation via WDDM GPU Engine performance counters.
/// Reports the same metric as Task Manager: max engine utilisation across all engine types.
/// Uses PDH wildcard queries for efficient single-call data collection with zero per-poll
/// managed allocations.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WddmGpuMonitor : IDisposable
{
    private IntPtr _query;
    private IntPtr _counter;
    private IntPtr _sharedMemCounter;
    private IntPtr _committedMemCounter;
    private IntPtr _buffer;
    private int _bufferCapacity;
    private bool _primed;
    private bool _disposed;

    /// <summary>Reused per poll to avoid dictionary allocation churn; keyed by engine hash.</summary>
    private readonly Dictionary<int, float> _engineSums = new(16);

    /// <summary>Whether the WDDM GPU Engine counters were initialised successfully.</summary>
    public bool IsAvailable { get; private set; }

    // Init
    /// <summary>
    /// Initialises a new <see cref="WddmGpuMonitor"/> and opens a PDH query
    /// against the <c>GPU Engine</c> performance counter category.
    /// </summary>
    public WddmGpuMonitor()
    {
        try { Init(); }
        catch (Exception ex) { Log.Error(ex, "Failed to initialize WDDM GPU monitor"); }
    }

    internal delegate int PdhOpenQueryDelegate(IntPtr dataSource, IntPtr userData, out IntPtr query);
    internal delegate int PdhAddEnglishCounterDelegate(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);
    internal delegate int PdhCollectQueryDataDelegate(IntPtr query);
    internal delegate int PdhGetFormattedCounterArrayDelegate(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);
    internal delegate int PdhCloseQueryDelegate(IntPtr query);

    internal static PdhOpenQueryDelegate PdhOpenQueryFunc { get; set; } = PdhOpenQuery;
    internal static PdhAddEnglishCounterDelegate PdhAddEnglishCounterFunc { get; set; } = PdhAddEnglishCounter;
    internal static PdhCollectQueryDataDelegate PdhCollectQueryDataFunc { get; set; } = PdhCollectQueryData;
    internal static PdhGetFormattedCounterArrayDelegate PdhGetFormattedCounterArrayFunc { get; set; } = PdhGetFormattedCounterArray;
    internal static PdhCloseQueryDelegate PdhCloseQueryFunc { get; set; } = PdhCloseQuery;

    internal static void ResetHooks()
    {
        PdhOpenQueryFunc = PdhOpenQuery;
        PdhAddEnglishCounterFunc = PdhAddEnglishCounter;
        PdhCollectQueryDataFunc = PdhCollectQueryData;
        PdhGetFormattedCounterArrayFunc = PdhGetFormattedCounterArray;
        PdhCloseQueryFunc = PdhCloseQuery;
    }

    private void Init()
    {
        int status = PdhOpenQueryFunc(IntPtr.Zero, IntPtr.Zero, out _query);
        if (status != 0)
        {
            Log.Error($"Failed to open performance data query (status 0x{status:X8})");
            return;
        }

        // Wildcard counter auto-tracks all current and future instances.
        // Handles process churn without manual re-enumeration - PDH resolves
        // the wildcard on each PdhCollectQueryData call.
        status = PdhAddEnglishCounterFunc(
            _query,
            @"\GPU Engine(*)\Utilization Percentage",
            IntPtr.Zero,
            out _counter);

        if (status != 0)
        {
            Log.Error($"Failed to add GPU engine counter (status 0x{status:X8})");
            PdhCloseQueryFunc(_query);
            _query = IntPtr.Zero;
            return;
        }

        int memStatus = PdhAddEnglishCounterFunc(
            _query,
            @"\GPU Adapter Memory(*)\Shared Usage",
            IntPtr.Zero,
            out _sharedMemCounter);
        if (memStatus != 0)
        {
            Log.Debug($"Failed to add GPU adapter shared memory counter (status 0x{memStatus:X8})");
            _sharedMemCounter = IntPtr.Zero;
        }

        int committedStatus = PdhAddEnglishCounterFunc(
            _query,
            @"\GPU Adapter Memory(*)\Total Committed",
            IntPtr.Zero,
            out _committedMemCounter);
        if (committedStatus != 0)
        {
            Log.Debug($"Failed to add GPU adapter committed memory counter (status 0x{committedStatus:X8})");
            _committedMemCounter = IntPtr.Zero;
        }

        // Collect initial baseline - rate counters need two samples for a delta.
        status = PdhCollectQueryDataFunc(_query);
        if (status != 0)
        {
            Log.Error($"Initial collection of performance data failed (status 0x{status:X8})");
            PdhCloseQueryFunc(_query);
            _query = IntPtr.Zero;
            return;
        }

        IsAvailable = true;
        Log.Debug("GPU engine counter initialized successfully");
    }

    private int _consecutiveErrors;
    private DateTime _lastResetTime = DateTime.MinValue;
    private static readonly TimeSpan ResetCooldown = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Closes the current PDH query and reinitializes GPU Engine counters.
    /// Safe to call following display driver restarts or resolution changes.
    /// </summary>
    public void Reset()
    {
        if (_disposed) return;
        IsAvailable = false;
        _primed = false;
        _consecutiveErrors = 0;

        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            _counter = IntPtr.Zero;
            _sharedMemCounter = IntPtr.Zero;
            _committedMemCounter = IntPtr.Zero;
        }

        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
            _bufferCapacity = 0;
        }

        try
        {
            Init();
            if (IsAvailable)
                Log.Debug("WDDM GPU monitor successfully re-initialized after reset");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to reset WDDM GPU monitor");
        }
    }

    // Query
    /// <summary>
    /// Returns system-wide GPU utilisation as a percentage (0-100).
    /// Mirrors Task Manager: sums per-process utilisation by engine, returns the busiest.
    /// </summary>
    public float GetUsagePct()
    {
        if (!IsAvailable || _disposed) return 0f;

        try
        {
            int status = PdhCollectQueryDataFunc(_query);
            if (status != 0)
            {
                HandleQueryError();
                return 0f;
            }
            _consecutiveErrors = 0;

            // Second collection required for a valid rate delta
            if (!_primed)
            {
                _primed = true;
                return 0f;
            }

            if (!FetchCounterArray(_counter, out uint itemCount))
            {
                return 0f;
            }

            return CalculateMaxEngineUsage(itemCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to query WDDM GPU utilization");
            return 0f;
        }
    }

    /// <summary>
    /// Returns current GPU memory usage in megabytes for the specified adapter LUID or overall active adapters.
    /// Used for integrated GPUs which dynamically allocate from host system RAM.
    /// </summary>
    public float GetGpuMemoryUsedMb(string? luidFilter = null)
    {
        if (!IsAvailable || _disposed) return 0f;

        IntPtr counter = _committedMemCounter != IntPtr.Zero ? _committedMemCounter : _sharedMemCounter;
        if (counter == IntPtr.Zero) return 0f;

        try
        {
            if (!FetchCounterArray(counter, out uint itemCount))
                return 0f;

            long totalRamBytes = SystemInfo.GetTotalRamBytes();
            if (totalRamBytes <= 0) totalRamBytes = 128L * 1024 * 1024 * 1024;

            double chosenBytes = SelectAdapterMemoryBytes(itemCount, luidFilter, totalRamBytes);
            return (float)(chosenBytes / (1024.0 * 1024.0));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to query WDDM GPU memory usage");
            return 0f;
        }
    }

    private double SelectAdapterMemoryBytes(uint itemCount, string? luidFilter, long totalRamBytes)
    {
        double matchedBytes = 0.0;
        double maxFallbackBytes = 0.0;

        for (uint i = 0; i < itemCount; i++)
        {
            if (!TryReadItemBytes((int)i * ItemStride, totalRamBytes, out double bytes, out string? instanceName))
                continue;

            if (MatchesLuid(instanceName, luidFilter))
            {
                matchedBytes = bytes;
                break;
            }

            if (bytes > maxFallbackBytes)
                maxFallbackBytes = bytes;
        }

        return matchedBytes > 0.0 ? matchedBytes : maxFallbackBytes;
    }

    private bool TryReadItemBytes(int offset, long totalRamBytes, out double bytes, out string? instanceName)
    {
        bytes = 0.0;
        instanceName = null;

        uint cStatus = unchecked((uint)Marshal.ReadInt32(_buffer, offset + CStatusOffset));
        if (cStatus != 0) return false;

        bytes = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(_buffer, offset + ValueOffset));
        if (bytes <= 0.0 || bytes >= totalRamBytes || double.IsNaN(bytes) || double.IsInfinity(bytes))
            return false;

        IntPtr namePtr = Marshal.ReadIntPtr(_buffer, offset + NameOffset);
        instanceName = namePtr != IntPtr.Zero ? Marshal.PtrToStringUni(namePtr) : null;
        return instanceName is null || !instanceName.Contains("1414", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesLuid(string? instanceName, string? luidFilter)
    {
        return !string.IsNullOrEmpty(luidFilter)
            && instanceName is not null
            && instanceName.Contains(luidFilter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Legacy overload for backward compatibility with existing callers and tests.
    /// </summary>
    public float GetSharedMemoryUsedMb() => GetGpuMemoryUsedMb();

    private void HandleQueryError()
    {
        _consecutiveErrors++;
        if (_consecutiveErrors >= 5 && DateTime.UtcNow - _lastResetTime >= ResetCooldown)
        {
            _lastResetTime = DateTime.UtcNow;
            Reset();
        }
    }

    private bool FetchCounterArray(IntPtr counterHandle, out uint itemCount)
    {
        itemCount = 0;
        uint bufSize = 0;
        int status = PdhGetFormattedCounterArrayFunc(
            counterHandle, PdhFmtDouble, ref bufSize, ref itemCount, IntPtr.Zero);

        if (itemCount == 0 || status != PdhMoreData) return false;

        EnsureBuffer((int)bufSize);

        status = PdhGetFormattedCounterArrayFunc(
            counterHandle, PdhFmtDouble, ref bufSize, ref itemCount, _buffer);
        return status == 0;
    }

    private float CalculateMaxEngineUsage(uint itemCount)
    {
        _engineSums.Clear();
        float maxPct = 0f;

        for (uint i = 0; i < itemCount; i++)
        {
            int offset = (int)i * ItemStride;
            uint cStatus = unchecked((uint)Marshal.ReadInt32(_buffer, offset + CStatusOffset));
            if (cStatus != 0) continue;

            double value = BitConverter.Int64BitsToDouble(
                Marshal.ReadInt64(_buffer, offset + ValueOffset));
            if (value <= 0.0) continue;

            IntPtr namePtr = Marshal.ReadIntPtr(_buffer, offset + NameOffset);
            int engineKey = ComputeEngineKey(namePtr);

            _engineSums.TryGetValue(engineKey, out float sum);
            sum += (float)value;
            _engineSums[engineKey] = sum;

            if (sum > maxPct) maxPct = sum;
        }

        return Math.Clamp(maxPct, 0f, 100f);
    }

    // Engine key extraction
    /// <summary>
    /// Computes a hash key identifying the GPU engine from a native instance name.
    /// Strips the per-process <c>pid_{N}_</c> prefix so that different processes on the
    /// same engine map to the same key. Uses FNV-1a for fast, low-collision hashing.
    /// </summary>
    private static int ComputeEngineKey(IntPtr namePtr)
    {
        if (namePtr == IntPtr.Zero) return 0;
        string? name = Marshal.PtrToStringUni(namePtr);
        if (string.IsNullOrEmpty(name)) return 0;

        ReadOnlySpan<char> span = name.AsSpan();
        if (span.StartsWith("pid_", StringComparison.Ordinal))
        {
            span = span[4..];
            int idx = 0;
            while (idx < span.Length && char.IsAsciiDigit(span[idx])) idx++;
            if (idx < span.Length && span[idx] == '_') idx++;
            span = span[idx..];
        }

        unchecked
        {
            int hash = (int)2166136261u;
            foreach (char c in span)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }

    // Buffer management
    private void EnsureBuffer(int requiredSize)
    {
        if (_bufferCapacity >= requiredSize) return;
        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);

        // Over-allocate by 50% to reduce future reallocs from instance churn
        int newSize = requiredSize + requiredSize / 2;
        _buffer = Marshal.AllocHGlobal(newSize);
        _bufferCapacity = newSize;
    }

    // Dispose
    /// <summary>Releases PDH query handle and unmanaged buffer.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsAvailable = false;

        if (_query != IntPtr.Zero) PdhCloseQueryFunc(_query);
        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
        _query = IntPtr.Zero;
        _counter = IntPtr.Zero;
        _sharedMemCounter = IntPtr.Zero;
        _committedMemCounter = IntPtr.Zero;
        _buffer = IntPtr.Zero;
        _bufferCapacity = 0;
    }

    // PDH P/Invoke
    // Follows project convention: GPU monitors keep API-specific P/Invoke inline
    // (consistent with NvGpuMonitor/nvml.dll, AmdGpuMonitor/atiadlxx.dll,
    //  IntelGpuMonitor/ControlLib.dll).
    private const uint PdhFmtDouble = 0x00000200;
    private const int PdhMoreData = unchecked((int)0x800007D2);

    // PDH_FMT_COUNTERVALUE_ITEM_W layout on x64 (project target = x64):
    //   offset  0: IntPtr szName  (8 bytes)
    //   offset  8: uint   CStatus (4 bytes)
    //   offset 12: [pad]          (4 bytes, aligns 8-byte double)
    //   offset 16: double Value   (8 bytes)
    //   stride:                   24 bytes
    private const int NameOffset = 0;
    private const int CStatusOffset = 8;
    private const int ValueOffset = 16;
    private const int ItemStride = 24;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQuery(
        IntPtr dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounter(
        IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhGetFormattedCounterArray(
        IntPtr counter, uint format, ref uint bufferSize,
        ref uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(IntPtr query);

    // DXGI Interop for GPU adapter LUID resolution
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1([In] ref Guid riid, out IntPtr ppFactory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr thisPtr, uint Adapter, out IntPtr ppAdapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr thisPtr, out DxgiAdapterDesc1 pDesc);

    /// <summary>
    /// Resolves the adapter LUID string (e.g. <c>luid_0x00000000_0x000144e4</c>) matching the active GPU via DXGI.
    /// Used to target performance counter instances precisely.
    /// </summary>
    public static string? GetAdapterLuid(string? gpuName = null)
    {
        try
        {
            Guid factoryGuid = new("7b7166ec-21c7-44ae-b21a-c9ae321ae369");
            if (CreateDXGIFactory1(ref factoryGuid, out IntPtr factoryPtr) != 0 || factoryPtr == IntPtr.Zero)
                return null;

            try
            {
                IntPtr factoryVtable = Marshal.ReadIntPtr(factoryPtr);
                IntPtr enumPtr = Marshal.ReadIntPtr(factoryVtable, 12 * IntPtr.Size);
                var enumAdapters1 = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(enumPtr);

                uint i = 0;
                string? firstHardwareLuid = null;

                while (enumAdapters1(factoryPtr, i, out IntPtr adapterPtr) == 0 && adapterPtr != IntPtr.Zero)
                {
                    try
                    {
                        IntPtr vtable = Marshal.ReadIntPtr(adapterPtr);
                        IntPtr getDesc1Ptr = Marshal.ReadIntPtr(vtable, 10 * IntPtr.Size);
                        var getDesc1 = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(getDesc1Ptr);
                        getDesc1(adapterPtr, out DxgiAdapterDesc1 desc);

                        // Skip software renderers (DXGI_ADAPTER_FLAG_SOFTWARE = 2)
                        if ((desc.Flags & 2) == 0)
                        {
                            string luidStr = $"luid_0x{desc.LuidHigh:x8}_0x{desc.LuidLow:x8}";
                            if (!string.IsNullOrEmpty(gpuName) &&
                                (desc.Description.Contains(gpuName, StringComparison.OrdinalIgnoreCase) ||
                                 gpuName.Contains(desc.Description, StringComparison.OrdinalIgnoreCase)))
                            {
                                return luidStr;
                            }

                            firstHardwareLuid ??= luidStr;
                        }
                    }
                    finally
                    {
                        Marshal.Release(adapterPtr);
                    }
                    i++;
                }

                return firstHardwareLuid;
            }
            finally
            {
                Marshal.Release(factoryPtr);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to resolve GPU adapter LUID via DXGI");
            return null;
        }
    }
}
