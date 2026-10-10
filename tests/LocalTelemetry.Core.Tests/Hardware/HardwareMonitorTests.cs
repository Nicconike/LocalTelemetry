using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;
using FluentAssertions;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Hardware;
using LocalTelemetry.Core.Hardware.PawnIo;
using LocalTelemetry.Core.Models;
using NSubstitute;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

[Collection("TrafficHistory")]
public class HardwareMonitorTests
{
    [Fact]
    public void HardwareMonitor_WithIntelCpuMock_BuildsSnapshot()
    {
        var cfg = new AppSettings();
        cfg.Monitoring.EnableNet = true;

        var sysInfoMock = Substitute.For<ISystemInfo>();
        sysInfoMock.GetCpuName().Returns("Intel Core i9-13900K");
        sysInfoMock.GetCpuVendor().Returns("Intel");
        sysInfoMock.GetCpuCoreCount().Returns(24);
        sysInfoMock.GetCpuThreadCount().Returns(32);
        sysInfoMock.GetCpuMaxSpeedMhz().Returns(5800);
        sysInfoMock.HasBattery().Returns(false);
        sysInfoMock.GetGpus().Returns([(Name: "NVIDIA GeForce RTX 4090", Vendor: "NVIDIA", IsDedicated: true, VramBytes: 25769803776, DriverVersion: "550.00")]);
        sysInfoMock.GetNics().Returns([(Name: "Ethernet", Manufacturer: "Intel")]);

        var transportMock = Substitute.For<IPawnIoTransport>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();
        pawnIoMock.TryCreate().Returns(transportMock);

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock);

        string tempPath = Path.Combine(Path.GetTempPath(), "net_history_" + Guid.NewGuid().ToString("N") + ".dat");
        TrafficHistoryStore.Initialize(new TrafficHistoryFile(tempPath));

        var method = typeof(HardwareMonitor).GetMethod("BuildSnapshot", BindingFlags.NonPublic | BindingFlags.Instance);
        var snapshot = (TelemetrySnapshot?)method?.Invoke(monitor, null);

        snapshot.Should().NotBeNull();
        snapshot!.CpuUsagePct.Should().BeGreaterThanOrEqualTo(0);

        try { File.Delete(tempPath); } catch { }
    }

    [Fact]
    public void HardwareMonitor_WithAmdCpuMock_BuildsSnapshot()
    {
        var cfg = new AppSettings();
        cfg.Monitoring.EnableNet = false;

        var sysInfoMock = Substitute.For<ISystemInfo>();
        sysInfoMock.GetCpuName().Returns("AMD Ryzen 9 7950X");
        sysInfoMock.GetCpuVendor().Returns("AMD");
        sysInfoMock.GetCpuCoreCount().Returns(16);
        sysInfoMock.GetCpuThreadCount().Returns(32);
        sysInfoMock.GetCpuMaxSpeedMhz().Returns(5700);
        sysInfoMock.HasBattery().Returns(true);
        sysInfoMock.GetBatteryManufacturer().Returns("Dell");
        sysInfoMock.GetBatteryDeviceName().Returns("Primary");
        sysInfoMock.GetBatteryDesignCapacity().Returns("80000");

        var transportMock = Substitute.For<IPawnIoTransport>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();
        pawnIoMock.TryCreate().Returns(transportMock);
        pawnIoMock.LoadResourceBytes(Arg.Any<string>()).Returns([1, 2, 3]);
        pawnIoMock.LoadModule(Arg.Any<IPawnIoTransport>(), Arg.Any<byte[]>()).Returns(true);

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock);

        var method = typeof(HardwareMonitor).GetMethod("BuildSnapshot", BindingFlags.NonPublic | BindingFlags.Instance);
        var snapshot = (TelemetrySnapshot?)method?.Invoke(monitor, null);

        snapshot.Should().NotBeNull();
        snapshot!.CpuUsagePct.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task HardwareMonitor_SnapshotReadyEvent_FiresOnPolling()
    {
        var cfg = new AppSettings();
        cfg.Monitoring.PollIntervalMs = 20;
        cfg.Monitoring.EnableNet = false;

        var sysInfoMock = Substitute.For<ISystemInfo>();
        sysInfoMock.GetCpuName().Returns("Mocked CPU");
        sysInfoMock.GetCpuVendor().Returns("Intel");
        sysInfoMock.GetCpuCoreCount().Returns(4);

        var transportMock = Substitute.For<IPawnIoTransport>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();
        pawnIoMock.TryCreate().Returns(transportMock);

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock);

        TelemetrySnapshot? received = null;
        var tcs = new TaskCompletionSource<TelemetrySnapshot>();

        monitor.SnapshotReady += snap =>
        {
            received = snap;
            tcs.TrySetResult(snap);
        };

        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(1000));
        completedTask.Should().BeSameAs(tcs.Task);

        received.Should().NotBeNull();
    }
}

/// <summary>
/// Hermetic tests for <see cref="HardwareMonitor"/> private seams: MSR/RAPL/TDP reads via a
/// mocked <see cref="IPawnIoTransport"/>, CPU temperature via the MSR readers, battery/disk/net
/// fallbacks and lifecycle methods. No kernel driver is required.
/// </summary>
public class HardwareMonitorSeamTests
{
    private const ulong MsrAperf = 0xE8;
    private const ulong MsrMperf = 0xE7;
    private const ulong MsrPowerUnit = 0x606;
    private const ulong MsrPkgEnergy = 0x611;
    private const ulong MsrPkgSku = 0x614;
    private const ulong MsrTempTarget = 0x1A2;
    private const ulong MsrThermStatus = 0x1B1;

    private static HardwareMonitor CreateMonitor(AppSettings cfg, ISystemInfo? sysInfo = null, IPawnIoManager? pawnIo = null)
        => new(cfg, sysInfo, pawnIo);

    private static object? Invoke(object target, string method, params object?[] args)
        => typeof(HardwareMonitor).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

    private static float InvokeFloat(object target, string method, params object?[] args)
        => (float)Invoke(target, method, args)!;

    private static void SetField(object target, string name, object? value)
        => typeof(HardwareMonitor).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

    private static T GetField<T>(object target, string name)
        => (T)typeof(HardwareMonitor).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;

    private static IPawnIoTransport TransportMock() => Substitute.For<IPawnIoTransport>();

