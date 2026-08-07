using System.Text.Json;
using System.Text.Json.Serialization;
using LocalTelemetry.Core.Diagnostics;
using LocalTelemetry.Core.Models;

namespace LocalTelemetry.Core.Config;

// AppSettings
/// <summary>Root configuration model serialized to <c>settings.json</c>.</summary>
public sealed class AppSettings
{
    /// <summary>Full path to the <c>settings.json</c> config file.</summary>
    [JsonIgnore] public static string ConfigPath { get; private set; } = string.Empty;
    /// <summary>Full path to the <c>internet_usage.jsonl</c> traffic history file.</summary>
    [JsonIgnore] public static string NetUsagePath { get; private set; } = string.Empty;
    /// <summary>Full path to the metrics log file (<c>lt_DD-MM-YYYY.log</c>).</summary>
    [JsonIgnore] public static string MetricsLogPath { get; private set; } = string.Empty;
    /// <summary>Full path to the system log file (<c>lt_system.log</c>).</summary>
    [JsonIgnore] public static string SystemLogPath { get; private set; } = string.Empty;
    /// <summary>Initializes all static paths from the application executable directory.</summary>
    public static void InitPaths(string exeDir)
    {
        string markerFile = Path.Combine(exeDir, "app.mode");
        bool standardMode = File.Exists(markerFile);

        string dataDir = standardMode
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalTelemetry")
            : exeDir;

        if (standardMode && !Directory.Exists(dataDir))
        {
            try { Directory.CreateDirectory(dataDir); }
            catch { /* fallback to exeDir if directory creation fails */ dataDir = exeDir; }
        }

        ConfigPath = Path.Combine(dataDir, "settings.json");
        NetUsagePath = Path.Combine(dataDir, "internet_usage.jsonl");
        MetricsLogPath = Path.Combine(dataDir, $"lt_{DateTime.Now:dd-MM-yyyy}.log");
        SystemLogPath = Path.Combine(dataDir, "lt_system.log");
    }

    /// <summary>Loads settings from <see cref="ConfigPath"/>, creating defaults if missing or corrupt.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(ConfigPath), JsonOpts);
                if (loaded is not null)
                {
                    MigrateConfig(loaded);
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Corrupt config at {ConfigPath}");
            try
            {
                File.Copy(ConfigPath, ConfigPath + ".corrupt", overwrite: true);
                Log.Info($"Backed up corrupt config to {ConfigPath}.corrupt");
            }
            catch (Exception backupEx)
            {
                Log.Error(backupEx, $"Failed to back up corrupt config");
            }
        }
        var fresh = new AppSettings();
        fresh.Save();
        return fresh;
    }

    private static void MigrateConfig(AppSettings cfg)
    {
        // Ensure MetricColors has all known metric entries (from older configs with fewer keys)
        var mc = cfg.Overlay.MetricColors;
        var allDefaults = BrandColorDefaults.BuildDefaultMetricColors();
        bool dirty = false;
        foreach (var kvp in allDefaults)
        {
            if (!mc.ContainsKey(kvp.Key))
            {
                mc[kvp.Key] = kvp.Value;
                dirty = true;
            }
        }
        if (dirty)
        {
            cfg.Save();
            Log.Info($"Migrated config: filled {mc.Count} MetricColors entries");
        }

        // Ensure GroupColors has all known group entries (from configs saved before group colors existed)
        var gc = cfg.Overlay.GroupColors;
        bool groupDirty = false;
        foreach (var kvp in BrandColorDefaults.BuildDefaultGroupColors(allDefaults))
        {
            if (!gc.ContainsKey(kvp.Key))
            {
                gc[kvp.Key] = kvp.Value;
                groupDirty = true;
            }
        }
        if (groupDirty)
        {
            cfg.Save();
            Log.Info($"Migrated config: filled {gc.Count} GroupColors entries");
        }
    }

    /// <summary>Serializes this instance to <see cref="ConfigPath"/> as JSON.</summary>
    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, JsonOpts);
            File.WriteAllText(ConfigPath, json);
            Log.Info($"AppSettings.Save: wrote {json.Length} chars to {ConfigPath}, visible={Overlay.Visible}");
        }
        catch (Exception ex) { Log.Error(ex, "Save FAILED"); }
    }

    /// <summary>Whether the app is registered to run automatically at user logon.</summary>
    public bool RunAtStartup { get; set; } = false;
    /// <summary>Whether the main window starts minimized to the tray.</summary>
    public bool StartMinimized { get; set; } = false;
    /// <summary>Whether closing the window minimizes to tray instead of exiting.</summary>
    public bool MinimizeToTrayOnClose { get; set; } = false;
    /// <summary>Whether metrics file logging is enabled (<c>lt_DD-MM-YYYY.log</c>). System logging to <c>lt_system.log</c> is always on.</summary>
    public bool EnableFileLogging { get; set; } = true;

    /// <summary>Hardware polling configuration.</summary>
    public MonitoringConfig Monitoring { get; set; } = new();
    /// <summary>Taskbar overlay appearance and layout configuration.</summary>
    public OverlayConfig Overlay { get; set; } = new();
    /// <summary>Threshold alert configuration.</summary>
    public AlertConfig Alerts { get; set; } = new();
    /// <summary>Network usage logging configuration.</summary>
    public NetUsageConfig NetUsage { get; set; } = new();
    /// <summary>Themes the settings window (<c>"default"</c>, <c>"dark"</c>, etc.).</summary>
    public string WindowTheme { get; set; } = "default";

    [JsonIgnore]
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

