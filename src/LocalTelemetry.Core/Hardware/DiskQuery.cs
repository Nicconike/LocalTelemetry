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
    /// <summary>Total capacity in bytes or null if unavailable.</summary>
    public long? SizeBytes { get; init; }
    /// <summary>Physical drive index (0-based).</summary>
    public int DiskIndex { get; init; }
    /// <summary>Whether this drive contains the boot/system partition.</summary>
    public bool IsBootDrive { get; init; }
}

/// <summary>Queries physical disk information via WMI and IOCTL.</summary>
[SupportedOSPlatform("windows")]
public static class DiskQuery
{
    private static void Log(string msg)
    {
        Diagnostics.Log.Info(msg);
    }

    private const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

    private static readonly Lazy<Dictionary<int, string>> _busTypeIoctlCache = new(() => []);

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

        var query = new STORAGE_PROPERTY_QUERY
        {
            PropertyId = 1, // StorageAdapterProperty
            QueryType = 0,  // PropertyStandardQuery
            AdditionalParameters = [0]
        };
        byte[] queryBytes = StructToBytes(query);

        var desc = new STORAGE_ADAPTER_DESCRIPTOR();
        byte[] descBytes = new byte[Marshal.SizeOf<STORAGE_ADAPTER_DESCRIPTOR>()];

        if (NativeMethods.DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY,
                queryBytes, (uint)queryBytes.Length,
                descBytes, (uint)descBytes.Length, out uint _, IntPtr.Zero))
        {
            try
            {
                desc = BytesToStruct<STORAGE_ADAPTER_DESCRIPTOR>(descBytes);
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Error($"DiskQuery: BytesToStruct failed for disk {diskIndex}: {ex.Message}");
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
        ("SK Hynix", "SK Hynix"),
        ("SKHynix", "SK Hynix"),
        ("HFS", "SK Hynix"),
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

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Index, Model, InterfaceType, PNPDeviceID, Size FROM Win32_DiskDrive");
            using var coll = searcher.Get();

            foreach (ManagementBaseObject mo in coll)
            {
                int idx = Convert.ToInt32(mo["Index"]);
                string model = mo["Model"]?.ToString()?.Trim() ?? "";
                string iface = mo["InterfaceType"]?.ToString()?.Trim() ?? "";
                string pnpId = mo["PNPDeviceID"]?.ToString() ?? "";
                ulong? size = mo["Size"] as ulong?;

                // 1) Authoritative IOCTL
                string busType = QueryBusTypeIoctl(idx);

                // 2) PNPDeviceID keyword match
                if (string.IsNullOrEmpty(busType))
                    busType = BusTypeFromPnpId(pnpId);

                // 3) WMI InterfaceType (least reliable)
                if (string.IsNullOrEmpty(busType))
                {
                    busType = iface switch
                    {
                        "SCSI" => "SSD",
                        "IDE" => "SATA",
                        _ => iface.Length > 0 ? iface : "DISK",
                    };
                }

                bool isBoot = bootIdx.HasValue && bootIdx.Value == idx;
                string vendor = ExtractVendorFromModel(model);

                results.Add(new DiskInfo
                {
                    DiskIndex = idx,
                    Model = model,
                    Vendor = vendor,
                    BusType = busType,
                    SizeBytes = (long?)size,
                    IsBootDrive = isBoot,
                });
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error($"DiskQuery: WMI Win32_DiskDrive failed: {ex.Message}"); }

        return results.OrderBy(d => d.DiskIndex).ToList();
    }

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
    private struct STORAGE_PROPERTY_QUERY
    {
        public uint PropertyId;
        public uint QueryType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public byte[] AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_ADAPTER_DESCRIPTOR
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
                Diagnostics.Log.Error("GetBootDriveIndex CreateFile failed");
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
                Diagnostics.Log.Error("GetBootDriveIndex IOCTL failed");
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error($"GetBootDriveIndex exception: {ex.Message}"); }

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
        catch (Exception ex) { Diagnostics.Log.Error($"DiskQuery: GetBootDriveIndex WMI exception: {ex.Message}"); }
        return null;
    }
}
