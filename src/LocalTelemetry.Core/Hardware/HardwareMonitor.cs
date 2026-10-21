using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LocalTelemetry.Core.Hardware.PawnIo;
using LocalTelemetry.Core.Diagnostics;
using LocalTelemetry.Core.Models;

namespace LocalTelemetry.Core.Hardware;

/// <summary>
/// Polls CPU, RAM, GPU, Disk and network metrics.
/// Sources:
///   CPU %    → GetSystemTimes (kernel32 single call, no COM)
///   CPU temp → PawnIO IntelMSR (Intel MSR) / RyzenSMU (AMD SMU)
///   CPU freq → PawnIO APERF/MPERF MSR delta, Intel Power Gadget fallback
///   RAM      → GlobalMemoryStatusEx
///   GPU %    → NVML (NVIDIA) / ADL (AMD)
///   GPU temp → NvGpuMonitor (NVAPI, user-mode)
///   Network  → NetworkInterface.GetIPv4Statistics() delta on active adapter
///   Disk I/O → PDH "PhysicalDisk\Disk * Bytes/sec\_Total"
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HardwareMonitor : IDisposable
{
    private readonly Config.AppSettings _cfg;
    private readonly ISystemInfo _sysInfo;
    private readonly IPawnIoManager _pawnIoManager;
    private readonly Timer? _timer;
    private int _isPolling;

    // PDH counters (disk only; CPU uses GetSystemTimes)
    private PerformanceCounter? _diskRead;
    private PerformanceCounter? _diskWrite;
    private List<(PerformanceCounter read, PerformanceCounter write)>? _perDiskCounters;

    // Network via NetworkInterface.GetIPv4Statistics delta
    private string? _nicInstanceName;
    private string _lastPreferredNic = "~~unset~~";
    private string? _lastBoundNicId;
    private NetworkInterface? _nicInterface;
    private long _prevRx, _prevTx;
    private DateTime _prevNicTime;
    private bool _nicPrimed;
    private string _primaryDiskType = string.Empty;
    private List<string> _diskBusTypes = [];

    // GetSystemTimes fallback state
    private long _prevIdle, _prevKernel, _prevUser;
    private float _prevCpuPct;
    private bool _hasPrevTimes;

    // CPU max frequency (from CallNtPowerInformation)
    private uint _maxCpuMhz;

    // NVML GPU monitor
    private NvGpuMonitor? _nvGpu;

    // Intel GPU monitor (IGCL)
    private IntelGpuMonitor? _intelGpu;

    // AMD GPU monitor (ADL)
    private AmdGpuMonitor? _amdGpu;

    // WDDM GPU monitor (D3DKMTQueryStatistics, like Task Manager)
    private WddmGpuMonitor? _wddmGpu;

    // Vendor CPU temp - Intel Power Gadget, signed by Intel
    private IntelPowerGadget? _intelPwrGadget;

    // PawnIO - EV-signed kernel driver for MSR/SMU access
    internal static IPawnIoTransport? SharedPawnIoDevice { get; private set; }
    private IPawnIoTransport? _pawnIoDevice;
    private IntelMsrReader? _intelMsr;
    private AmdMsrReader? _amdMsr;

    // APERF/MPERF frequency (delta over poll interval)
    private ulong _prevAperf, _prevMperf;
    private bool _hasMsrFreqPrimed;

    // RAPL CPU power (energy delta over poll interval)
    private double _energyUnit;          // joules per LSB (from MSR_RAPL_POWER_UNIT)
    private double _powerUnitDivisor = 1; // power divisor (from MSR_RAPL_POWER_UNIT bits 3:0)
    private ulong _prevPkgEnergy;
    private DateTime _prevEnergyTime;
    private bool _hasEnergyPrimed;

    private const string IntelVendor = "Intel";
    private const string AmdVendor = "AMD";
    private const string NvidiaVendor = "NVIDIA";
    private const string PhysicalDiskCategory = "PhysicalDisk";
    private const string IoctlReadMsrCommand = "ioctl_read_msr";

    private string _cpuVendor = string.Empty;
    private string _gpuVendor = string.Empty;
    private string _gpuName = string.Empty;

    // One-shot logging flags (prevent per-tick repeat warnings)
    private int _onceCpuTempIntelMsrNull;
    private int _onceCpuTempIntelMsrNotAvail;
    private int _onceCpuTempIntelMsrZero;
    private int _onceCpuTempAmdMsrNull;
    private int _onceCpuTempAmdMsrNotAvail;
    private int _onceCpuTempAmdMsrZero;
    private int _onceCpuTempUnknownVendor;

    // Battery
    private ManagementObjectSearcher? _batterySearcher;
    private long _lastBatteryMwH;
    private DateTime _lastBatteryPollTime;
    private bool _hasBatteryPrimed;

    // Event
    public event Action<TelemetrySnapshot>? SnapshotReady;
    internal static bool SuppressTimerForTesting { get; set; }

    private readonly IHardwareLifecycleManager _lifecycleManager;
    private volatile bool _isSuspended;
    private bool _disposed;

    // Constructor
    public HardwareMonitor(
        Config.AppSettings cfg,
        ISystemInfo? sysInfo = null,
        IPawnIoManager? pawnIoManager = null,
        IHardwareLifecycleManager? lifecycleManager = null)
    {
        _cfg = cfg;
        _sysInfo = sysInfo ?? new SystemInfoWrapper();
        _pawnIoManager = pawnIoManager ?? new PawnIoManagerWrapper();
        _lifecycleManager = lifecycleManager ?? HardwareLifecycleManager.Instance;

        _lifecycleManager.DisplayOrDriverChanged += OnDisplayOrDriverChanged;
        _lifecycleManager.PowerSuspended += OnPowerSuspended;
        _lifecycleManager.PowerResumed += OnPowerResumed;
        _lifecycleManager.NetworkTopologyChanged += OnNetworkTopologyChanged;
        _lifecycleManager.DevicePnPChanged += OnDevicePnPChanged;

        try
        {
            InitCpuFreq();
            InitIntelPwrGadget();
            InitGpu();
            var mc = _cfg.Monitoring;
            if (mc.EnableCpu)
            {
                InitPawnIo();
                Log.Debug($"Detected CPU {_sysInfo.GetCpuName()} ({_sysInfo.GetCpuCoreCount()} cores, {_sysInfo.GetCpuThreadCount()} threads, max clock {_sysInfo.GetCpuMaxSpeedMhz()} MHz)");
            }
            InitDiskType();

            if (mc.EnableDisk)
                InitDiskCounters();

            if (mc.EnableRam)
                InitRamLogging();

            InitBatteryLogging();

            int ms = Math.Max(100, _cfg.Monitoring.PollIntervalMs);
            if (!SuppressTimerForTesting)
                _timer = new Timer(_ => Poll(), null, ms, ms);

            // Prime NIC counters immediately so network shows real data from second poll
            if (_cfg.Monitoring.EnableNet)
                EnsureNicCounters();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize HardwareMonitor.");
        }
    }

    private void InitDiskCounters()
    {
        _diskRead = new PerformanceCounter(PhysicalDiskCategory, "Disk Read Bytes/sec", "_Total");
        _diskWrite = new PerformanceCounter(PhysicalDiskCategory, "Disk Write Bytes/sec", "_Total");
        _perDiskCounters = CreatePerDiskCounters();
        if (_perDiskCounters is not null)
        {
            for (int i = 0; i < _perDiskCounters.Count; i++)
                Metrics.RegisterDisk($"disk{i}", $"DISK{i}");
        }
    }



    private void InitRamLogging()
    {
        double installedGb = _sysInfo.GetInstalledRamGb();
        var memStatus = new NativeMethods.MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };
        if (NativeMethods.GlobalMemoryStatusEx(ref memStatus) && memStatus.ullTotalPhys > 0)
        {
            double usableGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            Log.Debug($"Installed system memory {installedGb:F1} GB ({usableGb:F1} GB usable)");
        }
        else
            Log.Debug($"Installed system memory {installedGb:F1} GB");
    }

    private void InitBatteryLogging()
    {
        if (_sysInfo.HasBattery())
            Log.Debug($"Battery detected {_sysInfo.GetBatteryManufacturer()} {_sysInfo.GetBatteryDeviceName()}, design capacity {_sysInfo.GetBatteryDesignCapacity()}");
        else
            Log.Debug("Battery not detected");
    }

    // Init helpers
    private void InitCpuFreq()
    {
        try
        {
            int count = Environment.ProcessorCount;
            int size = count * Marshal.SizeOf<NativeMethods.ProcessorPowerInformation>();
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                int ret = NativeMethods.CallNtPowerInformation(
                    NativeMethods.ProcessorInformation, IntPtr.Zero, 0, buf, size);
                if (ret == 0)
                {
                    uint maxMhz = 0;
                    for (int i = 0; i < count; i++)
                    {
                        var ppi = Marshal.PtrToStructure<NativeMethods.ProcessorPowerInformation>(
                            buf + i * Marshal.SizeOf<NativeMethods.ProcessorPowerInformation>());
                        if (ppi.MaxMhz > maxMhz) maxMhz = ppi.MaxMhz;
                    }
                    _maxCpuMhz = maxMhz;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to query processor frequency via power information");
        }
    }

    private void InitIntelPwrGadget()
    {
        try
        {
            _intelPwrGadget = new IntelPowerGadget();
            if (_intelPwrGadget.IsAvailable)
                Log.Info("Intel Power Gadget detected and active for CPU telemetry");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize Intel Power Gadget");
        }
    }

    private void InitPawnIo()
    {
        try
        {
            if (TryConnectPawnIo(out var device, isInitialProbe: true))
            {
                SetupCpuSensors(device!);
                return;
            }

            Log.Info("Hardware telemetry driver is not installed, attempting automatic setup");
            if (!_pawnIoManager.TryInstall() || !_pawnIoManager.StartDriverService())
            {
                Log.Error("Hardware driver is unavailable (manual setup available at https://pawnio.eu)");
                return;
            }

            if (TryConnectPawnIo(out device, isInitialProbe: false))
            {
                SetupCpuSensors(device!);
                return;
            }

            Log.Error("Hardware driver is unavailable (manual setup available at https://pawnio.eu)");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Hardware driver initialization failed");
        }
    }

    private bool TryConnectPawnIo(out IPawnIoTransport? device, bool isInitialProbe)
    {
        device = _pawnIoManager.TryCreate();
        if (device is not null) return true;

        if (isInitialProbe)
        {
            if (!_pawnIoManager.StartDriverService())
            {
                device = null;
                return false;
            }

            for (int i = 0; i < 5; i++)
            {
                Thread.Sleep(100);
                device = _pawnIoManager.TryCreate();
                if (device is not null) return true;
            }

            device = null;
            return false;
        }

        for (int retry = 0; retry < 10; retry++)
        {
            Thread.Sleep(500);
            device = _pawnIoManager.TryCreate();
            if (device is not null) return true;
        }

        device = null;
        return false;
    }

    private void SetupCpuSensors(IPawnIoTransport device)
    {
        _pawnIoDevice = device;
        SharedPawnIoDevice = device;

        if (_cpuVendor == IntelVendor)
        {
            InitIntelSensors(device);
        }
        else if (_cpuVendor == AmdVendor)
        {
            InitAmdSensors(device);
        }
        else
        {
            Log.Info($"Unknown processor vendor '{_cpuVendor}', skipping sensor module load");
            Log.Debug("Hardware telemetry driver device initialized successfully");
        }
    }

    private void InitIntelSensors(IPawnIoTransport device)
    {
        var intelBlob = _pawnIoManager.LoadResourceBytes("IntelMSR.bin");
        if (intelBlob is not null && _pawnIoManager.LoadModule(device, intelBlob))
        {
            _intelMsr = new IntelMsrReader(device);
            Log.Info(_intelMsr.IsAvailable ? "Sensor driver Intel MSR ready" : "Sensor driver Intel MSR unavailable");
        }
    }

    private void InitAmdSensors(IPawnIoTransport device)
    {
        var ryzenBlob = _pawnIoManager.LoadResourceBytes("RyzenSMU.bin");
        if (ryzenBlob is not null && _pawnIoManager.LoadModule(device, ryzenBlob))
        {
            _amdMsr = new AmdMsrReader(device);
            Log.Info(_amdMsr.IsAvailable ? "Sensor driver AMD Ryzen SMU ready" : "Sensor driver AMD Ryzen SMU unavailable");
        }

        var amdBlob = _pawnIoManager.LoadResourceBytes("AMDFamily17.bin");
        if (amdBlob is not null)
            _pawnIoManager.LoadModule(device, amdBlob);
    }

    private void InitNvGpu()
    {
        try { _nvGpu = new NvGpuMonitor(); }
        catch (Exception ex) { Log.Error(ex, "Failed to initialize NVIDIA graphics monitor"); _nvGpu = null; }
    }

    private void InitIntelGpu()
    {
        try
        {
            _intelGpu = new IntelGpuMonitor();
            if (_intelGpu.IsAvailable)
                Log.Debug($"Intel graphics adapter initialized ({_intelGpu.GpuName})");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to initialize Intel graphics monitor"); _intelGpu = null; }
    }

    private void InitWddmGpu()
    {
        try
        {
            _wddmGpu = new WddmGpuMonitor();
            if (_wddmGpu.IsAvailable)
                Log.Debug("Windows graphics display driver monitor ready");
            else
                Log.Warn("Windows graphics driver monitor unavailable, falling back to vendor monitor");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to initialize Windows graphics driver monitor"); _wddmGpu = null; }
    }

    private void InitAmdGpu()
    {
        try
        {
            _amdGpu = new AmdGpuMonitor();
            if (_amdGpu.IsAvailable)
                Log.Debug($"Detected AMD graphics adapter {_amdGpu.GpuName}");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to initialize AMD graphics monitor"); _amdGpu = null; }
    }

    private void InitGpu()
    {
        DetectCpuVendor();

        var displayGuid = NativeMethods.GUID_DISPLAY;
        IntPtr devInfoSet = NativeMethods.SetupDiGetClassDevs(ref displayGuid, IntPtr.Zero,
            IntPtr.Zero, NativeMethods.DIGCF_PRESENT);
        if (devInfoSet.ToInt64() == -1)
            return;

        try
        {
            EnumerateDisplayAdapters(devInfoSet);
            string adapterLog = FormatActiveGpuAdapterLog(_gpuName, _gpuVendor);
            Log.Info($"Active graphics adapter is {adapterLog}");
            InitWddmGpu();
            InitVendorGpuMonitor();
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(devInfoSet);
        }
    }

    private static string FormatActiveGpuAdapterLog(string gpuName, string gpuVendor)
    {
        if (!string.IsNullOrEmpty(gpuName))
            return $"{gpuName} ({gpuVendor})";

        return !string.IsNullOrEmpty(gpuVendor) ? gpuVendor : "None";
    }

    private void EnumerateDisplayAdapters(IntPtr devInfoSet)
    {
        NativeMethods.SpDevinfoData devInfo = new()
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.SpDevinfoData>()
        };
        uint i = 0;

        while (NativeMethods.SetupDiEnumDeviceInfo(devInfoSet, i++, ref devInfo))
        {
            string name = GetDevProp(devInfoSet, devInfo, NativeMethods.SPDRP_DEVICEDESC);
            if (string.IsNullOrEmpty(name)) continue;

            string hwId = GetDevProp(devInfoSet, devInfo, NativeMethods.SPDRP_HARDWAREID);
            string? vendor = DetectGpuVendor(hwId, name);
            if (vendor is null) continue;

            bool dedicated = IsDedicatedAdapter(vendor, name, devInfoSet, devInfo);
            Log.Debug($"Detected graphics adapter '{name}' (Vendor {vendor}, dedicated {dedicated})");

            if (TrySelectActiveGpuAdapter(vendor, name, dedicated))
                break;
        }
    }

    private bool TrySelectActiveGpuAdapter(string vendor, string name, bool dedicated)
    {
        if (vendor == NvidiaVendor)
        {
            _gpuVendor = NvidiaVendor;
            _gpuName = name;
            if (dedicated)
            {
                InitNvGpu();
                return true;
            }
        }
        else if (vendor == AmdVendor && dedicated && _gpuVendor != NvidiaVendor)
        {
            _gpuVendor = AmdVendor;
            _gpuName = name;
        }
        else if (vendor == IntelVendor && (dedicated || string.IsNullOrEmpty(_gpuVendor)) && _gpuVendor != NvidiaVendor && (_gpuVendor != AmdVendor || dedicated))
        {
            _gpuVendor = IntelVendor;
            _gpuName = name;
        }

        return false;
    }

    private static bool IsDedicatedAdapter(string? vendor, string name, IntPtr devInfoSet, NativeMethods.SpDevinfoData devInfo)
    {
        return vendor == NvidiaVendor
            || (vendor == IntelVendor && name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
            || IsDiscreteBus(devInfoSet, devInfo)
            || (vendor == AmdVendor && System.Text.RegularExpressions.Regex.IsMatch(name,
                @"\b(RX|PRO|VII|WX|W[36]\d{3})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)));
    }

    private void InitVendorGpuMonitor()
    {
        if (_gpuVendor == NvidiaVendor) { return; }
        if (_gpuVendor == AmdVendor) { InitAmdGpu(); }
        else if (_gpuVendor == IntelVendor) { InitIntelGpu(); }
        else Log.Debug("No compatible dedicated GPU monitor found");
    }

    private static bool IsDiscreteBus(IntPtr devInfoSet, NativeMethods.SpDevinfoData devInfo)
    {
        string loc = GetDevProp(devInfoSet, devInfo, NativeMethods.SPDRP_LOCATION_INFORMATION);
        if (string.IsNullOrEmpty(loc)) return false;
        var m = System.Text.RegularExpressions.Regex.Match(loc, @"(?i)PCI\s*bus\s*(\d+)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(250));
        if (!m.Success) return false;
        return int.TryParse(m.Groups[1].Value, out int bus) && bus > 0;
    }

    private static string GetDevProp(IntPtr devInfoSet, NativeMethods.SpDevinfoData devInfo, uint property)
    {
        IntPtr buf = Marshal.AllocHGlobal(2048);
        try
        {
            if (NativeMethods.SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfo, property,
                    out _, buf, 2048, out _))
            {
                return Marshal.PtrToStringUni(buf) ?? "";
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return "";
    }

    private static string? DetectGpuVendor(string hwId, string name)
    {
        if (hwId.Contains("VEN_10DE") || name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            return "NVIDIA";
        if (hwId.Contains("VEN_1002") || name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            return "AMD";
        if (hwId.Contains("VEN_8086") || name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            return "Intel";
        return null;
    }

    private void DetectCpuVendor()
    {
        _cpuVendor = _sysInfo.GetCpuVendor();
    }

    private void InitDiskType()
    {
        try
        {
            var diskInfos = DiskQuery.QueryAllDrives();
            _primaryDiskType = diskInfos.Count > 0 ? (diskInfos[0].DisplayType ?? diskInfos[0].BusType ?? "DISK") : "DISK";
            _diskBusTypes = diskInfos.Select(d => d.DisplayType ?? d.BusType ?? "DISK").ToList();
            for (int i = 0; i < diskInfos.Count; i++)
                Metrics.RegisterDisk($"disk{i}", $"DISK{i}");
            Log.Debug($"Detected {diskInfos.Count} storage device(s), primary type {_primaryDiskType}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to query storage drive information");
            _primaryDiskType = "DISK";
            _diskBusTypes = [];
        }
    }

    private static List<(PerformanceCounter read, PerformanceCounter write)>? CreatePerDiskCounters()
    {
        try
        {
            var cat = new PerformanceCounterCategory(PhysicalDiskCategory);
            string[] instances = cat.GetInstanceNames();
            var result = new List<(PerformanceCounter read, PerformanceCounter write)>();
            var seen = new HashSet<string>();

            foreach (var inst in instances)
            {
                if (inst == "_Total") continue;
                int end = 0;
                while (end < inst.Length && char.IsDigit(inst[end])) end++;
                if (end == 0) continue;
                string diskIndex = inst[..end];
                if (!seen.Add(diskIndex)) continue;

                result.Add((
                    new PerformanceCounter(PhysicalDiskCategory, "Disk Read Bytes/sec", inst),
                    new PerformanceCounter(PhysicalDiskCategory, "Disk Write Bytes/sec", inst)
                ));
            }

            result.Sort((a, b) =>
            {
                int ai = int.TryParse(ExtractDiskIndex(a.read.InstanceName), out var ia) ? ia : 0;
                int bi = int.TryParse(ExtractDiskIndex(b.read.InstanceName), out var ib) ? ib : 0;
                return ai.CompareTo(bi);
            });

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create per-disk PerformanceCounters");
            return null;
        }
    }

    private static string ExtractDiskIndex(string instanceName)
    {
        int end = 0;
        while (end < instanceName.Length && char.IsDigit(instanceName[end])) end++;
        return end > 0 ? instanceName[..end] : "";
    }

    // Poll
    private void Poll()
    {
        if (_isSuspended || _disposed) return;
        if (Interlocked.CompareExchange(ref _isPolling, 1, 0) != 0)
            return;
        try { SnapshotReady?.Invoke(BuildSnapshot()); }
        catch (Exception ex) { Log.Error(ex, "Transient poll error."); }
        finally { Volatile.Write(ref _isPolling, 0); }
    }

    private TelemetrySnapshot BuildSnapshot()
    {
        var mc = _cfg.Monitoring;

        var (cpuPct, cpuTemp, cpuFreq, cpuPower) = CollectCpuMetrics(mc);
        float ramPct = 0, ramUsedGb = 0, ramTotalGb = 0;
        if (mc.EnableRam) ReadMemory(out ramPct, out ramUsedGb, out ramTotalGb);

        var (gpuPct, gpuTemp, gpuVramUsed, gpuClock, gpuPower) = CollectGpuMetrics(mc);
        var (netDown, netUp, nicName) = CollectNetMetrics(mc);
        var (diskRead, diskWrite, disks) = CollectDiskMetrics(mc);
        var (batteryPct, batteryCharging, batteryOnAC, batteryRate) = CollectBatteryMetrics(mc);

        LogTickMetrics(mc,
            (cpuPct, cpuTemp, cpuFreq, cpuPower),
            (ramPct, ramUsedGb, ramTotalGb),
            (diskRead, diskWrite, disks.Count),
            (gpuPct, gpuTemp, gpuPower, gpuClock),
            (batteryPct, batteryCharging, batteryOnAC, batteryRate));

        long netTotal = 0;
        if (mc.EnableNet)
        {
            var (d, u) = TrafficHistoryStore.GetToday();
            netTotal = d + u;
        }

        return new TelemetrySnapshot
        {
            PrimaryDiskType = _primaryDiskType,
            Disks = disks,
            Timestamp = DateTime.UtcNow,
            CpuUsagePct = cpuPct,
            CpuTempPackageC = cpuTemp,
            CpuFreqGhz = cpuFreq,
            CpuPackagePowerW = cpuPower,
            RamUsagePct = ramPct,
            RamUsedGb = ramUsedGb,
            GpuUsagePct = gpuPct,
            GpuTempC = gpuTemp,
            GpuVramUsedMb = gpuVramUsed,
            GpuFreqMHz = gpuClock,
            GpuPowerW = gpuPower,
            NetDownBps = netDown,
            NetUpBps = netUp,
            NetInterfaceName = nicName,
            NetTotalBytes = netTotal,
            DiskReadMbps = (float)diskRead,
            DiskWriteMbps = (float)diskWrite,
            BatteryPct = mc.EnableBattery ? batteryPct : 0,
            BatteryIsCharging = mc.EnableBattery && batteryCharging,
            IsOnACPower = !mc.EnableBattery || batteryOnAC,
            BatteryChargeRateW = mc.EnableBattery ? batteryRate : 0,
        };
    }

    private (float cpuPct, float cpuTemp, float cpuFreq, float cpuPower) CollectCpuMetrics(Config.MonitoringConfig mc)
    {
        if (!mc.EnableCpu) return (0, 0, 0, 0);
        return (ReadCpuPct(), ReadCpuTemp(), ReadCpuFreq(), ReadCpuPowerRapl());
    }

    private (float gpuPct, float gpuTemp, float gpuVramUsed, float gpuClock, float gpuPower) CollectGpuMetrics(Config.MonitoringConfig mc)
    {
        if (!mc.EnableGpu) return (0, 0, 0, 0, 0);
        NvGpuMonitor.NvGpuData? gpuData = _nvGpu?.Query();
        return (
            ReadGpuPct(gpuData),
            ReadGpuTemp(gpuData),
            ReadGpuVramUsedMb(gpuData),
            ReadGpuFreqMHz(gpuData),
            ReadGpuPowerW(gpuData)
        );
    }

    private (double netDown, double netUp, string nicName) CollectNetMetrics(Config.MonitoringConfig mc)
    {
        if (!mc.EnableNet) return (0, 0, string.Empty);
        EnsureNicCounters();
        var (down, up) = ReadNetBytes();
        return (down, up, _nicInstanceName ?? string.Empty);
    }

    private (double diskRead, double diskWrite, List<DiskSnapshot> disks) CollectDiskMetrics(Config.MonitoringConfig mc)
    {
        if (!mc.EnableDisk) return (0, 0, []);

        double diskRead = 0, diskWrite = 0;
        var disks = new List<DiskSnapshot>();

        if (_perDiskCounters is not null)
        {
            for (int i = 0; i < _perDiskCounters.Count; i++)
            {
                var (read, write) = _perDiskCounters[i];
                float r = ReadPdh(read) / (1000f * 1000f);
                float w = ReadPdh(write) / (1000f * 1000f);
                diskRead += r;
                diskWrite += w;
                string busType = i < _diskBusTypes.Count ? _diskBusTypes[i] : _primaryDiskType;
                disks.Add(new DiskSnapshot
                {
                    Id = $"disk{i}",
                    Label = $"DISK{i}",
                    BusType = busType,
                    ReadMBps = r,
                    WriteMBps = w,
                });
            }
        }
        else
        {
            diskRead = ReadPdh(_diskRead) / (1000f * 1000f);
            diskWrite = ReadPdh(_diskWrite) / (1000f * 1000f);
        }

        return (diskRead, diskWrite, disks);
    }

    private (float batteryPct, bool batteryCharging, bool batteryOnAC, float batteryRate) CollectBatteryMetrics(Config.MonitoringConfig mc)
    {
        if (!mc.EnableBattery) return (0, false, true, 0);
        return ReadBattery();
    }

    private void LogTickMetrics(
        Config.MonitoringConfig mc,
        (float pct, float temp, float freq, float power) cpu,
        (float pct, float usedGb, float totalGb) ram,
        (double read, double write, int diskCount) disk,
        (float pct, float temp, float power, float clock) gpu,
        (float pct, bool isCharging, bool isOnAC, float rate) battery)
    {
        if (mc.EnableCpu && ShouldLogTick("cpu", 2)) Log.InfoMetric($"CPU {cpu.pct:F0}%, {cpu.temp:F0}°C, frequency {cpu.freq:F2}GHz, power {cpu.power:F1}W");
        if (mc.EnableRam && ShouldLogTick("ram", 2)) Log.InfoMetric($"RAM {ram.pct:F1}% used ({ram.usedGb:F1}/{ram.totalGb:F1} GB)");
        if (mc.EnableDisk && ShouldLogTick("disk", 2)) Log.InfoMetric($"Disk read {disk.read:F2} MB/s, write {disk.write:F2} MB/s across {disk.diskCount} drive(s)");
        if (mc.EnableGpu && ShouldLogTick("gpu", 2)) Log.InfoMetric($"GPU {gpu.pct:F0}%, {gpu.temp:F0}°C, power {gpu.power:F1}W, clock {gpu.clock:F0}MHz");
        if (mc.EnableBattery && battery.pct > 0 && ShouldLogTick("battery", 2))
        {
            string batteryState = GetBatteryStateDescription(battery.isCharging, battery.isOnAC);
            Log.InfoMetric($"Battery {battery.pct:F0}%, state {batteryState}, rate {battery.rate:F1}W");
        }
    }

    private static string GetBatteryStateDescription(bool batteryCharging, bool batteryOnAC)
    {
        if (batteryCharging) return "charging";
        if (batteryOnAC) return "charged";
        return "discharging";
    }

    // CPU % via GetSystemTimes
    private float ReadCpuPct()
    {
        try
        {
            if (!NativeMethods.GetSystemTimes(out long idle, out long kernel, out long user))
            {
                LogMetricErrorOnce("cpu", "cpu_pct_gst_failed", "Failed to retrieve system processor times via GetSystemTimes");
                return 0f;
            }

            if (!_hasPrevTimes)
            {
                _prevIdle = idle; _prevKernel = kernel; _prevUser = user;
                _hasPrevTimes = true;
                if (ShouldLogTick("cpu", 2)) Log.Debug("CPU load counter primed (GetSystemTimes)");
                return 0f;
            }

            long idleDelta = idle - _prevIdle;
            long kernelDelta = kernel - _prevKernel;
            long userDelta = user - _prevUser;

            long totalDelta = kernelDelta + userDelta;
            if (totalDelta <= 0)
            {
                if (ShouldLogTick("cpu", 2))
                    Log.Debug("CPU load calculation encountered zero delta; retaining previous reading");
                return _prevCpuPct;
            }

            _prevIdle = idle; _prevKernel = kernel; _prevUser = user;

            float gst = Math.Clamp((float)(totalDelta - idleDelta) * 100f / totalDelta, 0f, 100f);
            _prevCpuPct = gst;
            return gst;
        }
        catch (Exception ex)
        {
            LogMetricErrorOnce("cpu", "cpu_pct_exception", $"Failed to calculate CPU load: {ex.Message}");
            return 0f;
        }
    }

    // CPU Clock
    private float ReadCpuFreq()
    {
        // APERF/MPERF via PawnIO - matches Task Manager "Speed" column
        float msr = ReadCpuFreqMsr();
        if (msr > 0) return msr;

        // Intel: use Intel Power Gadget (signed by Intel)
        if (_intelPwrGadget is not null && _intelPwrGadget.IsAvailable)
        {
            double mhz = _intelPwrGadget.GetFrequency();
            if (!double.IsNaN(mhz) && mhz > 0)
                return (float)(mhz / 1000.0);
        }

        return 0f;
    }

    // CPU Clock via APERF/MPERF MSRs through PawnIO
    private const uint MSR_IA32_APERF = 0xE8;
    private const uint MSR_IA32_MPERF = 0xE7;
    private const uint MSR_RAPL_POWER_UNIT = 0x606;
    private const uint MSR_PKG_ENERGY_STATUS = 0x611;
    private static readonly ulong[] MsrAperfInput = [MSR_IA32_APERF];
    private static readonly ulong[] MsrMperfInput = [MSR_IA32_MPERF];
    private static readonly ulong[] MsrRaplPowerUnitInput = [MSR_RAPL_POWER_UNIT];
    private static readonly ulong[] MsrPkgEnergyStatusInput = [MSR_PKG_ENERGY_STATUS];
    private static readonly ulong[] MsrPkgPowerSkuInput = [MSR_PKG_POWER_SKU];

    private float ReadCpuFreqMsr()
    {
        if (_pawnIoDevice is null || _maxCpuMhz <= 0) return 0f;

        try
        {
            var aperfResult = _pawnIoDevice.Execute(IoctlReadMsrCommand, MsrAperfInput, 1);
            if (aperfResult.Length < 1 || aperfResult[0] == 0)
                return 0f;
            ulong aperf = aperfResult[0];

            var mperfResult = _pawnIoDevice.Execute(IoctlReadMsrCommand, MsrMperfInput, 1);
            if (mperfResult.Length < 1 || mperfResult[0] == 0)
                return 0f;
            ulong mperf = mperfResult[0];

            if (!_hasMsrFreqPrimed)
            {
                _prevAperf = aperf;
                _prevMperf = mperf;
                _hasMsrFreqPrimed = true;
                if (ShouldLogTick("cpu", 2)) Log.Debug("CPU frequency counters primed (APERF/MPERF)");
                return 0f;
            }

            ulong dAperf = aperf - _prevAperf;
            ulong dMperf = mperf - _prevMperf;
            _prevAperf = aperf;
            _prevMperf = mperf;

            if (dMperf == 0) return 0f;

            float ratio = (float)dAperf / dMperf;
            float freq = ratio * _maxCpuMhz / 1000f;
            return Math.Clamp(freq, 0f, _maxCpuMhz / 500f); // cap at 2× base
        }
        catch (Exception ex)
        {
            LogMetricErrorOnce("cpu", "cpu_freq_exception", $"Failed to calculate CPU frequency via APERF/MPERF: {ex.Message}");
            return 0f;
        }
    }

    // GPU
    private float ReadGpuPct(NvGpuMonitor.NvGpuData? gpu = null)
    {
        try
        {
            if (_cfg.Monitoring.GpuUsageSource == "wddm" &&
                _wddmGpu is { IsAvailable: true })
            {
                if (ShouldLogTick("gpu", 10)) Log.Debug("GPU utilization using WDDM source");
                return _wddmGpu.GetUsagePct();
            }

            if (TryReadDriverGpuPct(gpu, out float driverPct))
                return driverPct;

            // Fallback to WDDM when driver source is unavailable or sleeping
            if (_wddmGpu is { IsAvailable: true })
            {
                if (ShouldLogTick("gpu", 10)) Log.Debug("GPU vendor driver unavailable, falling back to WDDM");
                return _wddmGpu.GetUsagePct();
            }
        }
        catch (Exception ex)
        {
            LogMetricErrorOnce("gpu", "gpu_pct_exception", $"Failed to read GPU utilization: {ex.Message}");
        }
        if (ShouldLogTick("gpu", 10)) Log.Debug("GPU utilization source unavailable, returning 0");
        return 0f;
    }

    private bool TryReadDriverGpuPct(NvGpuMonitor.NvGpuData? gpu, out float usagePct)
    {
        usagePct = 0f;
        if (gpu.HasValue && gpu.Value.UsagePct >= 0)
        {
            if (ShouldLogTick("gpu", 10)) Log.Debug("GPU utilization using NVML source");
            usagePct = gpu.Value.UsagePct;
            return true;
        }

        if (_nvGpu is not null)
        {
            if (ShouldLogTick("gpu", 10)) Log.Debug("GPU utilization using NVML source");
            gpu = _nvGpu.Query();
            if (gpu.HasValue && gpu.Value.UsagePct >= 0)
            {
                usagePct = gpu.Value.UsagePct;
                return true;
            }
        }

        if (_amdGpu is { IsAvailable: true })
        {
            if (ShouldLogTick("gpu", 10)) Log.Debug("GPU utilization using ADL source");
            usagePct = _amdGpu.GetUsagePct();
            return true;
        }

        return false;
    }

    private float ReadGpuTemp(NvGpuMonitor.NvGpuData? gpu = null)
    {
        try
        {
            if (gpu.HasValue && gpu.Value.TempC >= 0) return gpu.Value.TempC;
            if (_nvGpu is not null)
            {
                gpu = _nvGpu.Query();
                if (gpu.HasValue && gpu.Value.TempC >= 0) return gpu.Value.TempC;
            }
            if (_amdGpu is not null && _amdGpu.IsAvailable)
                return _amdGpu.GetTempC();
            if (_intelGpu is not null && _intelGpu.IsAvailable)
                return _intelGpu.GetTempC();
        }
        catch (Exception ex)
        {
            LogMetricErrorOnce("gpu", "gpu_temp_exception", $"Failed to read GPU temperature: {ex.Message}");
        }
        return 0f;
    }

    private float ReadGpuVramUsedMb(NvGpuMonitor.NvGpuData? gpu = null)
    {
        try
        {
            if (gpu.HasValue) return gpu.Value.VramUsedMb;
            if (_nvGpu is not null)
            {
                gpu = _nvGpu.Query();
                if (gpu.HasValue) return gpu.Value.VramUsedMb;
            }
            if (_amdGpu is not null && _amdGpu.IsAvailable)
                return _amdGpu.GetVram().usedMb;
            if (_intelGpu is not null && _intelGpu.IsAvailable)
                return _intelGpu.GetVram().usedMb;
        }
        catch (Exception ex)
        {
            LogMetricErrorOnce("gpu", "gpu_vram_exception", $"Failed to read GPU VRAM usage: {ex.Message}");
        }
        return 0f;
    }

    private float ReadGpuFreqMHz(NvGpuMonitor.NvGpuData? gpu = null)
    {
        try
        {
            if (gpu.HasValue && gpu.Value.CoreClockMHz > 0) return gpu.Value.CoreClockMHz;
            if (_nvGpu is not null)
            {
                gpu = _nvGpu.Query();
                if (gpu.HasValue && gpu.Value.CoreClockMHz > 0) return gpu.Value.CoreClockMHz;
            }
            if (_amdGpu is not null && _amdGpu.IsAvailable)
                return _amdGpu.GetClockMHz();
            if (_intelGpu is not null && _intelGpu.IsAvailable)
                return _intelGpu.GetClockMHz();
        }
        catch (Exception ex)
        {
            LogMetricErrorOnce("gpu", "gpu_clock_exception", $"Failed to read GPU clock: {ex.Message}");
        }
        return 0f;
    }

    private float ReadGpuPowerW(NvGpuMonitor.NvGpuData? gpu = null)
    {
        try
        {
            if (gpu.HasValue && gpu.Value.PowerDrawW > 0) return gpu.Value.PowerDrawW;
            if (_nvGpu is not null)
            {
                gpu = _nvGpu.Query();
                if (gpu.HasValue && gpu.Value.PowerDrawW > 0) return gpu.Value.PowerDrawW;
            }
            if (_amdGpu is not null && _amdGpu.IsAvailable)
            {
                float w = _amdGpu.GetPowerDrawW();
                if (w > 0) return w;
            }
        }
        catch (Exception ex)
        {
            LogMetricErrorOnce("gpu", "gpu_power_exception", $"Failed to read GPU power draw: {ex.Message}");
        }
        return 0f;
    }

    // RAPL CPU power (MSR_PKG_ENERGY_STATUS delta)
    private void ReadRaplPowerUnit()
    {
        if (_pawnIoDevice is null) return;
        try
        {
            var result = _pawnIoDevice.Execute(IoctlReadMsrCommand, MsrRaplPowerUnitInput, 1);
            if (result.Length >= 1)
            {
                int pUnits = (int)(result[0] & 0xF);
                _powerUnitDivisor = 1.0 / (1 << pUnits);
                int eUnits = (int)((result[0] >> 8) & 0x1F);
                _energyUnit = 1.0 / (1L << eUnits);
                Log.Debug($"RAPL power unit = {_powerUnitDivisor}, energy unit = {_energyUnit * 1e6:F2} µJ/LSB (MSR 0x{result[0]:X})");
            }
        }
        catch (Exception ex)
        {
            LogMetricErrorOnce("cpu", "rapl_power_unit_failed", $"Failed to read RAPL power unit: {ex.Message}");
            _energyUnit = 0;
            _powerUnitDivisor = 1;
        }
    }

    private const uint MSR_PKG_POWER_SKU = 0x614;
    private uint? _tdpWatts;

    private uint? ReadCpuTdp()
    {
        if (_pawnIoDevice is null) return null;
        try
        {
            var result = _pawnIoDevice.Execute(IoctlReadMsrCommand, MsrRaplPowerUnitInput, 1);
            if (result.Length >= 1)
            {
                int pUnits = (int)(result[0] & 0xF);
                _powerUnitDivisor = 1.0 / (1 << pUnits);
            }
        }
        catch (Exception ex) { LogMetricErrorOnce("cpu", "cpu_tdp_rapl_unit", $"Failed to read RAPL power unit for TDP calculation: {ex.Message}"); }
        try
        {
            var result = _pawnIoDevice.Execute(IoctlReadMsrCommand, MsrPkgPowerSkuInput, 1);
            if (result.Length >= 1)
            {
                double tdpRaw = result[0] & 0x7FFF;
                if (tdpRaw > 0)
                {
                    uint tdp = (uint)Math.Round(tdpRaw * _powerUnitDivisor);
                    if (ShouldLogTick("cpu", 2)) Log.Debug($"RAPL TDP configured: {tdp} W (MSR 0x{result[0]:X})");
                    return tdp;
                }
            }
        }
        catch (Exception ex) { LogMetricErrorOnce("cpu", "cpu_tdp_pkg_sku", $"Failed to read package power SKU for TDP calculation: {ex.Message}"); }
        return null;
    }

    /// <summary>Cached CPU TDP in watts, read once from the RAPL MSR (may be null).</summary>
    public static int? CachedTdpWatts { get; private set; }

    private float ReadCpuPowerRapl()
    {
        if (_pawnIoDevice is null)
            return 0f;

        if (_energyUnit <= 0)
            ReadRaplPowerUnit();
        if (_energyUnit <= 0)
            return 0f;

        if (_tdpWatts is null)
        {
            var tdp = ReadCpuTdp();
            if (tdp.HasValue)
            {
                _tdpWatts = tdp.Value;
                CachedTdpWatts = (int?)_tdpWatts;
            }
        }

        try
        {
            var pkgResult = _pawnIoDevice.Execute(IoctlReadMsrCommand, MsrPkgEnergyStatusInput, 1);
            if (pkgResult.Length < 1)
                return 0f;
            ulong energy = pkgResult[0];

            var now = DateTime.UtcNow;
            if (!_hasEnergyPrimed)
            {
                _prevPkgEnergy = energy;
                _prevEnergyTime = now;
                _hasEnergyPrimed = true;
                if (ShouldLogTick("cpu", 2)) Log.Debug("RAPL energy counter primed");
                return 0f;
            }

            ulong dEnergy = energy - _prevPkgEnergy;
            double dt = (now - _prevEnergyTime).TotalSeconds;
            _prevPkgEnergy = energy;
            _prevEnergyTime = now;

            if (dt <= 0) return 0f;

            double joules = dEnergy * _energyUnit;
            float watts = (float)(joules / dt);
            return Math.Clamp(watts, 0f, 500f);
        }
        catch (Exception ex)
        {
            LogMetricErrorOnce("cpu", "rapl_pkg_energy_failed", $"Failed to read RAPL package energy status: {ex.Message}");
            return 0f;
        }
    }

    // CPU temperature
    private float ReadCpuTemp()
    {
        if (_cpuVendor == IntelVendor)
            return ReadIntelCpuTemp();

        if (_cpuVendor == AmdVendor)
            return ReadAmdCpuTemp();

        if (ShouldLogTick("cpu", 1) && Interlocked.CompareExchange(ref _onceCpuTempUnknownVendor, 1, 0) == 0)
            Log.ErrorMetric($"Unknown CPU vendor '{_cpuVendor}' for temperature monitoring");
        return 0f;
    }

    private float ReadIntelCpuTemp()
    {
        if (_intelMsr is null)
        {
            if (ShouldLogTick("cpu", 1) && Interlocked.CompareExchange(ref _onceCpuTempIntelMsrNull, 1, 0) == 0)
                Log.ErrorMetric("Intel MSR temperature reader is not initialized");
            return 0f;
        }
        if (!_intelMsr.IsAvailable)
        {
            if (ShouldLogTick("cpu", 1) && Interlocked.CompareExchange(ref _onceCpuTempIntelMsrNotAvail, 1, 0) == 0)
                Log.ErrorMetric("Intel MSR temperature reader is not available");
            return 0f;
        }
        float temp = _intelMsr.ReadTemperature();
        if (temp > 0) return temp;
        if (ShouldLogTick("cpu", 1) && Interlocked.CompareExchange(ref _onceCpuTempIntelMsrZero, 1, 0) == 0)
            Log.ErrorMetric("Intel MSR temperature reader returned zero");
        return 0f;
    }

    private float ReadAmdCpuTemp()
    {
        if (_amdMsr is null)
        {
            if (ShouldLogTick("cpu", 1) && Interlocked.CompareExchange(ref _onceCpuTempAmdMsrNull, 1, 0) == 0)
                Log.ErrorMetric("AMD Ryzen SMU temperature reader is not initialized");
            return 0f;
        }
        if (!_amdMsr.IsAvailable)
        {
            if (ShouldLogTick("cpu", 1) && Interlocked.CompareExchange(ref _onceCpuTempAmdMsrNotAvail, 1, 0) == 0)
                Log.ErrorMetric("AMD Ryzen SMU temperature reader is not available");
            return 0f;
        }
        float temp = _amdMsr.ReadTemperature();
        if (temp > 0) return temp;
        if (ShouldLogTick("cpu", 1) && Interlocked.CompareExchange(ref _onceCpuTempAmdMsrZero, 1, 0) == 0)
            Log.ErrorMetric("AMD Ryzen SMU temperature reader returned zero");
        return 0f;
    }

    private bool _wasOfflineLogged;

    // Network via NetworkInterface.GetIPv4Statistics delta
    private void EnsureNicCounters()
    {
        string pref = _cfg.Monitoring.PreferredNic;
        if (string.IsNullOrEmpty(pref)) pref = "auto";

        if (pref == _lastPreferredNic && _nicInterface is { OperationalStatus: OperationalStatus.Up })
            return;
        _lastPreferredNic = pref;

        _nicInterface = null;
        _nicPrimed = false;

        try
        {
            var (selected, _) = SelectActiveNic(pref);
            if (selected is not null)
            {
                _nicInterface = selected;
                _nicInstanceName = selected.Name;
                _wasOfflineLogged = false;
                if (!string.Equals(_lastBoundNicId, selected.Id, StringComparison.OrdinalIgnoreCase))
                {
                    _lastBoundNicId = selected.Id;
                    Log.Info($"Connected to network adapter '{selected.Name}' ({selected.Description})");
                }
                PrimeNicCounters(selected);
            }
            else
            {
                _nicInterface = null;
                _nicInstanceName = string.Empty;
                _lastBoundNicId = null;
                if (!_wasOfflineLogged)
                {
                    Log.Info("Offline / no active network adapter connected");
                    _wasOfflineLogged = true;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize network interface");
        }
    }

    private static (NetworkInterface? selected, bool hasGateway) SelectActiveNic(string pref)
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
            return (null, false);

        var allNics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                     && (n.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                      || n.NetworkInterfaceType == (NetworkInterfaceType)62
                      || n.NetworkInterfaceType == (NetworkInterfaceType)69
                      || n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                      || n.NetworkInterfaceType == (NetworkInterfaceType)117)
                     && !IsVirtualAdapter(n))
            .ToList();

        var withGateway = allNics
            .Where(HasActiveGateway)
            .ToList();

        NetworkInterface? selected = null;
        if (pref != "auto")
        {
            selected = allNics.FirstOrDefault(n =>
                n.Name.Equals(pref, StringComparison.OrdinalIgnoreCase)
                || n.Description.Contains(pref, StringComparison.OrdinalIgnoreCase)
                || pref.Contains(n.Name, StringComparison.OrdinalIgnoreCase));
        }
        selected ??= withGateway.FirstOrDefault() ?? allNics.FirstOrDefault();
        return (selected, withGateway.Count > 0);
    }

    private static bool HasActiveGateway(NetworkInterface n)
    {
        try
        {
            var gw = n.GetIPProperties()?.GatewayAddresses;
            return gw is not null && gw.Count > 0
                && gw.Any(g => g.Address is not null && !g.Address.ToString().StartsWith("0."));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check network gateway configuration");
            return false;
        }
    }

    private void PrimeNicCounters(NetworkInterface selected)
    {
        try
        {
            var stats = selected.GetIPv4Statistics();
            _prevRx = stats.BytesReceived;
            _prevTx = stats.BytesSent;
            _prevNicTime = DateTime.UtcNow;
            _nicPrimed = true;
            Log.Debug($"Network counters primed: RX {_prevRx} bytes, TX {_prevTx} bytes");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to prime network adapter counters");
            _nicPrimed = false;
        }
    }

    public static bool IsVirtualAdapter(NetworkInterface n)
    {
        string desc = n.Description ?? string.Empty;
        string name = n.Name ?? string.Empty;
        return desc.Contains("virtual", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("hyper-v", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("vethernet", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("vmware", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("virtualbox", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("wsl", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("tap-windows", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("tunnel", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("loopback", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("wan miniport", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("wfp native mac", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("filter-", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("WSL", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Local Area Connection*", StringComparison.OrdinalIgnoreCase);
    }

    private (double downBps, double upBps) ReadNetBytes()
    {
        if (_nicInterface is null)
        {
            return (0, 0);
        }

        try
        {
            var stats = _nicInterface.GetIPv4Statistics();
            return CalculateNetBytesDelta(stats.BytesReceived, stats.BytesSent);
        }
        catch (Exception ex)
        {
            LogMetricErrorOnce("net", "net_read_exception", $"Failed to read network adapter statistics: {ex.Message}");
            return (0, 0);
        }
    }

    private (double downBps, double upBps) CalculateNetBytesDelta(long rx, long tx)
    {
        if (!_nicPrimed)
        {
            _prevRx = rx;
            _prevTx = tx;
            _prevNicTime = DateTime.UtcNow;
            _nicPrimed = true;
            if (ShouldLogTick("net", 2)) Log.Debug($"Network adapter '{_nicInterface!.Name}' primed: RX {rx} bytes, TX {tx} bytes");
            return (0, 0);
        }

        var now = DateTime.UtcNow;
        double secs = (now - _prevNicTime).TotalSeconds;
        if (secs <= 0) return (0, 0);

        double downBps = Math.Max(0, (rx - _prevRx) / secs);
        double upBps = Math.Max(0, (tx - _prevTx) / secs);

        _prevRx = rx;
        _prevTx = tx;
        _prevNicTime = now;

        if (ShouldLogTick("net", 2)) Log.InfoMetric($"Network '{_nicInterface!.Name}' throughput {downBps:F1} B/s down, {upBps:F1} B/s up");
        return (downBps, upBps);
    }

    // Battery
    private (float pct, bool isCharging, bool isOnAC, float rateW) ReadBattery()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var sps))
            return (0f, false, false, 0f);

        // BatteryFlag: 128 = no system battery
        if (sps.BatteryFlag == 128)
        {
            _hasBatteryPrimed = false;
            return (0f, false, false, 0f);
        }

        float pct = sps.BatteryLifePercent;
        if (pct > 100) return (0f, false, false, 0f); // 255 = unknown → no battery info

        // BatteryFlag: bit 3 (0x08) = charging.
        bool isOnAC = sps.ACLineStatus == 1;
        bool isCharging = isOnAC && (sps.BatteryFlag & 8) == 8;

        float rateW = QueryBatteryRateW();
        return (pct, isCharging, isOnAC, rateW);
    }

    private float QueryBatteryRateW()
    {
        try
        {
            _batterySearcher ??= new ManagementObjectSearcher(
                new ManagementScope(@"\\.\ROOT\WMI"),
                new ObjectQuery("SELECT RemainingCapacity, ChargeRate, DischargeRate FROM BatteryStatus"));

            using var coll = _batterySearcher.Get();
            var mo = coll.Cast<ManagementBaseObject>().FirstOrDefault();
            if (mo is null) return 0f;

            // Direct charge/discharge rate (milliwatts) - same source as HWInfo
            object? cr = mo["ChargeRate"];
            object? dr = mo["DischargeRate"];
            if (cr is not null && Convert.ToInt64(cr) > 0)
                return Convert.ToInt64(cr) / 1000f;
            if (dr is not null && Convert.ToInt64(dr) > 0)
                return -(Convert.ToInt64(dr) / 1000f);

            // Fall back to capacity delta if no direct rate
            return ComputeBatteryRateFromCapacityDelta(mo["RemainingCapacity"]);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to read battery status");
            return 0f;
        }
    }

    private float ComputeBatteryRateFromCapacityDelta(object? cap)
    {
        long remainingMwH = cap is not null ? Convert.ToInt64(cap) : 0;
        if (remainingMwH <= 0) return 0f;

        var now = DateTime.UtcNow;
        float rateW = 0f;
        if (_hasBatteryPrimed)
        {
            double dtHours = (now - _lastBatteryPollTime).TotalHours;
            if (dtHours > 0)
            {
                long deltaMwH = remainingMwH - _lastBatteryMwH;
                float raw = (float)(deltaMwH / dtHours / 1000.0);
                rateW = Math.Clamp(raw, -300f, 300f);
            }
        }
        _lastBatteryMwH = remainingMwH;
        _lastBatteryPollTime = now;
        _hasBatteryPrimed = true;
        return rateW;
    }

    // RAM
    private void ReadMemory(out float usagePct, out float usedGb, out float totalGb)
    {
        try
        {
            var ms = new NativeMethods.MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };
            if (NativeMethods.GlobalMemoryStatusEx(ref ms) && ms.ullTotalPhys > 0)
            {
                ulong used = ms.ullTotalPhys - ms.ullAvailPhys;
                usagePct = (float)used / ms.ullTotalPhys * 100f;
                usedGb = (float)used / (1024f * 1024f * 1024f);
                totalGb = (float)ms.ullTotalPhys / (1024f * 1024f * 1024f);
            }
            else { usagePct = 0f; usedGb = 0f; totalGb = 0f; }
        }
        catch (Exception ex)
        {
            LogMetricErrorOnce("ram", "read_memory_failed", $"Failed to query system memory metrics: {ex.Message}");
            usagePct = 0f; usedGb = 0f; totalGb = 0f;
        }
    }

    // Public control
    /// <summary>Starts or resumes the hardware polling timer.</summary>
    public void Start()
    {
        int ms = Math.Max(100, _cfg.Monitoring.PollIntervalMs);
        _timer?.Change(0, ms);
    }

    /// <summary>Pauses the hardware polling timer.</summary>
    public void Stop()
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Restarts the polling timer with a new interval.</summary>
    /// <param name="intervalMs">New polling interval in milliseconds (clamped to minimum 100).</param>
    public void Restart(int intervalMs)
    {
        _timer?.Change(0, Math.Max(100, intervalMs));
    }

    private void OnDisplayOrDriverChanged()
    {
        if (_isSuspended) return;
        try
        {
            Log.Debug("Display settings or refresh rate changed; refreshing WDDM display adapter state");
            _wddmGpu?.Reset();
        }
        catch (Exception ex)
        {
            Log.Error($"Error refreshing WDDM on display change: {ex.Message}");
        }
    }

    private void ResetGpuMonitors(string reason)
    {
        try
        {
            var backends = new List<string>();
            if (_nvGpu is not null) backends.Add("NVIDIA NVML");
            if (_wddmGpu is not null) backends.Add("WDDM");
            if (_amdGpu is not null) backends.Add("AMD ADL");
            if (_intelGpu is not null) backends.Add("Intel IGCL");
            string backendDesc = backends.Count > 0 ? string.Join(", ", backends) : "None";

            Log.Info($"Resetting GPU monitors due to {reason} ({backendDesc})");
            _nvGpu?.Reset();
            _wddmGpu?.Reset();
            _amdGpu?.Reset();
            _intelGpu?.Reset();
        }
        catch (Exception ex)
        {
            Log.Error($"Error resetting GPU monitors: {ex.Message}");
        }
    }

    private void OnPowerSuspended()
    {
        _isSuspended = true;
        try
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            Log.Debug("Polling timer paused for suspend");
        }
        catch (Exception ex)
        {
            Log.Error($"Error pausing timer on suspend: {ex.Message}");
        }
    }

    private void OnPowerResumed()
    {
        Log.Debug("System resumed; scheduling hardware warm-up and re-initialization");
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3000).ConfigureAwait(false);
                ExecutePowerResumeWarmUp();
            }
            catch (Exception ex)
            {
                Log.Error($"Error during resume warm-up: {ex.Message}");
            }
            finally
            {
                _isSuspended = false;
            }
        });
    }

    internal void ExecutePowerResumeWarmUp()
    {
        if (_disposed) return;

        ResetGpuMonitors("system power resume");
        _lastPreferredNic = "~~unset~~";
        EnsureNicCounters();

        int ms = Math.Max(100, _cfg.Monitoring.PollIntervalMs);
        _timer?.Change(ms, ms);
        Log.Info("Polling resumed after power resume warm-up");
    }

    private void OnNetworkTopologyChanged()
    {
        if (_isSuspended) return;
        try
        {
            Log.Debug("Network topology changed, refreshing NIC binding");
            _lastPreferredNic = "~~unset~~";
            EnsureNicCounters();
        }
        catch (Exception ex)
        {
            Log.Error($"Error handling network change: {ex.Message}");
        }
    }

    private void OnDevicePnPChanged()
    {
        if (_isSuspended) return;
        try
        {
            Log.Debug("Device PnP event, refreshing disk cache");
            DiskQuery.RefreshDiskCache();
        }
        catch (Exception ex)
        {
            Log.Error($"Error handling device PnP change: {ex.Message}");
        }
    }

    /// <summary>Releases all managed and unmanaged resources.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _lifecycleManager.DisplayOrDriverChanged -= OnDisplayOrDriverChanged;
        _lifecycleManager.PowerSuspended -= OnPowerSuspended;
        _lifecycleManager.PowerResumed -= OnPowerResumed;
        _lifecycleManager.NetworkTopologyChanged -= OnNetworkTopologyChanged;
        _lifecycleManager.DevicePnPChanged -= OnDevicePnPChanged;

        _timer?.Dispose();
        _diskRead?.Dispose();
        _diskWrite?.Dispose();
        if (_perDiskCounters is not null)
        {
            foreach (var (read, write) in _perDiskCounters)
            {
                read.Dispose();
                write.Dispose();
            }
        }
        _intelPwrGadget?.Dispose();
        _intelMsr?.Dispose();
        _amdMsr?.Dispose();
        _pawnIoDevice?.Dispose();
        _batterySearcher?.Dispose();
        _nvGpu?.Dispose();
        _amdGpu?.Dispose();
        _intelGpu?.Dispose();
        _wddmGpu?.Dispose();
    }

    // Helpers
    private static float ReadPdh(PerformanceCounter? ctr)
    {
        if (ctr is null) return 0f;
        try { return Math.Max(0f, ctr.NextValue()); }
        catch (Exception ex)
        {
            Log.Error(ex, "ReadPdh failed");
            return 0f;
        }
    }

    // Log level gating
    private bool ShouldLogTick(string category, int level)
    {
        int mode = category switch
        {
            "cpu" => _cfg.Monitoring.LogCpuMode,
            "gpu" => _cfg.Monitoring.LogGpuMode,
            "ram" => _cfg.Monitoring.LogRamMode,
            "net" => _cfg.Monitoring.LogNetMode,
            "disk" => _cfg.Monitoring.LogDiskMode,
            "battery" => _cfg.Monitoring.LogBatteryMode,
            _ => 1,
        };
        return mode >= level;
    }

    // Metric error deduplication: logs each error once to prevent per-tick spam
    private readonly ConcurrentDictionary<string, byte> _loggedMetricErrors = new();

    private void LogMetricErrorOnce(string category, string errorKey, string message)
    {
        if (!ShouldLogTick(category, 1)) return;
        if (_loggedMetricErrors.TryAdd(errorKey, 0))
        {
            Log.ErrorMetric(message);
        }
    }
}