/// <summary>Hardware monitoring poll configuration.</summary>
public sealed class MonitoringConfig
{
    /// <summary>Polling interval in milliseconds (minimum 100).</summary>
    public int PollIntervalMs { get; set; } = 1000;
    /// <summary>Whether CPU usage/frequency monitoring is enabled.</summary>
    public bool EnableCpu { get; set; } = true;
    /// <summary>Whether GPU usage/temperature monitoring is enabled.</summary>
    public bool EnableGpu { get; set; } = true;
    /// <summary>Whether RAM usage monitoring is enabled.</summary>
    public bool EnableRam { get; set; } = true;
    /// <summary>Whether network traffic monitoring is enabled.</summary>
    public bool EnableNet { get; set; } = true;
    /// <summary>Whether disk I/O monitoring is enabled.</summary>
    public bool EnableDisk { get; set; } = true;
    /// <summary>Whether battery monitoring is enabled.</summary>
    public bool EnableBattery { get; set; } = true;
    /// <summary>Display temperatures in Fahrenheit instead of Celsius.</summary>
    public bool UseFahrenheit { get; set; } = false;
    /// <summary>Display network rates in bits/sec instead of bytes/sec.</summary>
    public bool UseNetBits { get; set; } = false;
    /// <summary>Preferred network adapter name or <c>"auto"</c> to pick the most active one.</summary>
    public string PreferredNic { get; set; } = "auto";
    /// <summary>GPU usage source: <c>"driver"</c> (NVML/ADL, kernel busy time) or <c>"wddm"</c> (D3DKMTQueryStatistics, max engine utilisation).</summary>
    public string GpuUsageSource { get; set; } = "driver";

    // Per-category log level
    // 0 = Off, 1 = Errors only, 2 = Full (per-tick summaries + errors)
    /// <summary>CPU logging level (0=off, 1=errors, 2=full).</summary>
    public int LogCpuMode { get; set; } = 1;
    /// <summary>GPU logging level (0=off, 1=errors, 2=full).</summary>
    public int LogGpuMode { get; set; } = 1;
    /// <summary>RAM logging level (0=off, 1=errors, 2=full).</summary>
    public int LogRamMode { get; set; } = 1;
    /// <summary>Network logging level (0=off, 1=errors, 2=full).</summary>
    public int LogNetMode { get; set; } = 1;
    /// <summary>Disk logging level (0=off, 1=errors, 2=full).</summary>
    public int LogDiskMode { get; set; } = 1;
    /// <summary>Battery logging level (0=off, 1=errors, 2=full).</summary>
    public int LogBatteryMode { get; set; } = 1;
}

