namespace LocalTelemetry.Core.Models;

/// <summary>
/// Central registry of all available telemetry metrics and their display formatting.
/// Defines metric ID constants, the AllMetrics registry and formatting helpers.
/// </summary>
public static class Metrics
{
    /// <summary>CPU usage percentage.</summary>
    public const string CpuPct = "cpu_pct";
    /// <summary>CPU temperature in Celsius.</summary>
    public const string CpuTemp = "cpu_temp";
    /// <summary>CPU effective frequency in GHz.</summary>
    public const string CpuFreq = "cpu_freq";
    /// <summary>CPU package power in watts.</summary>
    public const string CpuPower = "cpu_power";
    /// <summary>RAM usage percentage.</summary>
    public const string RamPct = "ram_pct";
    /// <summary>RAM used in gigabytes.</summary>
    public const string RamUsed = "ram_used";
    /// <summary>GPU usage percentage.</summary>
    public const string GpuPct = "gpu_pct";
    /// <summary>GPU temperature in Celsius.</summary>
    public const string GpuTemp = "gpu_temp";
    /// <summary>GPU VRAM usage in megabytes.</summary>
    public const string GpuVram = "gpu_vram";
    /// <summary>GPU core clock in MHz.</summary>
    public const string GpuFreq = "gpu_freq";
    /// <summary>GPU power in watts.</summary>
    public const string GpuPower = "gpu_power";
    /// <summary>Network download speed.</summary>
    public const string NetDown = "net_down";
    /// <summary>Network upload speed.</summary>
    public const string NetUp = "net_up";
    /// <summary>Cumulative total bytes transferred (down+up) since app start.</summary>
    public const string NetTotal = "net_total";
    /// <summary>Battery charge percentage.</summary>
    public const string BatteryPct = "battery_pct";
    /// <summary>Battery charge/discharge rate in watts.</summary>
    public const string BatteryRate = "battery_rate";

    /// <summary>Master list of all registered metric descriptors.</summary>
    public static readonly List<MetricDescriptor> AllMetrics =
    [
        new(CpuPct,   "CPU",  "CPU Usage",        "%",    "cpu"),
        new(CpuTemp,  "CPU",  "CPU Temp",          "°C",   "cpu"),
        new(CpuFreq,  "CPU",  "CPU Frequency",     "GHz",  "cpu"),
        new(CpuPower, "CPU",  "CPU Package Power", "W",    "cpu"),
        new(RamPct,   "RAM",  "RAM Usage",         "%",    "ram"),
        new(RamUsed,  "RAM",  "RAM Used",          "GB",   "ram"),
        new(GpuPct,   "GPU",  "GPU Usage",         "%",    "gpu"),
        new(GpuTemp,  "GPU",  "GPU Temp",          "°C",   "gpu"),
        new(GpuVram,  "VRAM", "GPU VRAM Usage",     "MB",   "gpu"),
        new(GpuFreq, "GPU",  "GPU Frequency",     "MHz",  "gpu"),
        new(GpuPower, "GPU",  "GPU Power",         "W",    "gpu"),
        new(NetDown,  "DOWN\u2193", "Download",             "",     "net"),
        new(NetUp,    "UP\u2191",   "Upload",               "",     "net"),
        new(NetTotal, "Total",     "Total Transferred",    "",     "net"),
        new(BatteryPct, "BAT", "Battery",      "%",    "battery"),
        new(BatteryRate, "BAT", "Charge Rate", "W",    "battery"),
    ];

    /// <summary>Lookup dictionary by metric ID for fast O(1) access in render hot paths.</summary>
    public static readonly Dictionary<string, MetricDescriptor> AllMetricsById = AllMetrics.ToDictionary(m => m.Id);

    /// <summary>Registers per-disk metric descriptors for a given disk index.</summary>
    /// <param name="id">Disk identifier (e.g. "disk0").</param>
    /// <param name="label">Display label (e.g. "DISK0").</param>
    public static void RegisterDisk(string id, string label)
    {
        string key = $"disk_{id}_read";
        if (AllMetricsById.ContainsKey(key)) return;
        var list = new MetricDescriptor[]
        {
            new($"disk_{id}_read", label, $"{label} Read", "MB/s", "disk"),
            new($"disk_{id}_write", label, $"{label} Write", "MB/s", "disk"),
        };
        foreach (var m in list)
        {
            AllMetrics.Add(m);
            AllMetricsById[m.Id] = m;
        }
    }

