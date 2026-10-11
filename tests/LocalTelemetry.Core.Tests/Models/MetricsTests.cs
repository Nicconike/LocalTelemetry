using FluentAssertions;
using LocalTelemetry.Core.Models;
using Xunit;

namespace LocalTelemetry.Core.Tests.Models;

public class MetricsTests
{
    [Fact]
    public void Metrics_RegisterDisk_AddsDescriptorsAndMapEntry()
    {
        Metrics.RegisterDisk("testdisk", "DISK_TEST");

        Metrics.AllMetricsById.Should().ContainKey("disk_testdisk_read");
        Metrics.AllMetricsById.Should().ContainKey("disk_testdisk_write");

        // Duplicate registration should be no-op
        Metrics.RegisterDisk("testdisk", "DISK_TEST");
    }

    [Fact]
    public void Metrics_Format_AllCpuAndRamMetrics()
    {
        var s = new TelemetrySnapshot
        {
            CpuUsagePct = 45.2f,
            CpuTempPackageC = 65.0f,
            CpuFreqGhz = 4.2f,
            CpuPackagePowerW = 85.0f,
            RamUsagePct = 60.0f,
            RamUsedGb = 16.4f,
        };

        Metrics.Format(Metrics.CpuPct, s, false, false).Should().Be("45%");
        Metrics.Format(Metrics.CpuTemp, s, false, false).Should().Be("65°C");
        Metrics.Format(Metrics.CpuTemp, s, false, true).Should().Be("149°F");

        var sNoTemp = s with { CpuTempPackageC = 0f };
        Metrics.Format(Metrics.CpuTemp, sNoTemp, false, false).Should().Be("--");

        Metrics.Format(Metrics.CpuFreq, s, false, false).Should().Be("4.20GHz");
        var sNoFreq = s with { CpuFreqGhz = 0f };
        Metrics.Format(Metrics.CpuFreq, sNoFreq, false, false).Should().Be("--");

        Metrics.Format(Metrics.CpuPower, s, false, false).Should().Be("85W");
        var sNoPower = s with { CpuPackagePowerW = 0f };
        Metrics.Format(Metrics.CpuPower, sNoPower, false, false).Should().Be("--");

        Metrics.Format(Metrics.RamPct, s, false, false).Should().Be("60%");
        Metrics.Format(Metrics.RamUsed, s, false, false).Should().Be("16.4GB");
    }

    [Fact]
    public void Metrics_Format_AllGpuMetrics()
    {
        var s = new TelemetrySnapshot
        {
            GpuUsagePct = 95.0f,
            GpuTempC = 72.0f,
            GpuVramUsedMb = 12288.0f,
            GpuFreqMHz = 2400.0f,
            GpuPowerW = 280.0f,
        };

        Metrics.Format(Metrics.GpuPct, s, false, false).Should().Be("95%");
        Metrics.Format(Metrics.GpuTemp, s, false, false).Should().Be("72°C");
        Metrics.Format(Metrics.GpuTemp, s, false, true).Should().Be("162°F");
        Metrics.Format(Metrics.GpuVram, s, false, false).Should().Be("12288MB");
        Metrics.Format(Metrics.GpuFreq, s, false, false).Should().Be("2400MHz");
        Metrics.Format(Metrics.GpuPower, s, false, false).Should().Be("280W");
    }

    [Fact]
    public void Metrics_Format_AllNetworkMetrics()
    {
        var s = new TelemetrySnapshot
        {
            NetDownBps = 10_000_000_000,
            NetUpBps = 50_000_000,
            NetTotalBytes = 1_500_000_000_000,
        };

        Metrics.Format(Metrics.NetDown, s, false, false).Should().Be("10.0GB/s");
        Metrics.Format(Metrics.NetDown, s, true, false).Should().Be("80.0Gb/s");

        var sMb = s with { NetDownBps = 5_000_000 };
        Metrics.Format(Metrics.NetDown, sMb, false, false).Should().Be("5.0MB/s");

        var sKb = s with { NetDownBps = 2_000 };
        Metrics.Format(Metrics.NetDown, sKb, false, false).Should().Be("2.0KB/s");

        var sB = s with { NetDownBps = 500 };
        Metrics.Format(Metrics.NetDown, sB, false, false).Should().Be("500B/s");

        Metrics.Format(Metrics.NetTotal, s, false, false).Should().Be("1.50TB");

        var sGbTot = s with { NetTotalBytes = 2_500_000_000 };
        Metrics.Format(Metrics.NetTotal, sGbTot, false, false).Should().Be("2.50GB");

        var sMbTot = s with { NetTotalBytes = 15_000_000 };
        Metrics.Format(Metrics.NetTotal, sMbTot, false, false).Should().Be("15.00MB");

        var sKbTot = s with { NetTotalBytes = 50_000 };
        Metrics.Format(Metrics.NetTotal, sKbTot, false, false).Should().Be("50.00KB");

        var sBTot = s with { NetTotalBytes = 800 };
        Metrics.Format(Metrics.NetTotal, sBTot, false, false).Should().Be("800B");

        var sOffline = new TelemetrySnapshot { NetDownBps = 0, NetUpBps = 0, NetInterfaceName = "" };
        Metrics.Format(Metrics.NetDown, sOffline, false, false).Should().Be("--");
        Metrics.Format(Metrics.NetUp, sOffline, false, false).Should().Be("--");

        var sConnectedIdle = new TelemetrySnapshot { NetDownBps = 0, NetUpBps = 0, NetInterfaceName = "Ethernet" };
        Metrics.Format(Metrics.NetDown, sConnectedIdle, false, false).Should().Be("0B/s");
        Metrics.Format(Metrics.NetUp, sConnectedIdle, false, false).Should().Be("0B/s");
    }

