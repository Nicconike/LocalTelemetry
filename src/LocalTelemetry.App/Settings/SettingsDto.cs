using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Hardware;

namespace LocalTelemetry.App.Settings;

// SettingsDto
/// <summary>Root DTO for all user-configurable settings transferred between C# and the Svelte frontend.</summary>
public sealed class SettingsDto
{
    /// <summary>Whether to launch the application on Windows login.</summary>
    public bool RunAtStartup { get; set; }

    /// <summary>Whether to start the application minimized to the tray.</summary>
    public bool StartMinimized { get; set; }

    /// <summary>Whether closing the settings window minimizes to tray instead of exiting.</summary>
    public bool MinimizeToTray { get; set; } = false;

    /// <summary>Whether to write diagnostic log output to a file.</summary>
    public bool EnableFileLogging { get; set; }

    /// <summary>Monitoring (polling) configuration.</summary>
    public MonitoringDto Monitoring { get; set; } = new();

    /// <summary>Taskbar overlay appearance and behaviour configuration.</summary>
    public OverlayDto Overlay { get; set; } = new();

    /// <summary>Threshold-based alert configuration.</summary>
    public AlertsDto Alerts { get; set; } = new();

    /// <summary>Network usage logging configuration.</summary>
    public NetUsageDto NetUsage { get; set; } = new();

    /// <summary>Active window theme identifier (e.g. "default", "dark").</summary>
    public string WindowTheme { get; set; } = "default";
}

// MonitoringDto

/// <summary>DTO for hardware monitoring polling configuration.</summary>
public sealed class MonitoringDto
{
    /// <summary>Hardware polling interval in milliseconds.</summary>
    public int IntervalMs { get; set; } = 1000;

    /// <summary>Whether to display temperatures in Fahrenheit instead of Celsius.</summary>
    public bool UseFahrenheit { get; set; }

    /// <summary>Whether to display network speeds in bits per second instead of bytes.</summary>
    public bool UseNetBits { get; set; }

    /// <summary>Preferred network interface identifier or "auto" for automatic selection.</summary>
    public string PreferredNic { get; set; } = "auto";

    /// <summary>Whether the system has a battery (laptop/tablet). Read-only - auto-detected.</summary>
    public bool HasBattery { get; set; }

    /// <summary>Whether CPU metrics are polled and displayed.</summary>
    public bool TrackCpu { get; set; } = true;

    /// <summary>Whether GPU metrics are polled and displayed.</summary>
    public bool TrackGpu { get; set; } = true;

    /// <summary>Whether RAM metrics are polled and displayed.</summary>
    public bool TrackRam { get; set; } = true;

    /// <summary>Whether network metrics are polled and displayed.</summary>
    public bool TrackNet { get; set; } = true;

    /// <summary>Whether disk I/O metrics are polled and displayed.</summary>
    public bool TrackDisk { get; set; } = true;

    /// <summary>Whether battery metrics are polled and displayed.</summary>
    public bool TrackBattery { get; set; } = true;

    /// <summary>GPU usage source: "driver" (NVML/ADL) or "wddm" (D3DKMTQueryStatistics).</summary>
    public string GpuUsageSource { get; set; } = "driver";
    /// <summary>CPU logging level (0=off, 1=errors, 2=full).</summary>
    public int LogCpuMode { get; set; }
    /// <summary>GPU logging level (0=off, 1=errors, 2=full).</summary>
    public int LogGpuMode { get; set; }
    /// <summary>RAM logging level (0=off, 1=errors, 2=full).</summary>
    public int LogRamMode { get; set; }
    /// <summary>Network logging level (0=off, 1=errors, 2=full).</summary>
    public int LogNetMode { get; set; }
    /// <summary>Disk logging level (0=off, 1=errors, 2=full).</summary>
    public int LogDiskMode { get; set; }
    /// <summary>Battery logging level (0=off, 1=errors, 2=full).</summary>
    public int LogBatteryMode { get; set; }
}

// OverlayDto
/// <summary>DTO for taskbar overlay appearance, layout and behaviour.</summary>
public sealed class OverlayDto
{
    /// <summary>Whether the overlay is currently visible on the taskbar.</summary>
    public bool Visible { get; set; }

    /// <summary>Action to perform on double-click ("settings" or "taskmanager").</summary>
    public string DoubleClickAction { get; set; } = "settings";

    /// <summary>Overlay position relative to the taskbar ("left" or "right").</summary>
    public string Position { get; set; } = "left";

    /// <summary>Horizontal offset from the chosen position edge.</summary>
    public int OffsetX { get; set; } = 0;

