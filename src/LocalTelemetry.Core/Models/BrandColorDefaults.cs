using System.Runtime.Versioning;
using LocalTelemetry.Core.Diagnostics;
using LocalTelemetry.Core.Hardware;

namespace LocalTelemetry.Core.Models;

/// <summary>Hardware vendor brand color defaults detected at launch.</summary>
[SupportedOSPlatform("windows")]
public static class BrandColorDefaults
{
    // Brand color dictionary
    private static readonly Dictionary<string, string> _brand = new(StringComparer.OrdinalIgnoreCase)
    {
        // Processors & Major Silicon
        ["Intel"] = "#0068B5",            // Intel Blue (2020 Rebrand)
        ["AMD"] = "#ED1C24",              // AMD Red
        ["NVIDIA"] = "#76B900",           // NVIDIA Green
        ["Qualcomm"] = "#3253DC",         // Qualcomm Blue
        ["Atheros"] = "#3253DC",          // Acquired by Qualcomm
        ["Broadcom"] = "#CC092F",         // Broadcom Red
        ["MediaTek"] = "#EC9430",         // MediaTek Orange
        ["Ralink"] = "#EC9430",           // Acquired by MediaTek
        ["Realtek"] = "#006DB6",          // Realtek Blue
        ["Marvell"] = "#0072CE",          // Science Blue
        ["Mellanox"] = "#76B900",         // Acquired by NVIDIA
        ["ASIX"] = "#0066A1",             // ASIX Blue

        // Memory & Storage
        ["Samsung"] = "#1428A0",          // Samsung Blue
        ["Micron"] = "#0033A0",           // Micron Corporate Blue
        ["Crucial"] = "#0068FF",          // Crucial Blue Ribbon
        ["Ballistix"] = "#E31937",        // Historical Ballistix Red
        ["SK Hynix"] = "#EA002C",         // SK Group Red
        ["Western Digital"] = "#005195",  // WD Blue
        ["SanDisk"] = "#E10600",          // SanDisk Red
        ["HGST"] = "#0072CE",             // WD/HGST Blue
        ["Seagate"] = "#6EBE49",          // Seagate Green
        ["Toshiba"] = "#FF0000",          // Toshiba Red
        ["Kioxia"] = "#00A4E4",           // Kioxia Cyan
        ["Corsair"] = "#FFE500",          // Corsair Gaming Yellow
        ["G Skill"] = "#ED1C23",          // Torch Red
        ["Kingston"] = "#ED1A3B",         // Kingston Rex Red
        ["Patriot"] = "#DA291C",          // Patriot Red
        ["PNY"] = "#003087",              // PNY Blue
        ["ADATA"] = "#DD00F8",            // Electric Violet
        ["Team"] = "#E10019",             // TeamGroup Red
        ["Nanya"] = "#00519E",            // Nanya Blue
        ["Elpida"] = "#0033A0",           // Historical Elpida Blue
        ["LITEON"] = "#0057A8",           // LITEON Blue
        ["Ramaxel"] = "#005BAB",          // Ramaxel Corporate Blue

        // Motherboards, OEMs & Peripherals
        ["ASUS"] = "#006CE1",             // ASUS Science Blue
        ["MSI"] = "#ED1C24",              // MSI Red
        ["Gigabyte"] = "#005B9A",         // Gigabyte Blue
        ["Dell"] = "#0076CE",             // Dell Blue
        ["HP"] = "#0096D6",               // HP Blue
        ["Lenovo"] = "#E2231A",           // Lenovo Red
        ["Acer"] = "#83B81A",             // Acer Green
        ["Apple"] = "#000000",            // Apple Black
        ["Microsoft"] = "#F35325",        // Microsoft Red/Orange
        ["Sony"] = "#000000",             // Sony Black
        ["Fujitsu"] = "#E60012",          // Fujitsu Red
        ["HUAWEI"] = "#CF0A2C",           // Huawei Red
        ["Razer"] = "#44D62C",            // Razer Green
        ["TP-Link"] = "#4AC7A3",          // TP-Link Teal
        ["Killer"] = "#E50000",           // Killer Gaming Red
        ["Sunwoda"] = "#005691",          // Sunwoda Blue
        ["Simplo"] = "#009087",           // Simplo Teal
        ["Dynapack"] = "#E2001A",         // Dynapack Red
        ["BYD"] = "#ED1C24",              // BYD Red
        ["LG Energy"] = "#E6007E",        // LG Pink
        ["Panasonic"] = "#004085",        // Panasonic Blue
    };

