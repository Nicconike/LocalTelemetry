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
    private static void Log(string msg) { Diagnostics.Log.Info(msg); }

    /// <summary>Gets whether NVML was initialized successfully.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Gets the name of the detected NVIDIA GPU or empty if unavailable.</summary>
    public string GpuName { get; private set; } = string.Empty;

    /// <summary>Gets the latest installed NVIDIA driver version string.</summary>
    public static string LatestDriverVersion { get; private set; } = string.Empty;

    /// <summary>Gets the most recent snapshot of GPU metrics from <see cref="Query"/>.</summary>
    public static NvGpuData? LatestData { get; private set; }

    private IntPtr _device = IntPtr.Zero;
    private bool _initialized;

    /// <summary>
    /// Initializes a new instance of <see cref="NvGpuMonitor"/> and attempts to connect to NVML.
    /// </summary>
    public NvGpuMonitor()
    {
        try { Init(); }
        catch (Exception ex) { Diagnostics.Log.Error($"NvGpuMonitor: Init failed: {ex.Message}"); IsAvailable = false; }
    }

    private void Init()
    {
        int ret = nvmlInit();
        if (ret != NVML_SUCCESS)
        {
            Log($"NVML: nvmlInit returned {ret}");
            return;
        }

        // Get first NVIDIA GPU
        ret = nvmlDeviceGetHandleByIndex(0, out _device);
        if (ret != NVML_SUCCESS || _device == IntPtr.Zero)
        {
            Log($"NVML: nvmlDeviceGetHandleByIndex(0) returned {ret}");
            _ = nvmlShutdown();
            return;
        }

        // Get GPU name
        var nameBuf = new byte[96];
        ret = nvmlDeviceGetName(_device, nameBuf, nameBuf.Length);
        if (ret == NVML_SUCCESS)
        {
            int len = Array.IndexOf(nameBuf, (byte)0);
            if (len < 0) len = nameBuf.Length;
            GpuName = System.Text.Encoding.ASCII.GetString(nameBuf, 0, len);
        }

        // Get driver version
        var verBuf = new byte[80];
        if (nvmlSystemGetDriverVersion(verBuf, verBuf.Length) == NVML_SUCCESS)
        {
            int len = Array.IndexOf(verBuf, (byte)0);
            if (len > 0) LatestDriverVersion = System.Text.Encoding.ASCII.GetString(verBuf, 0, len);
        }

        _initialized = true;
        IsAvailable = true;
        Log($"NVML initialized: '{GpuName}' (device=0x{_device:X})");
    }

    /// <summary>Query current GPU metrics.</summary>
    public NvGpuData? Query()
    {
        if (!_initialized || _device == IntPtr.Zero) return null;

        try
        {
            var data = new NvGpuData();

            // Temperature (NVML_TEMPERATURE_GPU = 0)
            uint temp = 0;
            if (nvmlDeviceGetTemperature(_device, 0, ref temp) == NVML_SUCCESS)
                data.TempC = temp;

            // Utilization (GPU + Memory)
            var util = new nvmlUtilization();
            if (nvmlDeviceGetUtilizationRates(_device, ref util) == NVML_SUCCESS)
                data.UsagePct = util.gpu;

            // Memory
            var mem = new nvmlMemory();
            if (nvmlDeviceGetMemoryInfo(_device, ref mem) == NVML_SUCCESS && mem.total > 0)
            {
                data.VramTotalMb = mem.total / (1024f * 1024f);
                data.VramUsedMb = mem.used / (1024f * 1024f);
            }

            // Core clock (NVML_CLOCK_GRAPHICS = 0)
            uint clock = 0;
            if (nvmlDeviceGetClockInfo(_device, 0, ref clock) == NVML_SUCCESS)
                data.CoreClockMHz = clock;

            // Power draw (milliwatts)
            uint powerMw = 0;
            if (nvmlDeviceGetPowerUsage(_device, ref powerMw) == NVML_SUCCESS)
                data.PowerDrawW = powerMw / 1000f;

            // TDP / power limit (milliwatts)
            uint powerLimitMw = 0;
            if (nvmlDeviceGetPowerManagementLimit(_device, ref powerLimitMw) == NVML_SUCCESS)
                data.TdpW = powerLimitMw / 1000f;

            LatestData = data;
            return data;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"NvGpuMonitor: Query failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Releases NVML resources and resets availability.</summary>
    public void Dispose()
    {
        if (_initialized) _ = nvmlShutdown();
        _initialized = false;
        IsAvailable = false;
    }

    // Data record
    /// <summary>
    /// Contains a snapshot of all queried GPU metrics from <see cref="NvGpuMonitor.Query"/>.
    /// </summary>
    public struct NvGpuData
    {
        /// <summary>GPU temperature in degrees Celsius.</summary>
        public float TempC { get; set; }

        /// <summary>GPU core utilization as a percentage (0-100).</summary>
        public float UsagePct { get; set; }

        /// <summary>Total VRAM in megabytes.</summary>
        public float VramTotalMb { get; set; }

        /// <summary>Used VRAM in megabytes.</summary>
        public float VramUsedMb { get; set; }

        /// <summary>Current GPU core clock frequency in MHz.</summary>
        public float CoreClockMHz { get; set; }

        /// <summary>Current GPU power draw in watts.</summary>
        public float PowerDrawW { get; set; }

        /// <summary>GPU power limit (TDP) in watts.</summary>
        public float TdpW { get; set; }
    }

    // NVML P/Invoke
    private const int NVML_SUCCESS = 0;

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
    private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, ref nvmlUtilization util);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, ref nvmlMemory mem);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetClockInfo(IntPtr device, int clockType, ref uint clock);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetPowerUsage(IntPtr device, ref uint power);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlSystemGetDriverVersion([Out] byte[] version, int length);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetPowerManagementLimit(IntPtr device, ref uint powerLimit);

    [StructLayout(LayoutKind.Sequential)]
    private struct nvmlUtilization
    {
        public uint gpu;
        public uint memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct nvmlMemory
    {
        public ulong total;
        public ulong free;
        public ulong used;
    }
}
