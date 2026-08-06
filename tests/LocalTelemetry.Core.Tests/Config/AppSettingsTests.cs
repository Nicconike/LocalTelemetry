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
        AppSettings.SystemLogPath.Should().Be(Path.Combine(_tempDir, "lt_system.log"));
        AppSettings.MetricsLogPath.Should().Contain("lt_");
    }

    [Fact]
    public void InitPaths_NormalMode_SetsPathsUnderAppData()
    {
        File.WriteAllText(Path.Combine(_tempDir, "app.mode"), "");
        AppSettings.InitPaths(_tempDir);

        AppSettings.ConfigPath.Should().Contain("LocalTelemetry");
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
            WindowTheme = "dark",
            Monitoring = new MonitoringConfig { PollIntervalMs = 500 }
        };
        custom.Save();

        var reloaded = AppSettings.Load();

        reloaded.RunAtStartup.Should().BeTrue();
        reloaded.WindowTheme.Should().Be("dark");
        reloaded.Monitoring.PollIntervalMs.Should().Be(500);
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
    public void Load_MigratesMissingMetricColors()
    {
        var json = """
        {
          "overlay": {
            "metricColors": {
              "cpu_pct": "#123456"
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
    public void ConfigSubModels_DefaultsAndPropertiesWork()
    {
        var monitoring = new MonitoringConfig
        {
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

        monitoring.EnableCpu.Should().BeFalse();
        monitoring.UseFahrenheit.Should().BeTrue();
        monitoring.UseNetBits.Should().BeTrue();
        monitoring.PreferredNic.Should().Be("eth0");
        monitoring.GpuUsageSource.Should().Be("wddm");
        monitoring.LogCpuMode.Should().Be(2);

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
            FollowWindowsTheme = false
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
        overlay.FollowWindowsTheme.Should().BeFalse();

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
        alerts.CpuTempMaxC.Should().Be(85f);
        alerts.ShowToastNotif.Should().BeFalse();
        alerts.CooldownSecs.Should().Be(30);

        var netUsage = new NetUsageConfig { Enabled = false };
        netUsage.Enabled.Should().BeFalse();
    }
}
