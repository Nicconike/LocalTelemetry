using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LocalTelemetry.Core.Hardware.PawnIo;
using LocalTelemetry.Core.Diagnostics;
using LocalTelemetry.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalTelemetry.Core.Hardware;

/// <summary>
/// Polls CPU, RAM, GPU, Disk and network metrics.
///
/// Sources:
///   CPU %      → GetSystemTimes (kernel32 single call, no COM)
///   CPU temp   → PawnIO IntelMSR (Intel MSR) / RyzenSMU (AMD SMU)
///   CPU freq   → PawnIO APERF/MPERF MSR delta, Intel Power Gadget fallback
///   RAM        → GlobalMemoryStatusEx
///   GPU %      → NVML (NVIDIA) / ADL (AMD)
///   GPU temp   → NvGpuMonitor (NVAPI, user-mode)
///   Network    → NetworkInterface.GetIPv4Statistics() delta on active adapter
///   Disk I/O   → PDH "PhysicalDisk\Disk * Bytes/sec\_Total"
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HardwareMonitor : IDisposable
{
    private readonly ILogger<HardwareMonitor> _log;
    private readonly Config.AppSettings _cfg;
    private readonly ISystemInfo _sysInfo;
    private readonly IPawnIoManager _pawnIoManager;
    private Timer? _timer;
    private int _isPolling;

    // PDH counters (disk only; CPU uses GetSystemTimes)
    private PerformanceCounter? _diskRead;
    private PerformanceCounter? _diskWrite;
    private List<(PerformanceCounter read, PerformanceCounter write)>? _perDiskCounters;
    private List<DiskInfo> _diskInfos = [];

    // Network via NetworkInterface.GetIPv4Statistics delta
    private string? _nicInstanceName;
    private string _lastPreferredNic = "~~unset~~";
    private NetworkInterface? _nicInterface;
    private long _prevRx, _prevTx;
    private DateTime _prevNicTime;
    private bool _nicPrimed;
    private string _primaryDiskType = string.Empty;
    private List<string> _diskBusTypes = [];

    // GetSystemTimes fallback state
    private long _prevIdle, _prevKernel, _prevUser;
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

    private string _cpuVendor = string.Empty;
    private string _gpuVendor = string.Empty;

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

    // Constructor
    public HardwareMonitor(
        ILogger<HardwareMonitor> log,
        Config.AppSettings cfg,
        ISystemInfo? sysInfo = null,
        IPawnIoManager? pawnIoManager = null)
    {
        _log = log;
        _cfg = cfg;
        _sysInfo = sysInfo ?? new SystemInfoWrapper();
        _pawnIoManager = pawnIoManager ?? new PawnIoManagerWrapper();

        try
        {
            InitCpuFreq();
            InitIntelPwrGadget();
            InitGpu();
            var mc = _cfg.Monitoring;
            if (mc.EnableCpu)
                InitPawnIo();
            if (mc.EnableCpu)
                Log.Info($"CPU: {_sysInfo.GetCpuName()}, {_sysInfo.GetCpuCoreCount()}C/{_sysInfo.GetCpuThreadCount()}T, maxFreq={_sysInfo.GetCpuMaxSpeedMhz()}MHz");
            InitDiskType();

            if (mc.EnableDisk)
            {
                _diskRead = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
                _diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
                _perDiskCounters = CreatePerDiskCounters();
                if (_perDiskCounters is not null)
                {
                    for (int i = 0; i < _perDiskCounters.Count; i++)
                        Metrics.RegisterDisk($"disk{i}", $"DISK{i}");
                }
            }

            if (mc.EnableRam)
            {
                double installedGb = _sysInfo.GetInstalledRamGb();
                var memStatus = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
                if (NativeMethods.GlobalMemoryStatusEx(ref memStatus) && memStatus.ullTotalPhys > 0)
                {
                    double usableGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                    Log.Info($"RAM: {installedGb:F1} GB installed, {usableGb:F1} GB usable");
                }
                else
                    Log.Info($"RAM: {installedGb:F1} GB installed (usable unknown)");
            }

            if (_sysInfo.HasBattery())
                Log.Info($"Battery: detected, manufacturer={_sysInfo.GetBatteryManufacturer()}, deviceName={_sysInfo.GetBatteryDeviceName()}, designCapacity={_sysInfo.GetBatteryDesignCapacity()}");
            else
                Log.Info("Battery: not detected");

            int ms = Math.Max(100, _cfg.Monitoring.PollIntervalMs);
            _timer = new Timer(_ => Poll(), null, ms, ms);

            // Prime NIC counters immediately so network shows real data from second poll
            if (_cfg.Monitoring.EnableNet)
                EnsureNicCounters();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to initialize HardwareMonitor.");
        }
    }

    // Init helpers
    private void InitCpuFreq()
    {
        try
        {
            int count = Environment.ProcessorCount;
            int size = count * Marshal.SizeOf<NativeMethods.PROCESSOR_POWER_INFORMATION>();
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
                        var ppi = Marshal.PtrToStructure<NativeMethods.PROCESSOR_POWER_INFORMATION>(
                            buf + i * Marshal.SizeOf<NativeMethods.PROCESSOR_POWER_INFORMATION>());
                        if (ppi.MaxMhz > maxMhz) maxMhz = ppi.MaxMhz;
                    }
                    _maxCpuMhz = maxMhz;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (Exception ex)
        {
            Log.Error($"InitCpuFreq: CallNtPowerInformation failed: {ex.Message}");
        }
    }

    private void InitIntelPwrGadget()
    {
        try
        {
            _intelPwrGadget = new IntelPowerGadget();
            if (_intelPwrGadget.IsAvailable)
                Log.Info($"Intel Power Gadget detected - using for CPU temp/freq");
        }
        catch (Exception ex)
        {
            Log.Error($"InitIntelPwrGadget failed: {ex.Message}");
        }
    }

    private void InitPawnIo()
    {
        try
        {
            IPawnIoTransport? device = null;
            for (int retry = 0; retry < 10; retry++)
            {
                device?.Dispose();
                device = _pawnIoManager.TryCreate();
                if (device is not null) goto useDevice;
                if (retry == 0)
                {
                    Log.Info("PawnIO: not available - installing...");
                    if (!_pawnIoManager.TryInstall())
                        goto fail;
                    if (!_pawnIoManager.StartDriverService())
                        goto fail;
                }
                Thread.Sleep(500);
            }

        fail:
            Log.Error("PawnIO: device unavailable - install manually from https://pawnio.eu");
            return;

        useDevice:
            _pawnIoDevice = device;
            SharedPawnIoDevice = device;

            if (_cpuVendor == "Intel")
            {
                var intelBlob = _pawnIoManager.LoadResourceBytes("IntelMSR.bin");
                if (intelBlob is not null && _pawnIoManager.LoadModule(device, intelBlob))
                {
                    _intelMsr = new IntelMsrReader(device);
                    Log.Info($"PawnIO IntelMSR: {(_intelMsr.IsAvailable ? "ready" : "not available")}");
                }
            }
            else if (_cpuVendor == "AMD")
            {
                var ryzenBlob = _pawnIoManager.LoadResourceBytes("RyzenSMU.bin");
                if (ryzenBlob is not null && _pawnIoManager.LoadModule(device, ryzenBlob))
                {
                    _amdMsr = new AmdMsrReader(device);
                    Log.Info($"PawnIO RyzenSMU: {(_amdMsr.IsAvailable ? "ready" : "not available")}");
                }

                var amdBlob = _pawnIoManager.LoadResourceBytes("AMDFamily17.bin");
                if (amdBlob is not null)
                    _pawnIoManager.LoadModule(device, amdBlob);
            }
            else
            {
                Log.Info($"PawnIO: unknown CPU vendor '{_cpuVendor}', skipping module load");
            }

            Log.Info("PawnIO: device ready");
        }
        catch (Exception ex)
        {
            Log.Error($"PawnIO init error: {ex.Message}");
        }
    }

    private void InitNvGpu()
    {
        try { _nvGpu = new NvGpuMonitor(); }
        catch (Exception ex) { Log.Error($"InitNvGpu failed: {ex.Message}"); _nvGpu = null; }
    }

    private void InitIntelGpu()
    {
        try
        {
            _intelGpu = new IntelGpuMonitor();
            if (_intelGpu.IsAvailable)
                Log.Info($"Intel GPU (IGCL) detected - GPU: {_intelGpu.GpuName}");
        }
        catch (Exception ex) { Log.Error($"InitIntelGpu failed: {ex.Message}"); _intelGpu = null; }
    }

    private void InitWddmGpu()
    {
        try
        {
            _wddmGpu = new WddmGpuMonitor();
            if (_wddmGpu.IsAvailable)
                Log.Info("WDDM GPU monitor ready (GPU Engine perf counters)");
            else
                Log.Error("WDDM GPU monitor init failed - source will fall back to NVML/ADL");
        }
        catch (Exception ex) { Log.Error($"InitWddmGpu failed: {ex.Message}"); _wddmGpu = null; }
    }

    private void InitAmdGpu()
    {
        try
        {
            _amdGpu = new AmdGpuMonitor();
            if (_amdGpu.IsAvailable)
                Log.Info($"AMD GPU (ADL) detected - GPU: {_amdGpu.GpuName}");
        }
        catch (Exception ex) { Log.Error($"InitAmdGpu failed: {ex.Message}"); _amdGpu = null; }
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
            NativeMethods.SP_DEVINFO_DATA devInfo = new()
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>()
            };
            uint i = 0;

            while (NativeMethods.SetupDiEnumDeviceInfo(devInfoSet, i++, ref devInfo))
            {
                string name = GetDevProp(devInfoSet, devInfo, NativeMethods.SPDRP_DEVICEDESC);
                if (string.IsNullOrEmpty(name)) continue;

                string hwId = GetDevProp(devInfoSet, devInfo, NativeMethods.SPDRP_HARDWAREID);
                string? vendor = hwId.Contains("VEN_10DE") ? "NVIDIA"
                    : hwId.Contains("VEN_1002") ? "AMD"
                    : hwId.Contains("VEN_8086") ? "Intel"
                    : name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
                    : name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "AMD"
                    : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel" : null;

                bool dedicated = vendor == "NVIDIA"
                    || (vendor == "Intel" && name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                    || IsDiscreteBus(devInfoSet, devInfo)
                    || (vendor == "AMD" && System.Text.RegularExpressions.Regex.IsMatch(name,
                        @"\b(RX|PRO|VII|WX|W[36]\d{3})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)));

                string hwIdTrunc = hwId.Length > 80 ? hwId[..80] : hwId;
                Log.Info($"GPU detected: '{name}' vendor={vendor ?? "null"} dedicated={dedicated} hwId='{hwIdTrunc}'");

                if (vendor is null) continue;

                if (vendor == "NVIDIA")
                {
                    _gpuVendor ??= "NVIDIA";
                    if (!dedicated) continue;
                    _gpuVendor = "NVIDIA";
                    Log.Info("GPU: initializing NVIDIA monitor");
                    InitNvGpu();
                    break;
                }

                if (vendor == "AMD" && dedicated)
                {
                    _gpuVendor ??= "AMD";
                }
                else if (vendor == "Intel" && dedicated)
                {
                    _gpuVendor ??= "Intel";
                }
                else if (vendor == "Intel" && string.IsNullOrEmpty(_gpuVendor))
                {
                    _gpuVendor = "Intel";
                }
            }

            Log.Info($"GPU: selected vendor = {_gpuVendor ?? "none"}");

            // Always init WDDM monitor regardless of vendor
            Log.Info("GPU: Initializing WDDM monitor");
            InitWddmGpu();

            if (_gpuVendor == "NVIDIA") { return; }
            if (_gpuVendor == "AMD") { Log.Info("GPU: initializing AMD monitor"); InitAmdGpu(); }
            else if (_gpuVendor == "Intel") { Log.Info("GPU: initializing Intel monitor"); InitIntelGpu(); }
            else Log.Info("GPU: no compatible GPU found");
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(devInfoSet);
        }
    }

    private static bool IsDiscreteBus(IntPtr devInfoSet, NativeMethods.SP_DEVINFO_DATA devInfo)
    {
        string loc = GetDevProp(devInfoSet, devInfo, NativeMethods.SPDRP_LOCATION_INFORMATION);
        if (string.IsNullOrEmpty(loc)) return false;
        var m = System.Text.RegularExpressions.Regex.Match(loc, @"(?i)PCI\s*bus\s*(\d+)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(250));
        if (!m.Success) return false;
        return int.TryParse(m.Groups[1].Value, out int bus) && bus > 0;
    }

    private static string GetDevProp(IntPtr devInfoSet, NativeMethods.SP_DEVINFO_DATA devInfo, uint property)
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

    private void DetectCpuVendor()
    {
        _cpuVendor = _sysInfo.GetCpuVendor();
    }

    private void InitDiskType()
    {
        try
        {
            _diskInfos = DiskQuery.QueryAllDrives();
            _primaryDiskType = _diskInfos.Count > 0 ? (_diskInfos[0].BusType ?? "DISK") : "DISK";
            _diskBusTypes = _diskInfos.Select(d => d.BusType ?? "DISK").ToList();
            for (int i = 0; i < _diskInfos.Count; i++)
                Metrics.RegisterDisk($"disk{i}", $"DISK{i}");
            Log.Info($"Disk: type={_primaryDiskType}, count={_diskInfos.Count}");
        }
        catch (Exception ex)
        {
            Log.Error($"DiskQuery error: {ex.Message}");
            _primaryDiskType = "DISK";
            _diskBusTypes = [];
            _diskInfos = [];
        }
    }

    private List<(PerformanceCounter read, PerformanceCounter write)>? CreatePerDiskCounters()
    {
        try
        {
            var cat = new PerformanceCounterCategory("PhysicalDisk");
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
                    new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", inst),
                    new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", inst)
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
            _log.LogError(ex, "Failed to create per-disk PerformanceCounters");
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
        if (Interlocked.CompareExchange(ref _isPolling, 1, 0) != 0)
            return;
        try { SnapshotReady?.Invoke(BuildSnapshot()); }
        catch (Exception ex) { _log.LogError(ex, "Transient poll error."); }
        finally { Volatile.Write(ref _isPolling, 0); }
    }

    private TelemetrySnapshot BuildSnapshot()
    {
        var mc = _cfg.Monitoring;

        float cpuPct = 0, cpuTemp = 0, cpuFreq = 0, cpuPower = 0;
        if (mc.EnableCpu)
        {
            cpuPct = ReadCpuPct();
            cpuFreq = ReadCpuFreq();
            cpuTemp = ReadCpuTemp();
            cpuPower = ReadCpuPowerRapl();
        }

        float ramPct = 0, ramUsedGb = 0, ramTotalGb = 0;
        if (mc.EnableRam) ReadMemory(out ramPct, out ramUsedGb, out ramTotalGb);

        float gpuPct = 0, gpuTemp = 0, /*gpuVramPct = 0,*/ gpuVramUsed = 0, gpuClock = 0, gpuPower = 0;
        if (mc.EnableGpu)
        {
            NvGpuMonitor.NvGpuData? gpuData = _nvGpu?.IsAvailable == true ? _nvGpu.Query() : null;
            gpuPct = ReadGpuPct(gpuData);
            gpuTemp = ReadGpuTemp(gpuData);
            // gpuVramPct = ReadGpuVramPct(gpuData);
            gpuVramUsed = ReadGpuVramUsedMb(gpuData);
            gpuClock = ReadGpuFreqMHz(gpuData);
            gpuPower = ReadGpuPowerW(gpuData);
        }

        double netDown = 0, netUp = 0;
        string nicName = string.Empty;
        if (mc.EnableNet)
        {
            EnsureNicCounters();
            (netDown, netUp) = ReadNetBytes();
            nicName = _nicInstanceName ?? string.Empty;
        }

        float diskRead = 0, diskWrite = 0;
        List<DiskSnapshot> disks = [];
        if (mc.EnableDisk)
        {
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
        }

        float batteryPct = 0;
        bool batteryCharging = false, batteryOnAC = true;
        float batteryRate = 0;
        if (mc.EnableBattery)
        {
            (batteryPct, batteryCharging, batteryOnAC, batteryRate) = ReadBattery();
        }

        if (mc.EnableCpu && ShouldLogTick("cpu", 2)) Log.InfoMetric($"CPU: {cpuPct:F0}% {cpuTemp:F0}°C freq={cpuFreq:F2}GHz power={cpuPower:F1}W");
        if (mc.EnableRam && ShouldLogTick("ram", 2)) Log.InfoMetric($"RAM: {ramPct:F1}% used ({ramUsedGb:F1}/{ramTotalGb:F1} GB)");
        if (mc.EnableDisk && ShouldLogTick("disk", 2))
        {
            Log.InfoMetric($"Disk: read={diskRead:F2} write={diskWrite:F2} MB/s, {disks.Count} disk(s)");
        }
        if (mc.EnableGpu && ShouldLogTick("gpu", 2)) Log.InfoMetric($"GPU: {gpuPct:F0}% {gpuTemp:F0}°C power={gpuPower:F1}W clock={gpuClock:F0}MHz");
        if (mc.EnableBattery && batteryPct > 0 && ShouldLogTick("battery", 2)) Log.InfoMetric($"Battery: {batteryPct:F0}% {(batteryCharging ? "charging" : batteryOnAC ? "charged" : "discharging")} rate={batteryRate:F1}W");

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
            // GpuVramUsagePct = gpuVramPct,
            GpuVramUsedMb = gpuVramUsed,
            GpuFreqMHz = gpuClock,
            GpuPowerW = gpuPower,
            NetDownBps = netDown,
            NetUpBps = netUp,
            NetInterfaceName = nicName,
            NetTotalBytes = mc.EnableNet
                ? TrafficHistoryStore.GetToday() is var (d, u) ? d + u : 0
                : 0,
            DiskReadMbps = diskRead,
            DiskWriteMbps = diskWrite,
            BatteryPct = mc.EnableBattery ? batteryPct : 0,
            BatteryIsCharging = mc.EnableBattery && batteryCharging,
            IsOnACPower = mc.EnableBattery ? batteryOnAC : true,
            BatteryChargeRateW = mc.EnableBattery ? batteryRate : 0,
        };
    }

    // CPU % via GetSystemTimes
    private float ReadCpuPct()
    {
        try
        {
            if (!NativeMethods.GetSystemTimes(out long idle, out long kernel, out long user))
            {
                if (ShouldLogTick("cpu", 1)) Log.ErrorMetric("CPU%: GetSystemTimes failed");
                return 0f;
            }

            if (!_hasPrevTimes)
            {
                _prevIdle = idle; _prevKernel = kernel; _prevUser = user;
                _hasPrevTimes = true;
                if (ShouldLogTick("cpu", 2)) Log.InfoMetric("CPU%: GetSystemTimes primed");
                return 0f;
            }

            long idleDelta = idle - _prevIdle;
            long kernelDelta = kernel - _prevKernel;
            long userDelta = user - _prevUser;

            _prevIdle = idle; _prevKernel = kernel; _prevUser = user;

            long totalDelta = kernelDelta + userDelta;
            if (totalDelta <= 0)
            {
                if (ShouldLogTick("cpu", 1)) Log.ErrorMetric("CPU%: GetSystemTimes zero delta");
                return 0f;
            }

            float gst = Math.Clamp((float)(totalDelta - idleDelta) * 100f / totalDelta, 0f, 100f);
            return gst;
        }
        catch (Exception ex)
        {
            if (ShouldLogTick("cpu", 1)) Log.ErrorMetric($"CPU%: GetSystemTimes exception: {ex.Message}");
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
            var aperfResult = _pawnIoDevice.Execute("ioctl_read_msr", MsrAperfInput, 1);
            if (aperfResult.Length < 1 || aperfResult[0] == 0)
                return 0f;
            ulong aperf = aperfResult[0];

            var mperfResult = _pawnIoDevice.Execute("ioctl_read_msr", MsrMperfInput, 1);
            if (mperfResult.Length < 1 || mperfResult[0] == 0)
                return 0f;
            ulong mperf = mperfResult[0];

            if (!_hasMsrFreqPrimed)
            {
                _prevAperf = aperf;
                _prevMperf = mperf;
                _hasMsrFreqPrimed = true;
                if (ShouldLogTick("cpu", 2)) Log.InfoMetric("CPUfreq: APERF/MPERF primed");
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
            if (ShouldLogTick("cpu", 1)) Log.ErrorMetric($"CPUfreq: APERF/MPERF error: {ex.Message}");
            return 0f;
        }
    }

    // GPU
    private float ReadGpuPct(NvGpuMonitor.NvGpuData? gpu = null)
    {
        try
        {
            // WDDM source (like Task Manager) - max across engine nodes
            if (_cfg.Monitoring.GpuUsageSource == "wddm" &&
                _wddmGpu is not null && _wddmGpu.IsAvailable)
            {
                if (ShouldLogTick("gpu", 10)) Log.Info("GPU%: using WDDM source");
                return _wddmGpu.GetUsagePct();
            }

            // Driver source (NVML/ADL - kernel busy time)
            if (_nvGpu is not null && _nvGpu.IsAvailable)
            {
                if (ShouldLogTick("gpu", 10)) Log.Info("GPU%: using NVML source");
                gpu ??= _nvGpu.Query();
                if (gpu.HasValue && gpu.Value.UsagePct >= 0) return gpu.Value.UsagePct;
            }
            if (_amdGpu is not null && _amdGpu.IsAvailable)
            {
                if (ShouldLogTick("gpu", 10)) Log.Info("GPU%: using ADL source");
                return _amdGpu.GetUsagePct();
            }
        }
        catch (Exception ex)
        {
            if (ShouldLogTick("gpu", 1)) Log.ErrorMetric($"GPU% failed: {ex.Message}");
        }
        if (ShouldLogTick("gpu", 10)) Log.Info("GPU%: no source available, returning 0");
        return 0f;
    }

    private float ReadGpuTemp(NvGpuMonitor.NvGpuData? gpu = null)
    {
        try
        {
            if (_nvGpu is not null && _nvGpu.IsAvailable)
            {
                gpu ??= _nvGpu.Query();
                if (gpu.HasValue && gpu.Value.TempC >= 0) return gpu.Value.TempC;
            }
            if (_amdGpu is not null && _amdGpu.IsAvailable)
                return _amdGpu.GetTempC();
            if (_intelGpu is not null && _intelGpu.IsAvailable)
                return _intelGpu.GetTempC();
        }
        catch (Exception ex)
        {
            if (ShouldLogTick("gpu", 1)) Log.ErrorMetric($"GPU temp failed: {ex.Message}");
        }
        return 0f;
    }

    private float ReadGpuVramUsedMb(NvGpuMonitor.NvGpuData? gpu = null)
    {
        try
        {
            if (_nvGpu is not null && _nvGpu.IsAvailable)
            {
                gpu ??= _nvGpu.Query();
                if (gpu.HasValue) return gpu.Value.VramUsedMb;
            }
            if (_amdGpu is not null && _amdGpu.IsAvailable)
                return _amdGpu.GetVram().usedMb;
            if (_intelGpu is not null && _intelGpu.IsAvailable)
                return _intelGpu.GetVram().usedMb;
        }
        catch (Exception ex)
        {
            if (ShouldLogTick("gpu", 1)) Log.ErrorMetric($"GPU VRAM used failed: {ex.Message}");
        }
        return 0f;
    }

    private float ReadGpuFreqMHz(NvGpuMonitor.NvGpuData? gpu = null)
    {
        try
        {
            if (_nvGpu is not null && _nvGpu.IsAvailable)
            {
                gpu ??= _nvGpu.Query();
                if (gpu.HasValue && gpu.Value.CoreClockMHz > 0) return gpu.Value.CoreClockMHz;
            }
            if (_amdGpu is not null && _amdGpu.IsAvailable)
                return _amdGpu.GetClockMHz();
            if (_intelGpu is not null && _intelGpu.IsAvailable)
                return _intelGpu.GetClockMHz();
        }
        catch (Exception ex)
        {
            if (ShouldLogTick("gpu", 1)) Log.ErrorMetric($"GPU clock failed: {ex.Message}");
        }
        return 0f;
    }

    private float ReadGpuPowerW(NvGpuMonitor.NvGpuData? gpu = null)
    {
        try
        {
            if (_nvGpu is not null && _nvGpu.IsAvailable)
            {
                gpu ??= _nvGpu.Query();
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
            if (ShouldLogTick("gpu", 1)) Log.ErrorMetric($"GPU power failed: {ex.Message}");
        }
        return 0f;
    }

    // RAPL CPU power (MSR_PKG_ENERGY_STATUS delta)
    private void ReadRaplPowerUnit()
    {
        if (_pawnIoDevice is null) return;
        try
        {
            var result = _pawnIoDevice.Execute("ioctl_read_msr", MsrRaplPowerUnitInput, 1);
            if (result.Length >= 1)
            {
                int pUnits = (int)(result[0] & 0xF);
                _powerUnitDivisor = 1.0 / (1 << pUnits);
                int eUnits = (int)((result[0] >> 8) & 0x1F);
                _energyUnit = 1.0 / (1L << eUnits);
                Log.Info($"RAPL: power unit = {_powerUnitDivisor}, energy unit = {_energyUnit * 1e6:F2} µJ/LSB (MSR=0x{result[0]:X})");
            }
        }
        catch (Exception ex) { if (ShouldLogTick("cpu", 1)) Log.ErrorMetric($"ReadRaplPowerUnit failed: {ex.Message}"); _energyUnit = 0; _powerUnitDivisor = 1; }
    }

    private const uint MSR_PKG_POWER_SKU = 0x614;
    private uint? _tdpWatts;

    private uint? ReadCpuTdp()
    {
        if (_pawnIoDevice is null) return null;
        try
        {
            var result = _pawnIoDevice.Execute("ioctl_read_msr", MsrRaplPowerUnitInput, 1);
            if (result.Length >= 1)
            {
                int pUnits = (int)(result[0] & 0xF);
                _powerUnitDivisor = 1.0 / (1 << pUnits);
            }
        }
        catch (Exception ex) { if (ShouldLogTick("cpu", 1)) Log.ErrorMetric($"ReadCpuTdp: MSR_RAPL_POWER_UNIT failed: {ex.Message}"); }
        try
        {
            var result = _pawnIoDevice.Execute("ioctl_read_msr", MsrPkgPowerSkuInput, 1);
            if (result.Length >= 1)
            {
                double tdpRaw = result[0] & 0x7FFF;
                if (tdpRaw > 0)
                {
                    uint tdp = (uint)Math.Round(tdpRaw * _powerUnitDivisor);
                    if (ShouldLogTick("cpu", 2)) Log.InfoMetric($"RAPL: TDP raw={tdpRaw}, divisor={_powerUnitDivisor}, scaled={tdp} W (MSR=0x{result[0]:X})");
                    return tdp;
                }
            }
        }
        catch (Exception ex) { if (ShouldLogTick("cpu", 1)) Log.ErrorMetric($"ReadCpuTdp: MSR_PKG_POWER_SKU failed: {ex.Message}"); }
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
            var pkgResult = _pawnIoDevice.Execute("ioctl_read_msr", MsrPkgEnergyStatusInput, 1);
            if (pkgResult.Length < 1)
                return 0f;
            ulong energy = pkgResult[0];

            var now = DateTime.UtcNow;
            if (!_hasEnergyPrimed)
            {
                _prevPkgEnergy = energy;
                _prevEnergyTime = now;
                _hasEnergyPrimed = true;
                if (ShouldLogTick("cpu", 2)) Log.InfoMetric("RAPL: energy primed");
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
            if (ShouldLogTick("cpu", 1)) Log.ErrorMetric($"RAPL: error: {ex.Message}");
            return 0f;
        }
    }

    // CPU temperature
    private float ReadCpuTemp()
    {
        if (_cpuVendor == "Intel")
        {
            if (_intelMsr is null)
            {
                if (ShouldLogTick("cpu", 1) && Interlocked.CompareExchange(ref _onceCpuTempIntelMsrNull, 1, 0) == 0)
                    Log.ErrorMetric("CPUtemp: IntelMSR reader is null");
                return 0f;
            }
            if (!_intelMsr.IsAvailable)
            {
                if (ShouldLogTick("cpu", 1) && Interlocked.CompareExchange(ref _onceCpuTempIntelMsrNotAvail, 1, 0) == 0)
                    Log.ErrorMetric("CPUtemp: IntelMSR not available");
                return 0f;
            }
            float temp = _intelMsr.ReadTemperature();
            if (temp > 0) return temp;
            if (ShouldLogTick("cpu", 1) && Interlocked.CompareExchange(ref _onceCpuTempIntelMsrZero, 1, 0) == 0)
                Log.ErrorMetric("CPUtemp: IntelMSR returned 0");
            return 0f;
        }

        if (_cpuVendor == "AMD")
        {
            if (_amdMsr is null)
            {
                if (ShouldLogTick("cpu", 1) && Interlocked.CompareExchange(ref _onceCpuTempAmdMsrNull, 1, 0) == 0)
                    Log.ErrorMetric("CPUtemp: RyzenSMU reader is null");
                return 0f;
            }
            if (!_amdMsr.IsAvailable)
            {
                if (ShouldLogTick("cpu", 1) && Interlocked.CompareExchange(ref _onceCpuTempAmdMsrNotAvail, 1, 0) == 0)
                    Log.ErrorMetric("CPUtemp: RyzenSMU not available");
                return 0f;
            }
            float temp = _amdMsr.ReadTemperature();
            if (temp > 0) return temp;
            if (ShouldLogTick("cpu", 1) && Interlocked.CompareExchange(ref _onceCpuTempAmdMsrZero, 1, 0) == 0)
                Log.ErrorMetric("CPUtemp: RyzenSMU returned 0");
            return 0f;
        }

        if (ShouldLogTick("cpu", 1) && Interlocked.CompareExchange(ref _onceCpuTempUnknownVendor, 1, 0) == 0)
            Log.ErrorMetric($"CPUtemp: unknown CPU vendor '{_cpuVendor}'");
        return 0f;
    }

    // Network via NetworkInterface.GetIPv4Statistics delta
    private void EnsureNicCounters()
    {
        string pref = _cfg.Monitoring.PreferredNic;
        if (string.IsNullOrEmpty(pref)) pref = "auto";

        if (pref == _lastPreferredNic) return;
        _lastPreferredNic = pref;

        _nicInterface = null;
        _nicPrimed = false;

        try
        {
            var allNics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && (n.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                          || n.NetworkInterfaceType == (NetworkInterfaceType)62
                          || n.NetworkInterfaceType == (NetworkInterfaceType)69
                          || n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                          || n.NetworkInterfaceType == (NetworkInterfaceType)117))
                .ToList();

            // Prefer NICs that have a default gateway (actively routing traffic)
            var withGateway = allNics
                .Where(n =>
                {
                    try
                    {
                        var gw = n.GetIPProperties()?.GatewayAddresses;
                        return gw is not null && gw.Count > 0
                            && gw.Any(g => g.Address is not null && !g.Address.ToString().StartsWith("0."));
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"EnsureNicCounters: gateway check failed: {ex.Message}");
                        return false;
                    }
                })
                .ToList();

            var candidates = withGateway.Count > 0 ? withGateway : allNics;

            NetworkInterface? selected = null;
            if (pref != "auto")
            {
                string lower = pref.ToLowerInvariant();
                selected = candidates.FirstOrDefault(n =>
                    n.Name.Equals(pref, StringComparison.OrdinalIgnoreCase)
                    || n.Description.Contains(pref, StringComparison.OrdinalIgnoreCase)
                    || pref.Contains(n.Name, StringComparison.OrdinalIgnoreCase));
            }
            selected ??= candidates.FirstOrDefault();
            selected ??= allNics.FirstOrDefault();

            if (selected is not null)
            {
                _nicInterface = selected;
                _nicInstanceName = selected.Name;
                Log.Info($"Network: using '{selected.Name}' ({selected.Description}), hasGateway={withGateway.Count > 0}");

                // Prime counters so first poll returns real data
                try
                {
                    var stats = selected.GetIPv4Statistics();
                    _prevRx = stats.BytesReceived;
                    _prevTx = stats.BytesSent;
                    _prevNicTime = DateTime.UtcNow;
                    _nicPrimed = true;
                    Log.Info($"Network: primed rx={_prevRx} tx={_prevTx}");
                }
                catch (Exception ex)
                {
                    Log.Error($"Network: prime failed: {ex.Message}");
                    _nicPrimed = false;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Network: init failed: {ex.Message}");
        }
    }

    private (double downBps, double upBps) ReadNetBytes()
    {
        // NetworkInterface.GetIPv4Statistics delta
        if (_nicInterface is not null)
        {
            try
            {
                var stats = _nicInterface.GetIPv4Statistics();
                long rx = stats.BytesReceived;
                long tx = stats.BytesSent;

                if (!_nicPrimed)
                {
                    _prevRx = rx;
                    _prevTx = tx;
                    _prevNicTime = DateTime.UtcNow;
                    _nicPrimed = true;
                    if (ShouldLogTick("net", 2)) Log.InfoMetric($"Net: primed '{_nicInterface.Name}' rx={rx} tx={tx}");
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

                if (ShouldLogTick("net", 2)) Log.InfoMetric($"Net: '{_nicInterface.Name}': down={downBps:F1}B/s up={upBps:F1}B/s");
                return (downBps, upBps);
            }
            catch (Exception ex)
            {
                if (ShouldLogTick("net", 1)) Log.ErrorMetric($"Net: exception: {ex.Message}");
                return (0, 0);
            }
        }

        if (ShouldLogTick("net", 1)) Log.ErrorMetric("Net: no adapter selected");
        return (0, 0);
    }

    // Battery
    private (float pct, bool isCharging, bool isOnAC, float rateW) ReadBattery()
    {
        var sps = new NativeMethods.SYSTEM_POWER_STATUS();
        if (!NativeMethods.GetSystemPowerStatus(out sps))
            return (0f, false, false, 0f);

        // BatteryFlag: 128 = no system battery
        if (sps.BatteryFlag == 128)
        {
            _hasBatteryPrimed = false;
            return (0f, false, false, 0f);
        }

        float pct = sps.BatteryLifePercent;
        if (pct > 100) return (0f, false, false, 0f); // 255 = unknown → no battery info

        // BatteryFlag: bit 3 (0x08) = charging. ACLineStatus alone can't distinguish
        // between "actively charging" and "plugged in but full / at charge limiter"
        // (e.g. Lenovo Conservation Mode stops at 80%, Dell at 90%, etc.)
        bool isOnAC = sps.ACLineStatus == 1;
        bool isCharging = isOnAC && (sps.BatteryFlag & 8) == 8;

        float rateW = 0f;
        try
        {
            _batterySearcher ??= new ManagementObjectSearcher(
                new ManagementScope(@"\\.\ROOT\WMI"),
                new ObjectQuery("SELECT RemainingCapacity, ChargeRate, DischargeRate FROM BatteryStatus"));

            foreach (ManagementBaseObject mo in _batterySearcher.Get())
            {
                // Direct charge/discharge rate (milliwatts) - same source as HWInfo
                object? cr = mo["ChargeRate"];
                object? dr = mo["DischargeRate"];
                if (cr is not null && Convert.ToInt64(cr) > 0)
                    rateW = Convert.ToInt64(cr) / 1000f;
                else if (dr is not null && Convert.ToInt64(dr) > 0)
                    rateW = -(Convert.ToInt64(dr) / 1000f);

                // Fall back to capacity delta if no direct rate
                if (rateW == 0)
                {
                    var cap = mo["RemainingCapacity"];
                    long remainingMwH = cap is not null ? Convert.ToInt64(cap) : 0;
                    if (remainingMwH > 0)
                    {
                        var now = DateTime.UtcNow;
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
                    }
                }
                break;
            }
        }
        catch (Exception ex) { _log.LogDebug(ex, "ReadBattery failed"); }

        return (pct, isCharging, isOnAC, rateW);
    }

    // RAM
    private void ReadMemory(out float usagePct, out float usedGb, out float totalGb)
    {
        try
        {
            var ms = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
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
            if (ShouldLogTick("ram", 1)) Log.ErrorMetric($"ReadMemory failed: {ex.Message}");
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
        _timer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }

    /// <summary>Restarts the polling timer with a new interval.</summary>
    /// <param name="intervalMs">New polling interval in milliseconds (clamped to minimum 100).</param>
    public void Restart(int intervalMs)
    {
        _timer?.Change(0, Math.Max(100, intervalMs));
    }

    /// <summary>Releases all managed and unmanaged resources.</summary>
    public void Dispose()
    {
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
}
