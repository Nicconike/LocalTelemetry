using FluentAssertions;
using LocalTelemetry.Core.Models;
using Xunit;

namespace LocalTelemetry.Core.Tests.Models;

public class ModelsTests
{
    [Fact]
    public void BrandColorDefaults_DictionariesPopulated()
    {
        BrandColorDefaults.CpuColors.Should().ContainKey("Intel");
        BrandColorDefaults.CpuColors["Intel"].Should().Be("#0068B5");
        BrandColorDefaults.CpuColors.Should().ContainKey("AMD");

        BrandColorDefaults.GpuColors.Should().ContainKey("NVIDIA");
        BrandColorDefaults.GpuColors.Should().ContainKey("AMD");

        BrandColorDefaults.RamColors.Should().ContainKey("Samsung");
        BrandColorDefaults.RamColors.Should().ContainKey("Corsair");

        BrandColorDefaults.DiskColors.Should().ContainKey("Western Digital");
        BrandColorDefaults.DiskColors.Should().ContainKey("Seagate");

        BrandColorDefaults.NicColors.Should().ContainKey("Realtek");
        BrandColorDefaults.NicColors.Should().ContainKey("Broadcom");

        BrandColorDefaults.SystemOemColors.Should().ContainKey("Dell");
        BrandColorDefaults.SystemOemColors.Should().ContainKey("Lenovo");

        BrandColorDefaults.BatteryColors.Should().ContainKey("Sunwoda");
    }

    [Fact]
    public void BrandColorDefaults_BuildDefaultMetricColors_ReturnsFullDictionary()
    {
        var dict = BrandColorDefaults.BuildDefaultMetricColors();

        dict.Should().NotBeNull();
        dict.Should().ContainKey(Metrics.CpuPct);
        dict.Should().ContainKey(Metrics.RamPct);
        dict.Should().ContainKey(Metrics.GpuPct);
        dict.Should().ContainKey(Metrics.NetDown);
        dict.Should().ContainKey(Metrics.BatteryPct);
    }

    [Fact]
    public void MetricDescriptor_PropertiesAssignedCorrectly()
    {
        var desc = new MetricDescriptor("test_id", "TST", "Test Metric", "ms", "test_group");

        desc.Id.Should().Be("test_id");
        desc.ShortLabel.Should().Be("TST");
        desc.FullLabel.Should().Be("Test Metric");
        desc.Unit.Should().Be("ms");
        desc.Group.Should().Be("test_group");
    }

    [Fact]
    public void Metrics_RegistryContainsAllCoreMetrics()
    {
        Metrics.AllMetrics.Should().NotBeEmpty();
        Metrics.AllMetricsById.Should().ContainKey(Metrics.CpuPct);
        Metrics.AllMetricsById.Should().ContainKey(Metrics.CpuTemp);
        Metrics.AllMetricsById.Should().ContainKey(Metrics.CpuFreq);
        Metrics.AllMetricsById.Should().ContainKey(Metrics.CpuPower);
        Metrics.AllMetricsById.Should().ContainKey(Metrics.RamPct);
        Metrics.AllMetricsById.Should().ContainKey(Metrics.RamUsed);
        Metrics.AllMetricsById.Should().ContainKey(Metrics.GpuPct);
        Metrics.AllMetricsById.Should().ContainKey(Metrics.GpuTemp);
        Metrics.AllMetricsById.Should().ContainKey(Metrics.GpuVram);
        Metrics.AllMetricsById.Should().ContainKey(Metrics.GpuFreq);
        Metrics.AllMetricsById.Should().ContainKey(Metrics.GpuPower);
        Metrics.AllMetricsById.Should().ContainKey(Metrics.NetDown);
        Metrics.AllMetricsById.Should().ContainKey(Metrics.NetUp);
        Metrics.AllMetricsById.Should().ContainKey(Metrics.NetTotal);
        Metrics.AllMetricsById.Should().ContainKey(Metrics.BatteryPct);
        Metrics.AllMetricsById.Should().ContainKey(Metrics.BatteryRate);
    }

    [Fact]
    public void Metrics_RegisterDisk_AddsReadAndWriteMetrics()
    {
        Metrics.RegisterDisk("0", "DISK0");

        Metrics.AllMetricsById.Should().ContainKey("disk_0_read");
        Metrics.AllMetricsById.Should().ContainKey("disk_0_write");

        Metrics.AllMetricsById["disk_0_read"].ShortLabel.Should().Be("DISK0");
        Metrics.AllMetricsById["disk_0_read"].Unit.Should().Be("MB/s");

        // Idempotency check
        Metrics.RegisterDisk("0", "DISK0");
    }

