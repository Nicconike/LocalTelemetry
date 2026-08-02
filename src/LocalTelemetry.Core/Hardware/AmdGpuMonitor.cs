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
    private const string DllName = "atiadlxx.dll";
    private const int AdlMaxAdapters = 16;

    /// <summary>Gets whether the AMD ADL interface was initialized successfully.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Gets the name of the detected AMD GPU or empty if unavailable.</summary>
    public string GpuName { get; private set; } = string.Empty;

    private int _adapterIndex = -1;
    private bool _initialized;

    /// <summary>
    /// Initializes a new instance of <see cref="AmdGpuMonitor"/> and attempts
    /// to connect to the ADL interface.
    /// </summary>
    public AmdGpuMonitor()
    {
        try { Init(); }
        catch (Exception ex) { Log.Error(ex, "Init failed"); IsAvailable = false; }
    }

    private void Init()
    {
        if (!ADL_Main_Control_Create(ADL_Main_Memory_Alloc, 1).IsOk()) return;

        int count = 0;
        var info = new ADLAdapterInfo[AdlMaxAdapters];
        if (!ADL_Adapter_AdapterInfo_Get(info, ref count).IsOk() || count == 0)
        {
            ADL_Main_Control_Destroy();
            return;
        }

        _adapterIndex = info[0].iAdapterIndex;
        GpuName = info[0].strAdapterName ?? string.Empty;
        IsAvailable = true;
        _initialized = true;
    }

    /// <summary>Reads the GPU temperature in degrees Celsius.</summary>
    /// <returns>Temperature in °C or 0 if unavailable.</returns>
    public float GetTempC()
    {
        if (!IsAvailable) return 0;
        var temp = default(ADLTemperature);
        temp.iSize = Marshal.SizeOf<ADLTemperature>();
        int ret = ADL_Overdrive5_Temperature_Get(_adapterIndex, 0, ref temp);
        return ret.IsOk() ? temp.iTemperature / 1000f : 0;
    }

    /// <summary>Reads the GPU core usage percentage.</summary>
    /// <returns>Usage from 0-100 or 0 if unavailable.</returns>
    public float GetUsagePct()
    {
        if (!IsAvailable) return 0;
        var activity = default(ADLPMActivity);
        activity.iSize = Marshal.SizeOf<ADLPMActivity>();
        int ret = ADL_Overdrive5_CurrentActivity_Get(_adapterIndex, ref activity);
        return ret.IsOk() ? activity.iActivityPercent : 0;
    }

    /// <summary>Reads the GPU core clock frequency in MHz.</summary>
    /// <returns>Core clock in MHz or 0 if unavailable.</returns>
    public float GetClockMHz()
    {
        if (!IsAvailable) return 0;
        var activity = default(ADLPMActivity);
        activity.iSize = Marshal.SizeOf<ADLPMActivity>();
        int ret = ADL_Overdrive5_CurrentActivity_Get(_adapterIndex, ref activity);
        return ret.IsOk() ? activity.iCoreClock / 100f : 0;
    }

    /// <summary>Reads GPU VRAM usage in megabytes.</summary>
    /// <returns>A tuple of (usedMb, totalMb) or (0, 0) if unavailable.</returns>
    public (float usedMb, float totalMb) GetVram()
    {
        if (!IsAvailable) return (0, 0);
        var mem = new ADLMemoryInfo();
        int ret = ADL_Adapter_MemoryInfo_Get(_adapterIndex, ref mem);
        if (!ret.IsOk()) return (0, 0);
        float totalMb = mem.iMemorySize / (1024f * 1024f);
        float usedMb = totalMb - mem.iFreeMemorySize / (1024f * 1024f);
        return (usedMb, totalMb);
    }

    /// <summary>Reads the maximum power limit in watts.</summary>
    /// <returns>Power limit in W or 0 if unavailable.</returns>
    public float GetPowerLimitW()
    {
        if (!IsAvailable) return 0;
        var pli = default(ADLPowerLimitInfo);
        int ret = ADL_Overdrive5_PowerLimit_Get(_adapterIndex, 0, ref pli);
        return ret.IsOk() ? pli.iMax / 1000f : 0;
    }

    /// <summary>Reads the current power draw in watts (default power limit).</summary>
    /// <returns>Power draw in W or 0 if unavailable.</returns>
    public float GetPowerDrawW()
    {
        if (!IsAvailable) return 0;
        var pli = default(ADLPowerLimitInfo);
        int ret = ADL_Overdrive5_PowerLimit_Get(_adapterIndex, 0, ref pli);
        if (!ret.IsOk()) return 0;
        return pli.iDefault / 1000f;
    }

    /// <summary>Releases the ADL interface and resets availability.</summary>
    public void Dispose()
    {
        if (_initialized) ADL_Main_Control_Destroy();
        IsAvailable = false;
        _initialized = false;
    }

    // P/Invoke
    private delegate IntPtr ADL_Main_Memory_AllocDelegate(int size);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Main_Control_Create(
        ADL_Main_Memory_AllocDelegate callback, int enumConnectedAdapters);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Main_Control_Destroy();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Adapter_AdapterInfo_Get(
        [In, Out] ADLAdapterInfo[] info, ref int count);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Overdrive5_Temperature_Get(
        int adapterIndex, int thermalControllerIndex, ref ADLTemperature temp);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Overdrive5_CurrentActivity_Get(
        int adapterIndex, ref ADLPMActivity activity);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Adapter_MemoryInfo_Get(
        int adapterIndex, ref ADLMemoryInfo memoryInfo);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Overdrive5_PowerLimit_Get(
        int adapterIndex, int defaultOrZero, ref ADLPowerLimitInfo powerLimitInfo);

    private static IntPtr ADL_Main_Memory_Alloc(int size)
    {
        return Marshal.AllocHGlobal(size);
    }

    // Structs
    [StructLayout(LayoutKind.Sequential)]
    private struct ADLAdapterInfo
    {
        public int iAdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string? strAdapterName;
        // remaining fields omitted (not needed)
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ADLTemperature
    {
        public int iSize;
        public int iTemperature;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ADLPMActivity
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
    private struct ADLMemoryInfo
    {
        public int iMemorySize;
        public int iMemoryType;
        public int iMemoryBandwidth;
        public int iFreeMemorySize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ADLPowerLimitInfo
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
