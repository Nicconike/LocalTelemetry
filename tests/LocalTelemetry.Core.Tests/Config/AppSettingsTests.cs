using System.Reflection;
using FluentAssertions;
using LocalTelemetry.Core.Config;
using Xunit;

namespace LocalTelemetry.Core.Tests.Config;

public class AppSettingsTests : IDisposable
{
    private readonly string _tempDir;

    public AppSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LocalTelemetryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        AppSettings.InitPaths(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void InitPaths_PortableMode_SetsPathsUnderExeDir()
    {
        AppSettings.InitPaths(_tempDir);

        AppSettings.ConfigPath.Should().Be(Path.Combine(_tempDir, "settings.json"));
        AppSettings.NetUsagePath.Should().Be(Path.Combine(_tempDir, "internet_usage.jsonl"));
        AppSettings.LogsDir.Should().Be(Path.Combine(_tempDir, "logs"));
        AppSettings.SystemLogPath.Should().Be(Path.Combine(_tempDir, "logs", "lt_system.log"));
        AppSettings.HardwareStatePath.Should().Be(Path.Combine(_tempDir, "hardware_state.json"));
        AppSettings.MetricsLogPath.Should().Contain("lt_");
        AppSettings.MetricsLogPath.Should().Contain("logs");
    }

    [Fact]
    public void InitPaths_NormalMode_CreatesDataDirectoryAndSetsPaths()
    {
        string subDir = Path.Combine(_tempDir, "SubExeDir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "app.mode"), "");

        AppSettings.InitPaths(subDir);

        AppSettings.ConfigPath.Should().Contain("LocalTelemetry");
        AppSettings.LogsDir.Should().Contain("LocalTelemetry");
        AppSettings.LogsDir.Should().EndWith("logs");
    }

    [Fact]
    public void Load_FileDoesNotExist_CreatesDefaultsAndSaves()
    {
        var settings = AppSettings.Load();

        settings.Should().NotBeNull();
        File.Exists(AppSettings.ConfigPath).Should().BeTrue();
        settings.Monitoring.EnableCpu.Should().BeTrue();
        settings.Overlay.Placement.Should().Be("left");
        settings.Alerts.CpuTempMaxC.Should().Be(90f);
    }

    [Fact]
    public void Load_ValidJsonFile_DeserializesCorrectly()
    {
        var custom = new AppSettings
        {
            RunAtStartup = true,
            ShowSystemTrayIcon = false,
            EnableFileLogging = false,
            WindowTheme = "dark",
            Monitoring = new MonitoringConfig { PollIntervalMs = 500 },
            Overlay = new OverlayConfig { Visible = true }
        };
        custom.Save();

        var reloaded = AppSettings.Load();

        reloaded.RunAtStartup.Should().BeTrue();
        reloaded.ShowSystemTrayIcon.Should().BeFalse();
        reloaded.EnableFileLogging.Should().BeFalse();
        reloaded.WindowTheme.Should().Be("dark");
        reloaded.Monitoring.PollIntervalMs.Should().Be(500);
        reloaded.Overlay.Visible.Should().BeTrue();
    }

    [Fact]
    public void Save_WhenBothTrayAndOverlayDisabled_EnforcesTrayEnabledGuardrail()
    {
        var settings = new AppSettings
        {
            ShowSystemTrayIcon = false,
            Overlay = new OverlayConfig { Visible = false }
        };
        settings.Save();

        settings.ShowSystemTrayIcon.Should().BeTrue();
        var reloaded = AppSettings.Load();
        reloaded.ShowSystemTrayIcon.Should().BeTrue();
    }

    [Fact]
    public void Load_NullJson_CreatesFreshDefaults()
    {
        File.WriteAllText(AppSettings.ConfigPath, "null");

        var loaded = AppSettings.Load();

        loaded.Should().NotBeNull();
    }

    [Fact]
    public void Load_CorruptJsonFile_BacksUpCorruptFileAndCreatesFresh()
    {
        File.WriteAllText(AppSettings.ConfigPath, "{ corrupt json ... invalid syntax }");

        var loaded = AppSettings.Load();

        loaded.Should().NotBeNull();
        File.Exists(AppSettings.ConfigPath + ".corrupt").Should().BeTrue();
        File.ReadAllText(AppSettings.ConfigPath + ".corrupt").Should().Contain("{ corrupt json");
    }

    [Fact]
    public void Load_LockedFile_BackupFailsGracefully()
    {
        using (var fs = new FileStream(AppSettings.ConfigPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(System.Text.Encoding.UTF8.GetBytes("{ corrupt"));
            fs.Flush();

            var loaded = AppSettings.Load();
            loaded.Should().NotBeNull();
        }

        AppSettings.InitPaths(_tempDir);
    }

    [Fact]
    public void Load_CorruptJsonBackupThrows_HandlesBackupException()
    {
        File.WriteAllText(AppSettings.ConfigPath, "{ invalid json }");
        var prop = typeof(AppSettings).GetProperty("ConfigPath", BindingFlags.Public | BindingFlags.Static);
        prop!.SetValue(null, "Z:\\InvalidFolder\\settings.json");

        var loaded = AppSettings.Load();
        loaded.Should().NotBeNull();

        AppSettings.InitPaths(_tempDir);
    }

    [Fact]
    public void Load_MigratesMissingMetricColorsOnly()
    {
        var json = """
        {
          "overlay": {
            "metricColors": {
              "cpu_pct": "#123456"
            },
            "groupColors": {
              "cpu": "#0068B5",
              "gpu": "#76B900",
              "ram": "#005BAB",
              "network": "#006DB6",
              "battery": "#005691",
              "disk": "#1428A0"
            }
          }
        }
        """;
        File.WriteAllText(AppSettings.ConfigPath, json);

        var settings = AppSettings.Load();

        settings.Overlay.MetricColors.Should().ContainKey("gpu_temp");
        settings.Overlay.MetricColors["cpu_pct"].Should().Be("#123456");
    }

    [Fact]
    public void Load_MigratesMissingGroupColorsOnly()
    {
        var defaults = LocalTelemetry.Core.Models.BrandColorDefaults.BuildDefaultMetricColors();
        var json = $$"""
        {
          "overlay": {
            "metricColors": {{System.Text.Json.JsonSerializer.Serialize(defaults)}},
            "groupColors": {
              "cpu": "#0068B5"
            }
          }
        }
        """;
        File.WriteAllText(AppSettings.ConfigPath, json);

        var settings = AppSettings.Load();

        settings.Overlay.GroupColors.Should().ContainKey("gpu");
    }

    [Fact]
    public void Save_HandlesFileWriteExceptionGracefully()
    {
        var settings = new AppSettings();
        var prop = typeof(AppSettings).GetProperty("ConfigPath", BindingFlags.Public | BindingFlags.Static);
        prop!.SetValue(null, "Z:\\InvalidDrivePath\\settings.json");

        var act = () => settings.Save();
        act.Should().NotThrow();

        AppSettings.InitPaths(_tempDir);
    }

    [Fact]
    public void ConfigSubModels_AllPropertiesCoverage()
    {
        var appSettings = new AppSettings
        {
            RunAtStartup = true,
            ShowSystemTrayIcon = true,
            EnableFileLogging = false,
            WindowTheme = "custom"
        };
        appSettings.RunAtStartup.Should().BeTrue();
        appSettings.ShowSystemTrayIcon.Should().BeTrue();
        appSettings.EnableFileLogging.Should().BeFalse();
        appSettings.WindowTheme.Should().Be("custom");

        var monitoring = new MonitoringConfig
        {
            PollIntervalMs = 2000,
            EnableCpu = false,
            EnableGpu = true,
            EnableRam = true,
            EnableNet = false,
            EnableDisk = false,
            EnableBattery = false,
            UseFahrenheit = true,
            UseNetBits = true,
            PreferredNic = "eth0",
            GpuUsageSource = "wddm",
            LogCpuMode = 2,
            LogGpuMode = 0,
            LogRamMode = 1,
            LogNetMode = 0,
            LogDiskMode = 1,
            LogBatteryMode = 2
        };

        monitoring.PollIntervalMs.Should().Be(2000);
        monitoring.EnableCpu.Should().BeFalse();
        monitoring.EnableGpu.Should().BeTrue();
        monitoring.EnableRam.Should().BeTrue();
        monitoring.EnableNet.Should().BeFalse();
        monitoring.EnableDisk.Should().BeFalse();
        monitoring.EnableBattery.Should().BeFalse();
        monitoring.UseFahrenheit.Should().BeTrue();
        monitoring.UseNetBits.Should().BeTrue();
        monitoring.PreferredNic.Should().Be("eth0");
        monitoring.GpuUsageSource.Should().Be("wddm");
        monitoring.LogCpuMode.Should().Be(2);
        monitoring.LogGpuMode.Should().Be(0);
        monitoring.LogRamMode.Should().Be(1);
        monitoring.LogNetMode.Should().Be(0);
        monitoring.LogDiskMode.Should().Be(1);
        monitoring.LogBatteryMode.Should().Be(2);
        appSettings.Monitoring = monitoring;
        appSettings.Monitoring.Should().BeSameAs(monitoring);

        var overlay = new OverlayConfig
        {
            Visible = true,
            Placement = "right",
            PlacementOffset = 10,
            FloatX = 100,
            FloatY = 200,
            DoubleClickAction = "taskmanager",
            Opacity = 80,
            ScalePct = 120,
            FontSizePx = 16f,
            FontBold = true,
            LabelColor = "#000000",
            ValueColor = "#FFFFFF",
            BgColor = "#222222",
            MetricColors = new Dictionary<string, string> { ["cpu_pct"] = "#FF0000" },
            GroupColors = new Dictionary<string, string> { ["cpu"] = "#00FF00" },
            DefaultMetricColors = new Dictionary<string, string> { ["cpu_pct"] = "#0000FF" },
            FollowWindowsTheme = false,
            UserCustomizedMetricColors = new HashSet<string> { "cpu_pct" },
            UserCustomizedGroupColors = new HashSet<string> { "cpu" },
            Row1 = new List<string> { "cpu_pct", "gpu_pct" }
        };

        overlay.Visible.Should().BeTrue();
        overlay.Placement.Should().Be("right");
        overlay.PlacementOffset.Should().Be(10);
        overlay.FloatX.Should().Be(100);
        overlay.FloatY.Should().Be(200);
        overlay.DoubleClickAction.Should().Be("taskmanager");
        overlay.Opacity.Should().Be(80);
        overlay.ScalePct.Should().Be(120);
        overlay.FontSizePx.Should().Be(16f);
        overlay.FontBold.Should().BeTrue();
        overlay.LabelColor.Should().Be("#000000");
        overlay.ValueColor.Should().Be("#FFFFFF");
        overlay.BgColor.Should().Be("#222222");
        overlay.MetricColors.Should().ContainKey("cpu_pct");
        overlay.GroupColors.Should().ContainKey("cpu");
        overlay.DefaultMetricColors.Should().ContainKey("cpu_pct");
        overlay.FollowWindowsTheme.Should().BeFalse();
        overlay.UserCustomizedMetricColors.Should().Contain("cpu_pct");
        overlay.UserCustomizedGroupColors.Should().Contain("cpu");
        overlay.Row1.Should().HaveCount(2);
        appSettings.Overlay = overlay;
        appSettings.Overlay.Should().BeSameAs(overlay);

        var alerts = new AlertConfig
        {
            Enabled = true,
            AlertCpuTemp = true,
            AlertGpuTemp = true,
            AlertCpuUsage = true,
            AlertRamUsage = true,
            AlertGpuUsage = true,
            AlertGpuVram = true,
            AlertBatteryLow = true,
            AlertCpuFreq = true,
            AlertCpuPower = true,
            AlertGpuFreq = true,
            AlertGpuPower = true,
            CpuTempMaxC = 85f,
            GpuTempMaxC = 80f,
            CpuUsageMaxPct = 90f,
            RamUsageMaxPct = 95f,
            GpuUsageMaxPct = 90f,
            GpuVramMaxMb = 8000f,
            BatteryLowPct = 15f,
            CpuFreqMinMhz = 1000f,
            CpuPowerMaxW = 100f,
            GpuFreqMinMhz = 500f,
            GpuPowerMaxW = 200f,
            ShowToastNotif = false,
            FlashOverlay = false,
            CooldownSecs = 30,
            FireOncePerSession = true
        };

        alerts.Enabled.Should().BeTrue();
        alerts.AlertCpuTemp.Should().BeTrue();
        alerts.AlertGpuTemp.Should().BeTrue();
        alerts.AlertCpuUsage.Should().BeTrue();
        alerts.AlertRamUsage.Should().BeTrue();
        alerts.AlertGpuUsage.Should().BeTrue();
        alerts.AlertGpuVram.Should().BeTrue();
        alerts.AlertBatteryLow.Should().BeTrue();
        alerts.AlertCpuFreq.Should().BeTrue();
        alerts.AlertCpuPower.Should().BeTrue();
        alerts.AlertGpuFreq.Should().BeTrue();
        alerts.AlertGpuPower.Should().BeTrue();
        alerts.CpuTempMaxC.Should().Be(85f);
        alerts.GpuTempMaxC.Should().Be(80f);
        alerts.CpuUsageMaxPct.Should().Be(90f);
        alerts.RamUsageMaxPct.Should().Be(95f);
        alerts.GpuUsageMaxPct.Should().Be(90f);
        alerts.GpuVramMaxMb.Should().Be(8000f);
        alerts.BatteryLowPct.Should().Be(15f);
        alerts.CpuFreqMinMhz.Should().Be(1000f);
        alerts.CpuPowerMaxW.Should().Be(100f);
        alerts.GpuFreqMinMhz.Should().Be(500f);
        alerts.GpuPowerMaxW.Should().Be(200f);
        alerts.ShowToastNotif.Should().BeFalse();
        alerts.FlashOverlay.Should().BeFalse();
        alerts.CooldownSecs.Should().Be(30);
        alerts.FireOncePerSession.Should().BeTrue();
        appSettings.Alerts = alerts;
        appSettings.Alerts.Should().BeSameAs(alerts);

        var netUsage = new NetUsageConfig { Enabled = false };
        netUsage.Enabled.Should().BeFalse();
        appSettings.NetUsage = netUsage;
        appSettings.NetUsage.Should().BeSameAs(netUsage);
    }

    [Fact]
    public void InitPaths_LogsDirBlockedByFile_CatchesExceptionAndFallsBack()
    {
        string subDir = Path.Combine(_tempDir, "BlockedLogsDir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "logs"), "blocked by file");

        AppSettings.LastDirectoryError = null;
        AppSettings.InitPaths(subDir);

        AppSettings.LastDirectoryError.Should().NotBeNull();
        AppSettings.LogsDir.Should().Be(subDir);
    }
}
