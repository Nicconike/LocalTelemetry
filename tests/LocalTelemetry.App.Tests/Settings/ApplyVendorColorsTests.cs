using FluentAssertions;
using LocalTelemetry.App;
using LocalTelemetry.Core.Config;
using Xunit;

namespace LocalTelemetry.App.Tests.Settings;

public class ApplyVendorColorsTests
{
    [Fact]
    public void ApplyVendorColors_PopulatesAllGroupColorsFromMetricDefaults()
    {
        var cfg = new AppSettings();

        App.ApplyVendorColors(cfg);

        cfg.Overlay.GroupColors.Should().ContainKeys("cpu", "gpu", "ram", "network", "battery", "disk");
        cfg.Overlay.GroupColors["cpu"].Should().Be(cfg.Overlay.DefaultMetricColors["cpu_pct"]);
        cfg.Overlay.GroupColors["gpu"].Should().Be(cfg.Overlay.DefaultMetricColors["gpu_pct"]);
        cfg.Overlay.GroupColors["ram"].Should().Be(cfg.Overlay.DefaultMetricColors["ram_pct"]);
        cfg.Overlay.GroupColors["network"].Should().Be(cfg.Overlay.DefaultMetricColors["net_down"]);
        cfg.Overlay.GroupColors["battery"].Should().Be(cfg.Overlay.DefaultMetricColors["battery_pct"]);
    }

    [Fact]
    public void ApplyVendorColors_OverwritesNonBrandDefaultGroupColors()
    {
        var cfg = new AppSettings();
        cfg.Overlay.GroupColors["cpu"] = "#00E5FF";
        cfg.Overlay.GroupColors["gpu"] = "#88CCFF";

        App.ApplyVendorColors(cfg);

        cfg.Overlay.GroupColors["cpu"].Should().Be(cfg.Overlay.DefaultMetricColors["cpu_pct"]);
        cfg.Overlay.GroupColors["gpu"].Should().Be(cfg.Overlay.DefaultMetricColors["gpu_pct"]);
    }

    [Fact]
    public void ApplyVendorColors_PreservesUserCustomizedGroupColors()
    {
        var cfg = new AppSettings();
        cfg.Overlay.UserCustomizedGroupColors.Add("cpu");
        cfg.Overlay.GroupColors["cpu"] = "#FF0000";
        cfg.Overlay.GroupColors["gpu"] = "#88CCFF";

        App.ApplyVendorColors(cfg);

        cfg.Overlay.GroupColors["cpu"].Should().Be("#FF0000");
        cfg.Overlay.GroupColors["gpu"].Should().Be(cfg.Overlay.DefaultMetricColors["gpu_pct"]);
    }

    [Fact]
    public void ApplyVendorColors_PreservesUserCustomizedMetricColors()
    {
        var cfg = new AppSettings();
        cfg.Overlay.UserCustomizedMetricColors.Add("cpu_pct");
        cfg.Overlay.MetricColors["cpu_pct"] = "#FF0000";

        App.ApplyVendorColors(cfg);

        cfg.Overlay.MetricColors["cpu_pct"].Should().Be("#FF0000");
        cfg.Overlay.MetricColors["gpu_pct"].Should().Be(cfg.Overlay.DefaultMetricColors["gpu_pct"]);
    }
}