    // Category brand lists
    // Each entry lists the brand keys (including aliases) that belong to
    // that category. The order within a list determines priority when the
    // detection code does a first-match scroll (RAM, Disk, NIC, SystemOEM).
    private static readonly Dictionary<string, string[]> _category = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cpu"] = ["Intel", "AMD"],
        ["gpu"] = ["NVIDIA", "AMD", "Intel"],
        ["ram"] =
        [
            "Samsung", "Corsair", "G Skill", "G.Skill", "GSkill",
            "Patriot", "PNY", "Ballistix", "Kingston", "Crucial",
            "Micron", "SK Hynix", "SKHYNIX", "Hynix",
            "ADATA", "Team", "Team Group", "Nanya", "Elpida",
            "Ramaxel",
        ],
        ["disk"] =
        [
            "Samsung", "Western Digital", "WD", "WDC", "HGST",
            "Seagate", "Crucial", "Kingston", "ADATA",
            "Kioxia", "Toshiba", "LITEON", "SanDisk", "PNY",
            "Micron", "Intel", "Corsair",
            "SK Hynix", "SKHYNIX",
        ],
        ["nic"] =
        [
            "Intel", "Realtek", "Qualcomm", "Atheros", "Broadcom",
            "Mellanox", "MediaTek", "Ralink", "TP-Link",
            "ASIX", "Marvell", "Killer",
        ],
        ["oem"] =
        [
            "Lenovo", "Dell", "HP", "Hewlett", "MSI", "ASUS", "Acer",
            "Microsoft", "Gigabyte", "Samsung", "HUAWEI",
            "Toshiba", "Razer", "Fujitsu", "Sony", "Apple",
        ],
        ["battery"] =
        [
            "Sunwoda", "Simplo", "Dynapack", "BYD", "LG Energy", "Panasonic",
        ],
    };

    // Brand alias resolution
    // Variant names (spelling / spacing / abbrev.) → canonical brand key.
    private static readonly Dictionary<string, string> _alias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["G.Skill"] = "G Skill",
        ["GSkill"] = "G Skill",
        ["SKHYNIX"] = "SK Hynix",
        ["Hynix"] = "SK Hynix",
        ["Team Group"] = "Team",
        ["WD"] = "Western Digital",
        ["WDC"] = "Western Digital",
        ["Hewlett"] = "HP",
    };

    // Auto-built category dictionaries
    /// <summary>CPU vendor → brand color map.</summary>
    public static readonly Dictionary<string, string> CpuColors = BuildCategoryDict("cpu");

    /// <summary>GPU vendor → brand color map.</summary>
    public static readonly Dictionary<string, string> GpuColors = BuildCategoryDict("gpu");

    /// <summary>RAM manufacturer → brand color map.</summary>
    public static readonly Dictionary<string, string> RamColors = BuildCategoryDict("ram");

    /// <summary>Disk manufacturer → brand color map.</summary>
    public static readonly Dictionary<string, string> DiskColors = BuildCategoryDict("disk");

    /// <summary>NIC vendor → brand color map.</summary>
    public static readonly Dictionary<string, string> NicColors = BuildCategoryDict("nic");

    /// <summary>System OEM → brand color map (used for battery metrics).</summary>
    public static readonly Dictionary<string, string> SystemOemColors = BuildCategoryDict("oem");

    /// <summary>Battery manufacturer → brand color map.</summary>
    public static readonly Dictionary<string, string> BatteryColors = BuildCategoryDict("battery");

    private static Dictionary<string, string> BuildCategoryDict(string category)
    {
        var keys = _category[category];
        var d = new Dictionary<string, string>(keys.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
            d[key] = _brand.TryGetValue(key, out var color) ? color : _brand[_alias[key]];
        return d;
    }

    // Detection helpers (delegated to SystemInfo)
    /// <summary>Detects the CPU brand color from the installed processor.</summary>
    [SupportedOSPlatform("windows")]
    public static string DetectCpuColor() => SystemInfo.GetCpuColor();

    /// <summary>Detects the GPU brand color from the installed graphics adapter.</summary>
    [SupportedOSPlatform("windows")]
    public static string DetectGpuColor() => SystemInfo.GetGpuColor();

    /// <summary>Detects the RAM brand color from installed memory modules.</summary>
    [SupportedOSPlatform("windows")]
    public static string DetectRamColor() => SystemInfo.GetRamColor();

    /// <summary>Detects the primary disk brand color.</summary>
    [SupportedOSPlatform("windows")]
    public static string DetectDiskColor() => SystemInfo.GetDiskColor();

    /// <summary>Resolves a brand color for a disk from its vendor/model strings (empty if unknown).</summary>
    public static string ResolveDiskColor(string vendor, string model)
    {
        if (!string.IsNullOrEmpty(vendor))
        {
            foreach (var kvp in DiskColors)
            {
                if (vendor.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
        }
        if (!string.IsNullOrEmpty(model))
        {
            foreach (var kvp in DiskColors)
            {
                if (model.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
        }
        return string.Empty;
    }

    /// <summary>Detects the active NIC brand color.</summary>
    [SupportedOSPlatform("windows")]
    public static string DetectNicColor() => SystemInfo.GetNicColor();

    /// <summary>Detects the system OEM brand color.</summary>
    [SupportedOSPlatform("windows")]
    public static string DetectSystemOemColor() => SystemInfo.GetSystemOemColor();

    /// <summary>Detects the battery brand color from the physical battery manufacturer.</summary>
    [SupportedOSPlatform("windows")]
    public static string DetectBatteryColor()
    {
        var mfr = SystemInfo.GetBatteryManufacturer();
        if (string.IsNullOrEmpty(mfr)) return string.Empty;
        foreach (var kvp in BatteryColors)
        {
            if (mfr.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }
        return string.Empty;
    }

    // Build default metric colors
    /// <summary>Builds a full dictionary of metric ID → detected brand color, with fallbacks.</summary>
    [SupportedOSPlatform("windows")]
    public static Dictionary<string, string> BuildDefaultMetricColors()
    {
        var cpuVendor = SystemInfo.GetCpuVendor();
        var cpuColor = DetectCpuColor();
        if (cpuColor.Length > 0)
            Log.Info($"BrandDetection: CPU manufacturer=\"{cpuVendor}\" → color={cpuColor}");
        else
            Log.Info($"BrandDetection: CPU manufacturer=\"{cpuVendor}\" (not in CpuColors) → using fallback #00E5FF");

        var gpuVendor = SystemInfo.GetGpuVendor();
        var gpuColor = DetectGpuColor();
        if (gpuColor.Length > 0)
            Log.Info($"BrandDetection: GPU manufacturer=\"{gpuVendor}\" → color={gpuColor}");
        else
            Log.Info($"BrandDetection: GPU → no GPU detected → using fallback #88CCFF");

        var ramMfr = SystemInfo.GetRamManufacturer();
        var ramColor = DetectRamColor();
        if (ramColor.Length > 0)
            Log.Info($"BrandDetection: RAM manufacturer=\"{ramMfr}\" → color={ramColor}");
        else
            Log.Info($"BrandDetection: RAM manufacturer=\"{ramMfr}\" (not in RamColors) → using fallback #A78BFA");

        var diskMfr = SystemInfo.GetDiskManufacturer();
        var diskColor = DetectDiskColor();
        if (diskColor.Length > 0)
            Log.Info($"BrandDetection: DISK manufacturer=\"{diskMfr}\" → color={diskColor}");
        else
            Log.Info($"BrandDetection: DISK manufacturer=\"{diskMfr}\" (not in DiskColors) → using fallback #AAAAAA");

        var nics = SystemInfo.GetNics();
        var nicMfr = nics.Length > 0 ? nics[0].Manufacturer : "?";
        var nicColor = DetectNicColor();
        if (nicColor.Length > 0)
            Log.Info($"BrandDetection: NIC manufacturer=\"{nicMfr}\" → color={nicColor}");
        else
            Log.Info($"BrandDetection: NIC manufacturer=\"{nicMfr}\" (not in NicColors) → using fallback");

        var batMfr = SystemInfo.GetBatteryManufacturer();
        var batColor = DetectBatteryColor();
        if (batColor.Length > 0)
            Log.Info($"BrandDetection: BATTERY manufacturer=\"{batMfr}\" → color={batColor}");
        else
            Log.Info($"BrandDetection: BATTERY manufacturer=\"{batMfr}\" (not in BatteryColors) → using fallback #80E080");

        var result = new Dictionary<string, string>
        {
            [Metrics.CpuPct] = cpuColor.Length > 0 ? cpuColor : "#00E5FF",
            [Metrics.CpuTemp] = cpuColor.Length > 0 ? cpuColor : "#00E5FF",
            [Metrics.CpuFreq] = cpuColor.Length > 0 ? cpuColor : "#00E5FF",
            [Metrics.CpuPower] = cpuColor.Length > 0 ? cpuColor : "#00E5FF",
            [Metrics.RamPct] = ramColor.Length > 0 ? ramColor : "#A78BFA",
            [Metrics.RamUsed] = ramColor.Length > 0 ? ramColor : "#A78BFA",
            [Metrics.GpuPct] = gpuColor.Length > 0 ? gpuColor : "#88CCFF",
            [Metrics.GpuTemp] = gpuColor.Length > 0 ? gpuColor : "#88CCFF",
            [Metrics.GpuVram] = gpuColor.Length > 0 ? gpuColor : "#88CCFF",
            [Metrics.GpuFreq] = gpuColor.Length > 0 ? gpuColor : "#88CCFF",
            [Metrics.GpuPower] = gpuColor.Length > 0 ? gpuColor : "#88CCFF",
            [Metrics.NetDown] = nicColor.Length > 0 ? nicColor : "#38BDF8",
            [Metrics.NetUp] = nicColor.Length > 0 ? nicColor : "#4ADE80",
            [Metrics.NetTotal] = nicColor.Length > 0 ? nicColor : "#FBBF24",
            [Metrics.BatteryPct] = batColor.Length > 0 ? batColor : "#80E080",
            [Metrics.BatteryRate] = batColor.Length > 0 ? batColor : "#80E080",
        };
        var disks = SystemInfo.GetAllDisks();
        bool multiDisk = disks.Count > 1;
        for (int i = 0; i < disks.Count; i++)
        {
            var disk = disks[i];
            string perDiskColor = ResolveDiskColor(disk.Vendor, disk.Model);
            string label = multiDisk ? $"DISK {i + 1}" : "DISK";
            string mfr = string.IsNullOrEmpty(disk.Vendor) ? disk.Model : disk.Vendor;
            if (perDiskColor.Length > 0)
                Log.Info($"BrandDetection: {label} manufacturer=\"{mfr}\" → color={perDiskColor}");
            else
                Log.Info($"BrandDetection: {label} manufacturer=\"{mfr}\" (not in DiskColors) → using fallback #AAAAAA");
            string fallback = perDiskColor.Length > 0 ? perDiskColor : "#AAAAAA";
            result[$"disk_disk{i}_read"] = fallback;
            result[$"disk_disk{i}_write"] = fallback;
        }
        return result;
    }

    /// <summary>
    /// Builds the default group color map (keys: <c>cpu</c>, <c>gpu</c>, <c>ram</c>,
    /// <c>network</c>, <c>battery</c>, <c>disk</c>) from the same detection results used by
    /// <see cref="BuildDefaultMetricColors"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static Dictionary<string, string> BuildDefaultGroupColors()
        => BuildDefaultGroupColors(BuildDefaultMetricColors());

    /// <summary>
    /// Builds the default group color map from an existing metric default color dictionary
    /// (no hardware detection; reuse the results of <see cref="BuildDefaultMetricColors"/>).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static Dictionary<string, string> BuildDefaultGroupColors(Dictionary<string, string> metricDefaults)
    {
        var diskColor = metricDefaults.FirstOrDefault(kvp => kvp.Key.StartsWith("disk_", StringComparison.Ordinal)).Value;
        return new Dictionary<string, string>
        {
            ["cpu"] = metricDefaults[Metrics.CpuPct],
            ["gpu"] = metricDefaults[Metrics.GpuPct],
            ["ram"] = metricDefaults[Metrics.RamPct],
            ["network"] = metricDefaults[Metrics.NetDown],
            ["battery"] = metricDefaults[Metrics.BatteryPct],
            ["disk"] = diskColor.Length > 0 ? diskColor : "#AAAAAA",
        };
    }
}