    /// <summary>Overlay opacity percentage (0-100).</summary>
    public int Opacity { get; set; } = 100;

    /// <summary>DPI scale factor percentage (100 = normal).</summary>
    public int Scale { get; set; } = 100;

    /// <summary>Background color in hex (e.g. "#1c1b19").</summary>
    public string BgColor { get; set; } = "#1c1b19";

    /// <summary>Default text color for metric values in hex.</summary>
    public string TextColor { get; set; } = "#cdccca";

    /// <summary>Font size in logical pixels.</summary>
    public float FontSizePx { get; set; } = 18f;

    /// <summary>Whether the overlay font renders in bold weight.</summary>
    public bool FontBold { get; set; }

    /// <summary>color for metric labels in hex.</summary>
    public string LabelColor { get; set; } = "#FFFFFF";

    /// <summary>Per-metric value colors keyed by metric ID.</summary>
    public Dictionary<string, string> MetricColors { get; set; } = new();

    /// <summary>Default vendor-brand colors for each metric, used as a fallback.</summary>
    public Dictionary<string, string> DefaultMetricColors { get; set; } = new();

    /// <summary>Whether to follow the Windows accent/theme color automatically.</summary>
    public bool FollowWindowsTheme { get; set; } = true;

    /// <summary>Metric IDs displayed in the first overlay row (top row).</summary>
    public List<string> Row1 { get; set; } = new();

    /// <summary>Set of metric IDs whose colors have been customised by the user.</summary>
    public HashSet<string> UserCustomizedMetricColors { get; set; } = new();
}

// AlertsDto
/// <summary>Threshold alert configuration for hardware metrics.</summary>
public sealed class AlertsDto
{
    /// <summary>Master toggle for all threshold alerts.</summary>
    public bool Enabled { get; set; }

    // Per-metric toggles
    /// <summary>Enable alert when CPU temperature exceeds <see cref="CpuTempMaxC"/>.</summary>
    public bool AlertCpuTemp { get; set; }

    /// <summary>Enable alert when GPU temperature exceeds <see cref="GpuTempMaxC"/>.</summary>
    public bool AlertGpuTemp { get; set; }

    /// <summary>Enable alert when CPU usage exceeds <see cref="CpuUsageMaxPct"/>.</summary>
    public bool AlertCpuUsage { get; set; }

    /// <summary>Enable alert when RAM usage exceeds <see cref="RamUsageMaxPct"/>.</summary>
    public bool AlertRamUsage { get; set; }

    /// <summary>Enable alert when GPU usage exceeds <see cref="GpuUsageMaxPct"/>.</summary>
    public bool AlertGpuUsage { get; set; }

    /// <summary>Enable alert when GPU VRAM usage exceeds <see cref="GpuVramMaxMb"/>.</summary>
    public bool AlertGpuVram { get; set; }

    /// <summary>Enable alert when battery level falls below <see cref="BatteryLowPct"/>.</summary>
    public bool AlertBatteryLow { get; set; }

    /// <summary>Alert when CPU frequency drops below <see cref="CpuFreqMinMhz"/> (throttling).</summary>
    public bool AlertCpuFreq { get; set; }

    /// <summary>Alert when CPU package power exceeds <see cref="CpuPowerMaxW"/>.</summary>
    public bool AlertCpuPower { get; set; }

    /// <summary>Alert when GPU frequency drops below <see cref="GpuFreqMinMhz"/> (throttling).</summary>
    public bool AlertGpuFreq { get; set; }

    /// <summary>Alert when GPU power exceeds <see cref="GpuPowerMaxW"/>.</summary>
    public bool AlertGpuPower { get; set; }

    // Thresholds
    /// <summary>CPU usage threshold (percent) above which an alert fires.</summary>
    public float CpuUsageMaxPct { get; set; } = 90;

    /// <summary>RAM usage threshold (percent) above which an alert fires.</summary>
    public float RamUsageMaxPct { get; set; } = 90;

    /// <summary>GPU usage threshold (percent) above which an alert fires.</summary>
    public float GpuUsageMaxPct { get; set; } = 95;

    /// <summary>GPU VRAM usage threshold (MB) above which an alert fires.</summary>
    public float GpuVramMaxMb { get; set; } = 6000;

    /// <summary>CPU temperature threshold (°C) above which an alert fires.</summary>
    public float CpuTempMaxC { get; set; } = 90;

    /// <summary>GPU temperature threshold (°C) above which an alert fires.</summary>
    public float GpuTempMaxC { get; set; } = 90;

