using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LocalTelemetry.Core.Hardware;

/// <summary>
/// Queries NVIDIA GPU metrics via NVML (NVIDIA Management Library).
/// NVML ships with every NVIDIA display driver (nvml.dll) - user-mode, no kernel component.
/// Returns zero/defaults when no NVIDIA GPU is present or NVML fails.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NvGpuMonitor : IDisposable
{
    /// <summary>Gets whether NVML was initialized successfully.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Gets the name of the detected NVIDIA GPU or empty if unavailable.</summary>
    public string GpuName { get; private set; } = string.Empty;

    /// <summary>Gets the latest installed NVIDIA driver version string.</summary>
    public static string LatestDriverVersion { get; private set; } = string.Empty;

    /// <summary>Gets the most recent snapshot of GPU metrics from <see cref="Query"/>.</summary>
    public static NvGpuData? LatestData { get; private set; }

    private readonly INvGpuNative _native;
    private readonly object _syncLock = new();
    private IntPtr _device = IntPtr.Zero;
    private bool _initialized;
    private DateTime _lastFailureTime = DateTime.MinValue;
    private static readonly TimeSpan ReinitCooldown = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Initializes a new instance of <see cref="NvGpuMonitor"/> and attempts to connect to NVML.
    /// </summary>
    public NvGpuMonitor() : this(null) { }

    internal NvGpuMonitor(INvGpuNative? native)
    {
        _native = native ?? NvGpuNativePInvoke.Instance;
        try { Init(); }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to initialize NVIDIA graphics monitor"); IsAvailable = false; }
    }

    private void Init()
    {
        lock (_syncLock)
        {
            int ret = _native.Init();
            if (ret != 0)
            {
                Diagnostics.Log.Debug($"NVIDIA library initialization failed with status {ret}");
                _lastFailureTime = DateTime.UtcNow;
                return;
            }

            ret = _native.GetDeviceHandleByIndex(0, out _device);
            if (ret != 0 || _device == IntPtr.Zero)
            {
                Diagnostics.Log.Debug($"Failed to get NVIDIA device handle for index 0 (status {ret})");
                _ = _native.Shutdown();
                _device = IntPtr.Zero;
                _lastFailureTime = DateTime.UtcNow;
                return;
            }

            if (_native.GetDeviceName(_device, out var name) == 0)
                GpuName = name ?? string.Empty;

            if (_native.GetDriverVersion(out var ver) == 0 && !string.IsNullOrEmpty(ver))
                LatestDriverVersion = ver;

            _initialized = true;
            IsAvailable = true;
        }
    }

    /// <summary>
    /// Resets the NVML session and reacquires device handles.
    /// Safe to call following display driver updates, TDR resets, or waking from sleep.
    /// </summary>
    public void Reset()
    {
        lock (_syncLock)
        {
            try
            {
                if (_initialized) _ = _native.Shutdown();
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Debug(ex, "Failed to shutdown NVML during reset");
            }
            finally
            {
                _initialized = false;
                _device = IntPtr.Zero;
                IsAvailable = false;
            }

            try
            {
                Init();
                if (IsAvailable)
                    Diagnostics.Log.Debug("NVIDIA GPU monitor successfully re-initialized after reset");
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Error(ex, "Failed to reset NVIDIA GPU monitor");
                IsAvailable = false;
                _lastFailureTime = DateTime.UtcNow;
            }
        }
    }

    private int _consecutiveUnknownFailures;

    private static bool IsDeviceLostError(int ret) =>
        ret is 1    // NVML_ERROR_UNINITIALIZED
           or 15   // NVML_ERROR_GPU_IS_LOST
           or 16;  // NVML_ERROR_RESET_REQUIRED

    private void HandleDeviceLost(int ret)
    {
        lock (_syncLock)
        {
            if (!_initialized) return;
            Diagnostics.Log.Warn($"NVIDIA GPU device lost or driver restarted (NVML status {ret}); pausing telemetry");
            try { _ = _native.Shutdown(); }
            catch (Exception ex)
            {
                Diagnostics.Log.Debug(ex, "Failed to shutdown NVML during device lost handling");
            }
            finally
            {
                _initialized = false;
                _device = IntPtr.Zero;
                IsAvailable = false;
                _lastFailureTime = DateTime.UtcNow;
            }
        }
    }

    /// <summary>Query current GPU metrics.</summary>
    public NvGpuData? Query()
    {
        lock (_syncLock)
        {
            if (!EnsureReady()) return null;

            try
            {
                var data = new NvGpuData();
                int metricsRead = 0;

                if (!ReadTemperature(ref data, ref metricsRead) ||
                    !ReadUtilization(ref data, ref metricsRead) ||
                    !ReadMemory(ref data, ref metricsRead) ||
                    !ReadClocksAndPower(ref data, ref metricsRead))
                {
                    return null;
                }

                if (metricsRead == 0)
                {
                    return HandleNoMetricsRead();
                }

                _consecutiveUnknownFailures = 0;
                LatestData = data;
                return data;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Error(ex, "Failed to query NVIDIA GPU telemetry metrics");
                return null;
            }
        }
    }

    private bool EnsureReady()
    {
        if (_initialized && _device != IntPtr.Zero)
            return true;

        if (DateTime.UtcNow - _lastFailureTime < ReinitCooldown)
            return false;

        Init();
        if (!_initialized || _device == IntPtr.Zero)
            return false;

        Diagnostics.Log.Info("NVIDIA GPU monitor successfully re-initialized");
        return true;
    }

    private bool ReadTemperature(ref NvGpuData data, ref int metricsRead)
    {
        int ret = _native.GetTemperature(_device, 0, out uint temp);
        if (ret == 0)
        {
            data.TempC = temp;
            metricsRead++;
            return true;
        }
        return CheckDeviceError(ret);
    }

    private bool ReadUtilization(ref NvGpuData data, ref int metricsRead)
    {
        int ret = _native.GetUtilizationRates(_device, out uint gpuUtil, out _);
        if (ret == 0)
        {
            data.UsagePct = gpuUtil;
            metricsRead++;
            return true;
        }
        return CheckDeviceError(ret);
    }

    private bool ReadMemory(ref NvGpuData data, ref int metricsRead)
    {
        int ret = _native.GetMemoryInfo(_device, out ulong total, out _, out ulong used);
        if (ret == 0)
        {
            if (total > 0)
            {
                data.VramTotalMb = total / (1024f * 1024f);
                data.VramUsedMb = used / (1024f * 1024f);
                metricsRead++;
            }
            return true;
        }
        return CheckDeviceError(ret);
    }

    private bool ReadClocksAndPower(ref NvGpuData data, ref int metricsRead)
    {
        int ret = _native.GetClockInfo(_device, 0, out uint clock);
        if (ret == 0)
        {
            data.CoreClockMHz = clock;
            metricsRead++;
        }
        else if (!CheckDeviceError(ret))
        {
            return false;
        }

        ret = _native.GetPowerUsage(_device, out uint powerMw);
        if (ret == 0)
        {
            data.PowerDrawW = powerMw / 1000f;
            metricsRead++;
        }
        else if (!CheckDeviceError(ret))
        {
            return false;
        }

        ret = _native.GetPowerManagementLimit(_device, out uint powerLimitMw);
        if (ret == 0)
        {
            data.TdpW = powerLimitMw / 1000f;
            metricsRead++;
        }
        else if (!CheckDeviceError(ret))
        {
            return false;
        }

        return true;
    }

    private bool CheckDeviceError(int ret)
    {
        if (IsDeviceLostError(ret))
        {
            HandleDeviceLost(ret);
            return false;
        }
        return true;
    }

    private NvGpuData? HandleNoMetricsRead()
    {
        _consecutiveUnknownFailures++;
        if (_consecutiveUnknownFailures >= 3)
        {
            _consecutiveUnknownFailures = 0;
            HandleDeviceLost(999);
        }
        return null;
    }

    /// <summary>Releases NVML resources and resets availability.</summary>
    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_initialized) _ = _native.Shutdown();
            _initialized = false;
            _device = IntPtr.Zero;
            IsAvailable = false;
        }
    }

    /// <summary>
    /// Contains a snapshot of all queried GPU metrics from <see cref="Query"/>.
    /// </summary>
    public struct NvGpuData
    {
        public float TempC { get; set; }
        public float UsagePct { get; set; }
        public float VramTotalMb { get; set; }
        public float VramUsedMb { get; set; }
        public float CoreClockMHz { get; set; }
        public float PowerDrawW { get; set; }
        public float TdpW { get; set; }
    }
}

