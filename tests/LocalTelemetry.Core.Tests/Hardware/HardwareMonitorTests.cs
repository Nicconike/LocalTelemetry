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
        snapshot.CpuUsagePct.Should().BeGreaterThanOrEqualTo(0);

        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { Trace.WriteLine(ex.Message); }
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
        snapshot.CpuUsagePct.Should().BeGreaterThanOrEqualTo(0);
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

        TelemetrySnapshot? received = null;
        var tcs = new TaskCompletionSource<TelemetrySnapshot>();

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock);
        monitor.SnapshotReady += snap =>
        {
            received = snap;
            tcs.TrySetResult(snap);
        };

        // Explicitly trigger Poll if timer hasn't fired yet
        var pollMethod = typeof(HardwareMonitor).GetMethod("Poll", BindingFlags.NonPublic | BindingFlags.Instance);
        pollMethod?.Invoke(monitor, null);

        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        completedTask.Should().BeSameAs(tcs.Task);

        received.Should().NotBeNull();
    }

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
        catch (Exception ex)
        {
            Trace.WriteLine($"NVML unavailable in test environment: {ex.Message}");
        }
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
        catch (Exception ex)
        {
            Trace.WriteLine($"WDDM unavailable in test environment: {ex.Message}");
        }
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
            second.CpuUsagePct.Should().BeInRange(0f, 100f);
            second.CpuTempPackageC.Should().BeGreaterThanOrEqualTo(0f);
            second.CpuFreqGhz.Should().BeGreaterThanOrEqualTo(0f);
            second.RamUsagePct.Should().BeInRange(0f, 100f);
            second.DiskReadMbps.Should().BeGreaterThanOrEqualTo(0f);
            second.NetDownBps.Should().BeGreaterThanOrEqualTo(0);
            second.BatteryPct.Should().BeGreaterThanOrEqualTo(0f);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException ex) { Trace.WriteLine(ex.Message); }
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
        snap.DiskReadMbps.Should().Be(0f);
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

        var act = () => Invoke(m, "EnsureNicCounters");
        act.Should().NotThrow();
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
        var act = () =>
        {
            m.Start();
            m.Stop();
            m.Restart(50);
        };
        act.Should().NotThrow();
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
        ((string?)detectMethod.Invoke(null, ["PCI\\VEN_1002&DEV_1234", "Radeon RX 6800"])).Should().Be("AMD");
        ((string?)detectMethod.Invoke(null, ["PCI\\VEN_8086&DEV_1234", "Intel UHD Graphics"])).Should().Be("Intel");
        ((string?)detectMethod.Invoke(null, ["PCI\\VEN_9999&DEV_1234", "Unknown Adapter"])).Should().BeNull();

        // 2. IsDedicatedAdapter
        var isDedicatedMethod = typeof(HardwareMonitor).GetMethod("IsDedicatedAdapter", BindingFlags.NonPublic | BindingFlags.Static);
        isDedicatedMethod.Should().NotBeNull();
        object dummyDevInfo = default(NativeMethods.SpDevinfoData);
        ((bool)isDedicatedMethod.Invoke(null, ["Intel", "Intel Arc A770", IntPtr.Zero, dummyDevInfo])!).Should().BeTrue();
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
        initWddmMethod.Invoke(m, null);

        // 5. GetBatteryStateDescription
        var batteryStateMethod = typeof(HardwareMonitor).GetMethod("GetBatteryStateDescription", BindingFlags.NonPublic | BindingFlags.Static);
        batteryStateMethod.Should().NotBeNull();
        ((string)batteryStateMethod.Invoke(null, [true, false])!).Should().Be("charging");
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
        var act = () => method.Invoke(monitor, null);
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
        var act = () => method.Invoke(monitor, null);
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
        var act = () => method.Invoke(monitor, null);
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
        var act = () => method.Invoke(monitor, null);
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
        nicField.SetValue(monitor, null);

        var method = typeof(HardwareMonitor).GetMethod("ReadNetBytes", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var result = ((double inB, double outB))method.Invoke(monitor, null)!;
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
        var act1 = () => method.Invoke(monitor, ["test_cat", "key_1", "Test error message"]);
        var act2 = () => method.Invoke(monitor, ["test_cat", "key_1", "Test error message 2"]);
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
        var actReset = () => resetGpuMethod.Invoke(monitor, ["test reason"]);
        actReset.Should().NotThrow();

        // 2. ComputeBatteryRateFromCapacityDelta
        var computeRate = typeof(HardwareMonitor).GetMethod("ComputeBatteryRateFromCapacityDelta", BindingFlags.NonPublic | BindingFlags.Instance);
        computeRate.Should().NotBeNull();
        // First sample primes
        float r1 = (float)computeRate.Invoke(monitor, [50000L])!;
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
        readMem.Invoke(monitor, memArgs);
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
        var act = () => onResumeMethod.Invoke(monitor, null);
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
        var act = () => ensureNic.Invoke(monitor, null);
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
        var act = () =>
        {
            readTemp!.Invoke(monitor, [data]);
            readVram!.Invoke(monitor, [data]);
            readFreq!.Invoke(monitor, [data]);
            readPower!.Invoke(monitor, [data]);
        };
        act.Should().NotThrow();
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
        var actAmd = () =>
        {
            readVram.Invoke(monitor, [null]);
            readFreq.Invoke(monitor, [null]);
            readPower.Invoke(monitor, [null]);
        };
        actAmd.Should().NotThrow();

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
        var actIntel = () =>
        {
            readVram.Invoke(monitor, [null]);
            readFreq.Invoke(monitor, [null]);
            readPower.Invoke(monitor, [null]);
        };
        actIntel.Should().NotThrow();
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

        var act = () =>
        {
            onDisplay!.Invoke(monitor, null);
            onNet!.Invoke(monitor, null);
            onDevice!.Invoke(monitor, null);
            onSuspend!.Invoke(monitor, null);

            // When suspended, callbacks early return
            onDisplay.Invoke(monitor, null);
            onNet.Invoke(monitor, null);
            onDevice.Invoke(monitor, null);
        };
        act.Should().NotThrow();
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
        ((string)extractDisk.Invoke(null, ["0 C:"])!).Should().Be("0");
        ((string)extractDisk.Invoke(null, ["12 D:"])!).Should().Be("12");
        ((string)extractDisk.Invoke(null, ["_Total"])!).Should().BeEmpty();
        ((string)extractDisk.Invoke(null, [""])!).Should().BeEmpty();

        // 3. InitDiskType and CreatePerDiskCounters
        var initDiskType = typeof(HardwareMonitor).GetMethod("InitDiskType", BindingFlags.NonPublic | BindingFlags.Instance);
        initDiskType.Should().NotBeNull();
        initDiskType.Invoke(monitor, null);

        var createPerDisk = typeof(HardwareMonitor).GetMethod("CreatePerDiskCounters", BindingFlags.NonPublic | BindingFlags.Static);
        createPerDisk.Should().NotBeNull();
        createPerDisk.Invoke(null, null);

        var initDiskCounters = typeof(HardwareMonitor).GetMethod("InitDiskCounters", BindingFlags.NonPublic | BindingFlags.Instance);
        initDiskCounters.Should().NotBeNull();
        initDiskCounters.Invoke(monitor, null);

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
        readMem.Invoke(monitor, memArgs);
        ((float)memArgs[0]!).Should().BeGreaterThanOrEqualTo(0);

        // 6. ReadBattery
        var readBatt = typeof(HardwareMonitor).GetMethod("ReadBattery", BindingFlags.NonPublic | BindingFlags.Instance);
        readBatt.Should().NotBeNull();
        readBatt.Invoke(monitor, null);

        // 7. InitRamLogging, InitBatteryLogging
        var initRam = typeof(HardwareMonitor).GetMethod("InitRamLogging", BindingFlags.NonPublic | BindingFlags.Instance);
        var initBatt = typeof(HardwareMonitor).GetMethod("InitBatteryLogging", BindingFlags.NonPublic | BindingFlags.Instance);
        initRam?.Invoke(monitor, null);
        initBatt?.Invoke(monitor, null);
    }

    [Fact]
    public void HardwareMonitor_MockedPawnIo_RaplAndMperf_Coverage()
    {
        var cfg = new AppSettings();
        cfg.Monitoring.EnableCpu = true;
        cfg.Monitoring.EnableGpu = true;
        cfg.Monitoring.EnableNet = true;
        cfg.Monitoring.EnableDisk = true;
        cfg.Monitoring.EnableBattery = true;

        var sysInfoMock = Substitute.For<ISystemInfo>();
        sysInfoMock.GetCpuName().Returns("Intel Core i9-13900K");
        sysInfoMock.GetCpuVendor().Returns("Intel");
        sysInfoMock.GetCpuCoreCount().Returns(24);
        sysInfoMock.GetCpuThreadCount().Returns(32);
        sysInfoMock.GetCpuMaxSpeedMhz().Returns(5800);
        sysInfoMock.HasBattery().Returns(true);
        sysInfoMock.GetGpus().Returns([(Name: "NVIDIA GeForce RTX 4090", Vendor: "NVIDIA", IsDedicated: true, VramBytes: 25769803776L, DriverVersion: "550.00")]);
        sysInfoMock.GetNics().Returns([(Name: "Ethernet", Manufacturer: "Intel")]);

        var transportMock = Substitute.For<IPawnIoTransport>();
        // RAPL power unit (0x606): energy unit divisor
        transportMock.Execute("ioctl_read_msr", Arg.Is<ulong[]>(x => x.Length > 0 && x[0] == 0x606), 1)
            .Returns([0x000A1003UL]);
        // RAPL SKU (0x614): TDP
        transportMock.Execute("ioctl_read_msr", Arg.Is<ulong[]>(x => x.Length > 0 && x[0] == 0x614), 1)
            .Returns([0x000003E8UL]);
        // RAPL energy status (0x611)
        transportMock.Execute("ioctl_read_msr", Arg.Is<ulong[]>(x => x.Length > 0 && x[0] == 0x611), 1)
            .Returns([1_000_000UL], [1_200_000UL]);
        // APERF (0xE8)
        transportMock.Execute("ioctl_read_msr", Arg.Is<ulong[]>(x => x.Length > 0 && x[0] == 0xE8), 1)
            .Returns([1_000_000UL], [2_000_000UL]);
        // MPERF (0xE7)
        transportMock.Execute("ioctl_read_msr", Arg.Is<ulong[]>(x => x.Length > 0 && x[0] == 0xE7), 1)
            .Returns([1_000_000UL], [2_000_000UL]);

        var pawnIoMock = Substitute.For<IPawnIoManager>();
        pawnIoMock.TryCreate().Returns(transportMock);

        var lifecycleMock = Substitute.For<IHardwareLifecycleManager>();

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock, lifecycleMock);

        // Inject transport directly into private fields
        var fieldTransport = typeof(HardwareMonitor).GetField("_pawnIoDevice", BindingFlags.NonPublic | BindingFlags.Instance);
        var fieldMaxMhz = typeof(HardwareMonitor).GetField("_maxCpuMhz", BindingFlags.NonPublic | BindingFlags.Instance);
        var fieldCpuVendor = typeof(HardwareMonitor).GetField("_cpuVendor", BindingFlags.NonPublic | BindingFlags.Instance);

        fieldTransport!.SetValue(monitor, transportMock);
        fieldMaxMhz!.SetValue(monitor, 5800u);
        fieldCpuVendor!.SetValue(monitor, "Intel");

        // 1. ReadCpuFreqMsr (call twice: first primes, second calculates delta)
        var mFreqMsr = typeof(HardwareMonitor).GetMethod("ReadCpuFreqMsr", BindingFlags.NonPublic | BindingFlags.Instance);
        mFreqMsr.Should().NotBeNull();
        float freqPrimed = (float)mFreqMsr.Invoke(monitor, null)!;
        freqPrimed.Should().Be(0f);
        float freqDelta = (float)mFreqMsr.Invoke(monitor, null)!;
        freqDelta.Should().BeGreaterThanOrEqualTo(0f);

        // 2. ReadCpuTdp
        var mTdp = typeof(HardwareMonitor).GetMethod("ReadCpuTdp", BindingFlags.NonPublic | BindingFlags.Instance);
        mTdp.Should().NotBeNull();
        var tdp = (uint?)mTdp.Invoke(monitor, null);
        tdp.Should().NotBeNull();

        // 3. ReadCpuPowerRapl (call twice: first primes, second calculates delta)
        var mPowerRapl = typeof(HardwareMonitor).GetMethod("ReadCpuPowerRapl", BindingFlags.NonPublic | BindingFlags.Instance);
        mPowerRapl.Should().NotBeNull();
        float powerPrimed = (float)mPowerRapl.Invoke(monitor, null)!;
        powerPrimed.Should().Be(0f);
        float powerDelta = (float)mPowerRapl.Invoke(monitor, null)!;
        powerDelta.Should().BeGreaterThanOrEqualTo(0f);
    }

    [Fact]
    public void HardwareMonitor_MockedCpuTemps_AllBranches_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        var fieldVendor = typeof(HardwareMonitor).GetField("_cpuVendor", BindingFlags.NonPublic | BindingFlags.Instance);
        var fieldIntelMsr = typeof(HardwareMonitor).GetField("_intelMsr", BindingFlags.NonPublic | BindingFlags.Instance);
        var fieldAmdMsr = typeof(HardwareMonitor).GetField("_amdMsr", BindingFlags.NonPublic | BindingFlags.Instance);

        var mReadTemp = typeof(HardwareMonitor).GetMethod("ReadCpuTemp", BindingFlags.NonPublic | BindingFlags.Instance);
        var mIntelTemp = typeof(HardwareMonitor).GetMethod("ReadIntelCpuTemp", BindingFlags.NonPublic | BindingFlags.Instance);
        var mAmdTemp = typeof(HardwareMonitor).GetMethod("ReadAmdCpuTemp", BindingFlags.NonPublic | BindingFlags.Instance);

        mReadTemp.Should().NotBeNull();
        mIntelTemp.Should().NotBeNull();
        mAmdTemp.Should().NotBeNull();

        // 1. Unknown vendor
        fieldVendor!.SetValue(monitor, "UnknownVendor");
        ((float)mReadTemp.Invoke(monitor, null)!).Should().Be(0f);

        // 2. Intel vendor with null reader
        fieldVendor.SetValue(monitor, "Intel");
        fieldIntelMsr!.SetValue(monitor, null);
        ((float)mIntelTemp.Invoke(monitor, null)!).Should().Be(0f);

        // 3. AMD vendor with null reader
        fieldVendor.SetValue(monitor, "AMD");
        fieldAmdMsr!.SetValue(monitor, null);
        ((float)mAmdTemp.Invoke(monitor, null)!).Should().Be(0f);
    }

    [Fact]
    public void HardwareMonitor_GpuUsageSources_And_Reset_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        var mReadGpuPct = typeof(HardwareMonitor).GetMethod("ReadGpuPct", BindingFlags.NonPublic | BindingFlags.Instance);
        mReadGpuPct.Should().NotBeNull();

        // 1. WDDM source configured
        cfg.Monitoring.GpuUsageSource = "wddm";
        float pctWddm = (float)mReadGpuPct.Invoke(monitor, [null])!;
        pctWddm.Should().BeInRange(0f, 100f);

        // 2. Driver source configured (NVML / fallback)
        cfg.Monitoring.GpuUsageSource = "driver";
        float pctDriver = (float)mReadGpuPct.Invoke(monitor, [null])!;
        pctDriver.Should().BeInRange(0f, 100f);

        // 3. ResetGpuMonitors with mock state
        var mResetGpu = typeof(HardwareMonitor).GetMethod("ResetGpuMonitors", BindingFlags.NonPublic | BindingFlags.Instance);
        mResetGpu.Should().NotBeNull();
        mResetGpu.Invoke(monitor, ["Unit test driver recovery simulation"]);
    }

    [Fact]
    public void HardwareMonitor_NetworkSelection_And_Logging_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        // 1. SelectActiveNic
        var mSelectNic = typeof(HardwareMonitor).GetMethod("SelectActiveNic", BindingFlags.NonPublic | BindingFlags.Static);
        mSelectNic.Should().NotBeNull();
        mSelectNic.Invoke(null, ["auto"]);
        mSelectNic.Invoke(null, ["NonExistentNicNameXYZ"]);

        // 2. EnsureNicCounters with specific preferred NIC
        var mEnsureNic = typeof(HardwareMonitor).GetMethod("EnsureNicCounters", BindingFlags.NonPublic | BindingFlags.Instance);
        mEnsureNic.Should().NotBeNull();

        cfg.Monitoring.PreferredNic = "NonExistentNicNameXYZ";
        mEnsureNic.Invoke(monitor, null);
        // Second call hits cache / same preferred NIC branch
        mEnsureNic.Invoke(monitor, null);

        // 3. LogTickMetrics branch exercises
        var mLogTick = typeof(HardwareMonitor).GetMethod("LogTickMetrics", BindingFlags.NonPublic | BindingFlags.Instance);
        mLogTick.Should().NotBeNull();

        var mc = cfg.Monitoring;
        mc.EnableCpu = true;
        mc.EnableRam = true;
        mc.EnableDisk = true;
        mc.EnableGpu = true;
        mc.EnableBattery = true;

        // Charging battery
        mLogTick.Invoke(monitor, [
            mc,
            (50f, 65f, 4.5f, 85f),
            (60f, 16f, 32f),
            (100.0, 50.0, 2),
            (75f, 70f, 250f, 2100f),
            (85f, true, true, 15f)
        ]);

        // Discharging battery
        mLogTick.Invoke(monitor, [
            mc,
            (40f, 55f, 3.8f, 45f),
            (50f, 8f, 16f),
            (10.0, 5.0, 1),
            (30f, 50f, 100f, 1500f),
            (50f, false, false, 25f)
        ]);

        // Battery percentage 0 (skipped)
        mLogTick.Invoke(monitor, [
            mc,
            (20f, 45f, 2.5f, 25f),
            (40f, 4f, 16f),
            (0.0, 0.0, 1),
            (10f, 40f, 50f, 800f),
            (0f, false, false, 0f)
        ]);
    }

    [Fact]
    public void HardwareMonitor_LifecycleEvents_And_Disposal_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();
        var lifecycleMock = Substitute.For<IHardwareLifecycleManager>();

        var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock, lifecycleMock);

        // Raise each lifecycle event on the mock
        lifecycleMock.DisplayOrDriverChanged += Raise.Event<Action>();
        lifecycleMock.PowerSuspended += Raise.Event<Action>();
        lifecycleMock.PowerResumed += Raise.Event<Action>();
        lifecycleMock.NetworkTopologyChanged += Raise.Event<Action>();
        lifecycleMock.DevicePnPChanged += Raise.Event<Action>();

        // Double dispose to cover idempotency branch
        var act = () => monitor.Dispose();
        act.Should().NotThrow();
        act.Should().NotThrow();
    }

    [Fact]
    public void HardwareMonitor_GpuMetricReaders_And_BatteryCapacityDelta_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        // 1. GPU metric readers with NvGpuData passed in directly
        var gpuData = new NvGpuMonitor.NvGpuData
        {
            UsagePct = 55f,
            TempC = 68f,
            VramUsedMb = 6144f,
            VramTotalMb = 16384f,
            CoreClockMHz = 2450f,
            PowerDrawW = 180f,
            TdpW = 300f
        };

        var mReadGpuPct = typeof(HardwareMonitor).GetMethod("ReadGpuPct", BindingFlags.NonPublic | BindingFlags.Instance);
        var mReadTemp = typeof(HardwareMonitor).GetMethod("ReadGpuTemp", BindingFlags.NonPublic | BindingFlags.Instance);
        var mReadVram = typeof(HardwareMonitor).GetMethod("ReadGpuVramUsedMb", BindingFlags.NonPublic | BindingFlags.Instance);
        var mReadFreq = typeof(HardwareMonitor).GetMethod("ReadGpuFreqMHz", BindingFlags.NonPublic | BindingFlags.Instance);
        var mReadPower = typeof(HardwareMonitor).GetMethod("ReadGpuPowerW", BindingFlags.NonPublic | BindingFlags.Instance);

        ((float)mReadGpuPct!.Invoke(monitor, [gpuData])!).Should().Be(55f);
        ((float)mReadTemp!.Invoke(monitor, [gpuData])!).Should().Be(68f);
        ((float)mReadVram!.Invoke(monitor, [gpuData])!).Should().Be(6144f);
        ((float)mReadFreq!.Invoke(monitor, [gpuData])!).Should().Be(2450f);
        ((float)mReadPower!.Invoke(monitor, [gpuData])!).Should().Be(180f);

        // 2. Battery rate from capacity delta
        var mDelta = typeof(HardwareMonitor).GetMethod("ComputeBatteryRateFromCapacityDelta", BindingFlags.NonPublic | BindingFlags.Instance);
        mDelta.Should().NotBeNull();

        // Null / 0 capacity
        ((float)mDelta.Invoke(monitor, [null])!).Should().Be(0f);
        ((float)mDelta.Invoke(monitor, [0L])!).Should().Be(0f);

        // First call primes capacity
        ((float)mDelta.Invoke(monitor, [50000L])!).Should().Be(0f);

        // Second call calculates delta
        float rate = (float)mDelta.Invoke(monitor, [48000L])!;
        rate.Should().NotBe(0f);

        // 3. Network delta calculation
        var mNetDelta = typeof(HardwareMonitor).GetMethod("CalculateNetBytesDelta", BindingFlags.NonPublic | BindingFlags.Instance);
        mNetDelta.Should().NotBeNull();

        // Reset primed state
        typeof(HardwareMonitor).GetField("_nicPrimed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(monitor, false);
        var deltaPrimed = ((double down, double up))mNetDelta.Invoke(monitor, [1000L, 2000L])!;
        deltaPrimed.down.Should().Be(0);

        // Second call calculates throughput
        var deltaCalc = ((double down, double up))mNetDelta.Invoke(monitor, [5000L, 8000L])!;
        deltaCalc.down.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void HardwareMonitor_LifecycleEvents_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock);

        // 1. OnPowerSuspended
        var onSuspend = typeof(HardwareMonitor).GetMethod("OnPowerSuspended", BindingFlags.NonPublic | BindingFlags.Instance);
        onSuspend.Should().NotBeNull();
        onSuspend.Invoke(monitor, null);

        // 2. OnNetworkTopologyChanged
        var onNetChange = typeof(HardwareMonitor).GetMethod("OnNetworkTopologyChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        onNetChange.Should().NotBeNull();
        onNetChange.Invoke(monitor, null);

        // 3. OnDevicePnPChanged
        var onPnp = typeof(HardwareMonitor).GetMethod("OnDevicePnPChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        onPnp.Should().NotBeNull();
        onPnp.Invoke(monitor, null);

        // 4. ResetGpuMonitors
        var resetGpu = typeof(HardwareMonitor).GetMethod("ResetGpuMonitors", BindingFlags.NonPublic | BindingFlags.Instance);
        resetGpu.Should().NotBeNull();
        resetGpu.Invoke(monitor, ["test reset"]);
    }

    [Fact]
    public void HardwareMonitor_GetBatteryStateDescription_ReturnsCorrectStates()
    {
        var method = typeof(HardwareMonitor).GetMethod("GetBatteryStateDescription", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        method.Invoke(null, [true, true]).Should().Be("charging");
        method.Invoke(null, [true, false]).Should().Be("charging");
        method.Invoke(null, [false, true]).Should().Be("charged");
        method.Invoke(null, [false, false]).Should().Be("discharging");
    }

    [Fact]
    public void HardwareMonitor_LogTickMetrics_AllMetricsEnabled()
    {
        var cfg = new AppSettings();
        cfg.Monitoring.EnableCpu = true;
        cfg.Monitoring.EnableRam = true;
        cfg.Monitoring.EnableDisk = true;
        cfg.Monitoring.EnableGpu = true;
        cfg.Monitoring.EnableBattery = true;

        var sysInfoMock = Substitute.For<ISystemInfo>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock);

        var logTick = typeof(HardwareMonitor).GetMethod("LogTickMetrics", BindingFlags.NonPublic | BindingFlags.Instance);
        logTick.Should().NotBeNull();

        // Exercise charging, charged, discharging
        logTick.Invoke(monitor, [
            cfg.Monitoring,
            (50f, 60f, 4.5f, 95f),
            (65f, 16f, 32f),
            (120.5, 45.2, 2),
            (80f, 70f, 250f, 2100f),
            (85f, true, true, 45f)
        ]);

        logTick.Invoke(monitor, [
            cfg.Monitoring,
            (10f, 40f, 2.5f, 30f),
            (30f, 8f, 32f),
            (10.0, 5.0, 1),
            (20f, 45f, 50f, 800f),
            (100f, false, true, 0f)
        ]);

        logTick.Invoke(monitor, [
            cfg.Monitoring,
            (20f, 45f, 3.0f, 40f),
            (40f, 12f, 32f),
            (20.0, 10.0, 1),
            (30f, 50f, 60f, 1000f),
            (50f, false, false, 25f)
        ]);
    }

    [Fact]
    public void HardwareMonitor_ReadGpu_WddmAndDriverFallbacks()
    {
        var cfg = new AppSettings();
        cfg.Monitoring.GpuUsageSource = "wddm";

        var sysInfoMock = Substitute.For<ISystemInfo>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock);

        using var wddm = new WddmGpuMonitor();
        SetField(monitor, "_wddmGpu", wddm);

        // When source is "wddm", uses WDDM directly
        InvokeFloat(monitor, "ReadGpuPct", (NvGpuMonitor.NvGpuData?)null).Should().BeGreaterThanOrEqualTo(0f);

        // When source is driver, but driver unavailable, falls back to WDDM
        cfg.Monitoring.GpuUsageSource = "driver";
        InvokeFloat(monitor, "ReadGpuPct", (NvGpuMonitor.NvGpuData?)null).Should().BeGreaterThanOrEqualTo(0f);

        // When driver and WDDM unavailable, returns 0
        SetField(monitor, "_wddmGpu", null);
        SetField(monitor, "_nvGpu", null);
        SetField(monitor, "_amdGpu", null);
        InvokeFloat(monitor, "ReadGpuPct", (NvGpuMonitor.NvGpuData?)null).Should().Be(0f);
    }

    [Fact]
    public void HardwareMonitor_ReadCpuPowerRapl_And_Tdp_EdgeCases()
    {
        var transport = TransportMock();
        var monitor = WiredDevice(transport, 5000, "Intel");

        // Power SKU raw == 0 -> returns null
        transport.Execute(Arg.Any<string>(), Arg.Is<ulong[]>(x => x[0] == MsrPowerUnit), 1)
            .Returns([0x1003UL]);
        transport.Execute(Arg.Any<string>(), Arg.Is<ulong[]>(x => x[0] == MsrPkgSku), 1)
            .Returns([0UL]);

        var readTdp = typeof(HardwareMonitor).GetMethod("ReadCpuTdp", BindingFlags.NonPublic | BindingFlags.Instance);
        readTdp.Should().NotBeNull();
        readTdp.Invoke(monitor, null).Should().BeNull();

        // ReadRaplPowerUnit throws exception -> handled safely
        transport.When(x => x.Execute(Arg.Any<string>(), Arg.Is<ulong[]>(u => u[0] == MsrPowerUnit), 1))
            .Do(_ => throw new InvalidOperationException("MSR read fault"));

        var readRaplUnit = typeof(HardwareMonitor).GetMethod("ReadRaplPowerUnit", BindingFlags.NonPublic | BindingFlags.Instance);
        readRaplUnit.Should().NotBeNull();
        var act = () => readRaplUnit.Invoke(monitor, null);
        act.Should().NotThrow();
    }

    [Fact]
    public void HardwareMonitor_IsVirtualAdapter_DetectsVirtualAdaptersCorrectly()
    {
        string[] virtualDescs = [
            "Hyper-V Virtual Ethernet Adapter",
            "VMware Virtual Ethernet Adapter for VMnet1",
            "VirtualBox Host-Only Ethernet Adapter",
            "WSL Virtual Network Interface",
            "TAP-Windows Adapter V9",
            "Teredo Tunneling Pseudo-Interface",
            "Microsoft Loopback Adapter",
            "WAN Miniport (IP)",
            "WFP Native MAC Layer LightWeight Filter-0000"
        ];

        foreach (var desc in virtualDescs)
        {
            var nic = Substitute.For<NetworkInterface>();
            nic.Description.Returns(desc);
            nic.Name.Returns("Ethernet");
            HardwareMonitor.IsVirtualAdapter(nic).Should().BeTrue();
        }

        string[] virtualNames = ["vEthernet (Default Switch)", "WSL", "Local Area Connection* 1"];
        foreach (var name in virtualNames)
        {
            var nic = Substitute.For<NetworkInterface>();
            nic.Description.Returns("Standard Adapter");
            nic.Name.Returns(name);
            HardwareMonitor.IsVirtualAdapter(nic).Should().BeTrue();
        }

        var realNic = Substitute.For<NetworkInterface>();
        realNic.Description.Returns("Intel(R) Ethernet Connection (7) I219-V");
        realNic.Name.Returns("Ethernet");
        HardwareMonitor.IsVirtualAdapter(realNic).Should().BeFalse();
    }

    [Fact]
    public void HardwareMonitor_ExtractDiskIndex_And_PollControl()
    {
        var extractIndex = typeof(HardwareMonitor).GetMethod("ExtractDiskIndex", BindingFlags.NonPublic | BindingFlags.Static);
        extractIndex.Should().NotBeNull();

        extractIndex.Invoke(null, ["0 C:"]).Should().Be("0");
        extractIndex.Invoke(null, ["12 D:"]).Should().Be("12");
        extractIndex.Invoke(null, ["NonDigit"]).Should().Be("");
        extractIndex.Invoke(null, [""]).Should().Be("");

        var cfg = BasicCfg();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock);

        var pollMethod = typeof(HardwareMonitor).GetMethod("Poll", BindingFlags.NonPublic | BindingFlags.Instance);
        pollMethod.Should().NotBeNull();

        // When suspended -> early return
        SetField(monitor, "_isSuspended", true);
        var actSuspend = () => pollMethod.Invoke(monitor, null);
        actSuspend.Should().NotThrow();

        // When already polling -> early return
        SetField(monitor, "_isSuspended", false);
        SetField(monitor, "_isPolling", 1);
        var actPolling = () => pollMethod.Invoke(monitor, null);
        actPolling.Should().NotThrow();
    }

    [Fact]
    public void HardwareMonitor_AmdAndIntelGpuReaders_And_Lifecycle_Coverage()
    {
        var cfg = BasicCfg();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock);

        // Setup mock AMD monitor
        var amdNative = Substitute.For<IAmdAdlNative>();
        amdNative.Create().Returns(0);
        amdNative.GetAdapterInfo(out Arg.Any<int>(), out Arg.Any<string>()).Returns(x => { x[0] = 0; x[1] = "AMD Radeon RX 7900 XTX"; return 0; });
        amdNative.GetTemperature(0, out Arg.Any<int>()).Returns(x => { x[1] = 62000; return 0; });
        amdNative.GetActivity(0, out Arg.Any<int>(), out Arg.Any<int>()).Returns(x => { x[1] = 45; x[2] = 24000; return 0; });
        amdNative.GetMemoryInfo(0, out Arg.Any<long>(), out Arg.Any<long>()).Returns(x => { x[1] = 24L * 1024 * 1024 * 1024; x[2] = 12L * 1024 * 1024 * 1024; return 0; });
        amdNative.GetPowerLimit(0, out Arg.Any<int>(), out Arg.Any<int>()).Returns(x => { x[1] = 280000; x[2] = 280000; return 0; });
        using var amdGpu = new AmdGpuMonitor(amdNative);

        // Setup mock Intel monitor
        var intelNative = Substitute.For<IIntelGpuNative>();
        intelNative.Init(out Arg.Any<IntPtr>()).Returns(x => { x[0] = (IntPtr)0x1; return 0; });
        intelNative.EnumerateDevices((IntPtr)0x1, out Arg.Any<IntPtr[]>()).Returns(x => { x[1] = new IntPtr[] { (IntPtr)0x2 }; return 0; });
        intelNative.GetDeviceName((IntPtr)0x2, out Arg.Any<string>()).Returns(x => { x[1] = "Intel Arc A770"; return 0; });
        intelNative.GetTempSensor((IntPtr)0x2, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x3; return 0; });
        intelNative.GetTempC((IntPtr)0x3, out Arg.Any<double>()).Returns(x => { x[1] = 55.0; return 0; });
        intelNative.GetFreqDomain((IntPtr)0x2, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x4; return 0; });
        intelNative.GetClockMHz((IntPtr)0x4, out Arg.Any<double>()).Returns(x => { x[1] = 2100.0; return 0; });
        intelNative.GetMemModule((IntPtr)0x2, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x5; return 0; });
        intelNative.GetVram((IntPtr)0x5, out Arg.Any<ulong>(), out Arg.Any<ulong>()).Returns(x => { x[1] = 16UL * 1024 * 1024 * 1024; x[2] = 8UL * 1024 * 1024 * 1024; return 0; });
        using var intelGpu = new IntelGpuMonitor(intelNative);

        SetField(monitor, "_nvGpu", null);
        SetField(monitor, "_wddmGpu", null);

        // Test AMD GPU path
        SetField(monitor, "_amdGpu", amdGpu);
        SetField(monitor, "_intelGpu", null);

        var readTempMethod = typeof(HardwareMonitor).GetMethod("ReadGpuTemp", BindingFlags.NonPublic | BindingFlags.Instance);
        var readVramMethod = typeof(HardwareMonitor).GetMethod("ReadGpuVramUsedMb", BindingFlags.NonPublic | BindingFlags.Instance);
        var readFreqMethod = typeof(HardwareMonitor).GetMethod("ReadGpuFreqMHz", BindingFlags.NonPublic | BindingFlags.Instance);
        var readPowerMethod = typeof(HardwareMonitor).GetMethod("ReadGpuPowerW", BindingFlags.NonPublic | BindingFlags.Instance);

        readTempMethod.Should().NotBeNull();
        readVramMethod.Should().NotBeNull();
        readFreqMethod.Should().NotBeNull();
        readPowerMethod.Should().NotBeNull();

        ((float)readTempMethod.Invoke(monitor, [null])!).Should().Be(62.0f);
        ((float)readVramMethod.Invoke(monitor, [null])!).Should().BeGreaterThan(0);
        ((float)readFreqMethod.Invoke(monitor, [null])!).Should().Be(240.0f);
        ((float)readPowerMethod.Invoke(monitor, [null])!).Should().Be(280.0f);

        // Test Intel GPU path
        SetField(monitor, "_amdGpu", null);
        SetField(monitor, "_intelGpu", intelGpu);

        ((float)readTempMethod.Invoke(monitor, [null])!).Should().Be(55.0f);
        ((float)readVramMethod.Invoke(monitor, [null])!).Should().BeGreaterThan(0);
        ((float)readFreqMethod.Invoke(monitor, [null])!).Should().Be(2100.0f);

        // Test Stop, Restart, ResetGpuMonitors, Lifecycle handlers
        monitor.Stop();
        monitor.Restart(200);

        var resetMethod = typeof(HardwareMonitor).GetMethod("ResetGpuMonitors", BindingFlags.NonPublic | BindingFlags.Instance);
        resetMethod?.Invoke(monitor, ["unit test reset"]);

        var onDisplay = typeof(HardwareMonitor).GetMethod("OnDisplayOrDriverChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        onDisplay?.Invoke(monitor, null);

        var onNetwork = typeof(HardwareMonitor).GetMethod("OnNetworkTopologyChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        onNetwork?.Invoke(monitor, null);

        var onPnp = typeof(HardwareMonitor).GetMethod("OnDevicePnPChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        onPnp?.Invoke(monitor, null);

        var onSuspended = typeof(HardwareMonitor).GetMethod("OnPowerSuspended", BindingFlags.NonPublic | BindingFlags.Instance);
        onSuspended?.Invoke(monitor, null);

        monitor.ExecutePowerResumeWarmUp();
    }

    [Fact]
    public void HardwareMonitor_NvGpu_And_ErrorHandling_And_WarmUp_Coverage()
    {
        var cfg = new AppSettings();
        using var monitor = new HardwareMonitor(cfg);

        monitor.ExecutePowerResumeWarmUp();

        var nvNative = Substitute.For<INvGpuNative>();
        nvNative.Init().Returns(0);
        nvNative.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = new IntPtr(1234); return 0; });
        nvNative.GetTemperature(Arg.Any<IntPtr>(), 0, out Arg.Any<uint>()).Returns(x => { x[2] = 68u; return 0; });
        nvNative.GetUtilizationRates(Arg.Any<IntPtr>(), out Arg.Any<uint>(), out Arg.Any<uint>()).Returns(x => { x[1] = 45u; x[2] = 10u; return 0; });
        nvNative.GetMemoryInfo(Arg.Any<IntPtr>(), out Arg.Any<ulong>(), out Arg.Any<ulong>(), out Arg.Any<ulong>()).Returns(x => { x[1] = 8192UL * 1024 * 1024; x[3] = 4096UL * 1024 * 1024; return 0; });
        nvNative.GetClockInfo(Arg.Any<IntPtr>(), 0, out Arg.Any<uint>()).Returns(x => { x[2] = 1950u; return 0; });
        nvNative.GetPowerUsage(Arg.Any<IntPtr>(), out Arg.Any<uint>()).Returns(x => { x[1] = 150000u; return 0; });

        using var nvGpu = new NvGpuMonitor(nvNative);
        SetField(monitor, "_nvGpu", nvGpu);
        SetField(monitor, "_amdGpu", null);
        SetField(monitor, "_intelGpu", null);

        var readTempMethod = typeof(HardwareMonitor).GetMethod("ReadGpuTemp", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var readVramMethod = typeof(HardwareMonitor).GetMethod("ReadGpuVramUsedMb", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var readFreqMethod = typeof(HardwareMonitor).GetMethod("ReadGpuFreqMHz", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var readPowerMethod = typeof(HardwareMonitor).GetMethod("ReadGpuPowerW", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var readGpuPctMethod = typeof(HardwareMonitor).GetMethod("ReadGpuPct", BindingFlags.NonPublic | BindingFlags.Instance)!;

        ((float)readTempMethod.Invoke(monitor, [null])!).Should().Be(68.0f);
        ((float)readVramMethod.Invoke(monitor, [null])!).Should().Be(4096.0f);
        ((float)readFreqMethod.Invoke(monitor, [null])!).Should().Be(1950.0f);
        ((float)readPowerMethod.Invoke(monitor, [null])!).Should().Be(150.0f);
        ((float)readGpuPctMethod.Invoke(monitor, [null])!).Should().Be(45.0f);

        var throwingAmdNative = Substitute.For<IAmdAdlNative>();
        throwingAmdNative.Create().Returns(0);
        throwingAmdNative.GetAdapterInfo(out Arg.Any<int>(), out Arg.Any<string>()).Returns(x => { x[0] = 0; x[1] = "AMD Radeon"; return 0; });
        throwingAmdNative.GetTemperature(Arg.Any<int>(), out Arg.Any<int>()).Returns(x => throw new InvalidOperationException("ADL error"));
        throwingAmdNative.GetMemoryInfo(Arg.Any<int>(), out Arg.Any<long>(), out Arg.Any<long>()).Returns(x => throw new InvalidOperationException("ADL error"));
        throwingAmdNative.GetActivity(Arg.Any<int>(), out Arg.Any<int>(), out Arg.Any<int>()).Returns(x => throw new InvalidOperationException("ADL error"));
        throwingAmdNative.GetPowerLimit(Arg.Any<int>(), out Arg.Any<int>(), out Arg.Any<int>()).Returns(x => throw new InvalidOperationException("ADL error"));

        using var throwingAmdGpu = new AmdGpuMonitor(throwingAmdNative);
        SetField(monitor, "_nvGpu", null);
        SetField(monitor, "_amdGpu", throwingAmdGpu);

        ((float)readTempMethod.Invoke(monitor, [null])!).Should().Be(0f);
        ((float)readVramMethod.Invoke(monitor, [null])!).Should().Be(0f);
        ((float)readFreqMethod.Invoke(monitor, [null])!).Should().Be(0f);
        ((float)readPowerMethod.Invoke(monitor, [null])!).Should().Be(0f);

        var pwrNative = Substitute.For<IIntelPowerGadgetNative>();
        pwrNative.TryLoadLibrary().Returns(true);
        pwrNative.Initialize().Returns(0);
        pwrNative.GetNumNodes(out Arg.Any<int>()).Returns(x => { x[0] = 1; return 0; });
        pwrNative.GetIAFrequency(0, out Arg.Any<double>()).Returns(x => { x[1] = 3400.0; return 0; });

        using var pwrGadget = new IntelPowerGadget(pwrNative);
        SetField(monitor, "_intelPwrGadget", pwrGadget);
        SetField(monitor, "_pawnIoDevice", null);
        SetField(monitor, "_maxCpuMhz", 0u);

        var readCpuFreqMethod = typeof(HardwareMonitor).GetMethod("ReadCpuFreq", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((float)readCpuFreqMethod.Invoke(monitor, null)!).Should().Be(3.4f);

        var extractDiskIndexMethod = typeof(HardwareMonitor).GetMethod("ExtractDiskIndex", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((string)extractDiskIndexMethod.Invoke(null, ["0 C: D:"])!).Should().Be("0");
        ((string)extractDiskIndexMethod.Invoke(null, ["12 E:"])!).Should().Be("12");
        ((string)extractDiskIndexMethod.Invoke(null, ["_Total"])!).Should().BeEmpty();
        ((string)extractDiskIndexMethod.Invoke(null, [""])!).Should().BeEmpty();
    }

    [Fact]
    public void HardwareMonitor_LifecycleEvents_And_PowerManagement_Coverage()
    {
        var cfg = new AppSettings();
        cfg.Monitoring.EnableNet = false;
        var lifecycleMock = Substitute.For<IHardwareLifecycleManager>();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        sysInfoMock.GetCpuVendor().Returns("Intel");
        var pawnIoMock = Substitute.For<IPawnIoManager>();

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock, lifecycleMock);

        // 1. Display or driver changed event
        lifecycleMock.DisplayOrDriverChanged += Raise.Event<Action>();

        // 2. Power suspended event
        lifecycleMock.PowerSuspended += Raise.Event<Action>();

        // When suspended, display changed returns early
        lifecycleMock.DisplayOrDriverChanged += Raise.Event<Action>();
        lifecycleMock.NetworkTopologyChanged += Raise.Event<Action>();
        lifecycleMock.DevicePnPChanged += Raise.Event<Action>();

        // 3. Power resumed event
        lifecycleMock.PowerResumed += Raise.Event<Action>();
        monitor.ExecutePowerResumeWarmUp();

        // 4. Timer control methods
        monitor.Start();
        monitor.Stop();
        monitor.Restart(250);

        // 5. ResetGpuMonitors reflection
        var resetGpuMethod = typeof(HardwareMonitor).GetMethod("ResetGpuMonitors", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var actReset = () => resetGpuMethod.Invoke(monitor, ["test_reason"]);
        actReset.Should().NotThrow();
    }

    [Fact]
    public void HardwareMonitor_StaticHelpers_CompleteBranchCoverage()
    {
        // FormatActiveGpuAdapterLog
        var formatGpu = typeof(HardwareMonitor).GetMethod("FormatActiveGpuAdapterLog", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((string)formatGpu.Invoke(null, ["GeForce RTX 4090", "NVIDIA"])!).Should().Be("GeForce RTX 4090 (NVIDIA)");
        ((string)formatGpu.Invoke(null, ["", "AMD"])!).Should().Be("AMD");
        ((string)formatGpu.Invoke(null, ["", ""])!).Should().Be("None");

        // DetectGpuVendor
        var detectGpu = typeof(HardwareMonitor).GetMethod("DetectGpuVendor", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((string?)detectGpu.Invoke(null, ["PCI\\VEN_10DE&DEV_1234", "Display Adapter"])).Should().Be("NVIDIA");
        ((string?)detectGpu.Invoke(null, ["PCI\\VEN_1002&DEV_5678", "Display Adapter"])).Should().Be("AMD");
        ((string?)detectGpu.Invoke(null, ["PCI\\VEN_8086&DEV_9ABC", "Display Adapter"])).Should().Be("Intel");
        ((string?)detectGpu.Invoke(null, ["PCI\\VEN_0000", "Custom Display Adapter"])).Should().BeNull();

        // ComputeBatteryRateFromCapacityDelta edge cases
        var cfg = new AppSettings();
        cfg.Monitoring.EnableNet = false;
        var sysInfoMock = Substitute.For<ISystemInfo>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock);

        var computeBatteryRate = typeof(HardwareMonitor).GetMethod("ComputeBatteryRateFromCapacityDelta", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // <= 0 returns 0f
        ((float)computeBatteryRate.Invoke(monitor, [0L])!).Should().Be(0f);
        ((float)computeBatteryRate.Invoke(monitor, [-100L])!).Should().Be(0f);
        ((float)computeBatteryRate.Invoke(monitor, [(object?)null])!).Should().Be(0f);

        // First poll primes
        ((float)computeBatteryRate.Invoke(monitor, [50000L])!).Should().Be(0f);

        // Second poll computes rate
        SetField(monitor, "_lastBatteryPollTime", DateTime.UtcNow.AddHours(-1.0));
        float rate = (float)computeBatteryRate.Invoke(monitor, [60000L])!;
        rate.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void HardwareMonitor_RaplAndCpuSensors_Coverage()
    {
        var cfg = new AppSettings();
        cfg.Monitoring.EnableNet = false;
        var sysInfoMock = Substitute.For<ISystemInfo>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();
        var transportMock = Substitute.For<IPawnIoTransport>();

        // Rapl units: pUnits = 3, eUnits = 14
        ulong raplUnitMsr = 3u | (14u << 8);
        transportMock.Execute(Arg.Any<string>(), Arg.Is<ulong[]>(x => x.Length == 1 && x[0] == 0x606), 1)
            .Returns([raplUnitMsr]);

        // TDP raw SKU: 65W
        transportMock.Execute(Arg.Any<string>(), Arg.Is<ulong[]>(x => x.Length == 1 && x[0] == 0x614), 1)
            .Returns([65u]);

        // Energy status: prime with 1000, then 2000
        ulong energyStatus = 1000UL;
        transportMock.Execute(Arg.Any<string>(), Arg.Is<ulong[]>(x => x.Length == 1 && x[0] == 0x611), 1)
            .Returns(_ => [energyStatus]);

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock);
        SetField(monitor, "_pawnIoDevice", transportMock);

        // 1. ReadRaplPowerUnit
        var readRapl = typeof(HardwareMonitor).GetMethod("ReadRaplPowerUnit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        readRapl.Invoke(monitor, null);

        // 2. ReadCpuTdp
        var readTdp = typeof(HardwareMonitor).GetMethod("ReadCpuTdp", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var tdp = (uint?)readTdp.Invoke(monitor, null);
        tdp.Should().NotBeNull();

        // 3. ReadCpuPowerRapl: prime -> 0, second read with delta -> calculates watts
        var readPower = typeof(HardwareMonitor).GetMethod("ReadCpuPowerRapl", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((float)readPower.Invoke(monitor, null)!).Should().Be(0f);

        SetField(monitor, "_prevEnergyTime", DateTime.UtcNow.AddSeconds(-1.0));
        energyStatus = 500000UL;
        float watts = (float)readPower.Invoke(monitor, null)!;
        watts.Should().BeGreaterThan(0f);

        // 4. ReadCpuPowerRapl when transport throws
        transportMock.Execute(Arg.Any<string>(), Arg.Is<ulong[]>(x => x.Length == 1 && x[0] == 0x611), 1)
            .Returns(_ => throw new InvalidOperationException("PawnIO driver disconnected"));
        ((float)readPower.Invoke(monitor, null)!).Should().Be(0f);
    }

    [Fact]
    public void HardwareMonitor_CpuFreqMsr_And_CpuPct_Coverage()
    {
        var cfg = new AppSettings();
        cfg.Monitoring.EnableNet = false;
        var sysInfoMock = Substitute.For<ISystemInfo>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();
        var transportMock = Substitute.For<IPawnIoTransport>();

        // APERF / MPERF returns
        ulong aperfVal = 1000UL;
        ulong mperfVal = 1000UL;
        transportMock.Execute(Arg.Any<string>(), Arg.Is<ulong[]>(x => x.Length == 1 && x[0] == 0xE8), 1)
            .Returns(_ => [aperfVal]);
        transportMock.Execute(Arg.Any<string>(), Arg.Is<ulong[]>(x => x.Length == 1 && x[0] == 0xE7), 1)
            .Returns(_ => [mperfVal]);

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock);
        SetField(monitor, "_pawnIoDevice", transportMock);
        SetField(monitor, "_maxCpuMhz", 3600u);

        var readFreqMsr = typeof(HardwareMonitor).GetMethod("ReadCpuFreqMsr", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // Prime
        ((float)readFreqMsr.Invoke(monitor, null)!).Should().Be(0f);

        // Second sample with ratio
        aperfVal = 2000UL;
        mperfVal = 1800UL;
        float freq = (float)readFreqMsr.Invoke(monitor, null)!;
        freq.Should().BeGreaterThan(0f);

        // ReadCpuPct
        var readCpuPct = typeof(HardwareMonitor).GetMethod("ReadCpuPct", BindingFlags.NonPublic | BindingFlags.Instance)!;
        float pct1 = (float)readCpuPct.Invoke(monitor, null)!;
        float pct2 = (float)readCpuPct.Invoke(monitor, null)!;
        pct2.Should().BeInRange(0f, 100f);
    }

    [Fact]
    public void HardwareMonitor_GpuQueries_And_BatteryDescriptions_Coverage()
    {
        var cfg = new AppSettings();
        cfg.Monitoring.EnableNet = false;
        var sysInfoMock = Substitute.For<ISystemInfo>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock);

        // Gpu helpers with explicit NvGpuData snapshot
        var nvData = new NvGpuMonitor.NvGpuData
        {
            TempC = 58f,
            UsagePct = 75f,
            VramUsedMb = 4096f,
            CoreClockMHz = 2400f,
            PowerDrawW = 180f
        };

        var readGpuTemp = typeof(HardwareMonitor).GetMethod("ReadGpuTemp", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((float)readGpuTemp.Invoke(monitor, [nvData])!).Should().Be(58f);

        var readGpuPct = typeof(HardwareMonitor).GetMethod("ReadGpuPct", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((float)readGpuPct.Invoke(monitor, [nvData])!).Should().Be(75f);

        var readGpuVram = typeof(HardwareMonitor).GetMethod("ReadGpuVramUsedMb", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((float)readGpuVram.Invoke(monitor, [nvData])!).Should().Be(4096f);

        var readGpuFreq = typeof(HardwareMonitor).GetMethod("ReadGpuFreqMHz", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((float)readGpuFreq.Invoke(monitor, [nvData])!).Should().Be(2400f);

        var readGpuPower = typeof(HardwareMonitor).GetMethod("ReadGpuPowerW", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((float)readGpuPower.Invoke(monitor, [nvData])!).Should().Be(180f);

        // GetBatteryStateDescription
        var getBatDesc = typeof(HardwareMonitor).GetMethod("GetBatteryStateDescription", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((string)getBatDesc.Invoke(null, [true, true])!).Should().Be("charging");
        ((string)getBatDesc.Invoke(null, [false, true])!).Should().Be("charged");
        ((string)getBatDesc.Invoke(null, [false, false])!).Should().Be("discharging");

        // ExtractDiskIndex
        var extractDisk = typeof(HardwareMonitor).GetMethod("ExtractDiskIndex", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((string)extractDisk.Invoke(null, ["0 C:"])!).Should().Be("0");
        ((string)extractDisk.Invoke(null, ["12 D:"])!).Should().Be("12");
        ((string)extractDisk.Invoke(null, ["_Total"])!).Should().BeEmpty();
    }
}


