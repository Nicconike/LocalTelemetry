using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Diagnostics;

namespace LocalTelemetry.Core.Hardware;

/// <summary>
/// Persisted hardware configuration model stored in <c>hardware_state.json</c>.
/// </summary>
public sealed class HardwareState
{
    public string Fingerprint { get; set; } = string.Empty;
    public DateTime LastDetectedUtc { get; set; }
    public string SystemModel { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string FormFactor { get; set; } = string.Empty;
    public string Motherboard { get; set; } = string.Empty;
    public string Processor { get; set; } = string.Empty;
    public List<string> Graphics { get; set; } = [];
    public List<string> Storage { get; set; } = [];
    public string Memory { get; set; } = string.Empty;
    public string Battery { get; set; } = string.Empty;
}

/// <summary>
/// Discovers and logs system hardware components. Emits condensed summary logging when
/// the hardware fingerprint matches the cached state, or full component logging when
/// changes are detected or on first run.
/// </summary>
[SupportedOSPlatform("windows")]
public static class HardwareInventory
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Evaluates hardware configuration, compares against <see cref="AppSettings.HardwareStatePath"/>,
    /// logs the result (condensed if unchanged, full if changed/first-run), and updates the cached state file.
    /// </summary>
    public static HardwareState LogHardwareConfiguration(
        ISystemInfo sysInfo,
        string? stateFilePath = null,
        bool forceFull = false)
    {
        stateFilePath ??= AppSettings.HardwareStatePath;

        // System
        string sysModel = sysInfo.GetSystemModel();
        string sysModelStr = !string.IsNullOrWhiteSpace(sysModel) && sysModel != "System Product Name" ? $" ({sysModel})" : "";
        string osVersion = sysInfo.GetOsDisplayVersion();
        string formFactor = sysInfo.GetSystemTypeLabel();
        string systemLine = $"{Environment.MachineName}{sysModelStr}";

        // Motherboard
        string mbMfr = sysInfo.GetMotherboardManufacturer();
        string mbProd = sysInfo.GetMotherboardProductName();
        string mbVer = sysInfo.GetMotherboardVersion();
        string biosVer = sysInfo.GetBiosVersion();
        bool isUefi = sysInfo.GetBiosIsUefi();
        var mbParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(mbVer) && mbVer != "Default string") mbParts.Add($"version {mbVer}");
        if (!string.IsNullOrWhiteSpace(biosVer)) mbParts.Add($"BIOS {biosVer}{(isUefi ? " [UEFI]" : "")}");
        string mbDetails = mbParts.Count > 0 ? $" ({string.Join(", ", mbParts)})" : "";
        string motherboardLine = $"{mbMfr} {mbProd}{mbDetails}".Trim();

        // Processor
        string cpuName = sysInfo.GetCpuName();
        var cpuParts = new List<string>
        {
            $"{sysInfo.GetCpuCoreCount()} Cores",
            $"{sysInfo.GetCpuThreadCount()} Threads"
        };
        int baseMhz = sysInfo.GetCpuBaseSpeedMhz();
        int maxMhz = sysInfo.GetCpuMaxSpeedMhz();
        if (baseMhz > 0) cpuParts.Add($"base {baseMhz} MHz");
        if (maxMhz > 0 && maxMhz > baseMhz) cpuParts.Add($"max {maxMhz} MHz");
        string socket = sysInfo.GetCpuSocket();
        if (!string.IsNullOrWhiteSpace(socket) && socket != "Unknown") cpuParts.Add($"socket {socket}");
        string processorLine = $"{cpuName} ({string.Join(", ", cpuParts)})";

        // Graphics
        var gpuList = sysInfo.GetGpus();
        var gpuLines = new List<string>();
        if (gpuList != null)
        {
            foreach (var g in gpuList)
            {
                var parts = new List<string>
                {
                    g.IsDedicated ? "Dedicated" : "Integrated"
                };
                if (g.VramBytes.HasValue && g.VramBytes.Value > 0)
                {
                    double vramGb = g.VramBytes.Value / (1024.0 * 1024.0 * 1024.0);
                    string vramStr = vramGb >= 1.0 ? $"{Math.Round(vramGb)} GB" : $"{Math.Round(g.VramBytes.Value / (1024.0 * 1024.0))} MB";
                    parts.Add($"VRAM {vramStr}");
                }
                if (!string.IsNullOrWhiteSpace(g.DriverVersion))
                {
                    parts.Add($"driver {g.DriverVersion}");
                }
                gpuLines.Add($"{g.Name} ({string.Join(", ", parts)})");
            }
        }

        // Storage (1-based drive numbering for readability)
        var diskInfos = sysInfo.GetAllDisks();
        var diskLines = new List<string>();
        string primaryDiskType = diskInfos.Count > 0 ? (diskInfos[0].DisplayType ?? diskInfos[0].BusType ?? "DISK") : "DISK";
        for (int i = 0; i < diskInfos.Count; i++)
        {
            var d = diskInfos[i];
            var parts = new List<string>();
            string typeStr = !string.IsNullOrWhiteSpace(d.DisplayType) ? d.DisplayType : d.BusType;
            if (!string.IsNullOrWhiteSpace(typeStr)) parts.Add(typeStr);
            if (!string.IsNullOrWhiteSpace(d.FormattedSize)) parts.Add(d.FormattedSize);
            if (d.IsBootDrive) parts.Add("Boot Drive");
            string details = parts.Count > 0 ? $" ({string.Join(", ", parts)})" : "";
            diskLines.Add($"Drive {i + 1} - {d.Model}{details}");
        }

