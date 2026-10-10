using FluentAssertions;
using LocalTelemetry.Core.Models;
using Xunit;

namespace LocalTelemetry.Core.Tests.Models;

public class BrandColorDefaultsTests
{
    [Fact]
    public void BuildDefaultMetricColors_ReturnsCompleteColorMap()
    {
        var colors = BrandColorDefaults.BuildDefaultMetricColors();

        colors.Should().NotBeNull();
        colors.Should().ContainKey(Metrics.CpuPct);
        colors.Should().ContainKey(Metrics.GpuPct);
        colors.Should().ContainKey(Metrics.RamPct);
        colors.Should().ContainKey(Metrics.NetDown);
        colors.Should().ContainKey(Metrics.BatteryPct);
    }

    [Fact]
    public void BuildDefaultGroupColors_ReturnsAllGroupColors()
    {
        var groupColors = BrandColorDefaults.BuildDefaultGroupColors();

        groupColors.Should().NotBeNull();
        groupColors.Should().ContainKey("cpu");
        groupColors.Should().ContainKey("gpu");
        groupColors.Should().ContainKey("ram");
        groupColors.Should().ContainKey("network");
        groupColors.Should().ContainKey("battery");
        groupColors.Should().ContainKey("disk");
    }

    [Fact]
    public void ResolveDiskColor_MatchesVendorsAndModelsCorrectly()
    {
        BrandColorDefaults.ResolveDiskColor("Samsung", "980 PRO").Should().Be("#1428A0");
        BrandColorDefaults.ResolveDiskColor("WDC", "WD10EZEX").Should().Be("#005195");
        BrandColorDefaults.ResolveDiskColor("", "Crucial MX500").Should().Be("#0068FF");
        BrandColorDefaults.ResolveDiskColor("UnknownVendor", "UnknownModel").Should().BeEmpty();
    }

    [Fact]
    public void DetectionHelpers_ReturnNonEmptyOrFallback()
    {
        BrandColorDefaults.DetectCpuColor().Should().NotBeNull();
        BrandColorDefaults.DetectGpuColor().Should().NotBeNull();
        BrandColorDefaults.DetectRamColor().Should().NotBeNull();
        BrandColorDefaults.DetectDiskColor().Should().NotBeNull();
        BrandColorDefaults.DetectNicColor().Should().NotBeNull();
        BrandColorDefaults.DetectSystemOemColor().Should().NotBeNull();
        BrandColorDefaults.DetectBatteryColor().Should().NotBeNull();
    }

    [Fact]
    public void BuildDefaultGroupColors_FromProvidedDefaults_UsesDiskColorOrFallback()
    {
        var withDisk = BrandColorDefaults.BuildDefaultGroupColors(new Dictionary<string, string>
        {
            [Metrics.CpuPct] = "#111111",
            [Metrics.GpuPct] = "#222222",
            [Metrics.RamPct] = "#333333",
            [Metrics.NetDown] = "#444444",
            [Metrics.BatteryPct] = "#555555",
            ["disk_disk0_read"] = "#666666",
            ["disk_disk0_write"] = "#666666",
        });

        withDisk["cpu"].Should().Be("#111111");
        withDisk["gpu"].Should().Be("#222222");
        withDisk["ram"].Should().Be("#333333");
        withDisk["network"].Should().Be("#444444");
        withDisk["battery"].Should().Be("#555555");
        withDisk["disk"].Should().Be("#666666");

        var emptyDisk = BrandColorDefaults.BuildDefaultGroupColors(new Dictionary<string, string>
        {
            [Metrics.CpuPct] = "#111111",
            [Metrics.GpuPct] = "#222222",
            [Metrics.RamPct] = "#333333",
            [Metrics.NetDown] = "#444444",
            [Metrics.BatteryPct] = "#555555",
            ["disk_disk0_read"] = "",
        });

        emptyDisk["disk"].Should().Be("#AAAAAA");
    }
}