    /// <summary>Battery level threshold (percent) below which an alert fires.</summary>
    public float BatteryLowPct { get; set; } = 20;

    /// <summary>CPU frequency threshold (MHz) - alert fires when below this value.</summary>
    public float CpuFreqMinMhz { get; set; } = 800;

    /// <summary>CPU package power threshold (watts) above which an alert fires.</summary>
    public float CpuPowerMaxW { get; set; } = 65;

    /// <summary>GPU frequency threshold (MHz) - alert fires when below this value.</summary>
    public float GpuFreqMinMhz { get; set; } = 300;

    /// <summary>GPU power threshold (watts) above which an alert fires.</summary>
    public float GpuPowerMaxW { get; set; } = 150;

    // Actions
    /// <summary>Whether to show a Windows toast notification on alert.</summary>
    public bool ShowToastNotif { get; set; } = true;

    /// <summary>Whether to flash the overlay briefly on alert.</summary>
    public bool FlashOverlay { get; set; } = true;

    /// <summary>Minimum seconds between repeated alerts for the same metric.</summary>
    public int CooldownSecs { get; set; } = 30;

    /// <summary>When true, alerts fire only once per app session (cooldown is ignored).</summary>
    public bool FireOncePerSession { get; set; }
}

// NetUsageDto
/// <summary>DTO for network usage logging configuration.</summary>
public sealed class NetUsageDto
{
    /// <summary>Whether network usage history logging is enabled.</summary>
    public bool Enabled { get; set; } = true;
}

// SettingsDtoMapping
/// <summary>Static mapping methods for converting between <see cref="AppSettings"/> and <see cref="SettingsDto"/>.</summary>
public static class SettingsDtoMapping
{
    /// <summary>Converts an <see cref="AppSettings"/> domain object into a <see cref="SettingsDto"/> for the frontend.</summary>
    /// <param name="src">The source application settings.</param>
    /// <returns>A new DTO populated from the source settings.</returns>
    public static SettingsDto ToDto(AppSettings src)
    {
        return new SettingsDto
        {
            RunAtStartup = src.RunAtStartup,
            StartMinimized = src.StartMinimized,
            MinimizeToTray = src.MinimizeToTrayOnClose,
            EnableFileLogging = src.EnableFileLogging,
            Monitoring = new MonitoringDto
            {
                IntervalMs = src.Monitoring.PollIntervalMs,
                UseFahrenheit = src.Monitoring.UseFahrenheit,
                UseNetBits = src.Monitoring.UseNetBits,
                PreferredNic = string.IsNullOrEmpty(src.Monitoring.PreferredNic)
                    ? "auto" : src.Monitoring.PreferredNic,
                TrackCpu = src.Monitoring.EnableCpu,
                TrackGpu = src.Monitoring.EnableGpu,
                TrackRam = src.Monitoring.EnableRam,
                TrackNet = src.Monitoring.EnableNet,
                TrackDisk = src.Monitoring.EnableDisk,
                TrackBattery = src.Monitoring.EnableBattery,
                HasBattery = SystemInfo.HasBattery(),
                GpuUsageSource = src.Monitoring.GpuUsageSource,
                LogCpuMode = src.Monitoring.LogCpuMode,
                LogGpuMode = src.Monitoring.LogGpuMode,
                LogRamMode = src.Monitoring.LogRamMode,
                LogNetMode = src.Monitoring.LogNetMode,
                LogDiskMode = src.Monitoring.LogDiskMode,
                LogBatteryMode = src.Monitoring.LogBatteryMode,
            },
            Overlay = new OverlayDto
            {
                Visible = src.Overlay.Visible,
                DoubleClickAction = src.Overlay.DoubleClickAction,
                Position = MapPlacementToPosition(src.Overlay.Placement),
                OffsetX = src.Overlay.PlacementOffset,
                Opacity = src.Overlay.Opacity,
                Scale = src.Overlay.ScalePct,
                FontSizePx = src.Overlay.FontSizePx,
                FontBold = src.Overlay.FontBold,
                LabelColor = src.Overlay.LabelColor,
                BgColor = src.Overlay.BgColor,
                TextColor = src.Overlay.ValueColor,
                MetricColors = new Dictionary<string, string>(src.Overlay.MetricColors),
                DefaultMetricColors = new Dictionary<string, string>(src.Overlay.DefaultMetricColors),
                FollowWindowsTheme = src.Overlay.FollowWindowsTheme,
                UserCustomizedMetricColors = [.. src.Overlay.UserCustomizedMetricColors],
                Row1 = [.. src.Overlay.Row1],
            },
            Alerts = new AlertsDto
            {
                Enabled = src.Alerts.Enabled,
                AlertCpuTemp = src.Alerts.AlertCpuTemp,
                AlertGpuTemp = src.Alerts.AlertGpuTemp,
                AlertCpuUsage = src.Alerts.AlertCpuUsage,
                AlertRamUsage = src.Alerts.AlertRamUsage,
                AlertGpuUsage = src.Alerts.AlertGpuUsage,
                AlertGpuVram = src.Alerts.AlertGpuVram,
                AlertBatteryLow = src.Alerts.AlertBatteryLow,
                AlertCpuFreq = src.Alerts.AlertCpuFreq,
                AlertCpuPower = src.Alerts.AlertCpuPower,
                AlertGpuFreq = src.Alerts.AlertGpuFreq,
                AlertGpuPower = src.Alerts.AlertGpuPower,
                CpuUsageMaxPct = src.Alerts.CpuUsageMaxPct,
                RamUsageMaxPct = src.Alerts.RamUsageMaxPct,
                GpuUsageMaxPct = src.Alerts.GpuUsageMaxPct,
                GpuVramMaxMb = src.Alerts.GpuVramMaxMb,
                CpuTempMaxC = src.Alerts.CpuTempMaxC,
                GpuTempMaxC = src.Alerts.GpuTempMaxC,
                BatteryLowPct = src.Alerts.BatteryLowPct,
                CpuFreqMinMhz = src.Alerts.CpuFreqMinMhz,
                CpuPowerMaxW = src.Alerts.CpuPowerMaxW,
                GpuFreqMinMhz = src.Alerts.GpuFreqMinMhz,
                GpuPowerMaxW = src.Alerts.GpuPowerMaxW,
                ShowToastNotif = src.Alerts.ShowToastNotif,
                FlashOverlay = src.Alerts.FlashOverlay,
                CooldownSecs = src.Alerts.CooldownSecs,
                FireOncePerSession = src.Alerts.FireOncePerSession,
            },
            NetUsage = new NetUsageDto
            {
                Enabled = src.NetUsage.Enabled,
            },
            WindowTheme = src.WindowTheme,
        };
    }

