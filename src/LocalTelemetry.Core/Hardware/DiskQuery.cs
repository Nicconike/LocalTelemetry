using System.Collections.Concurrent;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LocalTelemetry.Core.Hardware;

/// <summary>Information about a single physical disk drive.</summary>
public record DiskInfo
{
    /// <summary>Drive model name from WMI.</summary>
    public string Model { get; init; } = string.Empty;
    /// <summary>Vendor name, if detectable.</summary>
    public string Vendor { get; init; } = string.Empty;
    /// <summary>Bus type label ("NVMe", "SATA", "SSD", "HDD", "USB", etc.).</summary>
    public string BusType { get; init; } = "DISK";
    /// <summary>Media type label ("SSD", "HDD", etc.).</summary>
    public string MediaType { get; init; } = string.Empty;
    /// <summary>Display-friendly type string combining bus and media (e.g. "NVMe SSD", "SATA HDD").</summary>
    public string DisplayType { get; init; } = string.Empty;
    /// <summary>Total capacity in bytes or null if unavailable.</summary>
    public long? SizeBytes { get; init; }
    /// <summary>Physical drive index (0-based).</summary>
    public int DiskIndex { get; init; }
    /// <summary>Whether this drive contains the boot/system partition.</summary>
    public bool IsBootDrive { get; init; }
    /// <summary>Display-ready commercial capacity string (e.g. "1 TB", "512 GB").</summary>
    public string FormattedSize => SizeBytes.HasValue ? DiskQuery.FormatCommercialCapacity(SizeBytes.Value) : string.Empty;
}

/// <summary>Queries physical disk information via WMI and IOCTL.</summary>
[SupportedOSPlatform("windows")]
public static class DiskQuery
{
    private const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

    private static readonly Lazy<ConcurrentDictionary<int, string>> _busTypeIoctlCache = new(() => new());

    private static List<DiskInfo>? _cachedDrives;

    /// <summary>Returns all physical drives (cached after first call).</summary>
    public static List<DiskInfo> QueryAllDrives()
    {
        if (_cachedDrives is not null) return _cachedDrives;
        _cachedDrives = EnumerateAllDrives();
        return _cachedDrives;
    }

    /// <summary>Invalidates the drive cache and re-queries from WMI.</summary>
    public static void RefreshDiskCache()
    {
        _cachedDrives = null;
        QueryAllDrives();
    }

    /// <summary>Returns the first physical drive (usually the boot drive) or null.</summary>
    public static DiskInfo? QueryPhysicalDrive0()
    {
        return QueryAllDrives().FirstOrDefault();
    }

    /// <summary>
    /// Formats raw byte count into standard commercial drive capacity notation
    /// (e.g. 500107862016 bytes -> "500 GB", 1000204886016 bytes -> "1 TB").
    /// </summary>
    public static string FormatCommercialCapacity(long bytes)
    {
        if (bytes <= 0) return string.Empty;

        double gb = bytes / 1_000_000_000.0;

        // Check common commercial GB stepping by closest distance.
        // IDEMA drive capacities are within 0.1% of nominal rating; 2.5% tolerance safely
        // distinguishes close steps (e.g. 120 vs 128, 240 vs 250 vs 256, 480 vs 500 vs 512).
        int[] commonSteps = [16, 32, 64, 120, 128, 240, 250, 256, 480, 500, 512, 960];
        var bestStep = commonSteps
            .Select(step => (Step: step, Diff: Math.Abs(gb - step)))
            .Where(x => x.Diff <= x.Step * 0.025)
            .OrderBy(x => x.Diff)
            .Select(x => (int?)x.Step)
            .FirstOrDefault();

        if (bestStep.HasValue)
        {
            return $"{bestStep.Value} GB";
        }

        if (gb >= 980.0)
        {
            double tb = gb / 1000.0;

            // Common consumer, NAS/enterprise HDD & SSD stepping in TB
            double[] commonTbSteps =
            [
                1.0, 1.5, 1.92, 2.0, 3.0, 3.84, 4.0, 5.0, 6.0, 7.68, 8.0, 10.0,
                12.0, 14.0, 15.36, 16.0, 18.0, 20.0, 22.0, 24.0, 26.0, 28.0, 30.0, 30.72, 32.0, 61.44
            ];

            var bestTbStep = commonTbSteps
                .Select(step => (Step: step, Diff: Math.Abs(tb - step)))
                .Where(x => x.Diff <= x.Step * 0.02)
                .OrderBy(x => x.Diff)
                .Select(x => (double?)x.Step)
                .FirstOrDefault();

            if (bestTbStep.HasValue)
            {
                double stepVal = bestTbStep.Value;
                if (Math.Abs(stepVal - Math.Round(stepVal)) < 0.001)
                    return $"{Math.Round(stepVal):F0} TB";
                return $"{stepVal} TB";
            }

            double roundedTb = Math.Round(tb);
            if (Math.Abs(tb - roundedTb) < 0.05)
                return $"{roundedTb:F0} TB";
            return $"{tb:F1} TB";
        }

        return $"{Math.Round(gb):F0} GB";
    }