/// <summary>Taskbar overlay appearance and layout.</summary>
public sealed class OverlayConfig
{
    /// <summary>Whether the overlay is visible on the taskbar.</summary>
    public bool Visible { get; set; } = false;
    /// <summary>Taskbar placement: <c>"left"</c>, <c>"center"</c> or <c>"right"</c>.</summary>
    public string Placement { get; set; } = "left";
    /// <summary>Horizontal offset in pixels from the chosen placement.</summary>
    public int PlacementOffset { get; set; } = 0;
    /// <summary>Saved drag X for Approach-B float (-1 = auto).</summary>
    public int FloatX { get; set; } = -1;
    /// <summary>Saved drag Y for Approach-B float (-1 = auto).</summary>
    public int FloatY { get; set; } = -1;
    /// <summary>Action on double-click: <c>"none"</c>, <c>"taskmanager"</c>, <c>"settings"</c>.</summary>
    public string DoubleClickAction { get; set; } = "settings";
    /// <summary>Overlay opacity percentage (0-100).</summary>
    public int Opacity { get; set; } = 100;
    /// <summary>Overlay scale percentage (0-100).</summary>
    public int ScalePct { get; set; } = 100;
    /// <summary>Font size in pixels.</summary>
    public float FontSizePx { get; set; } = 14f;
    /// <summary>Whether overlay text is bold.</summary>
    public bool FontBold { get; set; } = false;
    /// <summary>Label color as a hex string (e.g. <c>"#FFFFFF"</c>).</summary>
    public string LabelColor { get; set; } = "#FFFFFF";
    /// <summary>Default value color as a hex string.</summary>
    public string ValueColor { get; set; } = "#FFFFFF";
    /// <summary>Background color as a hex string.</summary>
    public string BgColor { get; set; } = "#1c1b19";

    /// <summary>Per-metric value colors for the widget (overrides ValueColor).</summary>
    public Dictionary<string, string> MetricColors { get; set; } = new()
    {
        [Metrics.CpuPct] = "#00E5FF",
        [Metrics.CpuTemp] = "#00E5FF",
        [Metrics.CpuFreq] = "#00E5FF",
        [Metrics.CpuPower] = "#00E5FF",
        [Metrics.RamPct] = "#A78BFA",
        [Metrics.RamUsed] = "#A78BFA",
        [Metrics.GpuPct] = "#88CCFF",
        [Metrics.GpuTemp] = "#88CCFF",
        [Metrics.GpuVram] = "#88CCFF",
        [Metrics.GpuFreq] = "#88CCFF",
        [Metrics.GpuPower] = "#88CCFF",
        [Metrics.NetDown] = "#38BDF8",
        [Metrics.NetUp] = "#4ADE80",
        [Metrics.NetTotal] = "#FBBF24",
        [Metrics.BatteryPct] = "#80E080",

        [Metrics.BatteryRate] = "#80E080",
    };

    /// <summary>
    /// Per-group colors shown by the Appearance page group pickers ("All CPU", "All GPU", ...).
    /// Keys: <c>cpu</c>, <c>gpu</c>, <c>ram</c>, <c>network</c>, <c>battery</c>, <c>disk</c>.
    /// Defaults to the detected vendor-brand colors (see <c>ApplyVendorColors</c>).
    /// Independent of <see cref="MetricColors"/>: changing a group color stamps all metrics in the
    /// group, but changing a single metric color does not affect the group color.
    /// </summary>
    public Dictionary<string, string> GroupColors { get; set; } = new();

    /// <summary>Hardware-detected default colors (never saved, rebuilt on each launch).</summary>
    [JsonIgnore]
    public Dictionary<string, string> DefaultMetricColors { get; set; } = new();

    /// <summary>When true, widget background/text follows Windows taskbar theme.</summary>
    public bool FollowWindowsTheme { get; set; } = true;

    /// <summary>Metric IDs whose colors were customized by the user (skip in ApplyVendorColors).</summary>
    public HashSet<string> UserCustomizedMetricColors { get; set; } = new();

