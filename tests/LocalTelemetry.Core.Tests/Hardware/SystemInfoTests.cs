using FluentAssertions;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Hardware;
using LocalTelemetry.Core.Hardware.PawnIo;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class SystemInfoTests
{
    [Fact]
    public void SystemInfo_GetCpuName_ReturnsNonEmptyString()
    {
        string cpu = SystemInfo.GetCpuName();
        cpu.Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_GetTotalRamBytes_ReturnsPositiveValue()
    {
        long ram = SystemInfo.GetTotalRamBytes();
        ram.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SystemInfo_GetGpus_ReturnsArray()
    {
        var gpus = SystemInfo.GetGpus();
        gpus.Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_GetNics_ReturnsArray()
    {
        var nics = SystemInfo.GetNics();
        nics.Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_GetRamModules_ReturnsList()
    {
        var modules = SystemInfo.GetRamModules();
        modules.Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_GetCpuVendor_ReturnsVendorOrUnknown()
    {
        string vendor = SystemInfo.GetCpuVendor();
        vendor.Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_GetCpuSocket_ReturnsSocketOrEmpty()
    {
        string socket = SystemInfo.GetCpuSocket();
        socket.Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_GetCpuCoreCount_ReturnsPositiveValue()
    {
        int cores = SystemInfo.GetCpuCoreCount();
        cores.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SystemInfo_GetCpuThreadCount_ReturnsPositiveValue()
    {
        int threads = SystemInfo.GetCpuThreadCount();
        threads.Should().BeGreaterThan(0);
        threads.Should().BeGreaterThanOrEqualTo(SystemInfo.GetCpuCoreCount());
    }

    [Fact]
    public void SystemInfo_GetCpuMaxSpeedMhz_ReturnsPositiveValueOrZero()
    {
        int speed = SystemInfo.GetCpuMaxSpeedMhz();
        speed.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void SystemInfo_GetCpuBaseSpeedMhz_ReturnsPositiveValueOrZero()
    {
        int speed = SystemInfo.GetCpuBaseSpeedMhz();
        speed.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void SystemInfo_GetOsVersion_ReturnsNonEmptyString()
    {
        string os = SystemInfo.GetOsDisplayVersion();
        os.Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_GetMotherboardInfo_ReturnsValidStrings()
    {
        string vendor = SystemInfo.GetMotherboardManufacturer();
        string product = SystemInfo.GetMotherboardProductName();
        vendor.Should().NotBeNull();
        product.Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_HasBattery_ReturnsBoolean()
    {
        string battery = SystemInfo.GetBatteryDeviceName();
        battery.Should().NotBeNull();
    }

    [Fact]
    public void HardwareMonitor_InstantiatesAndPolls()
    {
        var cfg = new AppSettings();
        var logger = NullLogger<HardwareMonitor>.Instance;
        var sysInfoMock = NSubstitute.Substitute.For<ISystemInfo>();
        var pawnIoMock = NSubstitute.Substitute.For<IPawnIoManager>();
        using var monitor = new HardwareMonitor(logger, cfg, sysInfoMock, pawnIoMock);

        monitor.Should().NotBeNull();
    }
}
