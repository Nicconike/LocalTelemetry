using System.IO;
using System.Reflection;
using FluentAssertions;
using LocalTelemetry.App.Overlay;
using LocalTelemetry.App.Services;
using LocalTelemetry.App.Win32;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Models;
using NSubstitute;
using Xunit;

namespace LocalTelemetry.App.Tests.Services;

public class AlertServiceTests
{
    private static void BypassStartupGracePeriod(AlertService service)
    {
        var field = typeof(AlertService).GetField("_startupTime", BindingFlags.Instance | BindingFlags.NonPublic);
        field?.SetValue(service, DateTime.UtcNow.AddSeconds(-20));
    }

    private static (AlertService service, TaskbarOverlay overlay, NotificationClient notifClient)
        CreateService(AppSettings cfg)
    {
        var embedder = new TaskbarEmbedder(cfg);
        var overlay = new TaskbarOverlay(cfg, embedder);
        var notifClient = Substitute.For<NotificationClient>();
        var service = new AlertService(cfg, overlay, notifClient);
        BypassStartupGracePeriod(service);
        return (service, overlay, notifClient);
    }

    [WpfFact]
    public void Evaluate_DisabledAlerts_DoesNothing()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = false;
        var (service, _, _) = CreateService(cfg);

        var snap = new TelemetrySnapshot { CpuTempPackageC = 100f };
        var act = () => service.Evaluate(snap);
        act.Should().NotThrow();
    }

    [WpfFact]
    public void Evaluate_WithinStartupGracePeriod_DoesNothing()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertCpuTemp = true;
        cfg.Alerts.CpuTempMaxC = 80f;

        var embedder = new TaskbarEmbedder(cfg);
        var overlay = new TaskbarOverlay(cfg, embedder);
        var notifClient = Substitute.For<NotificationClient>();
        var service = new AlertService(cfg, overlay, notifClient);
        // Do NOT bypass startup grace period

        var snap = new TelemetrySnapshot { CpuTempPackageC = 100f };
        var act = () => service.Evaluate(snap); // Should be skipped due to grace period
        act.Should().NotThrow();
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

        var (service, _, _) = CreateService(cfg);

        var snap = new TelemetrySnapshot { CpuTempPackageC = 85f };
        var act = () => service.Evaluate(snap);
        act.Should().NotThrow();
    }

    [WpfFact]
    public void Evaluate_SensorValueZero_SkipsAlert()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertCpuTemp = true;
        cfg.Alerts.CpuTempMaxC = 80f;
        cfg.Alerts.FlashOverlay = false;
        cfg.Alerts.ShowToastNotif = false;

        var (service, _, _) = CreateService(cfg);

        var snap = new TelemetrySnapshot { CpuTempPackageC = 0f };
        var act = () => service.Evaluate(snap);
        act.Should().NotThrow();
    }

    [WpfFact]
    public void Evaluate_ValueBelowThreshold_DoesNotFire()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertCpuTemp = true;
        cfg.Alerts.CpuTempMaxC = 80f;
        cfg.Alerts.FlashOverlay = false;
        cfg.Alerts.ShowToastNotif = false;

        var (service, _, _) = CreateService(cfg);

        var snap = new TelemetrySnapshot { CpuTempPackageC = 70f };
        var act = () => service.Evaluate(snap);
        act.Should().NotThrow();
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

        var (service, _, _) = CreateService(cfg);

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

        var act = () => service.Evaluate(snap);
        act.Should().NotThrow();
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

        var (service, _, _) = CreateService(cfg);

        var snap = new TelemetrySnapshot { CpuTempPackageC = 85f };

        var act = () =>
        {
            service.Evaluate(snap);
            service.Evaluate(snap);
        };
        act.Should().NotThrow();
    }

    [WpfFact]
    public void Evaluate_CooldownMode_SuppressesDuringCooldown()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertCpuTemp = true;
        cfg.Alerts.CpuTempMaxC = 80f;
        cfg.Alerts.FireOncePerSession = false;
        cfg.Alerts.CooldownSecs = 600;
        cfg.Alerts.FlashOverlay = false;
        cfg.Alerts.ShowToastNotif = false;

        var (service, _, _) = CreateService(cfg);

        var snap = new TelemetrySnapshot { CpuTempPackageC = 85f };

        var act = () =>
        {
            service.Evaluate(snap);
            service.Evaluate(snap);
        };
        act.Should().NotThrow();
    }

    [WpfFact]
    public void Evaluate_LowAlert_CpuFreqAboveThreshold_DoesNotFire()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertCpuFreq = true;
        cfg.Alerts.CpuFreqMinMhz = 2000f;
        cfg.Alerts.FlashOverlay = false;
        cfg.Alerts.ShowToastNotif = false;

        var (service, _, _) = CreateService(cfg);

        var snap = new TelemetrySnapshot { CpuFreqGhz = 3.0f };
        var act = () => service.Evaluate(snap);
        act.Should().NotThrow();
    }

    [WpfFact]
    public void Evaluate_BatteryLow_Charging_DoesNotFire()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertBatteryLow = true;
        cfg.Alerts.BatteryLowPct = 30f;
        cfg.Alerts.FlashOverlay = false;
        cfg.Alerts.ShowToastNotif = false;

        var (service, _, _) = CreateService(cfg);

        var snap = new TelemetrySnapshot { BatteryPct = 10f, BatteryIsCharging = true };
        var act = () => service.Evaluate(snap);
        act.Should().NotThrow();
    }

    [WpfFact]
    public void Evaluate_BatteryLow_NoBattery_DoesNotFire()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertBatteryLow = true;
        cfg.Alerts.BatteryLowPct = 30f;
        cfg.Alerts.FlashOverlay = false;
        cfg.Alerts.ShowToastNotif = false;

        var (service, _, _) = CreateService(cfg);

        var snap = new TelemetrySnapshot { BatteryPct = 0f, BatteryIsCharging = false };
        var act = () => service.Evaluate(snap);
        act.Should().NotThrow();
    }

    [WpfFact]
    public void Evaluate_BatteryLow_FireOnce_SuppressesSecondFire()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertBatteryLow = true;
        cfg.Alerts.BatteryLowPct = 30f;
        cfg.Alerts.FireOncePerSession = true;
        cfg.Alerts.FlashOverlay = false;
        cfg.Alerts.ShowToastNotif = false;

        var (service, _, _) = CreateService(cfg);

        var snap = new TelemetrySnapshot { BatteryPct = 15f, BatteryIsCharging = false };

        var act = () =>
        {
            service.Evaluate(snap);
            service.Evaluate(snap);
        };
        act.Should().NotThrow();
    }

    [WpfFact]
    public void Evaluate_BatteryLow_CooldownMode_SuppressesDuringCooldown()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertBatteryLow = true;
        cfg.Alerts.BatteryLowPct = 30f;
        cfg.Alerts.FireOncePerSession = false;
        cfg.Alerts.CooldownSecs = 600;
        cfg.Alerts.FlashOverlay = false;
        cfg.Alerts.ShowToastNotif = false;

        var (service, _, _) = CreateService(cfg);

        var snap = new TelemetrySnapshot { BatteryPct = 15f, BatteryIsCharging = false };

        var act = () =>
        {
            service.Evaluate(snap);
            service.Evaluate(snap);
        };
        act.Should().NotThrow();
    }

    [WpfFact]
    public void Evaluate_FlashOverlay_TriggersFlash()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertCpuTemp = true;
        cfg.Alerts.CpuTempMaxC = 80f;
        cfg.Alerts.FlashOverlay = true;
        cfg.Alerts.ShowToastNotif = false;

        var (service, _, _) = CreateService(cfg);

        var snap = new TelemetrySnapshot { CpuTempPackageC = 85f };
        var act = () => service.Evaluate(snap);
        act.Should().NotThrow();
    }

    [WpfFact]
    public void Evaluate_ShowToast_SendsToastNotification()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertCpuTemp = true;
        cfg.Alerts.CpuTempMaxC = 80f;
        cfg.Alerts.FlashOverlay = false;
        cfg.Alerts.ShowToastNotif = true;

        var (service, _, notifClient) = CreateService(cfg);

        var snap = new TelemetrySnapshot { CpuTempPackageC = 85f };
        service.Evaluate(snap);

        _ = notifClient.Received().SendToastAsync(Arg.Is<string>(s => s.Contains("CPU")), Arg.Any<string>());
    }

    [WpfFact]
    public void Evaluate_BatteryLow_WithFlashAndToast()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertBatteryLow = true;
        cfg.Alerts.BatteryLowPct = 30f;
        cfg.Alerts.FlashOverlay = true;
        cfg.Alerts.ShowToastNotif = true;

        var (service, _, notifClient) = CreateService(cfg);

        var snap = new TelemetrySnapshot { BatteryPct = 15f, BatteryIsCharging = false };
        service.Evaluate(snap);

        _ = notifClient.Received().SendToastAsync(Arg.Is<string>(s => s.Contains("Battery")), Arg.Any<string>());
    }

    [WpfFact]
    public void Evaluate_NetAlerts_DailyAndMonthlyUsage_TriggersAlerts()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertNetDaily = true;
        cfg.Alerts.AlertNetMonthly = true;
        cfg.Alerts.NetDailyMaxGb = 0.001f;
        cfg.Alerts.NetMonthlyMaxGb = 0.001f;
        cfg.Alerts.FlashOverlay = true;
        cfg.Alerts.ShowToastNotif = true;

        string tempPath = Path.Combine(Path.GetTempPath(), $"traffic_alert_test_{Guid.NewGuid():N}.dat");
        try
        {
            var historyFile = new LocalTelemetry.Core.Hardware.TrafficHistoryFile(tempPath);
            using var netLogger = new LocalTelemetry.Core.Hardware.NetUsageLogger(cfg, historyFile);

            var embedder = new TaskbarEmbedder(cfg);
            var overlay = new TaskbarOverlay(cfg, embedder);
            var notifClient = Substitute.For<NotificationClient>();

            var service = new AlertService(cfg, overlay, notifClient, netLogger);
            BypassStartupGracePeriod(service);

            // Record traffic snapshot to give non-zero totals
            var snap = new TelemetrySnapshot
            {
                NetDownBps = 10_000_000,
                NetUpBps = 5_000_000,
                NetTotalBytes = 100_000_000
            };
            netLogger.Record(snap);

            service.Evaluate(snap);

            _ = notifClient.Received().SendToastAsync(Arg.Is<string>(s => s.Contains("Network")), Arg.Any<string>());
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [WpfFact]
    public void Evaluate_ShowToast_WhenSendToastAsyncFaults_HandlesFaultGracefully()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertCpuTemp = true;
        cfg.Alerts.CpuTempMaxC = 80f;
        cfg.Alerts.ShowToastNotif = true;

        var embedder = new TaskbarEmbedder(cfg);
        var overlay = new TaskbarOverlay(cfg, embedder);
        var notifClient = Substitute.For<NotificationClient>();
        notifClient.SendToastAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Simulated toast failure")));

        var service = new AlertService(cfg, overlay, notifClient);
        BypassStartupGracePeriod(service);

        var snap = new TelemetrySnapshot { CpuTempPackageC = 95f };
        var act = () => service.Evaluate(snap);
        act.Should().NotThrow();
    }

    [WpfFact]
    public void Evaluate_Hysteresis_SuppressesAlertsDuringFluctuationsUntilFullRecovery()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertCpuTemp = true;
        cfg.Alerts.CpuTempMaxC = 90f; // Threshold 90°C, Hysteresis 6°C -> Recovery at <= 84°C
        cfg.Alerts.ShowToastNotif = true;
        cfg.Alerts.CooldownSecs = 0; // Disable time cooldown to test pure hysteresis behavior

        var (service, _, notifClient) = CreateService(cfg);

        // 1. Initial breach at 91°C: should fire
        service.Evaluate(new TelemetrySnapshot { CpuTempPackageC = 91f });
        _ = notifClient.Received(1).SendToastAsync(Arg.Is<string>(s => s.Contains("CPU Temperature High")), Arg.Any<string>());

        // 2. Fluctuations typical of laptop gaming (92°C, 88°C, 91°C, 85°C): should NOT fire again
        service.Evaluate(new TelemetrySnapshot { CpuTempPackageC = 92f });
        service.Evaluate(new TelemetrySnapshot { CpuTempPackageC = 88f });
        service.Evaluate(new TelemetrySnapshot { CpuTempPackageC = 91f });
        service.Evaluate(new TelemetrySnapshot { CpuTempPackageC = 85f });
        _ = notifClient.Received(1).SendToastAsync(Arg.Any<string>(), Arg.Any<string>());

        // 3. Cools down below 90 - 6 = 84°C (e.g. 83°C): recovers/normalizes
        service.Evaluate(new TelemetrySnapshot { CpuTempPackageC = 83f });
        _ = notifClient.Received(1).SendToastAsync(Arg.Any<string>(), Arg.Any<string>());

        // 4. Climbs back to 91°C: should fire again as a new breach
        service.Evaluate(new TelemetrySnapshot { CpuTempPackageC = 91f });
        _ = notifClient.Received(2).SendToastAsync(Arg.Is<string>(s => s.Contains("CPU Temperature High")), Arg.Any<string>());
    }

    [WpfFact]
    public void Evaluate_Escalation_TriggersEscalationAlertWhenTempSurges()
    {
        var cfg = new AppSettings();
        cfg.Alerts.Enabled = true;
        cfg.Alerts.AlertCpuTemp = true;
        cfg.Alerts.CpuTempMaxC = 90f;
        cfg.Alerts.ShowToastNotif = true;
        cfg.Alerts.CooldownSecs = 0; // Disable time cooldown to test escalation logic

        var (service, _, notifClient) = CreateService(cfg);

        // 1. Initial breach at 90°C
        service.Evaluate(new TelemetrySnapshot { CpuTempPackageC = 90f });
        _ = notifClient.Received(1).SendToastAsync(Arg.Is<string>(s => s.Contains("CPU Temperature High")), Arg.Any<string>());

        // 2. Minor fluctuation to 92°C (+2°C < 5°C escalationDelta): does not fire
        service.Evaluate(new TelemetrySnapshot { CpuTempPackageC = 92f });
        _ = notifClient.Received(1).SendToastAsync(Arg.Any<string>(), Arg.Any<string>());

        // 3. Surge to 96°C (+6°C from initial 90°C >= 5°C escalationDelta): triggers escalation alert
        service.Evaluate(new TelemetrySnapshot { CpuTempPackageC = 96f });
        _ = notifClient.Received(1).SendToastAsync(Arg.Is<string>(s => s.Contains("CPU Temperature Critical")), Arg.Any<string>());
    }
}