    /// <summary>Formats a snapshot value for display based on metric ID.</summary>
    /// <param name="id">Metric identifier constant.</param>
    /// <param name="s">Telemetry snapshot to read values from.</param>
    /// <param name="useBits">If true, display network speeds in bits; otherwise bytes.</param>
    /// <param name="useFahrenheit">If true, display temperatures in Fahrenheit.</param>
    /// <returns>Formatted display string (e.g. "45%", "3.5GHz", "1.2GB/s").</returns>
    public static string Format(
        string id, TelemetrySnapshot s, bool useBits, bool useFahrenheit) => id switch
        {
            CpuPct => $"{s.CpuUsagePct:F0}%",
            CpuTemp => Temp(s.CpuTempPackageC, useFahrenheit),
            CpuFreq => s.CpuFreqGhz > 0 ? $"{s.CpuFreqGhz:F2}GHz" : "--",
            CpuPower => s.CpuPackagePowerW > 0 ? $"{s.CpuPackagePowerW:F0}W" : "--",
            RamPct => $"{s.RamUsagePct:F0}%",
            RamUsed => $"{s.RamUsedGb:F1}GB",
            GpuPct => $"{s.GpuUsagePct:F0}%",
            GpuTemp => Temp(s.GpuTempC, useFahrenheit),
            GpuVram => $"{s.GpuVramUsedMb:F0}MB",
            GpuFreq => s.GpuFreqMHz > 0 ? $"{s.GpuFreqMHz:F0}MHz" : "--",
            GpuPower => s.GpuPowerW > 0 ? $"{s.GpuPowerW:F0}W" : "--",
            NetDown => Net(s.NetDownBps, useBits),
            NetUp => Net(s.NetUpBps, useBits),
            NetTotal => FormatTotalBytes(s.NetTotalBytes),
            BatteryPct => s.BatteryPct > 0 ? $"{s.BatteryPct:F0}%" : "--",
            BatteryRate => s.BatteryPct > 0 && s.BatteryChargeRateW != 0
                ? $"{(s.BatteryChargeRateW > 0 ? "+" : "")}{s.BatteryChargeRateW:F1}W"
                : s.BatteryPct > 0 ? "Full" : "--",
            _ => DiskValue(id, s, useFahrenheit),
        };

    private static string Temp(float c, bool f)
        => c <= 0 ? "--" : f ? $"{c * 1.8f + 32f:F0}°F" : $"{c:F0}°C";

    private static string Net(double bps, bool bits)
    {
        if (bits) bps *= 8;
        string u = bits ? "b" : "B";
        return bps switch
        {
            >= 1_000_000_000 => $"{bps / 1_000_000_000.0:F1}G{u}/s",
            >= 1_000_000 => $"{bps / 1_000_000.0:F1}M{u}/s",
            >= 1_000 => $"{bps / 1_000.0:F1}K{u}/s",
            _ => $"{bps:F0}{u}/s",
        };
    }

    private static string FormatTotalBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_000_000_000_000 => $"{bytes / 1_000_000_000_000.0:F2}TB",
            >= 1_000_000_000 => $"{bytes / 1_000_000_000.0:F2}GB",
            >= 1_000_000 => $"{bytes / 1_000_000.0:F2}MB",
            >= 1_000 => $"{bytes / 1_000.0:F2}KB",
            _ => $"{bytes}B",
        };
    }

    private static string DiskValue(string id, TelemetrySnapshot s, bool f)
    {
        int last = id.LastIndexOf('_');
        if (last < 0 || last < 5) return "--";
        string aspect = id[(last + 1)..];
        string rawId = id[5..last];
        DiskSnapshot? disk = null;
        for (int i = 0; i < s.Disks.Count; i++)
        {
            if (s.Disks[i].Id == rawId) { disk = s.Disks[i]; break; }
        }
        if (disk is null) return "--";
        float mbps = aspect switch
        {
            "read" => disk.ReadMBps,
            "write" => disk.WriteMBps,
            _ => 0,
        };
        return FormatRate(mbps);
    }

    private static string FormatRate(float mbps)
    {
        return mbps switch
        {
            >= 1000 => $"{mbps / 1000.0:F1}GB/s",
            >= 1 => $"{mbps:F1}MB/s",
            _ => $"{mbps * 1000.0:F0}KB/s",
        };
    }

    /// <summary>Formats a temperature value for display.</summary>
    /// <param name="c">Temperature in Celsius.</param>
    /// <param name="f">If true, convert to Fahrenheit.</param>
    /// <returns>Formatted string (e.g. "45°C" or "113°F") or "--" if unavailable.</returns>
    public static string TempString(float c, bool f)
        => c <= 0 ? "--" : f ? $"{c * 1.8f + 32f:F0}°F" : $"{c:F0}°C";
}
