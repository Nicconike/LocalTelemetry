using FluentAssertions;
using LocalTelemetry.App.Settings;
using LocalTelemetry.Core.Config;
using Xunit;

namespace LocalTelemetry.App.Tests.Settings;

public class SettingsDtoTests
{
    [Fact]
    public void SettingsDtoMapping_ToDto_MapsAllFieldsCorrectly()
    {
        var src = new AppSettings
        {
            RunAtStartup = true,
            StartMinimized = true,
            MinimizeToTrayOnClose = true,
            EnableFileLogging = false,
            WindowTheme = "dark",
            Monitoring = new MonitoringConfig
            {
                PollIntervalMs = 500,
                UseFahrenheit = true,
                UseNetBits = true,
                PreferredNic = "Ethernet",
                EnableCpu = false,
                EnableGpu = true,
                EnableRam = true,
                EnableNet = true,
                EnableDisk = false,
                EnableBattery = true,
                GpuUsageSource = "wddm",
                LogCpuMode = 1,
                LogGpuMode = 2,
            },
            Overlay = new OverlayConfig
            {
                Visible = true,
                DoubleClickAction = "taskmanager",
                Placement = "right",
                PlacementOffset = 15,
                Opacity = 80,
                ScalePct = 110,
                FontSizePx = 16f,
                FontBold = true,
                LabelColor = "#111111",
                BgColor = "#222222",
                ValueColor = "#333333",
                FollowWindowsTheme = false,
                Row1 = ["cpu_pct", "gpu_pct"]
            },
            Alerts = new AlertConfig
            {
                Enabled = true,
                AlertCpuTemp = true,
                CpuTempMaxC = 95f,
                CooldownSecs = 120
            }
        };

        var dto = SettingsDtoMapping.ToDto(src);

        dto.RunAtStartup.Should().BeTrue();
        dto.StartMinimized.Should().BeTrue();
        dto.MinimizeToTray.Should().BeTrue();
        dto.EnableFileLogging.Should().BeFalse();
        dto.WindowTheme.Should().Be("dark");

        dto.Monitoring.IntervalMs.Should().Be(500);
        dto.Monitoring.UseFahrenheit.Should().BeTrue();
        dto.Monitoring.UseNetBits.Should().BeTrue();
        dto.Monitoring.PreferredNic.Should().Be("Ethernet");
        dto.Monitoring.TrackCpu.Should().BeFalse();
        dto.Monitoring.TrackGpu.Should().BeTrue();
        dto.Monitoring.GpuUsageSource.Should().Be("wddm");

        dto.Overlay.Visible.Should().BeTrue();
        dto.Overlay.DoubleClickAction.Should().Be("taskmanager");
        dto.Overlay.Position.Should().Be("right");
        dto.Overlay.OffsetX.Should().Be(15);
        dto.Overlay.Opacity.Should().Be(80);
        dto.Overlay.Scale.Should().Be(110);
        dto.Overlay.FontSizePx.Should().Be(16f);
        dto.Overlay.FontBold.Should().BeTrue();

        dto.Alerts.Enabled.Should().BeTrue();
        dto.Alerts.AlertCpuTemp.Should().BeTrue();
        dto.Alerts.CpuTempMaxC.Should().Be(95f);
        dto.Alerts.CooldownSecs.Should().Be(120);
    }

    [Fact]
    public void SettingsDtoMapping_ApplyTo_UpdatesAppSettings()
    {
        var target = new AppSettings();

        var dto = new SettingsDto
        {
            RunAtStartup = true,
            StartMinimized = true,
            MinimizeToTray = true,
            EnableFileLogging = true,
            WindowTheme = "custom",
            Monitoring = new MonitoringDto
            {
                IntervalMs = 2000,
                UseFahrenheit = true,
                UseNetBits = true,
                PreferredNic = "WiFi",
                TrackCpu = true,
                TrackGpu = false,
                TrackRam = true,
                TrackNet = false,
                TrackDisk = false,
                TrackBattery = false,
                GpuUsageSource = "driver",
            },
            Overlay = new OverlayDto
            {
                Visible = true,
                DoubleClickAction = "settings",
                Position = "center",
                OffsetX = 50,
                Opacity = 90,
                Scale = 100,
                FontSizePx = 12f,
                FontBold = false,
                LabelColor = "#AAAAAA",
                BgColor = "#BBBBBB",
                TextColor = "#CCCCCC",
                FollowWindowsTheme = true,
                Row1 = ["ram_pct", "ram_used"]
            },
            Alerts = new AlertsDto
            {
                Enabled = true,
                AlertRamUsage = true,
                RamUsageMaxPct = 88f,
                CooldownSecs = 45
            }
        };

        SettingsDtoMapping.ApplyTo(target, dto);

        target.RunAtStartup.Should().BeTrue();
        target.StartMinimized.Should().BeTrue();
        target.MinimizeToTrayOnClose.Should().BeTrue();
        target.WindowTheme.Should().Be("custom");

        target.Monitoring.PollIntervalMs.Should().Be(2000);
        target.Monitoring.UseFahrenheit.Should().BeTrue();
        target.Monitoring.PreferredNic.Should().Be("WiFi");
        target.Monitoring.EnableGpu.Should().BeFalse();

        target.Overlay.Visible.Should().BeTrue();
        target.Overlay.Placement.Should().Be("right");
        target.Overlay.PlacementOffset.Should().Be(50);
        target.Overlay.Row1.Should().Contain("ram_pct");

        target.Alerts.Enabled.Should().BeTrue();
        target.Alerts.AlertRamUsage.Should().BeTrue();
        target.Alerts.RamUsageMaxPct.Should().Be(88f);
    }
}
