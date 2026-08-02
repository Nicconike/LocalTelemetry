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
        catch (Exception ex) { Log.Error(ex.Message); }
    }

    private void Init()
    {
        int status = PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out _query);
        if (status != 0)
        {
            Log.Error($"PdhOpenQuery failed (0x{status:X8})");
            return;
        }

        // Wildcard counter auto-tracks all current and future instances.
        // Handles process churn without manual re-enumeration - PDH resolves
        // the wildcard on each PdhCollectQueryData call.
        status = PdhAddEnglishCounter(
            _query,
            @"\GPU Engine(*)\Utilization Percentage",
            IntPtr.Zero,
            out _counter);

        if (status != 0)
        {
            Log.Error($"PdhAddEnglishCounter failed (0x{status:X8})");
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            return;
        }

        // Collect initial baseline - rate counters need two samples for a delta.
        status = PdhCollectQueryData(_query);
        if (status != 0)
        {
            Log.Error($"initial PdhCollectQueryData failed (0x{status:X8})");
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            return;
        }

        IsAvailable = true;
        Log.Info("GPU Engine counter initialised (PDH wildcard)");
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
            int status = PdhCollectQueryData(_query);
            if (status != 0) return 0f;

            // Second collection required for a valid rate delta
            if (!_primed) { _primed = true; return 0f; }

            // 1. Probe for required buffer size
            uint bufSize = 0;
            uint itemCount = 0;
            status = PdhGetFormattedCounterArray(
                _counter, PdhFmtDouble, ref bufSize, ref itemCount, IntPtr.Zero);

            if (itemCount == 0 || status != PdhMoreData) return 0f;

            // 2. Grow reusable unmanaged buffer only when needed
            EnsureBuffer((int)bufSize);

            // 3. Fetch all formatted values in a single PDH call
            status = PdhGetFormattedCounterArray(
                _counter, PdhFmtDouble, ref bufSize, ref itemCount, _buffer);
            if (status != 0) return 0f;

            // 4. Walk items: sum utilisation per unique engine, return max.
            //    Instance format: pid_{PID}_luid_{LUID}_phys_{N}_eng_{N}_engtype_{Type}
            //    Engine identity = everything after "pid_{digits}_".
            //    Same engine shared by multiple processes → sum their usage.
            //    Task Manager shows the busiest engine → take max across engines.
            _engineSums.Clear();
            float maxPct = 0f;

            for (uint i = 0; i < itemCount; i++)
            {
                // Read struct fields directly from the unmanaged buffer.
                // Avoids Marshal.PtrToStructure and its per-call boxing allocation.
                // Layout (x64): IntPtr szName(8) | uint CStatus(4) | pad(4) | double Value(8)
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
        catch (Exception ex)
        {
            Log.Error($"query failed: {ex.Message}");
            return 0f;
        }
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

        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
        _query = IntPtr.Zero;
        _counter = IntPtr.Zero;
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
}
