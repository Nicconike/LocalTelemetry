using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class SystemInfoWrapperTests
{
    [Fact]
    public void SystemInfoWrapper_CallsDelegateMethods()
    {
        var wrapper = new SystemInfoWrapper();

        wrapper.GetCpuSocket().Should().NotBeNull();
        wrapper.GetCpuName().Should().NotBeNull();
        wrapper.GetCpuVendor().Should().NotBeNull();
        wrapper.GetCpuCoreCount().Should().BeGreaterThan(0);
        wrapper.GetCpuThreadCount().Should().BeGreaterThan(0);
        wrapper.GetCpuMaxSpeedMhz().Should().BeGreaterThanOrEqualTo(0);
        wrapper.GetCpuBaseSpeedMhz().Should().BeGreaterThanOrEqualTo(0);
        wrapper.GetGpus().Should().NotBeNull();
        wrapper.GetTotalRamBytes().Should().BeGreaterThanOrEqualTo(0);
        wrapper.GetInstalledRamGb().Should().BeGreaterThanOrEqualTo(0);
        wrapper.GetRamManufacturer().Should().NotBeNull();
        wrapper.GetRamModuleCount().Should().BeGreaterThanOrEqualTo(0);
        wrapper.GetRamSpeed().Should().NotBeNull();
        wrapper.GetMotherboardManufacturer().Should().NotBeNull();
        wrapper.GetMotherboardProductName().Should().NotBeNull();
        wrapper.GetMotherboardVersion().Should().NotBeNull();
        wrapper.GetMotherboardSerial().Should().NotBeNull();
        wrapper.GetBiosVersion().Should().NotBeNull();

        bool hasBattery = wrapper.HasBattery();
        if (hasBattery)
        {
            wrapper.GetBatteryManufacturer().Should().NotBeNull();
            wrapper.GetBatteryDeviceName().Should().NotBeNull();
            wrapper.GetBatteryDesignCapacity().Should().NotBeNull();
            wrapper.GetBatteryFullChargedCapacity().Should().NotBeNull();
        }

        wrapper.GetPsuName().Should().NotBeNull();
        wrapper.GetPsuMaxCapacity().Should().NotBeNull();
        wrapper.GetDiskModel().Should().NotBeNull();
        wrapper.GetDiskManufacturer().Should().NotBeNull();
        wrapper.GetAllDisks().Should().NotBeNull();
        wrapper.GetNics().Should().NotBeNull();
        wrapper.GetOsDisplayVersion().Should().NotBeNull();
        wrapper.GetCpuColor().Should().NotBeNull();
        wrapper.GetGpuColor().Should().NotBeNull();
        wrapper.GetGpuVendor().Should().NotBeNull();
        wrapper.GetDiskColor().Should().NotBeNull();
        wrapper.GetRamColor().Should().NotBeNull();
        wrapper.GetNicColor().Should().NotBeNull();
        wrapper.GetSystemOemColor().Should().NotBeNull();
        wrapper.GetRamModules().Should().NotBeNull();
        wrapper.GetRamType().Should().NotBeNull();
        wrapper.GetSystemTypeLabel().Should().NotBeNull();
    }
}
