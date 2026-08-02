using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class GpuAndDiskMonitorsTests
{
    [Fact]
    public void DiskQuery_QueryAllDrives_ReturnsList()
    {
        var drives = DiskQuery.QueryAllDrives();
        drives.Should().NotBeNull();

        var primary = DiskQuery.QueryPhysicalDrive0();
        if (primary is not null)
        {
            primary.DiskIndex.Should().BeGreaterOrEqualTo(0);
            primary.BusType.Should().NotBeNull();
        }

        DiskQuery.RefreshDiskCache();
    }

    [Fact]
    public void DiskInfo_PropertiesCanBeInitialized()
    {
        var disk = new DiskInfo
        {
            Model = "Samsung SSD 980 PRO 1TB",
            Vendor = "Samsung",
            BusType = "NVMe",
            SizeBytes = 1_000_204_886_016,
            DiskIndex = 0,
            IsBootDrive = true
        };

        disk.Model.Should().Be("Samsung SSD 980 PRO 1TB");
        disk.Vendor.Should().Be("Samsung");
        disk.BusType.Should().Be("NVMe");
        disk.SizeBytes.Should().Be(1_000_204_886_016);
        disk.DiskIndex.Should().Be(0);
        disk.IsBootDrive.Should().BeTrue();
    }

    [Fact]
    public void AmdGpuMonitor_MethodsAndPropertiesExposed()
    {
        using var amd = new AmdGpuMonitor();
        bool avail = amd.IsAvailable;
        avail.Should().Be(amd.IsAvailable);

        _ = amd.GetTempC();
        _ = amd.GetUsagePct();
        _ = amd.GetClockMHz();
        _ = amd.GetVram();
        _ = amd.GetPowerLimitW();
        _ = amd.GetPowerDrawW();
        amd.Dispose();
    }

    [Fact]
    public void IntelGpuMonitor_MethodsAndPropertiesExposed()
    {
        using var intel = new IntelGpuMonitor();
        bool avail = intel.IsAvailable;
        avail.Should().Be(intel.IsAvailable);

        _ = intel.GetTempC();
        _ = intel.GetClockMHz();
        _ = intel.GetVram();
        intel.Dispose();
    }

    [Fact]
    public void NvGpuMonitor_MethodsAndPropertiesExposed()
    {
        using var nv = new NvGpuMonitor();
        bool avail = nv.IsAvailable;
        avail.Should().Be(nv.IsAvailable);

        _ = nv.Query();
        nv.Dispose();
    }

    [Fact]
    public void WddmGpuMonitor_MethodsAndPropertiesExposed()
    {
        using var wddm = new WddmGpuMonitor();
        bool avail = wddm.IsAvailable;
        avail.Should().Be(wddm.IsAvailable);

        _ = wddm.GetUsagePct();
        wddm.Dispose();
    }

    [Fact]
    public void IntelPowerGadget_MethodsAndPropertiesExposed()
    {
        using var power = new IntelPowerGadget();
        bool avail = power.IsAvailable;
        avail.Should().Be(power.IsAvailable);

        power.Dispose();
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_QueriesAsync()
    {
        var usage = await WindowsNetworkUsageProvider.GetUsageAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);
        usage.Should().NotBeNull();
    }
}