    /// <summary>Applies values from a <see cref="SettingsDto"/> onto an <see cref="AppSettings"/> domain object.</summary>
    /// <param name="settings">The target application settings to update.</param>
    /// <param name="dto">The source DTO received from the frontend.</param>
    public static void ApplyTo(AppSettings settings, SettingsDto dto)
    {
        settings.RunAtStartup = dto.RunAtStartup;
        settings.MinimizeToTrayOnClose = dto.MinimizeToTray;
        settings.StartMinimized = dto.StartMinimized;
        settings.EnableFileLogging = dto.EnableFileLogging;

        settings.Monitoring.PollIntervalMs = Math.Max(100, dto.Monitoring.IntervalMs);
        settings.Monitoring.UseFahrenheit = dto.Monitoring.UseFahrenheit;
        settings.Monitoring.UseNetBits = dto.Monitoring.UseNetBits;
        settings.Monitoring.PreferredNic = !string.IsNullOrEmpty(dto.Monitoring.PreferredNic)
            ? dto.Monitoring.PreferredNic : "auto";
        settings.Monitoring.EnableCpu = dto.Monitoring.TrackCpu;
        settings.Monitoring.EnableGpu = dto.Monitoring.TrackGpu;
        settings.Monitoring.EnableRam = dto.Monitoring.TrackRam;
        settings.Monitoring.EnableNet = dto.Monitoring.TrackNet;
        settings.Monitoring.EnableDisk = dto.Monitoring.TrackDisk;
        settings.Monitoring.EnableBattery = dto.Monitoring.TrackBattery;
        settings.Monitoring.GpuUsageSource = dto.Monitoring.GpuUsageSource;
        settings.Monitoring.LogCpuMode = dto.Monitoring.LogCpuMode;
        settings.Monitoring.LogGpuMode = dto.Monitoring.LogGpuMode;
        settings.Monitoring.LogRamMode = dto.Monitoring.LogRamMode;
        settings.Monitoring.LogNetMode = dto.Monitoring.LogNetMode;
        settings.Monitoring.LogDiskMode = dto.Monitoring.LogDiskMode;
        settings.Monitoring.LogBatteryMode = dto.Monitoring.LogBatteryMode;
        settings.Overlay.Visible = dto.Overlay.Visible;
        settings.Overlay.DoubleClickAction = string.IsNullOrEmpty(dto.Overlay.DoubleClickAction)
            ? "settings" : dto.Overlay.DoubleClickAction;
        settings.Overlay.Placement = MapPositionToPlacement(dto.Overlay.Position);
        settings.Overlay.PlacementOffset = dto.Overlay.OffsetX;
        settings.Overlay.Opacity = dto.Overlay.Opacity;
        settings.Overlay.ScalePct = dto.Overlay.Scale;
        settings.Overlay.FontSizePx = dto.Overlay.FontSizePx;
        settings.Overlay.FontBold = dto.Overlay.FontBold;
        settings.Overlay.LabelColor = dto.Overlay.LabelColor;
        settings.Overlay.BgColor = dto.Overlay.BgColor;
        settings.Overlay.ValueColor = dto.Overlay.TextColor;
        if (dto.Overlay.MetricColors is { Count: > 0 })
            settings.Overlay.MetricColors = new Dictionary<string, string>(dto.Overlay.MetricColors);
        settings.Overlay.FollowWindowsTheme = dto.Overlay.FollowWindowsTheme;
        settings.Overlay.UserCustomizedMetricColors = dto.Overlay.UserCustomizedMetricColors is { Count: > 0 }
            ? [.. dto.Overlay.UserCustomizedMetricColors] : [];
        settings.Overlay.DefaultMetricColors = dto.Overlay.DefaultMetricColors is { Count: > 0 }
            ? new Dictionary<string, string>(dto.Overlay.DefaultMetricColors)
            : [];
        settings.Overlay.Row1 = [.. dto.Overlay.Row1 ?? []];

        settings.Alerts.Enabled = dto.Alerts.Enabled;
        settings.Alerts.AlertCpuTemp = dto.Alerts.AlertCpuTemp;
        settings.Alerts.AlertGpuTemp = dto.Alerts.AlertGpuTemp;
        settings.Alerts.AlertCpuUsage = dto.Alerts.AlertCpuUsage;
        settings.Alerts.AlertRamUsage = dto.Alerts.AlertRamUsage;
        settings.Alerts.AlertGpuUsage = dto.Alerts.AlertGpuUsage;
        settings.Alerts.AlertGpuVram = dto.Alerts.AlertGpuVram;
        settings.Alerts.AlertBatteryLow = dto.Alerts.AlertBatteryLow;
        settings.Alerts.AlertCpuFreq = dto.Alerts.AlertCpuFreq;
        settings.Alerts.AlertCpuPower = dto.Alerts.AlertCpuPower;
        settings.Alerts.AlertGpuFreq = dto.Alerts.AlertGpuFreq;
        settings.Alerts.AlertGpuPower = dto.Alerts.AlertGpuPower;
        settings.Alerts.CpuUsageMaxPct = dto.Alerts.CpuUsageMaxPct;
        settings.Alerts.RamUsageMaxPct = dto.Alerts.RamUsageMaxPct;
        settings.Alerts.GpuUsageMaxPct = dto.Alerts.GpuUsageMaxPct;
        settings.Alerts.GpuVramMaxMb = dto.Alerts.GpuVramMaxMb;
        settings.Alerts.CpuTempMaxC = dto.Alerts.CpuTempMaxC;
        settings.Alerts.GpuTempMaxC = dto.Alerts.GpuTempMaxC;
        settings.Alerts.BatteryLowPct = dto.Alerts.BatteryLowPct;
        settings.Alerts.CpuFreqMinMhz = dto.Alerts.CpuFreqMinMhz;
        settings.Alerts.CpuPowerMaxW = dto.Alerts.CpuPowerMaxW;
        settings.Alerts.GpuFreqMinMhz = dto.Alerts.GpuFreqMinMhz;
        settings.Alerts.GpuPowerMaxW = dto.Alerts.GpuPowerMaxW;
        settings.Alerts.ShowToastNotif = dto.Alerts.ShowToastNotif;
        settings.Alerts.FlashOverlay = dto.Alerts.FlashOverlay;
        settings.Alerts.CooldownSecs = dto.Alerts.CooldownSecs;
        settings.Alerts.FireOncePerSession = dto.Alerts.FireOncePerSession;

        settings.NetUsage.Enabled = dto.NetUsage.Enabled;
        settings.WindowTheme = dto.WindowTheme;
    }

    private static string MapPlacementToPosition(string placement) => placement switch
    {
        "left" => "left",
        "center" => "left",
        "right" => "right",
        _ => "right",
    };

    private static string MapPositionToPlacement(string position) => position switch
    {
        "left" => "left",
        _ => "right",
    };
}
