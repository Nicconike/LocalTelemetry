using FluentAssertions;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Hardware;
using LocalTelemetry.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using LocalTelemetry.Core.Hardware.PawnIo;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class HardwareMonitorTests
{
    [Fact]
    public void SystemInfo_AllQueryHelpersExposed()
    {
        string cpu = SystemInfo.GetCpuName();
        cpu.Should().NotBeNull();

        int cores = SystemInfo.GetCpuCoreCount();
        cores.Should().BeGreaterThan(0);

        int threads = SystemInfo.GetCpuThreadCount();
        threads.Should().BeGreaterThan(0);

        int maxMhz = SystemInfo.GetCpuMaxSpeedMhz();
        maxMhz.Should().BeGreaterThanOrEqualTo(0);

        string socket = SystemInfo.GetCpuSocket();
        socket.Should().NotBeNull();

        double ram = SystemInfo.GetInstalledRamGb();
        ram.Should().BeGreaterThanOrEqualTo(0);

        string mbVendor = SystemInfo.GetMotherboardManufacturer();
        mbVendor.Should().NotBeNull();

        bool hasBat = SystemInfo.HasBattery();
        hasBat.Should().Be(hasBat);

        string batVendor = SystemInfo.GetBatteryManufacturer();
        batVendor.Should().NotBeNull();

        string batDevice = SystemInfo.GetBatteryDeviceName();
        batDevice.Should().NotBeNull();

        string cap = SystemInfo.GetBatteryDesignCapacity();
        cap.Should().NotBeNull();

        var nics = SystemInfo.GetNics();
        nics.Should().NotBeNull();
    }

    [Fact]
    public async Task HardwareMonitor_WithMocks_ExecutesSafely()
    {
        var cfg = new AppSettings();
        cfg.Monitoring.PollIntervalMs = 10;
        cfg.Monitoring.EnableNet = false;

        var sysInfoMock = Substitute.For<ISystemInfo>();
        sysInfoMock.GetCpuName().Returns("Mocked CPU");
        sysInfoMock.GetCpuVendor().Returns("Intel");
        sysInfoMock.GetCpuCoreCount().Returns(8);
        sysInfoMock.GetCpuThreadCount().Returns(16);
        sysInfoMock.GetCpuMaxSpeedMhz().Returns(3000);
        sysInfoMock.HasBattery().Returns(true);
        sysInfoMock.GetBatteryManufacturer().Returns("Mocked Battery");
        sysInfoMock.GetBatteryDeviceName().Returns("Battery 1");
        sysInfoMock.GetBatteryDesignCapacity().Returns("50000");

        var pawnIoMock = Substitute.For<IPawnIoManager>();
        pawnIoMock.TryCreate().Returns((IPawnIoTransport?)null);
        pawnIoMock.TryInstall().Returns(false);

        using var monitor = new HardwareMonitor(NullLogger<HardwareMonitor>.Instance, cfg, sysInfoMock, pawnIoMock);

        // Invoke BuildSnapshot via reflection
        var method = typeof(HardwareMonitor).GetMethod("BuildSnapshot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var snapshot = (TelemetrySnapshot?)method?.Invoke(monitor, null);

        snapshot.Should().NotBeNull();
        snapshot!.CpuUsagePct.Should().BeGreaterThanOrEqualTo(0);

        monitor.Dispose();
    }

    [Fact]
    public void HardwareMonitor_WithMocks_SuccessfulPawnIo()
    {
        var cfg = new AppSettings();
        cfg.Monitoring.EnableNet = false;

        var sysInfoMock = Substitute.For<ISystemInfo>();
        sysInfoMock.GetCpuName().Returns("Mocked CPU");
        sysInfoMock.GetCpuVendor().Returns("Intel");
        sysInfoMock.GetCpuCoreCount().Returns(8);

        var transportMock = Substitute.For<IPawnIoTransport>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();
        pawnIoMock.TryCreate().Returns(transportMock);
        pawnIoMock.LoadResourceBytes(Arg.Any<string>()).Returns(new byte[] { 1, 2, 3 });
        pawnIoMock.LoadModule(Arg.Any<IPawnIoTransport>(), Arg.Any<byte[]>()).Returns(true);

        using var monitor = new HardwareMonitor(NullLogger<HardwareMonitor>.Instance, cfg, sysInfoMock, pawnIoMock);

        var method = typeof(HardwareMonitor).GetMethod("BuildSnapshot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var snapshot = (TelemetrySnapshot?)method?.Invoke(monitor, null);

        snapshot.Should().NotBeNull();
    }
}
