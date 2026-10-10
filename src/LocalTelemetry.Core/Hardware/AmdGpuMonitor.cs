using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LocalTelemetry.Core.Diagnostics;

namespace LocalTelemetry.Core.Hardware;

/// <summary>
/// Monitors AMD GPU metrics via the ADL (AMD Display Library) interface
/// using <c>atiadlxx.dll</c>. Returns zero/defaults when no AMD GPU is present.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AmdGpuMonitor : IDisposable
{
    /// <summary>Gets whether the AMD ADL interface was initialized successfully.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Gets the name of the detected AMD GPU or empty if unavailable.</summary>
    public string GpuName { get; private set; } = string.Empty;

    private readonly IAmdAdlNative _native;
    private int _adapterIndex = -1;
    private bool _initialized;

    /// <summary>
    /// Initializes a new instance of <see cref="AmdGpuMonitor"/> and attempts
    /// to connect to the ADL interface.
    /// </summary>
    public AmdGpuMonitor() : this(null) { }

    internal AmdGpuMonitor(IAmdAdlNative? native)
    {
        _native = native ?? AmdAdlNativePInvoke.Instance;
        try { Init(); }
        catch (Exception ex) { Log.Error(ex, "Failed to initialize AMD graphics monitor"); IsAvailable = false; }
    }

    private void Init()
    {
        if (!_native.Create().IsOk()) return;

        if (!_native.GetAdapterInfo(out _adapterIndex, out var adapterName).IsOk())
        {
            _native.Destroy();
            return;
        }

        GpuName = adapterName ?? string.Empty;
        IsAvailable = true;
        _initialized = true;
    }

    /// <summary>
    /// Resets the AMD ADL connection and reacquires adapter handles.
    /// Safe to call following display driver updates or waking from sleep.
    /// </summary>
    public void Reset()
    {
        try
        {
            if (_initialized) _ = _native.Destroy();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to destroy AMD ADL connection during reset");
        }
        finally
        {
            _initialized = false;
            _adapterIndex = -1;
            IsAvailable = false;
        }

        try
        {
            Init();
            if (IsAvailable)
                Log.Debug("AMD GPU monitor successfully re-initialized after reset");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to reset AMD graphics monitor");
            IsAvailable = false;
        }
    }

    /// <summary>Reads the GPU temperature in degrees Celsius.</summary>
    /// <returns>Temperature in °C or 0 if unavailable.</returns>
    public float GetTempC()
    {
        if (!IsAvailable) return 0;
        int ret = _native.GetTemperature(_adapterIndex, out int tempMilliC);
        return ret.IsOk() ? tempMilliC / 1000f : 0;
    }

    /// <summary>Reads the GPU core usage percentage.</summary>
    /// <returns>Usage from 0-100 or 0 if unavailable.</returns>
    public float GetUsagePct()
    {
        if (!IsAvailable) return 0;
        int ret = _native.GetActivity(_adapterIndex, out int activityPct, out _);
        return ret.IsOk() ? activityPct : 0;
    }

    /// <summary>Reads the GPU core clock frequency in MHz.</summary>
    /// <returns>Core clock in MHz or 0 if unavailable.</returns>
    public float GetClockMHz()
    {
        if (!IsAvailable) return 0;
        int ret = _native.GetActivity(_adapterIndex, out _, out int coreClock100Hz);
        return ret.IsOk() ? coreClock100Hz / 100f : 0;
    }

    /// <summary>Reads GPU VRAM usage in megabytes.</summary>
    /// <returns>A tuple of (usedMb, totalMb) or (0, 0) if unavailable.</returns>
    public (float usedMb, float totalMb) GetVram()
    {
        if (!IsAvailable) return (0, 0);
        int ret = _native.GetMemoryInfo(_adapterIndex, out long totalBytes, out long freeBytes);
        if (!ret.IsOk()) return (0, 0);
        float totalMb = totalBytes / (1024f * 1024f);
        float usedMb = totalMb - freeBytes / (1024f * 1024f);
        return (usedMb, totalMb);
    }

    /// <summary>Reads the maximum power limit in watts.</summary>
    /// <returns>Power limit in W or 0 if unavailable.</returns>
    public float GetPowerLimitW()
    {
        if (!IsAvailable) return 0;
        int ret = _native.GetPowerLimit(_adapterIndex, out int maxMilliW, out _);
        return ret.IsOk() ? maxMilliW / 1000f : 0;
    }

    /// <summary>Reads the current power draw in watts (default power limit).</summary>
    /// <returns>Power draw in W or 0 if unavailable.</returns>
    public float GetPowerDrawW()
    {
        if (!IsAvailable) return 0;
        int ret = _native.GetPowerLimit(_adapterIndex, out _, out int defaultMilliW);
        return ret.IsOk() ? defaultMilliW / 1000f : 0;
    }

    /// <summary>Releases the ADL interface and resets availability.</summary>
    public void Dispose()
    {
        if (_initialized) _native.Destroy();
        IsAvailable = false;
        _initialized = false;
    }
}

internal interface IAmdAdlNative
{
    int Create();
    int Destroy();
    int GetAdapterInfo(out int adapterIndex, out string adapterName);
    int GetTemperature(int adapterIndex, out int tempMilliC);
    int GetActivity(int adapterIndex, out int activityPct, out int coreClock100Hz);
    int GetMemoryInfo(int adapterIndex, out long totalBytes, out long freeBytes);
    int GetPowerLimit(int adapterIndex, out int maxMilliW, out int defaultMilliW);
}