        // Memory
        double installedGb = sysInfo.GetInstalledRamGb();
        string ramType = sysInfo.GetRamType();
        string ramSpeed = sysInfo.GetRamSpeed();
        int ramSlots = sysInfo.GetRamModuleCount();
        var ramParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ramType) && ramType != "Unknown") ramParts.Add($"type {ramType}");
        if (!string.IsNullOrWhiteSpace(ramSpeed) && ramSpeed != "0" && ramSpeed != "Unknown")
        {
            string speedFormatted = (ramSpeed.Contains("MT/s", StringComparison.OrdinalIgnoreCase) || ramSpeed.Contains("MHz", StringComparison.OrdinalIgnoreCase))
                ? ramSpeed
                : $"{ramSpeed} MHz";
            ramParts.Add($"speed {speedFormatted}");
        }
        if (ramSlots > 0) ramParts.Add($"{ramSlots} modules");
        string ramDetails = ramParts.Count > 0 ? $" ({string.Join(", ", ramParts)})" : "";

        var memStatus = new NativeMethods.MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };
        string memoryLine;
        if (NativeMethods.GlobalMemoryStatusEx(ref memStatus) && memStatus.ullTotalPhys > 0)
        {
            double usableGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            memoryLine = $"{installedGb:F1} GB installed, {usableGb:F1} GB usable{ramDetails}";
        }
        else
        {
            memoryLine = $"{installedGb:F1} GB installed{ramDetails}";
        }

        // Battery
        string batteryLine;
        string batteryFingerprint;
        if (sysInfo.HasBattery())
        {
            var bParts = new List<string>();
            string mfr = sysInfo.GetBatteryManufacturer();
            string name = sysInfo.GetBatteryDeviceName();
            string design = sysInfo.GetBatteryDesignCapacity();
            string full = sysInfo.GetBatteryFullChargedCapacity();
            string identity = $"{mfr} {name}".Trim();
            if (string.IsNullOrEmpty(identity)) identity = "Standard Battery";
            if (!string.IsNullOrEmpty(design)) bParts.Add($"design capacity {design}");
            if (!string.IsNullOrEmpty(full)) bParts.Add($"full charge {full}");
            string capInfo = bParts.Count > 0 ? $" ({string.Join(", ", bParts)})" : "";
            batteryLine = $"{identity}{capInfo}";
            batteryFingerprint = !string.IsNullOrEmpty(design) ? $"{identity} (design capacity {design})" : identity;
        }
        else
        {
            batteryLine = "None (AC Mains / Desktop)";
            batteryFingerprint = batteryLine;
        }

        // Deterministic fingerprint (uses static battery design identity so dynamic wear/recalibration does not cause spurious hardware re-detection)
        string rawFingerprintData = $"{systemLine}|{osVersion}|{formFactor}|{motherboardLine}|{processorLine}|{string.Join(";", gpuLines)}|{string.Join(";", diskLines)}|{memoryLine}|{batteryFingerprint}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawFingerprintData));
        string fingerprint = Convert.ToHexString(hash);

        var currentState = new HardwareState
        {
            Fingerprint = fingerprint,
            LastDetectedUtc = DateTime.UtcNow,
            SystemModel = systemLine,
            OsVersion = osVersion,
            FormFactor = formFactor,
            Motherboard = motherboardLine,
            Processor = processorLine,
            Graphics = gpuLines,
            Storage = diskLines,
            Memory = memoryLine,
            Battery = batteryLine
        };

        // Check cached state
        HardwareState? previousState = null;
        if (!string.IsNullOrEmpty(stateFilePath) && File.Exists(stateFilePath))
        {
            try
            {
                string json = File.ReadAllText(stateFilePath);
                previousState = JsonSerializer.Deserialize<HardwareState>(json, JsonOpts);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to read cached hardware state");
            }
        }

        bool unchanged = !forceFull
            && previousState is not null
            && string.Equals(previousState.Fingerprint, fingerprint, StringComparison.Ordinal);

        if (unchanged)
        {
            Log.Info("Hardware configuration unchanged");
            currentState.LastDetectedUtc = DateTime.UtcNow;
            SaveStateQuietly(stateFilePath, currentState);
            return currentState;
        }

        // Full component logging
        Log.Info("Hardware configuration detected");
        Log.Info($"Detected system {systemLine}, OS {osVersion}, form factor {formFactor}");
        if (!string.IsNullOrWhiteSpace(motherboardLine))
            Log.Info($"Detected motherboard {motherboardLine}");
        Log.Info($"Detected processor {processorLine}");
        foreach (var gpu in gpuLines)
            Log.Info($"Detected graphics adapter {gpu}");
        Log.Info($"Detected {diskInfos.Count} physical drive(s), primary {primaryDiskType}");
        foreach (var d in diskLines)
            Log.Info($"Detected {d}");
        Log.Info($"Detected memory {memoryLine}");
        Log.Info($"Detected battery {batteryLine}");

        SaveStateQuietly(stateFilePath, currentState);
        return currentState;
    }

    private static void SaveStateQuietly(string? path, HardwareState state)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(state, JsonOpts);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to save hardware state");
        }
    }
}
