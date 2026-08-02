namespace LocalTelemetry.Core.Models;

/// <summary>
/// Immutable snapshot of all telemetry data collected during a single poll tick.
/// Contains CPU, RAM, GPU, network, disk and battery metrics.
/// </summary>
public sealed record TelemetrySnapshot
{
    /// <summary>CPU usage percentage (0-100) or 0 if unavailable.</summary>
    public float CpuUsagePct { get; init; }
    /// <summary>CPU package temperature in Celsius or 0 if unavailable.</summary>
    public float CpuTempPackageC { get; init; }
    /// <summary>CPU effective frequency in GHz or 0 if unavailable.</summary>
    public float CpuFreqGhz { get; init; }
    /// <summary>CPU package power draw in watts or 0 if unavailable.</summary>
    public float CpuPackagePowerW { get; init; }

    /// <summary>RAM usage percentage (0-100).</summary>
    public float RamUsagePct { get; init; }
    /// <summary>RAM used in gigabytes.</summary>
    public float RamUsedGb { get; init; }

    /// <summary>GPU usage percentage (0-100) or 0 if unavailable.</summary>
    public float GpuUsagePct { get; init; }
    /// <summary>GPU temperature in Celsius or 0 if unavailable.</summary>
    public float GpuTempC { get; init; }
    /// <summary>GPU VRAM usage in megabytes or 0 if unavailable.</summary>
    public float GpuVramUsedMb { get; init; }
    /// <summary>GPU core clock in MHz or 0 if unavailable.</summary>
    public float GpuFreqMHz { get; init; }
    /// <summary>GPU power draw in watts or 0 if unavailable.</summary>
    public float GpuPowerW { get; init; }

    /// <summary>Network download speed in bytes per second.</summary>
    public double NetDownBps { get; init; }
    /// <summary>Network upload speed in bytes per second.</summary>
    public double NetUpBps { get; init; }
    /// <summary>Name of the active network interface.</summary>
    public string NetInterfaceName { get; init; } = string.Empty;
    /// <summary>Cumulative total bytes transferred (down+up) since app start.</summary>
    public long NetTotalBytes { get; init; }

    /// <summary>Per-disk I/O snapshots.</summary>
    public List<DiskSnapshot> Disks { get; init; } = [];
    /// <summary>Aggregate disk read speed in MB/s.</summary>
    public float DiskReadMbps { get; init; }
    /// <summary>Aggregate disk write speed in MB/s.</summary>
    public float DiskWriteMbps { get; init; }
    /// <summary>Primary disk bus type label ("NVMe", "SATA", "HDD", "SSD" or empty).</summary>
    public string PrimaryDiskType { get; init; } = string.Empty;

    /// <summary>Battery charge percentage (0-100) or 0 if no battery or unavailable.</summary>
    public float BatteryPct { get; init; }
    /// <summary>Whether the battery is currently charging.</summary>
    public bool BatteryIsCharging { get; init; }
    /// <summary>Whether AC power is connected (even if battery is at charge limit and not actively charging).</summary>
    public bool IsOnACPower { get; init; }
    /// <summary>Battery charge/discharge rate in watts (positive = charging, negative = discharging).</summary>
    public float BatteryChargeRateW { get; init; }

    /// <summary>UTC timestamp of when this snapshot was captured.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>Singleton empty snapshot for initial state.</summary>
    public static readonly TelemetrySnapshot Empty = new();
}

/// <summary>Snapshot of a single disk's I/O metrics collected during a poll tick.</summary>
public sealed record DiskSnapshot
{
    /// <summary>Unique identifier (e.g. "disk0").</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>Display label (e.g. "DISK0").</summary>
    public string Label { get; init; } = string.Empty;
    /// <summary>Bus type label ("NVMe", "SATA", "HDD", "SSD" or "DISK").</summary>
    public string BusType { get; init; } = string.Empty;
    /// <summary>Disk read speed in MB/s.</summary>
    public float ReadMBps { get; init; }
    /// <summary>Disk write speed in MB/s.</summary>
    public float WriteMBps { get; init; }
}