    /// <summary>Queries bus type via IOCTL_STORAGE_QUERY_PROPERTY (authoritative).</summary>
    private static string QueryBusTypeIoctl(int diskIndex)
    {
        var cache = _busTypeIoctlCache.Value;
        if (cache.TryGetValue(diskIndex, out string? cached))
            return cached;

        string path = $@"\\.\PhysicalDrive{diskIndex}";
        using var handle = NativeMethods.CreateFile(path, 0,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);

        if (handle is null || handle.IsInvalid)
            return cache[diskIndex] = string.Empty;

        var query = new StoragePropertyQuery
        {
            PropertyId = 1, // StorageAdapterProperty
            QueryType = 0,  // PropertyStandardQuery
            AdditionalParameters = [0]
        };
        byte[] queryBytes = StructToBytes(query);

        StorageAdapterDescriptor desc;
        byte[] descBytes = new byte[Marshal.SizeOf<StorageAdapterDescriptor>()];

        if (NativeMethods.DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY,
                queryBytes, (uint)queryBytes.Length,
                descBytes, (uint)descBytes.Length, out uint _, IntPtr.Zero))
        {
            try
            {
                desc = BytesToStruct<StorageAdapterDescriptor>(descBytes);
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Error(ex, $"Failed to parse storage adapter descriptor for disk {diskIndex}");
                return cache[diskIndex] = string.Empty;
            }

            // 0 = BusTypeUnknown, >32 = garbage (driver returned wrong struct) - let fallback chain handle it
            if (desc.BusType == 0 || desc.BusType > 32)
                return cache[diskIndex] = string.Empty;

            string busType = desc.BusType switch
            {
                17 => "NVMe",
                11 => "SATA",
                10 => "SAS",
                7 => "USB",
                14 => "Virtual",
                3 => "ATA",
                8 => "RAID",
                4 => "1394",
                12 => "SD",
                13 => "MMC",
                19 => "UFS",
                _ => $"BusType{desc.BusType}"
            };
            return cache[diskIndex] = busType;
        }