internal sealed class AmdAdlNativePInvoke : IAmdAdlNative
{
    public static readonly AmdAdlNativePInvoke Instance = new();

    public int Create()
    {
        try { return ADL_Main_Control_Create(ADL_Main_Memory_Alloc, 1); }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to initialize AMD display library (ADL_Main_Control_Create)");
            return -1;
        }
    }

    public int Destroy()
    {
        try { return ADL_Main_Control_Destroy(); }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to destroy AMD display library handle (ADL_Main_Control_Destroy)");
            return -1;
        }
    }

    public int GetAdapterInfo(out int adapterIndex, out string adapterName)
    {
        adapterIndex = -1;
        adapterName = string.Empty;
        try
        {
            int count = 0;
            var info = new AdlAdapterInfo[16];
            int ret = ADL_Adapter_AdapterInfo_Get(info, ref count);
            if (ret == 0 && count > 0)
            {
                adapterIndex = info[0].iAdapterIndex;
                adapterName = info[0].strAdapterName ?? string.Empty;
                return 0;
            }
            return ret != 0 ? ret : -1;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to retrieve AMD adapter information");
            return -1;
        }
    }

    public int GetTemperature(int adapterIndex, out int tempMilliC)
    {
        tempMilliC = 0;
        try
        {
            var temp = default(AdlTemperature);
            temp.iSize = Marshal.SizeOf<AdlTemperature>();
            int ret = ADL_Overdrive5_Temperature_Get(adapterIndex, 0, ref temp);
            if (ret == 0) tempMilliC = temp.iTemperature;
            return ret;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to retrieve AMD adapter temperature");
            return -1;
        }
    }

    public int GetActivity(int adapterIndex, out int activityPct, out int coreClock100Hz)
    {
        activityPct = 0;
        coreClock100Hz = 0;
        try
        {
            var activity = default(AdlPmActivity);
            activity.iSize = Marshal.SizeOf<AdlPmActivity>();
            int ret = ADL_Overdrive5_CurrentActivity_Get(adapterIndex, ref activity);
            if (ret == 0)
            {
                activityPct = activity.iActivityPercent;
                coreClock100Hz = activity.iCoreClock;
            }
            return ret;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to retrieve AMD adapter activity");
            return -1;
        }
    }

    public int GetMemoryInfo(int adapterIndex, out long totalBytes, out long freeBytes)
    {
        totalBytes = 0;
        freeBytes = 0;
        try
        {
            var mem = new AdlMemoryInfo();
            int ret = ADL_Adapter_MemoryInfo_Get(adapterIndex, ref mem);
            if (ret == 0)
            {
                totalBytes = mem.iMemorySize;
                freeBytes = mem.iFreeMemorySize;
            }
            return ret;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to retrieve AMD adapter memory information");
            return -1;
        }
    }

    public int GetPowerLimit(int adapterIndex, out int maxMilliW, out int defaultMilliW)
    {
        maxMilliW = 0;
        defaultMilliW = 0;
        try
        {
            var pli = default(AdlPowerLimitInfo);
            int ret = ADL_Overdrive5_PowerLimit_Get(adapterIndex, 0, ref pli);
            if (ret == 0)
            {
                maxMilliW = pli.iMax;
                defaultMilliW = pli.iDefault;
            }
            return ret;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to retrieve AMD adapter power limit");
            return -1;
        }
    }

    private delegate IntPtr ADL_Main_Memory_AllocDelegate(int size);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Main_Control_Create(ADL_Main_Memory_AllocDelegate callback, int enumConnectedAdapters);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Main_Control_Destroy();

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Adapter_AdapterInfo_Get([In, Out] AdlAdapterInfo[] info, ref int count);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Overdrive5_Temperature_Get(int adapterIndex, int thermalControllerIndex, ref AdlTemperature temp);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Overdrive5_CurrentActivity_Get(int adapterIndex, ref AdlPmActivity activity);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Adapter_MemoryInfo_Get(int adapterIndex, ref AdlMemoryInfo memoryInfo);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Overdrive5_PowerLimit_Get(int adapterIndex, int defaultOrZero, ref AdlPowerLimitInfo powerLimitInfo);

    private static IntPtr ADL_Main_Memory_Alloc(int size) => Marshal.AllocHGlobal(size);

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlAdapterInfo
    {
        public int iAdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string? strAdapterName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlTemperature
    {
        public int iSize;
        public int iTemperature;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlPmActivity
    {
        public int iSize;
        public int iEngineClock;
        public int iMemoryClock;
        public int iVddc;
        public int iActivityPercent;
        public int iCurrentPerformanceLevel;
        public int iCurrentBusSpeed;
        public int iCurrentBusLanes;
        public int iMaxBusLanes;
        public int iExtActivityPercent;
        public int iCoreClock;
        public int iMemoryClock1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlMemoryInfo
    {
        public int iMemorySize;
        public int iMemoryType;
        public int iMemoryBandwidth;
        public int iFreeMemorySize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlPowerLimitInfo
    {
        public int iMin;
        public int iMax;
        public int iDefault;
    }
}

file static class AdlExtensions
{
    public static bool IsOk(this int ret) => ret == 0;
}