    /// <summary>Group keys whose colors were customized by the user (skip in ApplyVendorColors).</summary>
    public HashSet<string> UserCustomizedGroupColors { get; set; } = new();

    /// <summary>Combined metric ID list (even indices = top row, odd indices = bottom row).</summary>
    public List<string> Row1 { get; set; } = [Metrics.CpuPct, Metrics.CpuTemp, Metrics.GpuPct, Metrics.GpuTemp, Metrics.GpuVram, Metrics.NetTotal, Metrics.RamPct, Metrics.RamUsed, Metrics.NetDown, Metrics.NetUp];
}

// Alert Config
/// <summary>Threshold alert configuration.</summary>
public sealed class AlertConfig
{
    /// <summary>Whether threshold alerts are enabled.</summary>
    public bool Enabled { get; set; } = false;

    // Per-metric toggles
    /// <summary>Alert when CPU temperature exceeds <see cref="CpuTempMaxC"/>.</summary>
    public bool AlertCpuTemp { get; set; }
    /// <summary>Alert when GPU temperature exceeds <see cref="GpuTempMaxC"/>.</summary>
    public bool AlertGpuTemp { get; set; }
    /// <summary>Alert when CPU usage exceeds <see cref="CpuUsageMaxPct"/>.</summary>
    public bool AlertCpuUsage { get; set; }
    /// <summary>Alert when RAM usage exceeds <see cref="RamUsageMaxPct"/>.</summary>
    public bool AlertRamUsage { get; set; }
    /// <summary>Alert when GPU usage exceeds <see cref="GpuUsageMaxPct"/>.</summary>
    public bool AlertGpuUsage { get; set; }
    /// <summary>Alert when GPU VRAM usage exceeds <see cref="GpuVramMaxMb"/>.</summary>
    public bool AlertGpuVram { get; set; }
    /// <summary>Alert when battery level drops below <see cref="BatteryLowPct"/>.</summary>
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
    /// <summary>CPU temperature threshold in Celsius.</summary>
    public float CpuTempMaxC { get; set; } = 90f;
    /// <summary>GPU temperature threshold in Celsius.</summary>
    public float GpuTempMaxC { get; set; } = 88f;
    /// <summary>CPU usage threshold in percent.</summary>
    public float CpuUsageMaxPct { get; set; } = 95f;
    /// <summary>RAM usage threshold in percent.</summary>
    public float RamUsageMaxPct { get; set; } = 92f;
    /// <summary>GPU usage threshold in percent.</summary>
    public float GpuUsageMaxPct { get; set; } = 95f;
    /// <summary>GPU VRAM usage threshold in megabytes.</summary>
    public float GpuVramMaxMb { get; set; } = 6000f;
    /// <summary>Battery level threshold in percent.</summary>
    public float BatteryLowPct { get; set; } = 20f;
    /// <summary>CPU frequency threshold in MHz - alert when below (throttling).</summary>
    public float CpuFreqMinMhz { get; set; } = 800f;
    /// <summary>CPU package power threshold in watts.</summary>
    public float CpuPowerMaxW { get; set; } = 65f;
    /// <summary>GPU frequency threshold in MHz - alert when below (throttling).</summary>
    public float GpuFreqMinMhz { get; set; } = 300f;
    /// <summary>GPU power threshold in watts.</summary>
    public float GpuPowerMaxW { get; set; } = 150f;

    // Actions
    /// <summary>Whether to show a Windows toast notification on alert.</summary>
    public bool ShowToastNotif { get; set; } = true;
    /// <summary>Whether to flash the overlay background on alert.</summary>
    public bool FlashOverlay { get; set; } = true;
    /// <summary>Minimum seconds between repeated alerts for the same threshold.</summary>
    public int CooldownSecs { get; set; } = 60;
    /// <summary>When true, alerts fire only once per app session (cooldown is ignored).</summary>
    public bool FireOncePerSession { get; set; }
}

// NetUsage Config
/// <summary>Network usage history logging configuration.</summary>
public sealed class NetUsageConfig
{
    /// <summary>Whether daily network usage tracking is enabled.</summary>
    public bool Enabled { get; set; } = true;
}