    [Fact]
    public void Metrics_Format_AllBatteryMetrics()
    {
        var s = new TelemetrySnapshot
        {
            BatteryPct = 85.0f,
            BatteryChargeRateW = 45.0f,
        };

        Metrics.Format(Metrics.BatteryPct, s, false, false).Should().Be("85%");
        Metrics.Format(Metrics.BatteryRate, s, false, false).Should().Be("+45.0W");

        var sDischarge = s with { BatteryChargeRateW = -15.0f };
        Metrics.Format(Metrics.BatteryRate, sDischarge, false, false).Should().Be("-15.0W");

        var sFull = s with { BatteryChargeRateW = 0f };
        Metrics.Format(Metrics.BatteryRate, sFull, false, false).Should().Be("Full");

        var sNoBat = s with { BatteryPct = 0f };
        Metrics.Format(Metrics.BatteryPct, sNoBat, false, false).Should().Be("--");
        Metrics.Format(Metrics.BatteryRate, sNoBat, false, false).Should().Be("--");
    }

    [Fact]
    public void Metrics_Format_DiskMetrics()
    {
        var disk = new DiskSnapshot
        {
            Id = "disk0",
            ReadMBps = 1500.0f,
            WriteMBps = 2.5f,
        };

        var s = new TelemetrySnapshot
        {
            Disks = [disk]
        };

        Metrics.Format("disk_disk0_read", s, false, false).Should().Be("1.5GB/s");
        Metrics.Format("disk_disk0_write", s, false, false).Should().Be("2.5MB/s");

        var diskKb = disk with { WriteMBps = 0.5f };
        var sKb = s with { Disks = [diskKb] };
        Metrics.Format("disk_disk0_write", sKb, false, false).Should().Be("500KB/s");

        Metrics.Format("disk_nonexistent_read", s, false, false).Should().Be("--");
        Metrics.Format("invalid_format", s, false, false).Should().Be("--");
    }

    [Fact]
    public void Metrics_WidestValueString_ReturnsMaximumWidths()
    {
        Metrics.WidestValueString(Metrics.CpuPct, false, false).Should().Be("100%");
        Metrics.WidestValueString(Metrics.CpuTemp, false, false).Should().Be("100°C");
        Metrics.WidestValueString(Metrics.CpuTemp, false, true).Should().Be("212°F");
        Metrics.WidestValueString(Metrics.CpuFreq, false, false).Should().Be("9.99GHz");
        Metrics.WidestValueString(Metrics.CpuPower, false, false).Should().Be("999W");
        Metrics.WidestValueString(Metrics.RamUsed, false, false).Should().Be("999.9GB");
        Metrics.WidestValueString(Metrics.GpuVram, false, false).Should().Be("65535MB");
        Metrics.WidestValueString(Metrics.GpuFreq, false, false).Should().Be("9999MHz");
        Metrics.WidestValueString(Metrics.NetDown, false, false).Should().Be("999.9GB/s");
        Metrics.WidestValueString(Metrics.NetDown, true, false).Should().Be("9999.9Gb/s");
        Metrics.WidestValueString(Metrics.NetTotal, false, false).Should().Be("999.99TB");
        Metrics.WidestValueString(Metrics.BatteryRate, false, false).Should().Be("+999.9W");
        Metrics.WidestValueString("disk_disk0_read", false, false).Should().Be("999.9GB/s");
        Metrics.WidestValueString("unknown_metric", false, false).Should().Be("9999");
    }
}