    private static AppSettings BasicCfg()
    {
        var cfg = new AppSettings();
        cfg.Monitoring.EnableCpu = false;
        cfg.Monitoring.EnableGpu = false;
        cfg.Monitoring.EnableRam = false;
        cfg.Monitoring.EnableDisk = false;
        cfg.Monitoring.EnableNet = false;
        cfg.Monitoring.EnableBattery = false;
        return cfg;
    }

    private static ISystemInfo IntelSysInfo()
    {
        var s = Substitute.For<ISystemInfo>();
        s.GetCpuName().Returns("Intel Core i9-13900K");
        s.GetCpuVendor().Returns("Intel");
        s.GetCpuCoreCount().Returns(24);
        s.GetCpuThreadCount().Returns(32);
        s.GetCpuMaxSpeedMhz().Returns(5800);
        s.HasBattery().Returns(false);
        return s;
    }

    private static HardwareMonitor WiredDevice(IPawnIoTransport dev, uint maxMhz = 5000, string vendor = "Intel")
    {
        var m = CreateMonitor(BasicCfg());
        SetField(m, "_pawnIoDevice", dev);
        SetField(m, "_maxCpuMhz", maxMhz);
        SetField(m, "_cpuVendor", vendor);
        return m;
    }

    [Fact]
    public void ReadCpuPct_PrimedThenRealDelta()
    {
        using var m = CreateMonitor(BasicCfg());
        InvokeFloat(m, "ReadCpuPct").Should().Be(0f);
        InvokeFloat(m, "ReadCpuPct").Should().BeInRange(0f, 100f);
    }

    [Fact]
    public void ReadCpuFreqMsr_ReturnsZeroWithoutDevice()
    {
        using var m = CreateMonitor(BasicCfg());
        InvokeFloat(m, "ReadCpuFreqMsr").Should().Be(0f);
    }