        return cache[diskIndex] = string.Empty;
    }

    /// <summary>Best-effort bus type from PNPDeviceID keywords.</summary>
    private static string BusTypeFromPnpId(string pnpId)
    {
        if (pnpId.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
            || pnpId.Contains("NVM Express", StringComparison.OrdinalIgnoreCase)
            || pnpId.Contains("NVMEx", StringComparison.OrdinalIgnoreCase))
            return "NVMe";

        if (pnpId.Contains("SATA", StringComparison.OrdinalIgnoreCase)
            || pnpId.Contains("ATA", StringComparison.OrdinalIgnoreCase)
            || pnpId.Contains("AHCI", StringComparison.OrdinalIgnoreCase))
            return "SATA";

        if (pnpId.Contains("SAS", StringComparison.OrdinalIgnoreCase))
            return "SAS";

        if (pnpId.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase))
            return "USB";

        return "";
    }

    private const string SkHynixVendor = "SK Hynix";

    private static readonly (string Keyword, string Vendor)[] VendorPatterns =
    [
        ("Samsung", "Samsung"),
        ("WDC", "WDC"),
        ("Western Digital", "Western Digital"),
        ("WD", "WD"),
        ("HGST", "HGST"),
        ("HP", "HP"),
        ("Seagate", "Seagate"),
        ("ST", "Seagate"),
        ("Crucial", "Crucial"),
        ("CT", "Crucial"),
        ("Kingston", "Kingston"),
        ("ADATA", "ADATA"),
        ("Kioxia", "Kioxia"),
        ("KXG", "Kioxia"),
        ("Toshiba", "Toshiba"),
        ("LITEON", "LITEON"),
        ("SanDisk", "SanDisk"),
        ("Micron", "Micron"),
        ("Intel", "Intel"),
        ("PNY", "PNY"),
        ("Corsair", "Corsair"),
        ("SK Hynix", SkHynixVendor),
        ("SKHynix", SkHynixVendor),
        ("HFS", SkHynixVendor),
    ];

    /// <summary>Extracts vendor name from a disk model string by matching known patterns.</summary>
    /// <remarks>Keywords ≤3 characters use StartsWith only to avoid substring false matches (e.g. "ST").</remarks>
    private static string ExtractVendorFromModel(string model)
    {
        if (string.IsNullOrEmpty(model)) return "";
        foreach (var (keyword, vendor) in VendorPatterns)
            if (model.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) ||
                (keyword.Length > 3 && model.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                return vendor;
        return "";
    }

    private static List<DiskInfo> EnumerateAllDrives()
    {
        var results = new List<DiskInfo>();
        int? bootIdx = GetBootDriveIndex();
        var msftMediaTypes = QueryMsftMediaTypes();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Index, Model, InterfaceType, PNPDeviceID, Size, MediaType FROM Win32_DiskDrive");
            using var coll = searcher.Get();

            foreach (ManagementBaseObject mo in coll)
            {
                int idx = Convert.ToInt32(mo["Index"]);
                string model = mo["Model"]?.ToString()?.Trim() ?? "";
                string iface = mo["InterfaceType"]?.ToString()?.Trim() ?? "";
                string pnpId = mo["PNPDeviceID"]?.ToString() ?? "";
                string win32Media = mo["MediaType"]?.ToString() ?? "";
                ulong? size = mo["Size"] as ulong?;

                if (IsExcludedDrive(iface, pnpId, win32Media, idx, out string busType))
                    continue;

                string mediaType = DetermineMediaType(msftMediaTypes, idx, busType, model);
                string displayType = DetermineDisplayType(busType, mediaType);
                bool isBoot = bootIdx.HasValue && bootIdx.Value == idx;
                string vendor = ExtractVendorFromModel(model);

                results.Add(new DiskInfo
                {
                    DiskIndex = idx,
                    Model = model,
                    Vendor = vendor,
                    BusType = busType,
                    MediaType = mediaType,
                    DisplayType = displayType,
                    SizeBytes = (long?)size,
                    IsBootDrive = isBoot,
                });
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query physical drives"); }

        return results.OrderBy(d => d.DiskIndex).ToList();
    }

    private static Dictionary<string, string> QueryMsftMediaTypes()
    {
        var msftMediaTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT DeviceId, MediaType, FriendlyName FROM MSFT_PhysicalDisk");
            using var coll = searcher.Get();
            foreach (ManagementBaseObject mo in coll)
            {
                string devId = mo["DeviceId"]?.ToString() ?? "";
                int mt = mo["MediaType"] switch
                {
                    int i => i,
                    ushort u => (int)u,
                    _ => 0
                };
                string mType = mt switch
                {
                    3 => "HDD",
                    4 => "SSD",
                    5 => "SCM",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(devId) && !string.IsNullOrEmpty(mType))
                    msftMediaTypes[devId] = mType;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, "WMI query for MSFT_PhysicalDisk failed; falling back to heuristic detection");
        }
        return msftMediaTypes;
    }

    private static bool IsExcludedDrive(string iface, string pnpId, string win32Media, int idx, out string resolvedBusType)
    {
        resolvedBusType = "";
        if (iface.Equals("USB", StringComparison.OrdinalIgnoreCase) ||
            pnpId.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase) ||
            pnpId.Contains("UASP", StringComparison.OrdinalIgnoreCase) ||
            win32Media.Contains("Removable", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        resolvedBusType = QueryBusTypeIoctl(idx);
        if (resolvedBusType.Equals("USB", StringComparison.OrdinalIgnoreCase) ||
            resolvedBusType.Equals("Virtual", StringComparison.OrdinalIgnoreCase) ||
            resolvedBusType.Equals("SD", StringComparison.OrdinalIgnoreCase) ||
            resolvedBusType.Equals("MMC", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrEmpty(resolvedBusType))
            resolvedBusType = BusTypeFromPnpId(pnpId);

        if (string.IsNullOrEmpty(resolvedBusType))
        {
            resolvedBusType = iface switch
            {
                "SCSI" => "SSD",
                "IDE" => "SATA",
                _ => iface.Length > 0 ? iface : "DISK",
            };
        }

        return false;
    }

    private static string DetermineMediaType(Dictionary<string, string> msftMediaTypes, int idx, string busType, string model)
    {
        if (msftMediaTypes.TryGetValue(idx.ToString(), out string? knownMedia))
            return knownMedia;

        if (busType.Equals("NVMe", StringComparison.OrdinalIgnoreCase) ||
            model.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
            model.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
        {
            return "SSD";
        }

        if (model.Contains("HDD", StringComparison.OrdinalIgnoreCase) ||
            model.Contains("Harddisk", StringComparison.OrdinalIgnoreCase) ||
            (model.StartsWith("ST", StringComparison.OrdinalIgnoreCase) && model.Length > 4 && char.IsDigit(model[2])))
        {
            return "HDD";
        }

        return "SSD";
    }

    private static string DetermineDisplayType(string busType, string mediaType) => busType switch
    {
        "NVMe" => "NVMe SSD",
        "SATA" when mediaType == "HDD" => "SATA HDD",
        "SATA" when mediaType == "SSD" => "SATA SSD",
        _ when mediaType == "HDD" => "HDD",
        _ when mediaType == "SSD" => "SSD",
        _ => busType
    };

    // Marshal helpers
    private static byte[] StructToBytes<T>(T obj) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buf = new byte[size];
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(obj, ptr, false);
            Marshal.Copy(ptr, buf, 0, size);
            return buf;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static T BytesToStruct<T>(byte[] buf) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(buf, 0, ptr, size);
            return Marshal.PtrToStructure<T>(ptr);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    // IOCTL structs
    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public uint PropertyId;
        public uint QueryType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public byte[] AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StorageAdapterDescriptor
    {
        public uint Version;
        public uint Size;
        public uint MaximumTransferLength;
        public uint MaximumPhysicalPages;
        public uint AlignmentMask;
        public byte AdapterUsesPio;
        public byte AdapterScansDown;
        public byte CommandQueueing;
        public byte AccelatedTransfer;
        public uint BusType;
        public uint BusMajorVersion;
        public uint BusMinorVersion;
        public uint SrbType;
        public uint AddressType;
    }

    private static int? GetBootDriveIndex()
    {
        try
        {
            string? systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrEmpty(systemDrive)) return null;
            string volumePath = @"\\.\" + systemDrive.TrimEnd('\\');
            using var hVolume = NativeMethods.CreateFile(volumePath,
                0, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
            if (hVolume is null || hVolume.IsInvalid)
            {
                Diagnostics.Log.Error("Could not open system volume to determine boot drive index");
                return null;
            }

            byte[] extents = new byte[32];
            if (NativeMethods.DeviceIoControl(hVolume, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS,
                    null, 0, extents, 32, out uint ret, IntPtr.Zero) && ret >= 12)
            {
                int diskNumber = BitConverter.ToInt32(extents, 8);
                return diskNumber;
            }
            else
            {
                Diagnostics.Log.Error("Failed to query volume disk extents via IOCTL");
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to determine boot drive index via volume extents"); }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DiskIndex FROM Win32_DiskPartition WHERE Bootable = TRUE");
            using var results = searcher.Get();
            foreach (ManagementBaseObject mo in results)
            {
                var di = mo["DiskIndex"];
                if (di is int idx) return idx;
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to determine boot drive index via WMI disk partition"); }
        return null;
    }
}
