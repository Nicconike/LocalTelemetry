using FluentAssertions;
using LocalTelemetry.Core.Models;
using Xunit;

namespace LocalTelemetry.Core.Tests.Models;

public class TelemetrySnapshotTests
{
    [Fact]
    public void TelemetrySnapshot_Properties_ReturnValues()
    {
        var now = DateTime.UtcNow;
        var disk = new DiskSnapshot
        {
            Id = "disk0",
            Label = "DISK0",
            BusType = "NVMe",
            ReadMBps = 100f,
            WriteMBps = 50f
        };

        var snapshot = new TelemetrySnapshot
        {
            CpuUsagePct = 50f,
            CpuTempPackageC = 60f,
            CpuFreqGhz = 4f,
            CpuPackagePowerW = 65f,
            RamUsagePct = 70f,
            RamUsedGb = 16f,
            GpuUsagePct = 80f,
            GpuTempC = 70f,
            GpuVramUsedMb = 8000f,
            GpuFreqMHz = 2000f,
            GpuPowerW = 200f,
            NetDownBps = 1000000,
            NetUpBps = 500000,
            NetDownBytesDelta = 1024,
            NetUpBytesDelta = 512,
            NetInterfaceName = "Ethernet",
            NetTotalBytes = 50000000,
            Disks = [disk],
            DiskReadMbps = 100f,
            DiskWriteMbps = 50f,
            PrimaryDiskType = "NVMe",
            BatteryPct = 90f,
            BatteryIsCharging = true,
            IsOnACPower = true,
            BatteryChargeRateW = 45f,
            Timestamp = now
        };

        snapshot.CpuUsagePct.Should().Be(50f);
        snapshot.CpuTempPackageC.Should().Be(60f);
        snapshot.CpuFreqGhz.Should().Be(4f);
        snapshot.CpuPackagePowerW.Should().Be(65f);
        snapshot.RamUsagePct.Should().Be(70f);
        snapshot.RamUsedGb.Should().Be(16f);
        snapshot.GpuUsagePct.Should().Be(80f);
        snapshot.GpuTempC.Should().Be(70f);
        snapshot.GpuVramUsedMb.Should().Be(8000f);
        snapshot.GpuFreqMHz.Should().Be(2000f);
        snapshot.GpuPowerW.Should().Be(200f);
        snapshot.NetDownBps.Should().Be(1000000);
        snapshot.NetUpBps.Should().Be(500000);
        snapshot.NetDownBytesDelta.Should().Be(1024);
        snapshot.NetUpBytesDelta.Should().Be(512);
        snapshot.NetInterfaceName.Should().Be("Ethernet");
        snapshot.NetTotalBytes.Should().Be(50000000);
        snapshot.Disks.Should().HaveCount(1);
        snapshot.DiskReadMbps.Should().Be(100f);
        snapshot.DiskWriteMbps.Should().Be(50f);
        snapshot.PrimaryDiskType.Should().Be("NVMe");
        snapshot.BatteryPct.Should().Be(90f);
        snapshot.BatteryIsCharging.Should().BeTrue();
        snapshot.IsOnACPower.Should().BeTrue();
        snapshot.BatteryChargeRateW.Should().Be(45f);
        snapshot.Timestamp.Should().Be(now);

        var emptyDisk = new DiskSnapshot();
        emptyDisk.Id.Should().BeEmpty();
        emptyDisk.Label.Should().BeEmpty();
        emptyDisk.BusType.Should().BeEmpty();
        emptyDisk.ReadMBps.Should().Be(0f);
        emptyDisk.WriteMBps.Should().Be(0f);

        TelemetrySnapshot.Empty.Should().NotBeNull();
        TelemetrySnapshot.Empty.CpuUsagePct.Should().Be(0f);
    }
}