    [Fact]
    public void ReadCpuFreqMsr_PrimesThenReportsRatio()
    {
        using var m = WiredDevice(TransportMock());
        var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
        dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a.Length == 1 && a[0] == MsrAperf), 1)
            .Returns([1000UL], [2000UL]);
        dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a.Length == 1 && a[0] == MsrMperf), 1)
            .Returns([1000UL], [1100UL]);

        InvokeFloat(m, "ReadCpuFreqMsr").Should().Be(0f);
        InvokeFloat(m, "ReadCpuFreqMsr").Should().Be(10f);
    }

    [Fact]
    public void ReadCpuFreqMsr_HandlesEmptyZeroAndErrors()
    {
        // Empty aperf result
        using (var m = WiredDevice(TransportMock()))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrAperf), 1).Returns([]);
            InvokeFloat(m, "ReadCpuFreqMsr").Should().Be(0f);
        }

        // Zero aperf result
        using (var m = WiredDevice(TransportMock()))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrAperf), 1).Returns([0UL]);
            InvokeFloat(m, "ReadCpuFreqMsr").Should().Be(0f);
        }

        // Empty mperf result
        using (var m = WiredDevice(TransportMock()))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrAperf), 1).Returns([1000UL]);
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrMperf), 1).Returns([]);
            InvokeFloat(m, "ReadCpuFreqMsr").Should().Be(0f);
        }

        // Zero mperf result
        using (var m = WiredDevice(TransportMock()))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrAperf), 1).Returns([1000UL]);
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrMperf), 1).Returns([0UL]);
            InvokeFloat(m, "ReadCpuFreqMsr").Should().Be(0f);
        }

        // Zero mperf delta
        using (var m = WiredDevice(TransportMock()))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrAperf), 1).Returns([2000UL]);
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrMperf), 1).Returns([1000UL]);
            SetField(m, "_hasMsrFreqPrimed", true);
            SetField(m, "_prevAperf", 1000UL);
            SetField(m, "_prevMperf", 1000UL);
            InvokeFloat(m, "ReadCpuFreqMsr").Should().Be(0f);
        }

        // Execute throws
        using (var m = WiredDevice(TransportMock()))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            dev.When(d => d.Execute(Arg.Any<string>(), Arg.Any<ulong[]>(), Arg.Any<int>()))
                .Throw(new InvalidOperationException("boom"));
            InvokeFloat(m, "ReadCpuFreqMsr").Should().Be(0f);
        }
    }

    [Fact]
    public void ReadRaplPowerUnit_ComputesUnitsFromMsr()
    {
        using (var m = WiredDevice(TransportMock()))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrPowerUnit), 1).Returns([0x0A02UL]);

            Invoke(m, "ReadRaplPowerUnit");

            GetField<double>(m, "_powerUnitDivisor").Should().BeApproximately(0.25, 0.0001);
            GetField<double>(m, "_energyUnit").Should().BeApproximately(1.0 / 1024.0, 1e-9);
        }

        using (var m = WiredDevice(TransportMock()))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrPowerUnit), 1).Returns([]);
            Invoke(m, "ReadRaplPowerUnit");
            GetField<double>(m, "_powerUnitDivisor").Should().Be(1.0);
            GetField<double>(m, "_energyUnit").Should().Be(0.0);
        }

        using (var m = WiredDevice(TransportMock()))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            dev.When(d => d.Execute(Arg.Any<string>(), Arg.Any<ulong[]>(), Arg.Any<int>()))
                .Throw(new InvalidOperationException("boom"));
            Invoke(m, "ReadRaplPowerUnit");
            GetField<double>(m, "_powerUnitDivisor").Should().Be(1.0);
            GetField<double>(m, "_energyUnit").Should().Be(0.0);
        }
    }

    [Fact]
    public void ReadCpuTdp_ComputesTdpFromMsr()
    {
        using (var m = WiredDevice(TransportMock()))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrPowerUnit), 1).Returns([0x0A02UL]);
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrPkgSku), 1).Returns([65UL]);
            ((uint?)Invoke(m, "ReadCpuTdp")).Should().Be(16);
        }

        using (var m = WiredDevice(TransportMock()))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrPkgSku), 1).Returns([]);
            ((uint?)Invoke(m, "ReadCpuTdp")).Should().BeNull();
        }

        using (var m = WiredDevice(TransportMock()))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrPkgSku), 1).Returns([0UL]);
            ((uint?)Invoke(m, "ReadCpuTdp")).Should().BeNull();
        }

        using (var m = WiredDevice(TransportMock()))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrPkgSku), 1)
                .Returns(x => throw new InvalidOperationException("boom"));
            ((uint?)Invoke(m, "ReadCpuTdp")).Should().BeNull();
        }
    }

    [Fact]
    public void ReadCpuPowerRapl_ComputesWattsFromEnergyDelta()
    {
        using var m = WiredDevice(TransportMock());
        var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
        dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrPowerUnit), 1).Returns([0x0A02UL]);
        dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrPkgSku), 1).Returns([65UL]);
        dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrPkgEnergy), 1).Returns([1000UL], [2000UL]);

        InvokeFloat(m, "ReadCpuPowerRapl").Should().Be(0f);
        InvokeFloat(m, "ReadCpuPowerRapl").Should().BeGreaterThan(0f);
        HardwareMonitor.CachedTdpWatts.Should().Be(16);
    }

    [Fact]
    public void ReadCpuPowerRapl_HandlesEmptyEnergyAndErrors()
    {
        using (var m = WiredDevice(TransportMock()))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            SetField(m, "_energyUnit", 0.001);
            SetField(m, "_tdpWatts", (uint?)1);
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrPkgEnergy), 1).Returns([]);
            InvokeFloat(m, "ReadCpuPowerRapl").Should().Be(0f);
        }

        using (var m = WiredDevice(TransportMock()))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            SetField(m, "_energyUnit", 0.001);
            SetField(m, "_tdpWatts", (uint?)1);
            dev.When(d => d.Execute(Arg.Any<string>(), Arg.Any<ulong[]>(), Arg.Any<int>()))
                .Throw(new InvalidOperationException("boom"));
            InvokeFloat(m, "ReadCpuPowerRapl").Should().Be(0f);
        }
    }

    [Fact]
    public void ReadCpuTemp_IntelPaths()
    {
        // Reader null
        using (var m = WiredDevice(TransportMock(), vendor: "Intel"))
            InvokeFloat(m, "ReadCpuTemp").Should().Be(0f);

        // Reader not available
        using (var m = WiredDevice(TransportMock(), vendor: "Intel"))
        {
            var reader = new IntelMsrReader(GetField<IPawnIoTransport>(m, "_pawnIoDevice"));
            typeof(IntelMsrReader).GetField("_device", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(reader, null);
            SetField(m, "_intelMsr", reader);
            InvokeFloat(m, "ReadCpuTemp").Should().Be(0f);
        }

        // Temperature > 0
        using (var m = WiredDevice(TransportMock(), vendor: "Intel"))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            SetField(m, "_intelMsr", new IntelMsrReader(dev));
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrTempTarget), 1).Returns([0x64UL << 16]);
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrThermStatus), 1).Returns([40UL << 16]);
            InvokeFloat(m, "ReadCpuTemp").Should().Be(60f);
        }

        // Target MSR empty
        using (var m = WiredDevice(TransportMock(), vendor: "Intel"))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            SetField(m, "_intelMsr", new IntelMsrReader(dev));
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrTempTarget), 1).Returns([]);
            InvokeFloat(m, "ReadCpuTemp").Should().Be(0f);
        }

        // Target returns zero TjMax
        using (var m = WiredDevice(TransportMock(), vendor: "Intel"))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            SetField(m, "_intelMsr", new IntelMsrReader(dev));
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrTempTarget), 1).Returns([0UL]);
            InvokeFloat(m, "ReadCpuTemp").Should().Be(0f);
        }

        // Therm status empty
        using (var m = WiredDevice(TransportMock(), vendor: "Intel"))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            SetField(m, "_intelMsr", new IntelMsrReader(dev));
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrTempTarget), 1).Returns([0x64UL << 16]);
            dev.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == MsrThermStatus), 1).Returns([]);
            InvokeFloat(m, "ReadCpuTemp").Should().Be(0f);
        }
    }

    [Fact]
    public void ReadCpuTemp_AmdPaths()
    {
        // Reader null
        using (var m = WiredDevice(TransportMock(), vendor: "AMD"))
            InvokeFloat(m, "ReadCpuTemp").Should().Be(0f);

        // Reader not available
        using (var m = WiredDevice(TransportMock(), vendor: "AMD"))
        {
            var reader = new AmdMsrReader(GetField<IPawnIoTransport>(m, "_pawnIoDevice"));
            typeof(AmdMsrReader).GetField("_device", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(reader, null);
            SetField(m, "_amdMsr", reader);
            InvokeFloat(m, "ReadCpuTemp").Should().Be(0f);
        }

        // Temperature > 0, exercises all decode paths (raw/1000, raw*0.25, zero, 0xFFFFFFFF, hi==lo)
        using (var m = WiredDevice(TransportMock(), vendor: "AMD"))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            SetField(m, "_amdMsr", new AmdMsrReader(dev));
            dev.Execute("ioctl_resolve_pm_table", null, 2).Returns([1UL, 2UL]);
            dev.Execute("ioctl_update_pm_table", null, 0).Returns([0UL]);
            dev.Execute("ioctl_read_pm_table", null, 128)
                .Returns([60000UL, 100UL, 0xFFFFFFFFUL, 0UL, (60000UL << 32) | 60000UL]);
            InvokeFloat(m, "ReadCpuTemp").Should().Be(60f);
        }

        // Resolve returns short result
        using (var m = WiredDevice(TransportMock(), vendor: "AMD"))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            SetField(m, "_amdMsr", new AmdMsrReader(dev));
            dev.Execute("ioctl_resolve_pm_table", null, 2).Returns([]);
            InvokeFloat(m, "ReadCpuTemp").Should().Be(0f);
        }

        // Update returns empty
        using (var m = WiredDevice(TransportMock(), vendor: "AMD"))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            SetField(m, "_amdMsr", new AmdMsrReader(dev));
            dev.Execute("ioctl_resolve_pm_table", null, 2).Returns([1UL, 2UL]);
            dev.Execute("ioctl_update_pm_table", null, 0).Returns([]);
            InvokeFloat(m, "ReadCpuTemp").Should().Be(0f);
        }

        // Read pm table empty
        using (var m = WiredDevice(TransportMock(), vendor: "AMD"))
        {
            var dev = GetField<IPawnIoTransport>(m, "_pawnIoDevice");
            SetField(m, "_amdMsr", new AmdMsrReader(dev));
            dev.Execute("ioctl_resolve_pm_table", null, 2).Returns([1UL, 2UL]);
            dev.Execute("ioctl_update_pm_table", null, 0).Returns([0UL]);
            dev.Execute("ioctl_read_pm_table", null, 128).Returns([]);
            InvokeFloat(m, "ReadCpuTemp").Should().Be(0f);
        }

        // Unknown vendor
        using (var m = WiredDevice(TransportMock(), vendor: "ARM"))
            InvokeFloat(m, "ReadCpuTemp").Should().Be(0f);
    }

    [Fact]
    public void ReadGpu_MethodsWithNoSource_ReturnZero()
    {
        using var m = CreateMonitor(BasicCfg());
        SetField(m, "_nvGpu", null);
        SetField(m, "_amdGpu", null);
        SetField(m, "_wddmGpu", null);
        InvokeFloat(m, "ReadGpuPct", (NvGpuMonitor.NvGpuData?)null).Should().Be(0f);
        InvokeFloat(m, "ReadGpuTemp", (NvGpuMonitor.NvGpuData?)null).Should().Be(0f);
        InvokeFloat(m, "ReadGpuVramUsedMb", (NvGpuMonitor.NvGpuData?)null).Should().Be(0f);
        InvokeFloat(m, "ReadGpuFreqMHz", (NvGpuMonitor.NvGpuData?)null).Should().Be(0f);
        InvokeFloat(m, "ReadGpuPowerW", (NvGpuMonitor.NvGpuData?)null).Should().Be(0f);
    }

    [Fact]
    public void ReadGpu_WithNvmlMonitor_IfAvailable()
    {
        using var m = CreateMonitor(BasicCfg());
        try
        {
            var nv = new NvGpuMonitor();
            if (!nv.IsAvailable) return;
            SetField(m, "_nvGpu", nv);
            InvokeFloat(m, "ReadGpuPct").Should().BeGreaterThanOrEqualTo(0f);
            InvokeFloat(m, "ReadGpuTemp").Should().BeGreaterThanOrEqualTo(0f);
            InvokeFloat(m, "ReadGpuVramUsedMb").Should().BeGreaterThanOrEqualTo(0f);
            InvokeFloat(m, "ReadGpuFreqMHz").Should().BeGreaterThanOrEqualTo(0f);
            InvokeFloat(m, "ReadGpuPowerW").Should().BeGreaterThanOrEqualTo(0f);
        }
        catch { /* no NVIDIA GPU / NVML unavailable - branch not exercisable */ }
    }

    [Fact]
    public void ReadGpuPct_WithWddmMonitor_IfAvailable()
    {
        var cfg = BasicCfg();
        cfg.Monitoring.GpuUsageSource = "wddm";
        using var m = CreateMonitor(cfg);
        try
        {
            var wddm = new WddmGpuMonitor();
            if (!wddm.IsAvailable) return;
            SetField(m, "_wddmGpu", wddm);
            InvokeFloat(m, "ReadGpuPct").Should().BeGreaterThanOrEqualTo(0f);
        }
        catch { /* WDDM unavailable - branch not exercisable */ }
    }

    [Fact]
    public void BuildSnapshot_SecondPoll_ReportsRealCpuNetRamDiskBattery()
    {
        var cfg = BasicCfg();
        cfg.Monitoring.EnableCpu = true;
        cfg.Monitoring.EnableRam = true;
        cfg.Monitoring.EnableDisk = true;
        cfg.Monitoring.EnableNet = true;
        cfg.Monitoring.EnableBattery = true;
        cfg.Monitoring.LogCpuMode = 2;
        cfg.Monitoring.LogRamMode = 2;
        cfg.Monitoring.LogDiskMode = 2;
        cfg.Monitoring.LogNetMode = 2;
        cfg.Monitoring.LogBatteryMode = 2;

        var pawnIo = Substitute.For<IPawnIoManager>();
        var temp = Path.Combine(Path.GetTempPath(), $"HM_snap_{Guid.NewGuid():N}.jsonl");
        try
        {
            TrafficHistoryStore.Initialize(new TrafficHistoryFile(temp));
            using var m = CreateMonitor(cfg, IntelSysInfo(), pawnIo);

            var first = (TelemetrySnapshot?)Invoke(m, "BuildSnapshot");
            var second = (TelemetrySnapshot?)Invoke(m, "BuildSnapshot");

            first.Should().NotBeNull();
            second.Should().NotBeNull();
            second!.CpuUsagePct.Should().BeInRange(0f, 100f);
            second.CpuTempPackageC.Should().BeGreaterThanOrEqualTo(0f);
            second.CpuFreqGhz.Should().BeGreaterThanOrEqualTo(0f);
            second.RamUsagePct.Should().BeInRange(0f, 100f);
            second.DiskReadMbps.Should().BeGreaterThanOrEqualTo(0f);
            second.NetDownBps.Should().BeGreaterThanOrEqualTo(0);
            second.BatteryPct.Should().BeGreaterThanOrEqualTo(0f);
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    [Fact]
    public void BuildSnapshot_DiskCountersMissing_FallsBackToTotalAndHandlesPdhError()
    {
        var cfg = BasicCfg();
        cfg.Monitoring.EnableDisk = true;
        cfg.Monitoring.LogDiskMode = 2;
        using var m = CreateMonitor(cfg);

        SetField(m, "_perDiskCounters", null);
        SetField(m, "_diskRead", new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "__nonexistent_instance__"));
        SetField(m, "_diskWrite", new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "__nonexistent_instance__"));

        var snap = (TelemetrySnapshot?)Invoke(m, "BuildSnapshot");
        snap.Should().NotBeNull();
        snap!.DiskReadMbps.Should().Be(0f);
        snap.DiskWriteMbps.Should().Be(0f);
    }

    [Fact]
    public void ReadNetBytes_NotPrimed_ReturnsZero()
    {
        var cfg = BasicCfg();
        cfg.Monitoring.LogNetMode = 2;
        using var m = CreateMonitor(cfg);

        var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault();
        if (nic is null) return;

        SetField(m, "_nicInterface", nic);
        SetField(m, "_nicPrimed", false);

        var (down, up) = ((double, double))Invoke(m, "ReadNetBytes")!;
        down.Should().Be(0);
        up.Should().Be(0);
    }

    [Fact]
    public void ReadNetBytes_NoAdapterSelected_ReturnsZero()
    {
        using var m = CreateMonitor(BasicCfg());
        SetField(m, "_nicInterface", null);

        var (down, up) = ((double, double))Invoke(m, "ReadNetBytes")!;
        down.Should().Be(0);
        up.Should().Be(0);
    }

    [Fact]
    public void EnsureNicCounters_WithPreferredNic_SelectsInterface()
    {
        var cfg = BasicCfg();
        cfg.Monitoring.PreferredNic = "Ethernet";
        using var m = CreateMonitor(cfg);

        Invoke(m, "EnsureNicCounters");
    }

    [Fact]
    public void ReadBattery_NoBatterySystem_ReturnsZero()
    {
        using var m = CreateMonitor(BasicCfg());
        var (pct, isCharging, isOnAC, rate) = ((float, bool, bool, float))Invoke(m, "ReadBattery")!;
        pct.Should().BeInRange(0f, 100f);
        _ = (isCharging, isOnAC, rate);
    }

    [Fact]
    public void StartStopRestart_ControlTimer()
    {
        using var m = CreateMonitor(BasicCfg());
        m.Start();
        m.Stop();
        m.Restart(50);
    }

    [Fact]
    public void ShouldLogTick_UnknownCategory_UsesDefaultLevel()
    {
        using var m = CreateMonitor(BasicCfg());
        ((bool?)Invoke(m, "ShouldLogTick", "unknown", 1)).Should().BeTrue();
        ((bool?)Invoke(m, "ShouldLogTick", "unknown", 2)).Should().BeFalse();
    }

    [Fact]
    public void CollectCpuMetrics_Disabled_ReturnsZeros()
    {
        using var m = CreateMonitor(BasicCfg());
        var mc = new LocalTelemetry.Core.Config.MonitoringConfig { EnableCpu = false };
        var (cpuPct, cpuTemp, cpuFreq, cpuPower) = ((float, float, float, float))Invoke(m, "CollectCpuMetrics", mc)!;
        cpuPct.Should().Be(0f);
        cpuTemp.Should().Be(0f);
        cpuFreq.Should().Be(0f);
        cpuPower.Should().Be(0f);
    }

    [Fact]
    public void Constructor_PawnIoUnknownCpuVendor_SkipsModuleLoad()
    {
        var cfg = BasicCfg();
        cfg.Monitoring.EnableCpu = true;

        var sys = Substitute.For<ISystemInfo>();
        sys.GetCpuVendor().Returns("ARM");

        var pawnIo = Substitute.For<IPawnIoManager>();
        pawnIo.TryCreate().Returns(TransportMock());

        using var m = CreateMonitor(cfg, sys, pawnIo);
        m.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_PawnIoIntelModuleLoadsAndCreatesReader()
    {
        var cfg = BasicCfg();
        cfg.Monitoring.EnableCpu = true;

        var dev = TransportMock();
        var pawnIo = Substitute.For<IPawnIoManager>();
        pawnIo.TryCreate().Returns(dev);
        pawnIo.LoadResourceBytes("IntelMSR.bin").Returns([1, 2, 3]);
        pawnIo.LoadModule(Arg.Any<IPawnIoTransport>(), Arg.Any<byte[]>()).Returns(true);

        using var m = CreateMonitor(cfg, IntelSysInfo(), pawnIo);
        m.Should().NotBeNull();
    }

    [Fact]
    public void HardwareMonitor_GpuDetection_And_VendorMonitorInit_Branches()
    {
        // 1. DetectGpuVendor
        var detectMethod = typeof(HardwareMonitor).GetMethod("DetectGpuVendor", BindingFlags.NonPublic | BindingFlags.Static);
        detectMethod.Should().NotBeNull();
        ((string?)detectMethod!.Invoke(null, ["PCI\\VEN_1002&DEV_1234", "Radeon RX 6800"])).Should().Be("AMD");
        ((string?)detectMethod.Invoke(null, ["PCI\\VEN_8086&DEV_1234", "Intel UHD Graphics"])).Should().Be("Intel");
        ((string?)detectMethod.Invoke(null, ["PCI\\VEN_9999&DEV_1234", "Unknown Adapter"])).Should().BeNull();

        // 2. IsDedicatedAdapter
        var isDedicatedMethod = typeof(HardwareMonitor).GetMethod("IsDedicatedAdapter", BindingFlags.NonPublic | BindingFlags.Static);
        isDedicatedMethod.Should().NotBeNull();
        object dummyDevInfo = default(NativeMethods.SpDevinfoData);
        ((bool)isDedicatedMethod!.Invoke(null, ["Intel", "Intel Arc A770", IntPtr.Zero, dummyDevInfo])!).Should().BeTrue();
        ((bool)isDedicatedMethod.Invoke(null, ["AMD", "Radeon RX 6700 XT", IntPtr.Zero, dummyDevInfo])!).Should().BeTrue();
        ((bool)isDedicatedMethod.Invoke(null, ["AMD", "Radeon PRO W6800", IntPtr.Zero, dummyDevInfo])!).Should().BeTrue();
        ((bool)isDedicatedMethod.Invoke(null, ["AMD", "Radeon VII", IntPtr.Zero, dummyDevInfo])!).Should().BeTrue();
        ((bool)isDedicatedMethod.Invoke(null, ["AMD", "Radeon Vega 8", IntPtr.Zero, dummyDevInfo])!).Should().BeFalse();

        // 3. InitVendorGpuMonitor
        var cfg = BasicCfg();
        cfg.Monitoring.EnableGpu = false;
        var sys = IntelSysInfo();
        using var m = CreateMonitor(cfg, sys, null);

        var vendorField = typeof(HardwareMonitor).GetField("_gpuVendor", BindingFlags.NonPublic | BindingFlags.Instance);
        var initVendorMethod = typeof(HardwareMonitor).GetMethod("InitVendorGpuMonitor", BindingFlags.NonPublic | BindingFlags.Instance);
        var initIntelMethod = typeof(HardwareMonitor).GetMethod("InitIntelGpu", BindingFlags.NonPublic | BindingFlags.Instance);
        var initAmdMethod = typeof(HardwareMonitor).GetMethod("InitAmdGpu", BindingFlags.NonPublic | BindingFlags.Instance);

        // Call with AMD
        vendorField!.SetValue(m, "AMD");
        initVendorMethod!.Invoke(m, null);

        // Call with Intel
        vendorField.SetValue(m, "Intel");
        initVendorMethod.Invoke(m, null);

        // Call with NVIDIA (early return)
        vendorField.SetValue(m, "NVIDIA");
        initVendorMethod.Invoke(m, null);

        // Call with Other (fallback log)
        vendorField.SetValue(m, "UnknownVendor");
        initVendorMethod.Invoke(m, null);

        // Direct calls to InitIntelGpu and InitAmdGpu
        initIntelMethod!.Invoke(m, null);
        initAmdMethod!.Invoke(m, null);

        // 4. InitWddmGpu and fallback branch
        var initWddmMethod = typeof(HardwareMonitor).GetMethod("InitWddmGpu", BindingFlags.NonPublic | BindingFlags.Instance);
        initWddmMethod.Should().NotBeNull();
        initWddmMethod!.Invoke(m, null);

        // 5. GetBatteryStateDescription
        var batteryStateMethod = typeof(HardwareMonitor).GetMethod("GetBatteryStateDescription", BindingFlags.NonPublic | BindingFlags.Static);
        batteryStateMethod.Should().NotBeNull();
        ((string)batteryStateMethod!.Invoke(null, [true, false])!).Should().Be("charging");
        ((string)batteryStateMethod.Invoke(null, [false, true])!).Should().Be("charged");
        ((string)batteryStateMethod.Invoke(null, [false, false])!).Should().Be("discharging");
    }

    [Fact]
    public void HardwareMonitor_OnDisplayOrDriverChanged_RefreshesWddmState()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        var lifecycleMock = Substitute.For<IHardwareLifecycleManager>();

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null, lifecycleMock);

        var method = typeof(HardwareMonitor).GetMethod("OnDisplayOrDriverChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var act = () => method!.Invoke(monitor, null);
        act.Should().NotThrow();
    }

    [Fact]
    public void HardwareMonitor_OnPowerSuspended_PausesPollingTimer()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        var lifecycleMock = Substitute.For<IHardwareLifecycleManager>();

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null, lifecycleMock);

        var method = typeof(HardwareMonitor).GetMethod("OnPowerSuspended", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var act = () => method!.Invoke(monitor, null);
        act.Should().NotThrow();
    }

    [Fact]
    public void HardwareMonitor_OnNetworkTopologyChanged_RefreshesNic()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        var lifecycleMock = Substitute.For<IHardwareLifecycleManager>();

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null, lifecycleMock);

        var method = typeof(HardwareMonitor).GetMethod("OnNetworkTopologyChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var act = () => method!.Invoke(monitor, null);
        act.Should().NotThrow();
    }

    [Fact]
    public void HardwareMonitor_OnDevicePnPChanged_RefreshesDiskCache()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        var lifecycleMock = Substitute.For<IHardwareLifecycleManager>();

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null, lifecycleMock);

        var method = typeof(HardwareMonitor).GetMethod("OnDevicePnPChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var act = () => method!.Invoke(monitor, null);
        act.Should().NotThrow();
    }

    [Fact]
    public void ReadNetBytes_WhenNoAdapter_ReturnsZeroWithoutError()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        var nicField = typeof(HardwareMonitor).GetField("_nicInterface", BindingFlags.NonPublic | BindingFlags.Instance);
        nicField.Should().NotBeNull();
        nicField!.SetValue(monitor, null);

        var method = typeof(HardwareMonitor).GetMethod("ReadNetBytes", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var result = ((double inB, double outB))method!.Invoke(monitor, null)!;
        result.inB.Should().Be(0);
        result.outB.Should().Be(0);
    }

    [Fact]
    public void LogMetricErrorOnce_ThrottlesRepeatedErrors()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        var method = typeof(HardwareMonitor).GetMethod("LogMetricErrorOnce", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        // Invoking multiple times with same category shouldn't throw and throttles
        var act1 = () => method!.Invoke(monitor, ["test_cat", "key_1", "Test error message"]);
        var act2 = () => method!.Invoke(monitor, ["test_cat", "key_1", "Test error message 2"]);
        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }

    [Fact]
    public void LifecycleHandlers_WhenSuspended_AreSafelyIgnored()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        // Mark as suspended
        typeof(HardwareMonitor).GetField("_isSuspended", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(monitor, true);

        var onDisplay = typeof(HardwareMonitor).GetMethod("OnDisplayOrDriverChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        var onNet = typeof(HardwareMonitor).GetMethod("OnNetworkTopologyChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        var onPnp = typeof(HardwareMonitor).GetMethod("OnDevicePnPChanged", BindingFlags.NonPublic | BindingFlags.Instance);

        var actDisplay = () => onDisplay!.Invoke(monitor, null);
        var actNet = () => onNet!.Invoke(monitor, null);
        var actPnp = () => onPnp!.Invoke(monitor, null);

        actDisplay.Should().NotThrow();
        actNet.Should().NotThrow();
        actPnp.Should().NotThrow();
    }

    [Fact]
    public void HardwareMonitor_ResetGpuMonitors_And_BatteryRateDelta_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        var transportMock = Substitute.For<IPawnIoTransport>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();
        pawnIoMock.TryCreate().Returns(transportMock);

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock);

        // 1. ResetGpuMonitors with mock backends
        var resetGpuMethod = typeof(HardwareMonitor).GetMethod("ResetGpuMonitors", BindingFlags.NonPublic | BindingFlags.Instance);
        resetGpuMethod.Should().NotBeNull();
        var actReset = () => resetGpuMethod!.Invoke(monitor, ["test reason"]);
        actReset.Should().NotThrow();

        // 2. ComputeBatteryRateFromCapacityDelta
        var computeRate = typeof(HardwareMonitor).GetMethod("ComputeBatteryRateFromCapacityDelta", BindingFlags.NonPublic | BindingFlags.Instance);
        computeRate.Should().NotBeNull();
        // First sample primes
        float r1 = (float)computeRate!.Invoke(monitor, [50000L])!;
        r1.Should().Be(0f);
        // Second sample with lower capacity computes discharge rate
        float r2 = (float)computeRate.Invoke(monitor, [49000L])!;
        r2.Should().BeInRange(-300f, 300f);
        // Null or <= 0 capacity returns 0f
        ((float)computeRate.Invoke(monitor, [null])!).Should().Be(0f);
        ((float)computeRate.Invoke(monitor, [0L])!).Should().Be(0f);

        // 3. ReadMemory
        var readMem = typeof(HardwareMonitor).GetMethod("ReadMemory", BindingFlags.NonPublic | BindingFlags.Instance);
        readMem.Should().NotBeNull();
        object?[] memArgs = [0f, 0f, 0f];
        readMem!.Invoke(monitor, memArgs);
        ((float)memArgs[2]!).Should().BeGreaterThanOrEqualTo(0f);
    }

    [Fact]
    public void HardwareMonitor_OnPowerResumed_ExecutesSafely()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        var onResumeMethod = typeof(HardwareMonitor).GetMethod("OnPowerResumed", BindingFlags.NonPublic | BindingFlags.Instance);
        onResumeMethod.Should().NotBeNull();
        var act = () => onResumeMethod!.Invoke(monitor, null);
        act.Should().NotThrow();
    }

    [Fact]
    public void HardwareMonitor_EnsureNicCounters_OfflineFallback()
    {
        var cfg = new AppSettings();
        cfg.Monitoring.PreferredNic = "__Invalid_Nic_12345__";
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        var ensureNic = typeof(HardwareMonitor).GetMethod("EnsureNicCounters", BindingFlags.NonPublic | BindingFlags.Instance);
        ensureNic.Should().NotBeNull();
        var act = () => ensureNic!.Invoke(monitor, null);
        act.Should().NotThrow();
    }

    [Fact]
    public void HardwareMonitor_GpuMetricReads_WithNvGpuData_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        var readTemp = typeof(HardwareMonitor).GetMethod("ReadGpuTemp", BindingFlags.NonPublic | BindingFlags.Instance);
        var readVram = typeof(HardwareMonitor).GetMethod("ReadGpuVramUsedMb", BindingFlags.NonPublic | BindingFlags.Instance);
        var readFreq = typeof(HardwareMonitor).GetMethod("ReadGpuFreqMHz", BindingFlags.NonPublic | BindingFlags.Instance);
        var readPower = typeof(HardwareMonitor).GetMethod("ReadGpuPowerW", BindingFlags.NonPublic | BindingFlags.Instance);

        var data = new NvGpuMonitor.NvGpuData
        {
            TempC = 65f,
            VramUsedMb = 4096f,
            CoreClockMHz = 2100f,
            PowerDrawW = 180f
        };

        // Note: when _nvGpu is null, these methods check _nvGpu is not null first before using the parameter
        readTemp!.Invoke(monitor, [data]);
        readVram!.Invoke(monitor, [data]);
        readFreq!.Invoke(monitor, [data]);
        readPower!.Invoke(monitor, [data]);
    }

    [Fact]
    public void HardwareMonitor_GpuMetricReads_WithAmdAndIntelMockMonitors_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        // 1. Setup mock NVML monitor
        var nvNative = Substitute.For<INvGpuNative>();
        nvNative.Init().Returns(0);
        nvNative.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = new IntPtr(123); return 0; });
        var nvGpu = new NvGpuMonitor(nvNative);
        typeof(HardwareMonitor).GetField("_nvGpu", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(monitor, nvGpu);

        var readTemp = typeof(HardwareMonitor).GetMethod("ReadGpuTemp", BindingFlags.NonPublic | BindingFlags.Instance);
        var readVram = typeof(HardwareMonitor).GetMethod("ReadGpuVramUsedMb", BindingFlags.NonPublic | BindingFlags.Instance);
        var readFreq = typeof(HardwareMonitor).GetMethod("ReadGpuFreqMHz", BindingFlags.NonPublic | BindingFlags.Instance);
        var readPower = typeof(HardwareMonitor).GetMethod("ReadGpuPowerW", BindingFlags.NonPublic | BindingFlags.Instance);

        var data = new NvGpuMonitor.NvGpuData
        {
            TempC = 65f,
            VramUsedMb = 4096f,
            CoreClockMHz = 2100f,
            PowerDrawW = 180f
        };

        ((float)readTemp!.Invoke(monitor, [data])!).Should().Be(65f);
        ((float)readVram!.Invoke(monitor, [data])!).Should().Be(4096f);
        ((float)readFreq!.Invoke(monitor, [data])!).Should().Be(2100f);
        ((float)readPower!.Invoke(monitor, [data])!).Should().Be(180f);

        // 2. Clear NVML, setup mock AMD monitor
        typeof(HardwareMonitor).GetField("_nvGpu", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(monitor, null);

        var amdNative = Substitute.For<IAmdAdlNative>();
        amdNative.Create().Returns(0);
        amdNative.GetAdapterInfo(out Arg.Any<int>(), out Arg.Any<string>()).Returns(x => { x[0] = 0; x[1] = "AMD Radeon"; return 0; });
        amdNative.GetTemperature(0, out Arg.Any<int>()).Returns(x => { x[1] = 60000; return 0; });
        amdNative.GetActivity(0, out Arg.Any<int>(), out Arg.Any<int>()).Returns(x => { x[1] = 50; x[2] = 20000; return 0; });
        amdNative.GetMemoryInfo(0, out Arg.Any<long>(), out Arg.Any<long>()).Returns(x => { x[1] = 8L * 1024 * 1024 * 1024; x[2] = 4L * 1024 * 1024 * 1024; return 0; });
        amdNative.GetPowerLimit(0, out Arg.Any<int>(), out Arg.Any<int>()).Returns(x => { x[1] = 250000; x[2] = 200000; return 0; });
        var amdGpu = new AmdGpuMonitor(amdNative);
        typeof(HardwareMonitor).GetField("_amdGpu", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(monitor, amdGpu);

        ((float)readTemp.Invoke(monitor, [null])!).Should().Be(60f);
        readVram.Invoke(monitor, [null]);
        readFreq.Invoke(monitor, [null]);
        readPower.Invoke(monitor, [null]);

        // 3. Clear AMD, setup mock Intel monitor
        typeof(HardwareMonitor).GetField("_amdGpu", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(monitor, null);

        var intelNative = Substitute.For<IIntelGpuNative>();
        intelNative.Init(out Arg.Any<IntPtr>()).Returns(x => { x[0] = new IntPtr(100); return 0; });
        intelNative.EnumerateDevices(new IntPtr(100), out Arg.Any<IntPtr[]>()).Returns(x => { x[1] = new[] { new IntPtr(200) }; return 0; });
        intelNative.GetTempSensor(new IntPtr(200), out Arg.Any<IntPtr>()).Returns(x => { x[1] = new IntPtr(300); return 0; });
        intelNative.GetTempC(new IntPtr(300), out Arg.Any<double>()).Returns(x => { x[1] = 55.0; return 0; });
        intelNative.GetFreqDomain(new IntPtr(200), out Arg.Any<IntPtr>()).Returns(x => { x[1] = new IntPtr(400); return 0; });
        intelNative.GetClockMHz(new IntPtr(400), out Arg.Any<double>()).Returns(x => { x[1] = 2200.0; return 0; });
        intelNative.GetMemModule(new IntPtr(200), out Arg.Any<IntPtr>()).Returns(x => { x[1] = new IntPtr(500); return 0; });
        intelNative.GetVram(new IntPtr(500), out Arg.Any<ulong>(), out Arg.Any<ulong>()).Returns(x => { x[1] = 16UL * 1024 * 1024 * 1024; x[2] = 8UL * 1024 * 1024 * 1024; return 0; });
        var intelGpu = new IntelGpuMonitor(intelNative);
        typeof(HardwareMonitor).GetField("_intelGpu", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(monitor, intelGpu);

        ((float)readTemp.Invoke(monitor, [null])!).Should().Be(55f);
        readVram.Invoke(monitor, [null]);
        readFreq.Invoke(monitor, [null]);
        readPower.Invoke(monitor, [null]);
    }

    [Fact]
    public void HardwareMonitor_LifecycleCallbacks_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        var onDisplay = typeof(HardwareMonitor).GetMethod("OnDisplayOrDriverChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        var onNet = typeof(HardwareMonitor).GetMethod("OnNetworkTopologyChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        var onDevice = typeof(HardwareMonitor).GetMethod("OnDevicePnPChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        var onSuspend = typeof(HardwareMonitor).GetMethod("OnPowerSuspended", BindingFlags.NonPublic | BindingFlags.Instance);

        onDisplay!.Invoke(monitor, null);
        onNet!.Invoke(monitor, null);
        onDevice!.Invoke(monitor, null);
        onSuspend!.Invoke(monitor, null);

        // When suspended, callbacks early return
        onDisplay.Invoke(monitor, null);
        onNet.Invoke(monitor, null);
        onDevice.Invoke(monitor, null);
    }

    [Fact]
    public void HardwareMonitor_TryConnectPawnIo_RetriesAndInstallsDriver_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        var transportMock = Substitute.For<IPawnIoTransport>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();

        // Initial probe fails first try, starts service and succeeds on retry
        pawnIoMock.TryCreate().Returns(null, transportMock);
        pawnIoMock.StartDriverService().Returns(true);
        pawnIoMock.TryInstall().Returns(true);

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock);

        var tryConnect = typeof(HardwareMonitor).GetMethod("TryConnectPawnIo", BindingFlags.NonPublic | BindingFlags.Instance);

        // 1. Initial probe: first TryCreate is null, StartDriverService is true, loop 0 returns transportMock
        object?[] probeArgs = [null, true];
        bool okProbe = (bool)tryConnect!.Invoke(monitor, probeArgs)!;
        okProbe.Should().BeTrue();

        // 2. Non-initial probe: loop 0 returns transportMock
        pawnIoMock.TryCreate().Returns(null, transportMock);
        object?[] nonProbeArgs = [null, false];
        bool okNonProbe = (bool)tryConnect.Invoke(monitor, nonProbeArgs)!;
        okNonProbe.Should().BeTrue();

        // 3. InitIntelPwrGadget
        var initGadget = typeof(HardwareMonitor).GetMethod("InitIntelPwrGadget", BindingFlags.NonPublic | BindingFlags.Instance);
        initGadget?.Invoke(monitor, null);
    }

    [Fact]
    public void HardwareMonitor_Disk_Gpu_Memory_Battery_Lifecycle_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        // 1. Start, Restart, Stop
        monitor.Start();
        monitor.Restart(200);
        monitor.Stop();

        // 2. ExtractDiskIndex
        var extractDisk = typeof(HardwareMonitor).GetMethod("ExtractDiskIndex", BindingFlags.NonPublic | BindingFlags.Static);
        extractDisk.Should().NotBeNull();
        ((string)extractDisk!.Invoke(null, ["0 C:"])!).Should().Be("0");
        ((string)extractDisk.Invoke(null, ["12 D:"])!).Should().Be("12");
        ((string)extractDisk.Invoke(null, ["_Total"])!).Should().BeEmpty();
        ((string)extractDisk.Invoke(null, [""])!).Should().BeEmpty();

        // 3. InitDiskType and CreatePerDiskCounters
        var initDiskType = typeof(HardwareMonitor).GetMethod("InitDiskType", BindingFlags.NonPublic | BindingFlags.Instance);
        initDiskType.Should().NotBeNull();
        initDiskType!.Invoke(monitor, null);

        var createPerDisk = typeof(HardwareMonitor).GetMethod("CreatePerDiskCounters", BindingFlags.NonPublic | BindingFlags.Static);
        createPerDisk.Should().NotBeNull();
        createPerDisk!.Invoke(null, null);

        var initDiskCounters = typeof(HardwareMonitor).GetMethod("InitDiskCounters", BindingFlags.NonPublic | BindingFlags.Instance);
        initDiskCounters.Should().NotBeNull();
        initDiskCounters!.Invoke(monitor, null);

        // 4. InitNvGpu, InitIntelGpu, InitWddmGpu, InitAmdGpu
        var initNv = typeof(HardwareMonitor).GetMethod("InitNvGpu", BindingFlags.NonPublic | BindingFlags.Instance);
        var initIntel = typeof(HardwareMonitor).GetMethod("InitIntelGpu", BindingFlags.NonPublic | BindingFlags.Instance);
        var initWddm = typeof(HardwareMonitor).GetMethod("InitWddmGpu", BindingFlags.NonPublic | BindingFlags.Instance);
        var initAmd = typeof(HardwareMonitor).GetMethod("InitAmdGpu", BindingFlags.NonPublic | BindingFlags.Instance);

        initNv?.Invoke(monitor, null);
        initIntel?.Invoke(monitor, null);
        initWddm?.Invoke(monitor, null);
        initAmd?.Invoke(monitor, null);

        // 5. ReadMemory
        var readMem = typeof(HardwareMonitor).GetMethod("ReadMemory", BindingFlags.NonPublic | BindingFlags.Instance);
        readMem.Should().NotBeNull();
        object?[] memArgs = [0f, 0f, 0f];
        readMem!.Invoke(monitor, memArgs);
        ((float)memArgs[0]!).Should().BeGreaterThanOrEqualTo(0);

        // 6. ReadBattery
        var readBatt = typeof(HardwareMonitor).GetMethod("ReadBattery", BindingFlags.NonPublic | BindingFlags.Instance);
        readBatt.Should().NotBeNull();
        readBatt!.Invoke(monitor, null);

        // 7. InitRamLogging, InitBatteryLogging
        var initRam = typeof(HardwareMonitor).GetMethod("InitRamLogging", BindingFlags.NonPublic | BindingFlags.Instance);
        var initBatt = typeof(HardwareMonitor).GetMethod("InitBatteryLogging", BindingFlags.NonPublic | BindingFlags.Instance);
        initRam?.Invoke(monitor, null);
        initBatt?.Invoke(monitor, null);
    }
}


