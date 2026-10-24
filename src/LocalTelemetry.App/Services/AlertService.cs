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
            EvaluateThreshold(new(Metrics.CpuTemp, "CPU Temperature", snap.CpuTempPackageC, _cfg.Alerts.CpuTempMaxC,
                unit: useFahrenheit ? "°F" : "°C",
                displayValue: Metrics.TempString(snap.CpuTempPackageC, useFahrenheit),
                displayThreshold: Metrics.TempString(_cfg.Alerts.CpuTempMaxC, useFahrenheit))
            {
                Hysteresis = 6f,
                EscalationDelta = 5f
            });

        if (_cfg.Alerts.AlertCpuUsage)
            EvaluateThreshold(new(Metrics.CpuPct, "CPU Usage", snap.CpuUsagePct, _cfg.Alerts.CpuUsageMaxPct,
                unit: "%",
                displayValue: $"{snap.CpuUsagePct:F0}%",
                displayThreshold: $"{_cfg.Alerts.CpuUsageMaxPct:F0}%")
            {
                Hysteresis = 8f,
                EscalationDelta = 5f
            });

        if (_cfg.Alerts.AlertCpuFreq)
            EvaluateThreshold(new(Metrics.CpuFreq, "CPU Frequency", snap.CpuFreqGhz * 1000f, _cfg.Alerts.CpuFreqMinMhz,
                unit: "MHz",
                displayValue: $"{snap.CpuFreqGhz:F2} GHz",
                displayThreshold: $"{_cfg.Alerts.CpuFreqMinMhz / 1000f:F2} GHz")
            {
                LowAlert = true,
                Hysteresis = 200f
            });

        if (_cfg.Alerts.AlertCpuPower)
            EvaluateThreshold(new(Metrics.CpuPower, "CPU Package Power", snap.CpuPackagePowerW, _cfg.Alerts.CpuPowerMaxW,
                unit: "W",
                displayValue: $"{snap.CpuPackagePowerW:F0} W",
                displayThreshold: $"{_cfg.Alerts.CpuPowerMaxW:F0} W")
            {
                Hysteresis = 10f,
                EscalationDelta = 15f
            });
    }

    private void EvaluateGpuAlerts(TelemetrySnapshot snap, bool useFahrenheit)
    {
        if (_cfg.Alerts.AlertGpuTemp)
            EvaluateThreshold(new(Metrics.GpuTemp, "GPU Temperature", snap.GpuTempC, _cfg.Alerts.GpuTempMaxC,
                unit: useFahrenheit ? "°F" : "°C",
                displayValue: Metrics.TempString(snap.GpuTempC, useFahrenheit),
                displayThreshold: Metrics.TempString(_cfg.Alerts.GpuTempMaxC, useFahrenheit))
            {
                Hysteresis = 6f,
                EscalationDelta = 5f
            });

        if (_cfg.Alerts.AlertGpuUsage)
            EvaluateThreshold(new(Metrics.GpuPct, "GPU Usage", snap.GpuUsagePct, _cfg.Alerts.GpuUsageMaxPct,
                unit: "%",
                displayValue: $"{snap.GpuUsagePct:F0}%",
                displayThreshold: $"{_cfg.Alerts.GpuUsageMaxPct:F0}%")
            {
                Hysteresis = 8f,
                EscalationDelta = 5f
            });

        if (_cfg.Alerts.AlertGpuVram)
            EvaluateThreshold(new(Metrics.GpuVram, "GPU VRAM", snap.GpuVramUsedMb, _cfg.Alerts.GpuVramMaxMb,
                unit: "MB",
                displayValue: $"{snap.GpuVramUsedMb:F0} MB",
                displayThreshold: $"{_cfg.Alerts.GpuVramMaxMb:F0} MB")
            {
                Hysteresis = 512f,
                EscalationDelta = 512f
            });

        if (_cfg.Alerts.AlertGpuFreq)
            EvaluateThreshold(new(Metrics.GpuFreq, "GPU Frequency", snap.GpuFreqMHz, _cfg.Alerts.GpuFreqMinMhz,
                unit: "MHz",
                displayValue: $"{snap.GpuFreqMHz:F0} MHz",
                displayThreshold: $"{_cfg.Alerts.GpuFreqMinMhz:F0} MHz")
            {
                LowAlert = true,
                Hysteresis = 150f
            });

        if (_cfg.Alerts.AlertGpuPower)
            EvaluateThreshold(new(Metrics.GpuPower, "GPU Power", snap.GpuPowerW, _cfg.Alerts.GpuPowerMaxW,
                unit: "W",
                displayValue: $"{snap.GpuPowerW:F0} W",
                displayThreshold: $"{_cfg.Alerts.GpuPowerMaxW:F0} W")
            {
                Hysteresis = 15f,
                EscalationDelta = 20f
            });
    }

    private void EvaluateRamAndMiscAlerts(TelemetrySnapshot snap)
    {
        if (_cfg.Alerts.AlertRamUsage)
            EvaluateThreshold(new(Metrics.RamPct, "RAM Usage", snap.RamUsagePct, _cfg.Alerts.RamUsageMaxPct,
                unit: "%",
                displayValue: $"{snap.RamUsagePct:F0}%",
                displayThreshold: $"{_cfg.Alerts.RamUsageMaxPct:F0}%")
            {
                Hysteresis = 5f,
                EscalationDelta = 4f
            });

        if (_cfg.Alerts.AlertBatteryLow && snap.BatteryPct > 0 && !snap.BatteryIsCharging)
        {
            EvaluateThreshold(new(Metrics.BatteryPct, "Battery Level", snap.BatteryPct, _cfg.Alerts.BatteryLowPct,
                unit: "%",
                displayValue: $"{snap.BatteryPct:F0}%",
                displayThreshold: $"≤ {_cfg.Alerts.BatteryLowPct:F0}%")
            {
                LowAlert = true,
                Hysteresis = 5f
            });
        }
    }

    private void EvaluateNetAlerts()
    {
        if (_netLogger is null) return;

        if (_cfg.Alerts.AlertNetDaily)
        {
            float todayGb = (float)_netLogger.GetTodayTotalGb();
            EvaluateThreshold(new("net_daily", "Daily Internet Usage", todayGb, _cfg.Alerts.NetDailyMaxGb,
                unit: "GB",
                displayValue: $"{todayGb:F1} GB",
                displayThreshold: $"{_cfg.Alerts.NetDailyMaxGb:F1} GB"));
        }

        if (_cfg.Alerts.AlertNetMonthly)
        {
            float monthGb = (float)_netLogger.GetMonthTotalGb();
            EvaluateThreshold(new("net_monthly", "Monthly Internet Usage", monthGb, _cfg.Alerts.NetMonthlyMaxGb,
                unit: "GB",
                displayValue: $"{monthGb:F1} GB",
                displayThreshold: $"{_cfg.Alerts.NetMonthlyMaxGb:F1} GB"));
        }
    }

    private readonly record struct ThresholdEvaluationContext
    {
        public string MetricId { get; init; }
        public string MetricName { get; init; }
        public float Value { get; init; }
        public float Threshold { get; init; }
        public string Unit { get; init; }
        public string DisplayValue { get; init; }
        public string DisplayThreshold { get; init; }
        public bool LowAlert { get; init; } = false;
        public float Hysteresis { get; init; } = 0f;
        public float EscalationDelta { get; init; } = 0f;

        public ThresholdEvaluationContext(
            string metricId,
            string metricName,
            float value,
            float threshold,
            string unit,
            string displayValue,
            string displayThreshold)
        {
            MetricId = metricId;
            MetricName = metricName;
            Value = value;
            Threshold = threshold;
            Unit = unit;
            DisplayValue = displayValue;
            DisplayThreshold = displayThreshold;
            LowAlert = false;
            Hysteresis = 0f;
            EscalationDelta = 0f;
        }
    }

    // Firing Logic with Hysteresis & Escalation
    private void EvaluateThreshold(in ThresholdEvaluationContext ctx)
    {
        if (ctx.Value <= 0f)
        {
            Log.Debug($"Alert check skipped for {ctx.MetricId}, sensor unavailable (value={ctx.Value:F1})");
            return;
        }

        bool isBreached = ctx.LowAlert ? ctx.Value <= ctx.Threshold : ctx.Value >= ctx.Threshold;
        bool wasActive = _activeAlerts.Contains(ctx.MetricId);

        if (isBreached)
        {
            HandleBreach(ctx, wasActive);
        }
        else if (wasActive)
        {
            HandleRecovery(ctx);
        }
    }

    private void HandleBreach(in ThresholdEvaluationContext ctx, bool wasActive)
    {
        if (!wasActive)
        {
            if (!CanFireAlert(ctx.MetricId)) return;

            _activeAlerts.Add(ctx.MetricId);
            _lastAlertedValue[ctx.MetricId] = ctx.Value;

            string title = GetAlertTitle(ctx.MetricId, ctx.MetricName, ctx.LowAlert);
            string direction = ctx.LowAlert ? "low" : "high";
            string body = $"{ctx.MetricName} {direction} at {ctx.DisplayValue} (limit: {ctx.DisplayThreshold})";

            DispatchAlert(ctx.MetricId, title, body, $"{ctx.MetricName} reached {ctx.DisplayValue} (limit: {ctx.DisplayThreshold})");
            return;
        }

        HandleEscalation(ctx);
    }

    private void HandleEscalation(in ThresholdEvaluationContext ctx)
    {
        if (ctx.EscalationDelta <= 0f || !_lastAlertedValue.TryGetValue(ctx.MetricId, out float lastVal))
            return;

        bool isEscalated = ctx.LowAlert
            ? (lastVal - ctx.Value) >= ctx.EscalationDelta
            : (ctx.Value - lastVal) >= ctx.EscalationDelta;

        if (isEscalated && CanFireAlert(ctx.MetricId))
        {
            _lastAlertedValue[ctx.MetricId] = ctx.Value;
            string title = $"{ctx.MetricName} Critical";
            string body = $"Surged to {ctx.DisplayValue} (was {lastVal:F0} {ctx.Unit}, limit: {ctx.DisplayThreshold})";
            DispatchAlert(ctx.MetricId, title, body, $"{ctx.MetricName} critical surge at {ctx.DisplayValue} (limit: {ctx.DisplayThreshold})");
        }
    }

    private void HandleRecovery(in ThresholdEvaluationContext ctx)
    {
        bool isRecovered = ctx.LowAlert
            ? ctx.Value >= (ctx.Threshold + ctx.Hysteresis)
            : ctx.Value <= (ctx.Threshold - ctx.Hysteresis);

        if (isRecovered)
        {
            _activeAlerts.Remove(ctx.MetricId);
            _lastAlertedValue.Remove(ctx.MetricId);
            Log.Info($"Alert normalized for {ctx.MetricName}, dropped to {ctx.DisplayValue} (recovered below {ctx.DisplayThreshold})");
        }
    }

    private static string GetAlertTitle(string metricId, string metricName, bool lowAlert)
    {
        if (lowAlert)
        {
            return metricId switch
            {
                Metrics.CpuFreq => "CPU Throttling",
                Metrics.BatteryPct => "Low Battery",
                _ => $"{metricName} Low"
            };
        }

        return metricId.StartsWith("net_", StringComparison.Ordinal)
            ? "Network Limit Reached"
            : $"{metricName} High";
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