internal interface INvGpuNative
{
    int Init();
    int Shutdown();
    int GetDeviceHandleByIndex(int index, out IntPtr device);
    int GetDeviceName(IntPtr device, out string name);
    int GetDriverVersion(out string version);
    int GetTemperature(IntPtr device, int sensorType, out uint temp);
    int GetUtilizationRates(IntPtr device, out uint gpuUtil, out uint memUtil);
    int GetMemoryInfo(IntPtr device, out ulong total, out ulong free, out ulong used);
    int GetClockInfo(IntPtr device, int clockType, out uint clock);
    int GetPowerUsage(IntPtr device, out uint powerMw);
    int GetPowerManagementLimit(IntPtr device, out uint powerLimitMw);
}

internal sealed class NvGpuNativePInvoke : INvGpuNative
{
    public static readonly NvGpuNativePInvoke Instance = new();

    private const int NVML_SUCCESS = 0;

    internal Func<int> NvmlInitFunc { get; set; } = nvmlInit;
    internal Func<int> NvmlShutdownFunc { get; set; } = nvmlShutdown;
    internal delegate int GetDeviceHandleByIndexDelegate(int index, out IntPtr device);
    internal GetDeviceHandleByIndexDelegate GetDeviceHandleByIndexFunc { get; set; } = nvmlDeviceGetHandleByIndex;
    internal delegate int GetDeviceNameDelegate(IntPtr device, byte[] name, int length);
    internal GetDeviceNameDelegate GetDeviceNameFunc { get; set; } = nvmlDeviceGetName;
    internal delegate int GetDriverVersionDelegate(byte[] version, int length);
    internal GetDriverVersionDelegate GetDriverVersionFunc { get; set; } = nvmlSystemGetDriverVersion;
    internal delegate int GetTemperatureDelegate(IntPtr device, int sensorType, ref uint temp);
    internal GetTemperatureDelegate GetTemperatureFunc { get; set; } = nvmlDeviceGetTemperature;
    internal delegate int GetUtilizationRatesDelegate(IntPtr device, ref NvmlUtilization util);
    internal GetUtilizationRatesDelegate GetUtilizationRatesFunc { get; set; } = nvmlDeviceGetUtilizationRates;
    internal delegate int GetMemoryInfoDelegate(IntPtr device, ref NvmlMemory mem);
    internal GetMemoryInfoDelegate GetMemoryInfoFunc { get; set; } = nvmlDeviceGetMemoryInfo;
    internal delegate int GetClockInfoDelegate(IntPtr device, int clockType, ref uint clock);
    internal GetClockInfoDelegate GetClockInfoFunc { get; set; } = nvmlDeviceGetClockInfo;
    internal delegate int GetPowerUsageDelegate(IntPtr device, ref uint powerMw);
    internal GetPowerUsageDelegate GetPowerUsageFunc { get; set; } = nvmlDeviceGetPowerUsage;
    internal delegate int GetPowerManagementLimitDelegate(IntPtr device, ref uint powerLimitMw);
    internal GetPowerManagementLimitDelegate GetPowerManagementLimitFunc { get; set; } = nvmlDeviceGetPowerManagementLimit;