    [Fact]
    public void Metrics_Format_AllTypesFormattedCorrectly()
    {
        var snap = new TelemetrySnapshot
        {
            CpuUsagePct = 45.2f,
            CpuTempPackageC = 65.8f,
            CpuFreqGhz = 4.25f,
            CpuPackagePowerW = 85f,
            RamUsagePct = 60.1f,
            RamUsedGb = 16.4f,
            GpuUsagePct = 98.0f,
            GpuTempC = 72.3f,
            GpuVramUsedMb = 4096f,
            GpuFreqMHz = 1850f,
            GpuPowerW = 220f,
            NetDownBps = 10_500_000, // 10.5 MB/s
            NetUpBps = 1_200_000,    // 1.2 MB/s
            NetTotalBytes = 5_400_000_000, // 5.4 GB
            Disks = [new DiskSnapshot { Id = "0", Label = "DISK0", ReadMBps = 1500f, WriteMBps = 250f }],
            BatteryPct = 85f,
            BatteryChargeRateW = 12.5f,
            BatteryIsCharging = true,
            IsOnACPower = true
        };

        Metrics.Format(Metrics.CpuPct, snap, false, false).Should().Be("45%");
        Metrics.Format(Metrics.CpuTemp, snap, false, false).Should().Be("66°C");
        Metrics.Format(Metrics.CpuTemp, snap, false, true).Should().Be("150°F");
        Metrics.Format(Metrics.CpuFreq, snap, false, false).Should().Be("4.25GHz");
        Metrics.Format(Metrics.CpuPower, snap, false, false).Should().Be("85W");

        Metrics.Format(Metrics.RamPct, snap, false, false).Should().Be("60%");
        Metrics.Format(Metrics.RamUsed, snap, false, false).Should().Be("16.4GB");

        Metrics.Format(Metrics.GpuPct, snap, false, false).Should().Be("98%");
        Metrics.Format(Metrics.GpuTemp, snap, false, false).Should().Be("72°C");
        Metrics.Format(Metrics.GpuVram, snap, false, false).Should().Be("4096MB");
        Metrics.Format(Metrics.GpuFreq, snap, false, false).Should().Be("1850MHz");
        Metrics.Format(Metrics.GpuPower, snap, false, false).Should().Be("220W");

        // Net formatting (Bytes vs Bits)
        Metrics.Format(Metrics.NetDown, snap, false, false).Should().Be("10.5MB/s");
        Metrics.Format(Metrics.NetDown, snap, true, false).Should().Be("84.0Mb/s");
        Metrics.Format(Metrics.NetUp, snap, false, false).Should().Be("1.2MB/s");
        Metrics.Format(Metrics.NetUp, snap, true, false).Should().Be("9.6Mb/s");

        // NetTotal
        Metrics.Format(Metrics.NetTotal, snap, false, false).Should().Be("5.40GB");

        // Battery
        Metrics.Format(Metrics.BatteryPct, snap, false, false).Should().Be("85%");
        Metrics.Format(Metrics.BatteryRate, snap, false, false).Should().Be("+12.5W");

        // Disk formatting
        Metrics.Format("disk_0_read", snap, false, false).Should().Be("1.5GB/s");
        Metrics.Format("disk_0_write", snap, false, false).Should().Be("250.0MB/s");
    }

    [Fact]
    public void Metrics_Format_ZeroOrNegativeValues_HandledGracefully()
    {
        var emptySnap = TelemetrySnapshot.Empty;

        Metrics.Format(Metrics.CpuTemp, emptySnap, false, false).Should().Be("--");
        Metrics.Format(Metrics.CpuFreq, emptySnap, false, false).Should().Be("--");
        Metrics.Format(Metrics.CpuPower, emptySnap, false, false).Should().Be("--");
        Metrics.Format(Metrics.GpuFreq, emptySnap, false, false).Should().Be("--");
        Metrics.Format(Metrics.GpuPower, emptySnap, false, false).Should().Be("--");
        Metrics.Format(Metrics.BatteryPct, emptySnap, false, false).Should().Be("--");
        Metrics.Format(Metrics.BatteryRate, emptySnap, false, false).Should().Be("--");
        Metrics.Format("unknown_metric", emptySnap, false, false).Should().Be("--");
    }

    [Fact]
    public void TelemetrySnapshot_PropertiesAndDefaults()
    {
        var snap = TelemetrySnapshot.Empty;

        snap.CpuUsagePct.Should().Be(0f);
        snap.Timestamp.Should().BeBefore(DateTime.UtcNow.AddSeconds(1));
        snap.Disks.Should().BeEmpty();
        snap.PrimaryDiskType.Should().BeEmpty();
    }
}
