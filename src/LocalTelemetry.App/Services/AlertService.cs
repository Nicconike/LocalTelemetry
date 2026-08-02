using System.Runtime.Versioning;
using LocalTelemetry.App.Overlay;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalTelemetry.App.Services;

/// <summary>
/// Evaluates hardware telemetry snapshots against user-configured thresholds and
/// fires overlay flashes and/or Windows toast notifications when thresholds are exceeded.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AlertService(
    AppSettings cfg,
    ILogger<AlertService> log,
    TaskbarOverlay overlay,
    NotificationClient notifClient)
{
    private readonly AppSettings _cfg = cfg;
    private readonly ILogger _log = log;
    private readonly TaskbarOverlay _overlay = overlay;
    private readonly NotificationClient _notifClient = notifClient;
    private readonly Dictionary<string, DateTime> _cooldowns = [];
    private readonly HashSet<string> _firedOnce = [];
    private readonly DateTime _startupTime = DateTime.UtcNow;


    // Evaluation
    /// <summary>
    /// Checks all enabled alert thresholds against the current snapshot and fires
    /// notifications for any metric that exceeds its configured limit.
    /// </summary>
    /// <param name="snap">The latest telemetry snapshot to evaluate.</param>
    public void Evaluate(TelemetrySnapshot snap)
    {
        if (!_cfg.Alerts.Enabled) return;
        // Skip alerts for first 10 seconds to let the notification process start
        if ((DateTime.UtcNow - _startupTime).TotalSeconds < 10) return;

        bool f = _cfg.Monitoring.UseFahrenheit;

        if (_cfg.Alerts.AlertCpuTemp)
            TryFire(Metrics.CpuTemp, snap.CpuTempPackageC,
                    _cfg.Alerts.CpuTempMaxC,
                    $"WARNING: CPU at {Metrics.TempString(snap.CpuTempPackageC, f)}");

        if (_cfg.Alerts.AlertGpuTemp)
            TryFire(Metrics.GpuTemp, snap.GpuTempC,
                    _cfg.Alerts.GpuTempMaxC,
                    $"WARNING: GPU at {Metrics.TempString(snap.GpuTempC, f)}");

        if (_cfg.Alerts.AlertCpuUsage)
            TryFire(Metrics.CpuPct, snap.CpuUsagePct,
                    _cfg.Alerts.CpuUsageMaxPct,
                    $"WARNING: CPU at {snap.CpuUsagePct:F0}%");

        if (_cfg.Alerts.AlertRamUsage)
            TryFire(Metrics.RamPct, snap.RamUsagePct,
                    _cfg.Alerts.RamUsageMaxPct,
                    $"WARNING: RAM at {snap.RamUsagePct:F0}%");

        if (_cfg.Alerts.AlertGpuUsage)
            TryFire(Metrics.GpuPct, snap.GpuUsagePct,
                    _cfg.Alerts.GpuUsageMaxPct,
                    $"WARNING: GPU at {snap.GpuUsagePct:F0}%");

        if (_cfg.Alerts.AlertGpuVram)
            TryFire(Metrics.GpuVram, snap.GpuVramUsedMb,
                    _cfg.Alerts.GpuVramMaxMb,
                    $"WARNING: GPU VRAM at {snap.GpuVramUsedMb:F0} MB");

        if (_cfg.Alerts.AlertCpuFreq)
            TryFire(Metrics.CpuFreq, snap.CpuFreqGhz * 1000f,
                    _cfg.Alerts.CpuFreqMinMhz,
                    $"WARNING: CPU throttled at {snap.CpuFreqGhz:F2} GHz",
                    lowAlert: true);

        if (_cfg.Alerts.AlertCpuPower)
            TryFire(Metrics.CpuPower, snap.CpuPackagePowerW,
                    _cfg.Alerts.CpuPowerMaxW,
                    $"WARNING: CPU at {snap.CpuPackagePowerW:F0} W");

        if (_cfg.Alerts.AlertGpuFreq)
            TryFire(Metrics.GpuFreq, snap.GpuFreqMHz,
                    _cfg.Alerts.GpuFreqMinMhz,
                    $"WARNING: GPU throttled at {snap.GpuFreqMHz:F0} MHz",
                    lowAlert: true);

        if (_cfg.Alerts.AlertGpuPower)
            TryFire(Metrics.GpuPower, snap.GpuPowerW,
                    _cfg.Alerts.GpuPowerMaxW,
                    $"WARNING: GPU at {snap.GpuPowerW:F0} W");

        if (_cfg.Alerts.AlertBatteryLow
            && snap.BatteryPct > 0 && !snap.BatteryIsCharging
            && snap.BatteryPct <= _cfg.Alerts.BatteryLowPct)
        {
            FireBatteryLow(snap);
        }
    }

    // Firing Logic
    private void FireBatteryLow(TelemetrySnapshot snap)
    {
        DateTime now = DateTime.UtcNow;

        if (_cfg.Alerts.FireOncePerSession)
        {
            if (_firedOnce.Contains(Metrics.BatteryPct)) return;
            _firedOnce.Add(Metrics.BatteryPct);
        }
        else
        {
            if (_cooldowns.TryGetValue(Metrics.BatteryPct, out DateTime last)
                && (now - last).TotalSeconds < _cfg.Alerts.CooldownSecs)
                return;
            _cooldowns[Metrics.BatteryPct] = now;
        }

        _log.LogError("Alert: Battery low: {Pct}%", snap.BatteryPct);
        if (_cfg.Alerts.FlashOverlay)
            _overlay.Flash(TimeSpan.FromMilliseconds(800));
        if (_cfg.Alerts.ShowToastNotif)
            ShowToast("LocalTelemetry", $"WARNING: Battery at {snap.BatteryPct:F0}%");
    }

    private void TryFire(string metricId, float value, float threshold, string message, bool lowAlert = false)
    {
        if (value <= 0f)
        {
            _log.LogDebug("Alert skipped: {MetricId} sensor unavailable (value={Value})", metricId, value);
            return;
        }

        if (lowAlert)
        {
            if (value > threshold) return;
        }
        else
        {
            if (value < threshold) return;
        }

        DateTime now = DateTime.UtcNow;

        if (_cfg.Alerts.FireOncePerSession)
        {
            if (_firedOnce.Contains(metricId)) return;
            _firedOnce.Add(metricId);
        }
        else
        {
            if (_cooldowns.TryGetValue(metricId, out DateTime last)
                && (now - last).TotalSeconds < _cfg.Alerts.CooldownSecs)
                return;
            _cooldowns[metricId] = now;
        }

        _log.LogError("Alert: {Message}", message);

        if (_cfg.Alerts.FlashOverlay)
            _overlay.Flash(TimeSpan.FromMilliseconds(800));

        if (_cfg.Alerts.ShowToastNotif)
            ShowToast("LocalTelemetry", message);
    }

    // Toast Helpers
    private void ShowToast(string title, string text)
    {
        _notifClient.SendToastAsync(title, text).ContinueWith(t =>
        {
            if (t.Exception is not null)
                _log.LogError(t.Exception, "Toast send failed");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}
