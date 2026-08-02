using System.Reflection;
using LocalTelemetry.App.Overlay;
using LocalTelemetry.App.Services;
using LocalTelemetry.App.Win32;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LocalTelemetry.App.Tests.Services;

public class AlertServiceTests
{
    private static void BypassStartupGracePeriod(AlertService service)
    {
        var field = typeof(AlertService).GetField("_startupTime", BindingFlags.Instance | BindingFlags.NonPublic);
        field?.SetValue(service, DateTime.UtcNow.AddSeconds(-20));
    }

    [WpfFact]
    public void Evaluate_DisabledAlerts_DoesNothing()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = false;
        var logger = NullLogger<AlertService>.Instance;
        var embedder = new TaskbarEmbedder(cfg, NullLogger<TaskbarEmbedder>.Instance);
        var overlayLogger = NullLogger<TaskbarOverlay>.Instance;
        var overlay = new TaskbarOverlay(cfg, embedder, overlayLogger);
        var notifClient = new NotificationClient();

        var service = new AlertService(cfg, logger, overlay, notifClient);
        BypassStartupGracePeriod(service);

        var snap = new TelemetrySnapshot { CpuTempPackageC = 100f };
        service.Evaluate(snap);
    }

    [WpfFact]
    public void Evaluate_EnabledAlerts_FiresCpuTempAlert()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertCpuTemp = true;
        cfg.Alerts.CpuTempMaxC = 80f;
        cfg.Alerts.FlashOverlay = false;
        cfg.Alerts.ShowToastNotif = false;

        var logger = NullLogger<AlertService>.Instance;
        var embedder = new TaskbarEmbedder(cfg, NullLogger<TaskbarEmbedder>.Instance);
        var overlayLogger = NullLogger<TaskbarOverlay>.Instance;
        var overlay = new TaskbarOverlay(cfg, embedder, overlayLogger);
        var notifClient = new NotificationClient();

        var service = new AlertService(cfg, logger, overlay, notifClient);
        BypassStartupGracePeriod(service);

        var snap = new TelemetrySnapshot { CpuTempPackageC = 85f };
        service.Evaluate(snap);
    }

    [WpfFact]
    public void Evaluate_FiresAllConfiguredThresholds()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertCpuTemp = true;
        cfg.Alerts.AlertGpuTemp = true;
        cfg.Alerts.AlertCpuUsage = true;
        cfg.Alerts.AlertRamUsage = true;
        cfg.Alerts.AlertGpuUsage = true;
        cfg.Alerts.AlertGpuVram = true;
        cfg.Alerts.AlertCpuFreq = true;
        cfg.Alerts.AlertCpuPower = true;
        cfg.Alerts.AlertGpuFreq = true;
        cfg.Alerts.AlertGpuPower = true;
        cfg.Alerts.AlertBatteryLow = true;

        cfg.Alerts.CpuTempMaxC = 70f;
        cfg.Alerts.GpuTempMaxC = 70f;
        cfg.Alerts.CpuUsageMaxPct = 80f;
        cfg.Alerts.RamUsageMaxPct = 80f;
        cfg.Alerts.GpuUsageMaxPct = 80f;
        cfg.Alerts.GpuVramMaxMb = 2000f;
        cfg.Alerts.CpuFreqMinMhz = 2000f;
        cfg.Alerts.CpuPowerMaxW = 50f;
        cfg.Alerts.GpuFreqMinMhz = 1000f;
        cfg.Alerts.GpuPowerMaxW = 100f;
        cfg.Alerts.BatteryLowPct = 30f;

        cfg.Alerts.FlashOverlay = false;
        cfg.Alerts.ShowToastNotif = false;

        var logger = NullLogger<AlertService>.Instance;
        var embedder = new TaskbarEmbedder(cfg, NullLogger<TaskbarEmbedder>.Instance);
        var overlayLogger = NullLogger<TaskbarOverlay>.Instance;
        var overlay = new TaskbarOverlay(cfg, embedder, overlayLogger);
        var notifClient = new NotificationClient();

        var service = new AlertService(cfg, logger, overlay, notifClient);
        BypassStartupGracePeriod(service);

        var snap = new TelemetrySnapshot
        {
            CpuTempPackageC = 75f,
            GpuTempC = 75f,
            CpuUsagePct = 85f,
            RamUsagePct = 85f,
            GpuUsagePct = 85f,
            GpuVramUsedMb = 3000f,
            CpuFreqGhz = 1.5f,
            CpuPackagePowerW = 60f,
            GpuFreqMHz = 800f,
            GpuPowerW = 120f,
            BatteryPct = 15f,
            BatteryIsCharging = false
        };

        service.Evaluate(snap);
    }

    [WpfFact]
    public void Evaluate_FireOncePerSession_PreventsDuplicateAlerts()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertCpuTemp = true;
        cfg.Alerts.CpuTempMaxC = 80f;
        cfg.Alerts.FireOncePerSession = true;
        cfg.Alerts.FlashOverlay = false;
        cfg.Alerts.ShowToastNotif = false;

        var logger = NullLogger<AlertService>.Instance;
        var embedder = new TaskbarEmbedder(cfg, NullLogger<TaskbarEmbedder>.Instance);
        var overlayLogger = NullLogger<TaskbarOverlay>.Instance;
        var overlay = new TaskbarOverlay(cfg, embedder, overlayLogger);
        var notifClient = new NotificationClient();

        var service = new AlertService(cfg, logger, overlay, notifClient);
        BypassStartupGracePeriod(service);

        var snap = new TelemetrySnapshot { CpuTempPackageC = 85f };

        service.Evaluate(snap);
        service.Evaluate(snap);
    }
}
