using System.Runtime.Versioning;
using LocalTelemetry.App.Overlay;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Diagnostics;
using LocalTelemetry.Core.Models;

using LocalTelemetry.Core.Hardware;

namespace LocalTelemetry.App.Services;

/// <summary>
/// Evaluates hardware telemetry snapshots against user-configured thresholds and
/// fires overlay flashes and/or Windows toast notifications when thresholds are exceeded.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AlertService(
    AppSettings cfg,
    TaskbarOverlay overlay,
    NotificationClient notifClient,
    NetUsageLogger? netLogger = null)
{
    private readonly AppSettings _cfg = cfg;
    private readonly TaskbarOverlay _overlay = overlay;
    private readonly NotificationClient _notifClient = notifClient;
    private readonly NetUsageLogger? _netLogger = netLogger;
    private readonly Dictionary<string, DateTime> _cooldowns = [];
    private readonly HashSet<string> _firedOnce = [];
    private readonly HashSet<string> _activeAlerts = [];
    private readonly Dictionary<string, float> _lastAlertedValue = [];
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

        EvaluateCpuAlerts(snap, f);
        EvaluateGpuAlerts(snap, f);
        EvaluateRamAndMiscAlerts(snap);
        EvaluateNetAlerts();
    }

    private void EvaluateCpuAlerts(TelemetrySnapshot snap, bool useFahrenheit)
    {
        if (_cfg.Alerts.AlertCpuTemp)
            EvaluateThreshold(Metrics.CpuTemp, "CPU Temperature", snap.CpuTempPackageC, _cfg.Alerts.CpuTempMaxC,
                unit: useFahrenheit ? "°F" : "°C",
                displayValue: Metrics.TempString(snap.CpuTempPackageC, useFahrenheit),
                displayThreshold: Metrics.TempString(_cfg.Alerts.CpuTempMaxC, useFahrenheit),
                hysteresis: 6f,
                escalationDelta: 5f);

        if (_cfg.Alerts.AlertCpuUsage)
            EvaluateThreshold(Metrics.CpuPct, "CPU Usage", snap.CpuUsagePct, _cfg.Alerts.CpuUsageMaxPct,
                unit: "%",
                displayValue: $"{snap.CpuUsagePct:F0}%",
                displayThreshold: $"{_cfg.Alerts.CpuUsageMaxPct:F0}%",
                hysteresis: 8f,
                escalationDelta: 5f);

        if (_cfg.Alerts.AlertCpuFreq)
            EvaluateThreshold(Metrics.CpuFreq, "CPU Frequency", snap.CpuFreqGhz * 1000f, _cfg.Alerts.CpuFreqMinMhz,
                unit: "MHz",
                displayValue: $"{snap.CpuFreqGhz:F2} GHz",
                displayThreshold: $"{_cfg.Alerts.CpuFreqMinMhz / 1000f:F2} GHz",
                lowAlert: true,
                hysteresis: 200f);

        if (_cfg.Alerts.AlertCpuPower)
            EvaluateThreshold(Metrics.CpuPower, "CPU Package Power", snap.CpuPackagePowerW, _cfg.Alerts.CpuPowerMaxW,
                unit: "W",
                displayValue: $"{snap.CpuPackagePowerW:F0} W",
                displayThreshold: $"{_cfg.Alerts.CpuPowerMaxW:F0} W",
                hysteresis: 10f,
                escalationDelta: 15f);
    }

    private void EvaluateGpuAlerts(TelemetrySnapshot snap, bool useFahrenheit)
    {
        if (_cfg.Alerts.AlertGpuTemp)
            EvaluateThreshold(Metrics.GpuTemp, "GPU Temperature", snap.GpuTempC, _cfg.Alerts.GpuTempMaxC,
                unit: useFahrenheit ? "°F" : "°C",
                displayValue: Metrics.TempString(snap.GpuTempC, useFahrenheit),
                displayThreshold: Metrics.TempString(_cfg.Alerts.GpuTempMaxC, useFahrenheit),
                hysteresis: 6f,
                escalationDelta: 5f);

        if (_cfg.Alerts.AlertGpuUsage)
            EvaluateThreshold(Metrics.GpuPct, "GPU Usage", snap.GpuUsagePct, _cfg.Alerts.GpuUsageMaxPct,
                unit: "%",
                displayValue: $"{snap.GpuUsagePct:F0}%",
                displayThreshold: $"{_cfg.Alerts.GpuUsageMaxPct:F0}%",
                hysteresis: 8f,
                escalationDelta: 5f);

        if (_cfg.Alerts.AlertGpuVram)
            EvaluateThreshold(Metrics.GpuVram, "GPU VRAM", snap.GpuVramUsedMb, _cfg.Alerts.GpuVramMaxMb,
                unit: "MB",
                displayValue: $"{snap.GpuVramUsedMb:F0} MB",
                displayThreshold: $"{_cfg.Alerts.GpuVramMaxMb:F0} MB",
                hysteresis: 512f,
                escalationDelta: 512f);

        if (_cfg.Alerts.AlertGpuFreq)
            EvaluateThreshold(Metrics.GpuFreq, "GPU Frequency", snap.GpuFreqMHz, _cfg.Alerts.GpuFreqMinMhz,
                unit: "MHz",
                displayValue: $"{snap.GpuFreqMHz:F0} MHz",
                displayThreshold: $"{_cfg.Alerts.GpuFreqMinMhz:F0} MHz",
                lowAlert: true,
                hysteresis: 150f);

        if (_cfg.Alerts.AlertGpuPower)
            EvaluateThreshold(Metrics.GpuPower, "GPU Power", snap.GpuPowerW, _cfg.Alerts.GpuPowerMaxW,
                unit: "W",
                displayValue: $"{snap.GpuPowerW:F0} W",
                displayThreshold: $"{_cfg.Alerts.GpuPowerMaxW:F0} W",
                hysteresis: 15f,
                escalationDelta: 20f);
    }

    private void EvaluateRamAndMiscAlerts(TelemetrySnapshot snap)
    {
        if (_cfg.Alerts.AlertRamUsage)
            EvaluateThreshold(Metrics.RamPct, "RAM Usage", snap.RamUsagePct, _cfg.Alerts.RamUsageMaxPct,
                unit: "%",
                displayValue: $"{snap.RamUsagePct:F0}%",
                displayThreshold: $"{_cfg.Alerts.RamUsageMaxPct:F0}%",
                hysteresis: 5f,
                escalationDelta: 4f);

        if (_cfg.Alerts.AlertBatteryLow && snap.BatteryPct > 0 && !snap.BatteryIsCharging)
        {
            EvaluateThreshold(Metrics.BatteryPct, "Battery Level", snap.BatteryPct, _cfg.Alerts.BatteryLowPct,
                unit: "%",
                displayValue: $"{snap.BatteryPct:F0}%",
                displayThreshold: $"≤ {_cfg.Alerts.BatteryLowPct:F0}%",
                lowAlert: true,
                hysteresis: 5f);
        }
    }

    private void EvaluateNetAlerts()
    {
        if (_netLogger is null) return;

        if (_cfg.Alerts.AlertNetDaily)
        {
            float todayGb = (float)_netLogger.GetTodayTotalGb();
            EvaluateThreshold("net_daily", "Daily Internet Usage", todayGb, _cfg.Alerts.NetDailyMaxGb,
                unit: "GB",
                displayValue: $"{todayGb:F1} GB",
                displayThreshold: $"{_cfg.Alerts.NetDailyMaxGb:F1} GB",
                hysteresis: 0f);
        }

        if (_cfg.Alerts.AlertNetMonthly)
        {
            float monthGb = (float)_netLogger.GetMonthTotalGb();
            EvaluateThreshold("net_monthly", "Monthly Internet Usage", monthGb, _cfg.Alerts.NetMonthlyMaxGb,
                unit: "GB",
                displayValue: $"{monthGb:F1} GB",
                displayThreshold: $"{_cfg.Alerts.NetMonthlyMaxGb:F1} GB",
                hysteresis: 0f);
        }
    }

    // Firing Logic with Hysteresis & Escalation
    private void EvaluateThreshold(
        string metricId,
        string metricName,
        float value,
        float threshold,
        string unit,
        string displayValue,
        string displayThreshold,
        bool lowAlert = false,
        float hysteresis = 0f,
        float escalationDelta = 0f)
    {
        if (value <= 0f)
        {
            Log.Debug($"Alert check skipped for {metricId}, sensor unavailable (value={value:F1})");
            return;
        }

        bool isBreached = lowAlert ? value <= threshold : value >= threshold;
        bool wasActive = _activeAlerts.Contains(metricId);

        if (isBreached)
        {
            // Initial breach into warning state
            if (!wasActive)
            {
                if (!CanFireAlert(metricId)) return;

                _activeAlerts.Add(metricId);
                _lastAlertedValue[metricId] = value;

                string title = lowAlert switch
                {
                    true when metricId == Metrics.CpuFreq => "CPU Throttling",
                    true when metricId == Metrics.BatteryPct => "Low Battery",
                    _ => metricId.StartsWith("net_", StringComparison.Ordinal)
                        ? "Network Limit Reached"
                        : $"{metricName} High"
                };

                string body = lowAlert
                    ? $"{metricName} low at {displayValue} (limit: {displayThreshold})"
                    : $"{metricName} high at {displayValue} (limit: {displayThreshold})";

                DispatchAlert(metricId, title, body, $"{metricName} reached {displayValue} (limit: {displayThreshold})");
                return;
            }

            // Already active: check for significant escalation
            if (escalationDelta > 0f && _lastAlertedValue.TryGetValue(metricId, out float lastVal))
            {
                bool isEscalated = lowAlert ? (lastVal - value) >= escalationDelta : (value - lastVal) >= escalationDelta;
                if (isEscalated && CanFireAlert(metricId))
                {
                    _lastAlertedValue[metricId] = value;
                    string title = $"{metricName} Critical";
                    string body = $"Surged to {displayValue} (was {lastVal:F0} {unit}, limit: {displayThreshold})";
                    DispatchAlert(metricId, title, body, $"{metricName} critical surge at {displayValue} (limit: {displayThreshold})");
                }
            }
        }
        else if (wasActive)
        {
            // Check if metric cooled down past the hysteresis buffer
            bool isRecovered = lowAlert ? value >= (threshold + hysteresis) : value <= (threshold - hysteresis);
            if (isRecovered)
            {
                _activeAlerts.Remove(metricId);
                _lastAlertedValue.Remove(metricId);
                Log.Info($"Alert normalized for {metricName}, dropped to {displayValue} (recovered below {displayThreshold})");
            }
        }
    }

    private void DispatchAlert(string metricId, string title, string body, string logSummary)
    {
        var actions = new List<string>();
        if (_cfg.Alerts.ShowToastNotif) actions.Add("Toast");
        if (_cfg.Alerts.FlashOverlay) actions.Add("Overlay Flash");
        string actionStr = actions.Count > 0 ? string.Join(", ", actions) : "None (Muted)";

        Log.Warn($"Alert [{metricId}] triggered for {logSummary} [Actions: {actionStr}]");

        if (_cfg.Alerts.FlashOverlay)
            _overlay.Flash(TimeSpan.FromMilliseconds(800));

        if (_cfg.Alerts.ShowToastNotif)
            ShowToast(title, body);
    }

    private bool CanFireAlert(string metricId)
    {
        DateTime now = DateTime.UtcNow;

        if (_cfg.Alerts.FireOncePerSession)
            return _firedOnce.Add(metricId);

        if (_cooldowns.TryGetValue(metricId, out DateTime last)
            && (now - last).TotalSeconds < _cfg.Alerts.CooldownSecs)
            return false;

        _cooldowns[metricId] = now;
        return true;
    }

    // Toast Helpers
    private void ShowToast(string title, string text)
    {
        _notifClient.SendToastAsync(title, text).ContinueWith(t =>
        {
            if (t.Exception is not null)
                Log.Error(t.Exception, "Failed to send toast notification");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}