    internal void ResetHooks()
    {
        NvmlInitFunc = nvmlInit;
        NvmlShutdownFunc = nvmlShutdown;
        GetDeviceHandleByIndexFunc = nvmlDeviceGetHandleByIndex;
        GetDeviceNameFunc = nvmlDeviceGetName;
        GetDriverVersionFunc = nvmlSystemGetDriverVersion;
        GetTemperatureFunc = nvmlDeviceGetTemperature;
        GetUtilizationRatesFunc = nvmlDeviceGetUtilizationRates;
        GetMemoryInfoFunc = nvmlDeviceGetMemoryInfo;
        GetClockInfoFunc = nvmlDeviceGetClockInfo;
        GetPowerUsageFunc = nvmlDeviceGetPowerUsage;
        GetPowerManagementLimitFunc = nvmlDeviceGetPowerManagementLimit;
    }

    public int Init()
    {
        try { return NvmlInitFunc(); }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, "Failed to initialize NVIDIA management library (nvmlInit)");
            return -1;
        }
    }

    public int Shutdown()
    {
        try { return NvmlShutdownFunc(); }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, "Failed to shutdown NVIDIA management library (nvmlShutdown)");
            return -1;
        }
    }

    public int GetDeviceHandleByIndex(int index, out IntPtr device)
    {
        device = IntPtr.Zero;
        try { return GetDeviceHandleByIndexFunc(index, out device); }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, "Failed to get NVIDIA device handle by index");
            return -1;
        }
    }

    public int GetDeviceName(IntPtr device, out string name)
    {
        name = string.Empty;
        if (device == IntPtr.Zero) return -1;
        try
        {
            var nameBuf = new byte[96];
            int ret = GetDeviceNameFunc(device, nameBuf, nameBuf.Length);
            if (ret == NVML_SUCCESS)
            {
                int len = Array.IndexOf(nameBuf, (byte)0);
                if (len < 0) len = nameBuf.Length;
                name = System.Text.Encoding.ASCII.GetString(nameBuf, 0, len);
            }
            return ret;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, "Failed to get NVIDIA device name");
            return -1;
        }
    }

    public int GetDriverVersion(out string version)
    {
        version = string.Empty;
        try
        {
            var verBuf = new byte[80];
            int ret = GetDriverVersionFunc(verBuf, verBuf.Length);
            if (ret == NVML_SUCCESS)
            {
                int len = Array.IndexOf(verBuf, (byte)0);
                if (len > 0) version = System.Text.Encoding.ASCII.GetString(verBuf, 0, len);
            }
            return ret;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, "Failed to get NVIDIA driver version");
            return -1;
        }
    }

    public int GetTemperature(IntPtr device, int sensorType, out uint temp)
    {
        temp = 0;
        if (device == IntPtr.Zero) return -1;
        try { return GetTemperatureFunc(device, sensorType, ref temp); }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, "Failed to get NVIDIA device temperature");
            return -1;
        }
    }

    public int GetUtilizationRates(IntPtr device, out uint gpuUtil, out uint memUtil)
    {
        gpuUtil = 0;
        memUtil = 0;
        if (device == IntPtr.Zero) return -1;
        try
        {
            var util = new NvmlUtilization();
            int ret = GetUtilizationRatesFunc(device, ref util);
            if (ret == NVML_SUCCESS)
            {
                gpuUtil = util.gpu;
                memUtil = util.memory;
            }
            return ret;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, "Failed to get NVIDIA device utilization rates");
            return -1;
        }
    }

    public int GetMemoryInfo(IntPtr device, out ulong total, out ulong free, out ulong used)
    {
        total = 0; free = 0; used = 0;
        if (device == IntPtr.Zero) return -1;
        try
        {
            var mem = new NvmlMemory();
            int ret = GetMemoryInfoFunc(device, ref mem);
            if (ret == NVML_SUCCESS)
            {
                total = mem.total;
                free = mem.free;
                used = mem.used;
            }
            return ret;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, "Failed to get NVIDIA memory information");
            return -1;
        }
    }

    public int GetClockInfo(IntPtr device, int clockType, out uint clock)
    {
        clock = 0;
        if (device == IntPtr.Zero) return -1;
        try { return GetClockInfoFunc(device, clockType, ref clock); }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, "Failed to get NVIDIA clock information");
            return -1;
        }
    }

    public int GetPowerUsage(IntPtr device, out uint powerMw)
    {
        powerMw = 0;
        if (device == IntPtr.Zero) return -1;
        try { return GetPowerUsageFunc(device, ref powerMw); }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, "Failed to get NVIDIA power usage");
            return -1;
        }
    }

    public int GetPowerManagementLimit(IntPtr device, out uint powerLimitMw)
    {
        powerLimitMw = 0;
        if (device == IntPtr.Zero) return -1;
        try { return GetPowerManagementLimitFunc(device, ref powerLimitMw); }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, "Failed to get NVIDIA power management limit");
            return -1;
        }
    }

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlInit();

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlShutdown();

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetHandleByIndex(int index, out IntPtr device);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetName(IntPtr device, [Out] byte[] name, int length);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, ref uint temp);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, ref NvmlUtilization util);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, ref NvmlMemory mem);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetClockInfo(IntPtr device, int clockType, ref uint clock);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetPowerUsage(IntPtr device, ref uint power);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlSystemGetDriverVersion([Out] byte[] version, int length);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetPowerManagementLimit(IntPtr device, ref uint powerLimit);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NvmlUtilization
    {
        public uint gpu;
        public uint memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NvmlMemory
    {
        public ulong total;
        public ulong free;
        public ulong used;
    }
}
