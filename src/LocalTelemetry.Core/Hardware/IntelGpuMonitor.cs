using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LocalTelemetry.Core.Diagnostics;

namespace LocalTelemetry.Core.Hardware;

/// <summary>
/// Monitors Intel GPU metrics via the IGCL (Intel Graphics Control Library)
/// using <c>ControlLib.dll</c>. Returns zero/defaults when no Intel GPU is present.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class IntelGpuMonitor : IDisposable
{
    private const int CtlResultSuccess = 0;

    /// <summary>Gets whether the IGCL interface was initialized successfully.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Gets the name of the detected Intel GPU or empty if unavailable.</summary>
    public string GpuName { get; private set; } = string.Empty;

    private readonly IIntelGpuNative _native;
    private IntPtr _apiHandle = IntPtr.Zero;
    private IntPtr _deviceHandle = IntPtr.Zero;
    private IntPtr _tempHandle = IntPtr.Zero;
    private IntPtr _freqHandle = IntPtr.Zero;
    private IntPtr _memHandle = IntPtr.Zero;

    /// <summary>
    /// Initializes a new instance of <see cref="IntelGpuMonitor"/> and attempts
    /// to connect to the IGCL interface.
    /// </summary>
    public IntelGpuMonitor() : this(null) { }

    internal IntelGpuMonitor(IIntelGpuNative? native)
    {
        _native = native ?? IntelGpuNativePInvoke.Instance;
        try { Init(); }
        catch (Exception ex) { Log.Error(ex, "Failed to initialize Intel graphics monitor"); IsAvailable = false; }
    }

    private void Init()
    {
        int ret = _native.Init(out _apiHandle);
        if (ret != CtlResultSuccess || _apiHandle == IntPtr.Zero) return;

        ret = _native.EnumerateDevices(_apiHandle, out var devices);
        if (ret != CtlResultSuccess || devices.Length == 0) return;

        _deviceHandle = devices[0];

        if (_native.GetDeviceName(_deviceHandle, out var name) == CtlResultSuccess)
            GpuName = name ?? string.Empty;

        // Temperature
        if (_native.GetTempSensor(_deviceHandle, out var tempH) == CtlResultSuccess)
            _tempHandle = tempH;

        // Frequency
        if (_native.GetFreqDomain(_deviceHandle, out var freqH) == CtlResultSuccess)
            _freqHandle = freqH;

        // Memory
        if (_native.GetMemModule(_deviceHandle, out var memH) == CtlResultSuccess)
            _memHandle = memH;

        IsAvailable = true;
    }

    /// <summary>Reads the GPU temperature in degrees Celsius.</summary>
    /// <returns>Temperature in °C or 0 if unavailable.</returns>
    public float GetTempC()
    {
        if (!IsAvailable || _tempHandle == IntPtr.Zero) return 0;
        int ret = _native.GetTempC(_tempHandle, out double t);
        return ret == CtlResultSuccess ? (float)t : 0;
    }

    /// <summary>Reads the GPU core clock frequency in MHz.</summary>
    /// <returns>Core clock in MHz or 0 if unavailable.</returns>
    public float GetClockMHz()
    {
        if (!IsAvailable || _freqHandle == IntPtr.Zero) return 0;
        int ret = _native.GetClockMHz(_freqHandle, out double clock);
        return ret == CtlResultSuccess ? (float)clock : 0;
    }

    /// <summary>Reads GPU VRAM usage in megabytes.</summary>
    /// <returns>A tuple of (usedMb, totalMb) or (0, 0) if unavailable.</returns>
    public (float usedMb, float totalMb) GetVram()
    {
        if (!IsAvailable || _memHandle == IntPtr.Zero) return (0, 0);
        int ret = _native.GetVram(_memHandle, out ulong totalBytes, out ulong freeBytes);
        if (ret != CtlResultSuccess) return (0, 0);
        float totalMb = (float)(totalBytes / (1024.0 * 1024.0));
        float usedMb = (float)((totalBytes - freeBytes) / (1024.0 * 1024.0));
        return (usedMb, totalMb);
    }

    /// <summary>
    /// Resets the Intel IGCL connection and reacquires device handles.
    /// Safe to call following display driver updates or waking from sleep.
    /// </summary>
    public void Reset()
    {
        try
        {
            if (_apiHandle != IntPtr.Zero)
            {
                _native.Close(_apiHandle);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to close Intel IGCL handle during reset");
        }
        finally
        {
            _apiHandle = IntPtr.Zero;
            _deviceHandle = IntPtr.Zero;
            _tempHandle = IntPtr.Zero;
            _freqHandle = IntPtr.Zero;
            _memHandle = IntPtr.Zero;
            IsAvailable = false;
        }

        try
        {
            Init();
            if (IsAvailable)
                Log.Debug("Intel GPU monitor successfully re-initialized after reset");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to reset Intel graphics monitor");
            IsAvailable = false;
        }
    }

    /// <summary>Releases the IGCL API handle and resets availability.</summary>
    public void Dispose()
    {
        if (_apiHandle != IntPtr.Zero)
        {
            _native.Close(_apiHandle);
            _apiHandle = IntPtr.Zero;
        }
        IsAvailable = false;
        GC.SuppressFinalize(this);
    }
}

internal interface IIntelGpuNative
{
    int Init(out IntPtr apiHandle);
    int Close(IntPtr apiHandle);
    int EnumerateDevices(IntPtr apiHandle, out IntPtr[] devices);
    int GetDeviceName(IntPtr deviceHandle, out string gpuName);
    int GetTempSensor(IntPtr deviceHandle, out IntPtr tempHandle);
    int GetTempC(IntPtr tempHandle, out double tempC);
    int GetFreqDomain(IntPtr deviceHandle, out IntPtr freqHandle);
    int GetClockMHz(IntPtr freqHandle, out double clockMHz);
    int GetMemModule(IntPtr deviceHandle, out IntPtr memHandle);
    int GetVram(IntPtr memHandle, out ulong totalBytes, out ulong freeBytes);
}

internal sealed class IntelGpuNativePInvoke : IIntelGpuNative
{
    public static readonly IntelGpuNativePInvoke Instance = new();
    private const int CtlResultSuccess = 0;
    private const CallingConvention Cdecl = CallingConvention.Cdecl;

    internal delegate int InitDelegate(ref CtlInitArgs pInitDesc, out IntPtr phAPIHandle);
    internal InitDelegate InitFunc { get; set; } = ctlInit;
    internal Func<IntPtr, int> CloseFunc { get; set; } = ctlClose;
    internal delegate int EnumerateDevicesDelegate(IntPtr hAPIHandle, ref uint pCount, IntPtr[]? phDevices);
    internal EnumerateDevicesDelegate EnumerateDevicesFunc { get; set; } = ctlEnumerateDevices;
    internal delegate int GetDevicePropertiesDelegate(IntPtr hDAhandle, ref CtlDeviceAdapterProperties pProperties);
    internal GetDevicePropertiesDelegate GetDevicePropertiesFunc { get; set; } = ctlGetDeviceProperties;
    internal delegate int EnumTemperatureSensorsDelegate(IntPtr hDAhandle, ref uint pCount, IntPtr[]? phTemperature);
    internal EnumTemperatureSensorsDelegate EnumTemperatureSensorsFunc { get; set; } = ctlEnumTemperatureSensors;
    internal delegate int TemperatureGetStateDelegate(IntPtr hTemperature, ref double pTemperature);
    internal TemperatureGetStateDelegate TemperatureGetStateFunc { get; set; } = ctlTemperatureGetState;
    internal delegate int EnumFrequencyDomainsDelegate(IntPtr hDAhandle, ref uint pCount, IntPtr[]? phFrequency);
    internal EnumFrequencyDomainsDelegate EnumFrequencyDomainsFunc { get; set; } = ctlEnumFrequencyDomains;
    internal delegate int FrequencyGetStateDelegate(IntPtr hFrequency, ref CtlFreqState pState);
    internal FrequencyGetStateDelegate FrequencyGetStateFunc { get; set; } = ctlFrequencyGetState;
    internal delegate int EnumMemoryModulesDelegate(IntPtr hDAhandle, ref uint pCount, IntPtr[]? phMemory);
    internal EnumMemoryModulesDelegate EnumMemoryModulesFunc { get; set; } = ctlEnumMemoryModules;
    internal delegate int MemoryGetStateDelegate(IntPtr hMemory, ref CtlMemState pState);
    internal MemoryGetStateDelegate MemoryGetStateFunc { get; set; } = ctlMemoryGetState;

    internal void ResetHooks()
    {
        InitFunc = ctlInit;
        CloseFunc = ctlClose;
        EnumerateDevicesFunc = ctlEnumerateDevices;
        GetDevicePropertiesFunc = ctlGetDeviceProperties;
        EnumTemperatureSensorsFunc = ctlEnumTemperatureSensors;
        TemperatureGetStateFunc = ctlTemperatureGetState;
        EnumFrequencyDomainsFunc = ctlEnumFrequencyDomains;
        FrequencyGetStateFunc = ctlFrequencyGetState;
        EnumMemoryModulesFunc = ctlEnumMemoryModules;
        MemoryGetStateFunc = ctlMemoryGetState;
    }

    public int Init(out IntPtr apiHandle)
    {
        apiHandle = IntPtr.Zero;
        try
        {
            var initArgs = default(CtlInitArgs);
            initArgs.Size = (uint)Marshal.SizeOf<CtlInitArgs>();
            initArgs.Version = 0;
            initArgs.flags = 1;
            return InitFunc(ref initArgs, out apiHandle);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to initialize Intel graphics control library (ctlInit)");
            return -1;
        }
    }

    public int Close(IntPtr apiHandle)
    {
        try { return CloseFunc(apiHandle); }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to close Intel graphics control library handle (ctlClose)");
            return -1;
        }
    }

    public int EnumerateDevices(IntPtr apiHandle, out IntPtr[] devices)
    {
        devices = [];
        try
        {
            uint count = 0;
            int ret = EnumerateDevicesFunc(apiHandle, ref count, null);
            if (ret == CtlResultSuccess && count > 0)
            {
                devices = new IntPtr[count];
                ret = EnumerateDevicesFunc(apiHandle, ref count, devices);
                return ret;
            }
            return ret != CtlResultSuccess ? ret : -1;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to enumerate Intel graphics devices");
            return -1;
        }
    }

    public int GetDeviceName(IntPtr deviceHandle, out string gpuName)
    {
        gpuName = string.Empty;
        try
        {
            var props = default(CtlDeviceAdapterProperties);
            props.Size = (uint)Marshal.SizeOf<CtlDeviceAdapterProperties>();
            int ret = GetDevicePropertiesFunc(deviceHandle, ref props);
            if (ret == CtlResultSuccess) gpuName = props.name ?? string.Empty;
            return ret;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to get Intel graphics device properties");
            return -1;
        }
    }

    public int GetTempSensor(IntPtr deviceHandle, out IntPtr tempHandle)
    {
        tempHandle = IntPtr.Zero;
        try
        {
            uint count = 0;
            int ret = EnumTemperatureSensorsFunc(deviceHandle, ref count, null);
            if (ret == CtlResultSuccess && count > 0)
            {
                var temps = new IntPtr[count];
                ret = EnumTemperatureSensorsFunc(deviceHandle, ref count, temps);
                if (ret == CtlResultSuccess) tempHandle = temps[0];
                return ret;
            }
            return ret != CtlResultSuccess ? ret : -1;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to enumerate Intel graphics temperature sensors");
            return -1;
        }
    }

    public int GetTempC(IntPtr tempHandle, out double tempC)
    {
        tempC = 0;
        try { return TemperatureGetStateFunc(tempHandle, ref tempC); }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to read Intel graphics temperature sensor state");
            return -1;
        }
    }

    public int GetFreqDomain(IntPtr deviceHandle, out IntPtr freqHandle)
    {
        freqHandle = IntPtr.Zero;
        try
        {
            uint count = 0;
            int ret = EnumFrequencyDomainsFunc(deviceHandle, ref count, null);
            if (ret == CtlResultSuccess && count > 0)
            {
                var freqs = new IntPtr[count];
                ret = EnumFrequencyDomainsFunc(deviceHandle, ref count, freqs);
                if (ret == CtlResultSuccess) freqHandle = freqs[0];
                return ret;
            }
            return ret != CtlResultSuccess ? ret : -1;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to enumerate Intel graphics frequency domains");
            return -1;
        }
    }

    public int GetClockMHz(IntPtr freqHandle, out double clockMHz)
    {
        clockMHz = 0;
        try
        {
            var state = default(CtlFreqState);
            state.Size = (uint)Marshal.SizeOf<CtlFreqState>();
            int ret = FrequencyGetStateFunc(freqHandle, ref state);
            if (ret == CtlResultSuccess) clockMHz = state.actual;
            return ret;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to read Intel graphics frequency domain state");
            return -1;
        }
    }

    public int GetMemModule(IntPtr deviceHandle, out IntPtr memHandle)
    {
        memHandle = IntPtr.Zero;
        try
        {
            uint count = 0;
            int ret = EnumMemoryModulesFunc(deviceHandle, ref count, null);
            if (ret == CtlResultSuccess && count > 0)
            {
                var mems = new IntPtr[count];
                ret = EnumMemoryModulesFunc(deviceHandle, ref count, mems);
                if (ret == CtlResultSuccess) memHandle = mems[0];
                return ret;
            }
            return ret != CtlResultSuccess ? ret : -1;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to enumerate Intel graphics memory modules");
            return -1;
        }
    }

    public int GetVram(IntPtr memHandle, out ulong totalBytes, out ulong freeBytes)
    {
        totalBytes = 0;
        freeBytes = 0;
        try
        {
            var state = default(CtlMemState);
            state.Size = (uint)Marshal.SizeOf<CtlMemState>();
            int ret = MemoryGetStateFunc(memHandle, ref state);
            if (ret == CtlResultSuccess)
            {
                totalBytes = state.size;
                freeBytes = state.free;
            }
            return ret;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to read Intel graphics memory state");
            return -1;
        }
    }

    [DllImport("ControlLib.dll", CallingConvention = Cdecl)]
    private static extern int ctlInit(ref CtlInitArgs pInitDesc, out IntPtr phAPIHandle);

    [DllImport("ControlLib.dll", CallingConvention = Cdecl)]
    private static extern int ctlClose(IntPtr hAPIHandle);

    [DllImport("ControlLib.dll", CallingConvention = Cdecl)]
    private static extern int ctlEnumerateDevices(IntPtr hAPIHandle, ref uint pCount, [In, Out] IntPtr[]? phDevices);

    [DllImport("ControlLib.dll", CallingConvention = Cdecl)]
    private static extern int ctlGetDeviceProperties(IntPtr hDAhandle, ref CtlDeviceAdapterProperties pProperties);

    [DllImport("ControlLib.dll", CallingConvention = Cdecl)]
    private static extern int ctlEnumTemperatureSensors(IntPtr hDAhandle, ref uint pCount, [In, Out] IntPtr[]? phTemperature);

    [DllImport("ControlLib.dll", CallingConvention = Cdecl)]
    private static extern int ctlTemperatureGetState(IntPtr hTemperature, ref double pTemperature);

    [DllImport("ControlLib.dll", CallingConvention = Cdecl)]
    private static extern int ctlEnumFrequencyDomains(IntPtr hDAhandle, ref uint pCount, [In, Out] IntPtr[]? phFrequency);

    [DllImport("ControlLib.dll", CallingConvention = Cdecl)]
    private static extern int ctlFrequencyGetState(IntPtr hFrequency, ref CtlFreqState pState);

    [DllImport("ControlLib.dll", CallingConvention = Cdecl)]
    private static extern int ctlEnumMemoryModules(IntPtr hDAhandle, ref uint pCount, [In, Out] IntPtr[]? phMemory);

    [DllImport("ControlLib.dll", CallingConvention = Cdecl)]
    private static extern int ctlMemoryGetState(IntPtr hMemory, ref CtlMemState pState);

    [StructLayout(LayoutKind.Sequential)]
    internal struct CtlInitArgs
    {
        public uint Size;
        public byte Version;
        public ulong AppVersion;
        public uint flags;
        public ulong SupportedVersion;
        public Guid ApplicationUID;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CtlDeviceAdapterProperties
    {
        public uint Size;
        public byte Version;
        public IntPtr pDeviceID;
        public uint device_id_size;
        public uint device_type;
        public uint supported_subfunction_flags;
        public ulong driver_version;
        public CtlFirmwareVersion firmware_version;
        public uint pci_vendor_id;
        public uint pci_device_id;
        public uint rev_id;
        public uint num_eus_per_sub_slice;
        public uint num_sub_slices_per_slice;
        public uint num_slices;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string? name;
        public uint graphics_adapter_properties;
        public uint Frequency;
        public ushort pci_subsys_id;
        public ushort pci_subsys_vendor_id;
        public CtlAdapterBdf adapter_bdf;
        public uint num_xe_cores;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CtlFirmwareVersion
    {
        public ushort major;
        public ushort minor;
        public ushort build;
        public ushort revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CtlAdapterBdf
    {
        public uint bus;
        public uint device;
        public uint function;
        public uint domain;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CtlFreqState
    {
        public uint Size;
        public byte Version;
        public double actual;
        public double efficient;
        public double request;
        public double tdp;
        public uint throttleReasons;
        public double currentVoltage;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CtlMemState
    {
        public uint Size;
        public byte Version;
        public ulong size;
        public ulong free;
    }
}
