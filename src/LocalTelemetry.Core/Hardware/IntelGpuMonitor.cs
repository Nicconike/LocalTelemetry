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
    private const string DllName = "ControlLib.dll";
    private const int CtlResultSuccess = 0;

    /// <summary>Gets whether the IGCL interface was initialized successfully.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Gets the name of the detected Intel GPU or empty if unavailable.</summary>
    public string GpuName { get; private set; } = string.Empty;

    private IntPtr _apiHandle = IntPtr.Zero;
    private IntPtr _deviceHandle = IntPtr.Zero;
    private IntPtr _tempHandle = IntPtr.Zero;
    private IntPtr _freqHandle = IntPtr.Zero;
    private IntPtr _memHandle = IntPtr.Zero;

    /// <summary>
    /// Initializes a new instance of <see cref="IntelGpuMonitor"/> and attempts
    /// to connect to the IGCL interface.
    /// </summary>
    public IntelGpuMonitor()
    {
        try { Init(); }
        catch (Exception ex) { Log.Error(ex, "Init failed"); IsAvailable = false; }
    }

    private void Init()
    {
        var initArgs = default(ctl_init_args_t);
        initArgs.Size = (uint)Marshal.SizeOf<ctl_init_args_t>();
        initArgs.Version = 0;
        initArgs.flags = 1; // CTL_INIT_FLAG_USE_LEVEL_ZERO

        int ret = ctlInit(ref initArgs, out _apiHandle);
        if (ret != CtlResultSuccess) return;

        uint count = 0;
        ret = ctlEnumerateDevices(_apiHandle, ref count, null);
        if (ret != CtlResultSuccess || count == 0) return;

        var devices = new IntPtr[count];
        ret = ctlEnumerateDevices(_apiHandle, ref count, devices);
        if (ret != CtlResultSuccess) return;

        _deviceHandle = devices[0];

        var props = default(ctl_device_adapter_properties_t);
        props.Size = (uint)Marshal.SizeOf<ctl_device_adapter_properties_t>();
        props.Version = 0;
        ret = ctlGetDeviceProperties(_deviceHandle, ref props);
        if (ret == CtlResultSuccess)
            GpuName = props.name ?? string.Empty;

        // Temperature
        uint tempCount = 0;
        ret = ctlEnumTemperatureSensors(_deviceHandle, ref tempCount, null);
        if (ret == CtlResultSuccess && tempCount > 0)
        {
            var temps = new IntPtr[tempCount];
            ret = ctlEnumTemperatureSensors(_deviceHandle, ref tempCount, temps);
            if (ret == CtlResultSuccess) _tempHandle = temps[0];
        }

        // Frequency
        uint freqCount = 0;
        ret = ctlEnumFrequencyDomains(_deviceHandle, ref freqCount, null);
        if (ret == CtlResultSuccess && freqCount > 0)
        {
            var freqs = new IntPtr[freqCount];
            ret = ctlEnumFrequencyDomains(_deviceHandle, ref freqCount, freqs);
            if (ret == CtlResultSuccess) _freqHandle = freqs[0];
        }

        // Memory
        uint memCount = 0;
        ret = ctlEnumMemoryModules(_deviceHandle, ref memCount, null);
        if (ret == CtlResultSuccess && memCount > 0)
        {
            var mems = new IntPtr[memCount];
            ret = ctlEnumMemoryModules(_deviceHandle, ref memCount, mems);
            if (ret == CtlResultSuccess) _memHandle = mems[0];
        }

        IsAvailable = true;
    }

    /// <summary>Reads the GPU temperature in degrees Celsius.</summary>
    /// <returns>Temperature in °C or 0 if unavailable.</returns>
    public float GetTempC()
    {
        if (!IsAvailable || _tempHandle == IntPtr.Zero) return 0;
        double t = 0;
        int ret = ctlTemperatureGetState(_tempHandle, ref t);
        return ret == CtlResultSuccess ? (float)t : 0;
    }

    /// <summary>Reads the GPU core clock frequency in MHz.</summary>
    /// <returns>Core clock in MHz or 0 if unavailable.</returns>
    public float GetClockMHz()
    {
        if (!IsAvailable || _freqHandle == IntPtr.Zero) return 0;
        var state = default(ctl_freq_state_t);
        state.Size = (uint)Marshal.SizeOf<ctl_freq_state_t>();
        state.Version = 0;
        int ret = ctlFrequencyGetState(_freqHandle, ref state);
        return ret == CtlResultSuccess ? (float)state.actual : 0;
    }

    /// <summary>Reads GPU VRAM usage in megabytes.</summary>
    /// <returns>A tuple of (usedMb, totalMb) or (0, 0) if unavailable.</returns>
    public (float usedMb, float totalMb) GetVram()
    {
        if (!IsAvailable || _memHandle == IntPtr.Zero) return (0, 0);
        var state = default(ctl_mem_state_t);
        state.Size = (uint)Marshal.SizeOf<ctl_mem_state_t>();
        state.Version = 0;
        int ret = ctlMemoryGetState(_memHandle, ref state);
        if (ret != CtlResultSuccess) return (0, 0);
        float totalMb = (float)(state.size / (1024.0 * 1024.0));
        float usedMb = (float)((state.size - state.free) / (1024.0 * 1024.0));
        return (usedMb, totalMb);
    }

    /// <summary>Releases the IGCL API handle and resets availability.</summary>
    public void Dispose()
    {
        if (_apiHandle != IntPtr.Zero)
        {
            ctlClose(_apiHandle);
            _apiHandle = IntPtr.Zero;
        }
        IsAvailable = false;
        GC.SuppressFinalize(this);
    }

    // P/Invoke

    private const CallingConvention Cdecl = CallingConvention.Cdecl;

    [DllImport(DllName, CallingConvention = Cdecl)]
    private static extern int ctlInit(ref ctl_init_args_t pInitDesc, out IntPtr phAPIHandle);

    [DllImport(DllName, CallingConvention = Cdecl)]
    private static extern int ctlClose(IntPtr hAPIHandle);

    [DllImport(DllName, CallingConvention = Cdecl)]
    private static extern int ctlEnumerateDevices(IntPtr hAPIHandle, ref uint pCount, [In, Out] IntPtr[]? phDevices);

    [DllImport(DllName, CallingConvention = Cdecl)]
    private static extern int ctlGetDeviceProperties(IntPtr hDAhandle, ref ctl_device_adapter_properties_t pProperties);

    [DllImport(DllName, CallingConvention = Cdecl)]
    private static extern int ctlEnumTemperatureSensors(IntPtr hDAhandle, ref uint pCount, [In, Out] IntPtr[]? phTemperature);

    [DllImport(DllName, CallingConvention = Cdecl)]
    private static extern int ctlTemperatureGetState(IntPtr hTemperature, ref double pTemperature);

    [DllImport(DllName, CallingConvention = Cdecl)]
    private static extern int ctlEnumFrequencyDomains(IntPtr hDAhandle, ref uint pCount, [In, Out] IntPtr[]? phFrequency);

    [DllImport(DllName, CallingConvention = Cdecl)]
    private static extern int ctlFrequencyGetState(IntPtr hFrequency, ref ctl_freq_state_t pState);

    [DllImport(DllName, CallingConvention = Cdecl)]
    private static extern int ctlEnumMemoryModules(IntPtr hDAhandle, ref uint pCount, [In, Out] IntPtr[]? phMemory);

    [DllImport(DllName, CallingConvention = Cdecl)]
    private static extern int ctlMemoryGetState(IntPtr hMemory, ref ctl_mem_state_t pState);

    // Structs
    [StructLayout(LayoutKind.Sequential)]
    private struct ctl_init_args_t
    {
        public uint Size;
        public byte Version;
        public ulong AppVersion;
        public uint flags;
        public ulong SupportedVersion;
        public Guid ApplicationUID;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ctl_device_adapter_properties_t
    {
        public uint Size;
        public byte Version;
        public IntPtr pDeviceID;
        public uint device_id_size;
        public uint device_type;
        public uint supported_subfunction_flags;
        public ulong driver_version;
        public ctl_firmware_version_t firmware_version;
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
        public ctl_adapter_bdf_t adapter_bdf;
        public uint num_xe_cores;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ctl_firmware_version_t
    {
        public ushort major;
        public ushort minor;
        public ushort build;
        public ushort revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ctl_adapter_bdf_t
    {
        public uint bus;
        public uint device;
        public uint function;
        public uint domain;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ctl_freq_state_t
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
    private struct ctl_mem_state_t
    {
        public uint Size;
        public byte Version;
        public ulong size;
        public ulong free;
    }
}
