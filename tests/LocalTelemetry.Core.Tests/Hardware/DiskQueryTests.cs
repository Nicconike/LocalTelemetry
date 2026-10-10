using System.Reflection;
using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class DiskQueryTests
{
    [Fact]
    public void DiskQuery_QueryAllDrives_ExecutesAndCaches()
    {
        var drives = DiskQuery.QueryAllDrives();
        drives.Should().NotBeNull();

        var drivesCached = DiskQuery.QueryAllDrives();
        drivesCached.Should().BeEquivalentTo(drives);

        DiskQuery.RefreshDiskCache();
        var drivesRefreshed = DiskQuery.QueryAllDrives();
        drivesRefreshed.Should().NotBeNull();

        var drive0 = DiskQuery.QueryPhysicalDrive0();
        if (drivesRefreshed.Count > 0)
        {
            drive0.Should().NotBeNull();
            drive0.Should().BeEquivalentTo(drivesRefreshed[0]);
        }
    }

    [Fact]
    public void DiskQuery_BusTypeFromPnpId_ParsesKeywordsCorrectly()
    {
        var method = typeof(DiskQuery).GetMethod("BusTypeFromPnpId", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        method!.Invoke(null, ["SCSI\\DISK&VEN_NVME&PROD_SAMSUNG_SSD_980_PRO"]).Should().Be("NVMe");
        method.Invoke(null, ["IDE\\DISKSAMSUNG_SSD_870_EVO\\SATA"]).Should().Be("SATA");
        method.Invoke(null, ["USBSTOR\\DISK&VEN_SANDISK&PROD_CRUZER"]).Should().Be("USB");
        method.Invoke(null, ["SCSI\\DISK&VEN_HP&PROD_SAS_DRIVE"]).Should().Be("SAS");
        method.Invoke(null, ["UNKNOWN_BUS_ID"]).Should().Be("");
    }

    [Fact]
    public void DiskQuery_ExtractVendorFromModel_ParsesVendorsCorrectly()
    {
        var method = typeof(DiskQuery).GetMethod("ExtractVendorFromModel", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        method!.Invoke(null, ["Samsung SSD 980 PRO 1TB"]).Should().Be("Samsung");
        method.Invoke(null, ["WDC WD10EZEX-00BN5A0"]).Should().Be("WDC");
        method.Invoke(null, ["Western Digital WD_BLACK 1TB"]).Should().Be("Western Digital");
        method.Invoke(null, ["WD Blue 1TB"]).Should().Be("WD");
        method.Invoke(null, ["HGST Ultrastar 7K4000"]).Should().Be("HGST");
        method.Invoke(null, ["HP EG0300FCVBF"]).Should().Be("HP");
        method.Invoke(null, ["Seagate BarraCuda 2TB"]).Should().Be("Seagate");
        method.Invoke(null, ["ST1000DM010-2EP102"]).Should().Be("Seagate");
        method.Invoke(null, ["Crucial MX500 1TB"]).Should().Be("Crucial");
        method.Invoke(null, ["CT1000P3SSD8"]).Should().Be("Crucial");
        method.Invoke(null, ["Kingston A400 480GB"]).Should().Be("Kingston");
        method.Invoke(null, ["ADATA SU800 512GB"]).Should().Be("ADATA");
        method.Invoke(null, ["Kioxia Exceria G2"]).Should().Be("Kioxia");
        method.Invoke(null, ["KXG60ZNV512G KIOXIA"]).Should().Be("Kioxia");
        method.Invoke(null, ["Toshiba DT01ACA100"]).Should().Be("Toshiba");
        method.Invoke(null, ["LITEON CV8-8CE128"]).Should().Be("LITEON");
        method.Invoke(null, ["SanDisk Ultra 3D"]).Should().Be("SanDisk");
        method.Invoke(null, ["Micron 2450 NVMe"]).Should().Be("Micron");
        method.Invoke(null, ["Intel 660p SSD"]).Should().Be("Intel");
        method.Invoke(null, ["PNY CS900 240GB"]).Should().Be("PNY");
        method.Invoke(null, ["Corsair MP600 PRO"]).Should().Be("Corsair");
        method.Invoke(null, ["SK Hynix Gold P31"]).Should().Be("SK Hynix");
        method.Invoke(null, ["SKHynix BC711"]).Should().Be("SK Hynix");
        method.Invoke(null, ["HFS512G39MND-3510A"]).Should().Be("SK Hynix");
        method.Invoke(null, ["Generic Unknown Drive"]).Should().Be("");
        method.Invoke(null, [""]).Should().Be("");
    }

    [Fact]
    public void DiskQuery_QueryMsftMediaTypes_ExecutesSafely()
    {
        var method = typeof(DiskQuery).GetMethod("QueryMsftMediaTypes", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = (Dictionary<string, string>?)method!.Invoke(null, null);
        result.Should().NotBeNull();
    }

    [Fact]
    public void DiskQuery_QueryBusTypeIoctl_InvalidDisk_ReturnsEmptyAndCaches()
    {
        var method = typeof(DiskQuery).GetMethod("QueryBusTypeIoctl", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        method!.Invoke(null, [999999]).Should().Be("");
        method.Invoke(null, [999999]).Should().Be("");
    }

    [Fact]
    public void DiskQuery_FormatCommercialCapacity_CorrectlyIdentifiesCommercialStepping()
    {
        // 0 or negative
        DiskQuery.FormatCommercialCapacity(0).Should().BeEmpty();
        DiskQuery.FormatCommercialCapacity(-100).Should().BeEmpty();

        // 120 GB vs 128 GB
        DiskQuery.FormatCommercialCapacity(120_034_123_776L).Should().Be("120 GB");
        DiskQuery.FormatCommercialCapacity(128_035_676_160L).Should().Be("128 GB");

        // 240 GB vs 250 GB vs 256 GB
        DiskQuery.FormatCommercialCapacity(240_057_409_536L).Should().Be("240 GB");
        DiskQuery.FormatCommercialCapacity(250_059_350_016L).Should().Be("250 GB");
        DiskQuery.FormatCommercialCapacity(256_060_514_304L).Should().Be("256 GB");

        // 480 GB vs 500 GB vs 512 GB
        DiskQuery.FormatCommercialCapacity(480_103_981_056L).Should().Be("480 GB");
        DiskQuery.FormatCommercialCapacity(500_107_862_016L).Should().Be("500 GB");
        DiskQuery.FormatCommercialCapacity(512_110_190_592L).Should().Be("512 GB");

        // 960 GB SSD
        DiskQuery.FormatCommercialCapacity(960_197_124_096L).Should().Be("960 GB");

        // 1 TB and 2 TB
        DiskQuery.FormatCommercialCapacity(1_000_204_886_016L).Should().Be("1 TB");
        DiskQuery.FormatCommercialCapacity(1_024_203_640_320L).Should().Be("1 TB");
        DiskQuery.FormatCommercialCapacity(2_000_398_934_016L).Should().Be("2 TB");

        // Larger TB sizes (3, 4, 8, 16, 20 TB)
        DiskQuery.FormatCommercialCapacity(3_000_592_982_016L).Should().Be("3 TB");
        DiskQuery.FormatCommercialCapacity(4_000_787_030_016L).Should().Be("4 TB");
        DiskQuery.FormatCommercialCapacity(8_001_563_222_016L).Should().Be("8 TB");
        DiskQuery.FormatCommercialCapacity(16_000_900_000_000L).Should().Be("16 TB");
        DiskQuery.FormatCommercialCapacity(20_000_000_000_000L).Should().Be("20 TB");

        // Enterprise fractional SSD steps (1.92, 3.84, 7.68, 15.36 TB)
        DiskQuery.FormatCommercialCapacity(1_920_000_000_000L).Should().Be("1.92 TB");
        DiskQuery.FormatCommercialCapacity(3_840_000_000_000L).Should().Be("3.84 TB");
        DiskQuery.FormatCommercialCapacity(7_680_000_000_000L).Should().Be("7.68 TB");
        DiskQuery.FormatCommercialCapacity(15_360_000_000_000L).Should().Be("15.36 TB");

        // Fallback / unlisted sizes
        DiskQuery.FormatCommercialCapacity(300_000_000_000L).Should().Be("300 GB");
        DiskQuery.FormatCommercialCapacity(1_500_000_000_000L).Should().Be("1.5 TB");
        DiskQuery.FormatCommercialCapacity(36_000_000_000_000L).Should().Be("36 TB");
    }

    [Fact]
    public void DiskQuery_DetermineDisplayType_And_DetermineMediaType_Coverage()
    {
        var displayMethod = typeof(DiskQuery).GetMethod("DetermineDisplayType", BindingFlags.NonPublic | BindingFlags.Static);
        var mediaMethod = typeof(DiskQuery).GetMethod("DetermineMediaType", BindingFlags.NonPublic | BindingFlags.Static);
        var excludedMethod = typeof(DiskQuery).GetMethod("IsExcludedDrive", BindingFlags.NonPublic | BindingFlags.Static);

        displayMethod.Should().NotBeNull();
        mediaMethod.Should().NotBeNull();
        excludedMethod.Should().NotBeNull();

        // DetermineDisplayType branches
        ((string)displayMethod!.Invoke(null, ["NVMe", "SSD"])!).Should().Be("NVMe SSD");
        ((string)displayMethod.Invoke(null, ["SATA", "HDD"])!).Should().Be("SATA HDD");
        ((string)displayMethod.Invoke(null, ["SATA", "SSD"])!).Should().Be("SATA SSD");
        ((string)displayMethod.Invoke(null, ["SCSI", "HDD"])!).Should().Be("HDD");
        ((string)displayMethod.Invoke(null, ["SCSI", "SSD"])!).Should().Be("SSD");
        ((string)displayMethod.Invoke(null, ["CUSTOM", "UNKNOWN"])!).Should().Be("CUSTOM");

        // DetermineMediaType branches
        var msftDict = new Dictionary<string, string> { ["0"] = "HDD" };
        ((string)mediaMethod!.Invoke(null, [msftDict, 0, "SATA", "Model"])!).Should().Be("HDD");
        ((string)mediaMethod.Invoke(null, [msftDict, 1, "NVMe", "Model"])!).Should().Be("SSD");
        ((string)mediaMethod.Invoke(null, [msftDict, 1, "SATA", "Samsung SSD 870"])!).Should().Be("SSD");
        ((string)mediaMethod.Invoke(null, [msftDict, 1, "SATA", "WDC WD10EZEX HDD"])!).Should().Be("HDD");
        ((string)mediaMethod.Invoke(null, [msftDict, 1, "SATA", "ST2000DM008"])!).Should().Be("HDD");
        ((string)mediaMethod.Invoke(null, [msftDict, 1, "SATA", "Generic Harddisk"])!).Should().Be("HDD");
        ((string)mediaMethod.Invoke(null, [msftDict, 1, "SATA", "Generic Drive"])!).Should().Be("SSD");

        // IsExcludedDrive branches
        object[] argsUsb = ["USB", "", "", 0, ""];
        ((bool)excludedMethod!.Invoke(null, argsUsb)!).Should().BeTrue();

        object[] argsUsbStor = ["SCSI", "USBSTOR\\DISK", "", 0, ""];
        ((bool)excludedMethod.Invoke(null, argsUsbStor)!).Should().BeTrue();

        object[] argsRemovable = ["SCSI", "", "Removable Media", 0, ""];
        ((bool)excludedMethod.Invoke(null, argsRemovable)!).Should().BeTrue();

        // DiskInfo properties & FormattedSize
        var diskInfo = new DiskInfo
        {
            Model = "Samsung 980",
            Vendor = "Samsung",
            BusType = "NVMe",
            MediaType = "SSD",
            DisplayType = "NVMe SSD",
            SizeBytes = null,
            DiskIndex = 0,
            IsBootDrive = true
        };
        diskInfo.FormattedSize.Should().BeEmpty();

        var diskInfoWithSize = diskInfo with { SizeBytes = 1_000_000_000_000L };
        diskInfoWithSize.FormattedSize.Should().Be("1 TB");
    }

    [Fact]
    public void DiskQuery_IsExcludedDrive_And_Cache_Branches()
    {
        var excludedMethod = typeof(DiskQuery).GetMethod("IsExcludedDrive", BindingFlags.NonPublic | BindingFlags.Static);
        excludedMethod.Should().NotBeNull();

        var cacheField = typeof(DiskQuery).GetField("_busTypeIoctlCache", BindingFlags.NonPublic | BindingFlags.Static);
        cacheField.Should().NotBeNull();
        var cache = (Lazy<Dictionary<int, string>>)cacheField!.GetValue(null)!;
        var dict = cache.Value;

        // Virtual, SD, MMC, USB in ioctl cache
        dict[101] = "Virtual";
        object[] argsVirtual = ["", "PCI\\VEN_1234", "", 101, ""];
        ((bool)excludedMethod!.Invoke(null, argsVirtual)!).Should().BeTrue();
        argsVirtual[4].Should().Be("Virtual");

        dict[102] = "SD";
        object[] argsSd = ["", "PCI\\VEN_1234", "", 102, ""];
        ((bool)excludedMethod.Invoke(null, argsSd)!).Should().BeTrue();

        dict[103] = "MMC";
        object[] argsMmc = ["", "PCI\\VEN_1234", "", 103, ""];
        ((bool)excludedMethod.Invoke(null, argsMmc)!).Should().BeTrue();

        dict[104] = "USB";
        object[] argsIoctlUsb = ["", "PCI\\VEN_1234", "", 104, ""];
        ((bool)excludedMethod.Invoke(null, argsIoctlUsb)!).Should().BeTrue();

        // Fallback to iface switch when bus type is empty
        dict[105] = "";
        object[] argsScsi = ["SCSI", "PCI\\VEN_1234", "", 105, ""];
        ((bool)excludedMethod.Invoke(null, argsScsi)!).Should().BeFalse();
        argsScsi[4].Should().Be("SSD");

        object[] argsIde = ["IDE", "PCI\\VEN_1234", "", 105, ""];
        ((bool)excludedMethod.Invoke(null, argsIde)!).Should().BeFalse();
        argsIde[4].Should().Be("SATA");

        object[] argsEmpty = ["", "PCI\\VEN_1234", "", 105, ""];
        ((bool)excludedMethod.Invoke(null, argsEmpty)!).Should().BeFalse();
        argsEmpty[4].Should().Be("DISK");

        object[] argsCustom = ["CUSTOM_BUS", "PCI\\VEN_1234", "", 105, ""];
        ((bool)excludedMethod.Invoke(null, argsCustom)!).Should().BeFalse();
        argsCustom[4].Should().Be("CUSTOM_BUS");

        // Public API calls
        DiskQuery.RefreshDiskCache();
        var drive0 = DiskQuery.QueryPhysicalDrive0();
        // May be null or not depending on host, but call executes cleanly
    }

    [Fact]
    public void DiskQuery_BusTypeFromPnpId_FullKeywordCoverage()
    {
        var method = typeof(DiskQuery).GetMethod("BusTypeFromPnpId", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        ((string)method!.Invoke(null, ["SCSI\\DISK&VEN_NVME_EX"])!).Should().Be("NVMe");
        ((string)method.Invoke(null, ["PCI\\VEN_1234_SATA_DRIVE"])!).Should().Be("SATA");
        ((string)method.Invoke(null, ["PCI\\VEN_1234_ATA_DRIVE"])!).Should().Be("SATA");
        ((string)method.Invoke(null, ["PCI\\VEN_1234_AHCI_DRIVE"])!).Should().Be("SATA");
        ((string)method.Invoke(null, ["USBSTOR\\GENERIC_FLASH"])!).Should().Be("USB");
        ((string)method.Invoke(null, ["SAS\\DISK_CONTROLLER"])!).Should().Be("SAS");
        ((string)method.Invoke(null, ["IDE\\UNKNOWN_DRIVE"])!).Should().BeEmpty();
        ((string)method.Invoke(null, [""])!).Should().BeEmpty();
    }

    [Fact]
    public void DiskQuery_DetermineDisplayType_FullBranchCoverage()
    {
        var method = typeof(DiskQuery).GetMethod("DetermineDisplayType", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        ((string)method!.Invoke(null, ["NVMe", "SSD"])!).Should().Be("NVMe SSD");
        ((string)method.Invoke(null, ["SATA", "SSD"])!).Should().Be("SATA SSD");
        ((string)method.Invoke(null, ["SATA", "HDD"])!).Should().Be("SATA HDD");
        ((string)method.Invoke(null, ["", "SSD"])!).Should().Be("SSD");
        ((string)method.Invoke(null, ["", "HDD"])!).Should().Be("HDD");
        ((string)method.Invoke(null, ["CUSTOM", ""])!).Should().Be("CUSTOM");
        ((string)method.Invoke(null, ["", ""])!).Should().Be("");
    }


    [Fact]
    public void DiskQuery_GetBootDriveIndex_ExecutesSafely()
    {
        var method = typeof(DiskQuery).GetMethod("GetBootDriveIndex", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var bootIndex = (int?)method!.Invoke(null, null);
        // On Windows host with admin or standard rights, either succeeds (returning disk index >= 0) or returns fallback/null gracefully
        if (bootIndex.HasValue)
        {
            bootIndex.Value.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void DiskQuery_QueryBusTypeIoctl_Drive0_And_BytesToStruct_Coverage()
    {
        // 1. QueryBusTypeIoctl for Drive 0 (physical drive 0)
        var ioctlMethod = typeof(DiskQuery).GetMethod("QueryBusTypeIoctl", BindingFlags.NonPublic | BindingFlags.Static);
        ioctlMethod.Should().NotBeNull();
        var busType = (string?)ioctlMethod!.Invoke(null, [0]);
        busType.Should().NotBeNull();

        // 2. BytesToStruct with invalid buffer length triggers exception
        var bytesMethod = typeof(DiskQuery).GetMethod("BytesToStruct", BindingFlags.NonPublic | BindingFlags.Static);
        bytesMethod.Should().NotBeNull();
        var genericMethod = bytesMethod!.MakeGenericMethod(typeof(Guid));
        Action act = () => genericMethod.Invoke(null, [new byte[2]]);
        act.Should().Throw<TargetInvocationException>();
    }

    [Fact]
    public void DiskQuery_IsExcludedDrive_And_DetermineMediaType_FullBranchCoverage()
    {
        // 1. Private Log method
        var logMethod = typeof(DiskQuery).GetMethod("Log", BindingFlags.NonPublic | BindingFlags.Static);
        logMethod.Should().NotBeNull();
        logMethod!.Invoke(null, ["DiskQuery unit test log message"]);

        // 2. FormatCommercialCapacity edge cases
        DiskQuery.FormatCommercialCapacity(0).Should().BeEmpty();
        DiskQuery.FormatCommercialCapacity(-100).Should().BeEmpty();

        // 3. IsExcludedDrive all branches
        var isExcluded = typeof(DiskQuery).GetMethod("IsExcludedDrive", BindingFlags.NonPublic | BindingFlags.Static);
        isExcluded.Should().NotBeNull();

        object?[] argsUsb = ["USB", "", "", 0, ""];
        ((bool)isExcluded!.Invoke(null, argsUsb)!).Should().BeTrue();

        object?[] argsUsbStor = ["SCSI", "USBSTOR_DEV", "", 0, ""];
        ((bool)isExcluded.Invoke(null, argsUsbStor)!).Should().BeTrue();

        object?[] argsUasp = ["SCSI", "UASP_DEV", "", 0, ""];
        ((bool)isExcluded.Invoke(null, argsUasp)!).Should().BeTrue();

        object?[] argsRemovable = ["SCSI", "", "Removable Media", 0, ""];
        ((bool)isExcluded.Invoke(null, argsRemovable)!).Should().BeTrue();

        object?[] argsScsi = ["SCSI", "", "Fixed", 999999, ""];
        isExcluded.Invoke(null, argsScsi);
        ((string)argsScsi[4]!).Should().Be("SSD");

        object?[] argsIde = ["IDE", "", "Fixed", 999999, ""];
        isExcluded.Invoke(null, argsIde);
        ((string)argsIde[4]!).Should().Be("SATA");

        object?[] argsCustom = ["PCIe", "", "Fixed", 999999, ""];
        isExcluded.Invoke(null, argsCustom);
        ((string)argsCustom[4]!).Should().Be("PCIe");

        object?[] argsEmpty = ["", "", "Fixed", 999999, ""];
        isExcluded.Invoke(null, argsEmpty);
        ((string)argsEmpty[4]!).Should().Be("DISK");

        // 4. DetermineMediaType branches
        var detMedia = typeof(DiskQuery).GetMethod("DetermineMediaType", BindingFlags.NonPublic | BindingFlags.Static);
        detMedia.Should().NotBeNull();

        var dict = new Dictionary<string, string> { ["0"] = "SSD" };
        ((string)detMedia!.Invoke(null, [dict, 0, "SATA", "Drive"])!).Should().Be("SSD");
        ((string)detMedia.Invoke(null, [dict, 1, "NVMe", "Drive"])!).Should().Be("SSD");
        ((string)detMedia.Invoke(null, [dict, 1, "SATA", "Samsung SSD 870"])!).Should().Be("SSD");
        ((string)detMedia.Invoke(null, [dict, 1, "SATA", "Samsung NVMe 980"])!).Should().Be("SSD");
        ((string)detMedia.Invoke(null, [dict, 1, "SATA", "Seagate HDD 2TB"])!).Should().Be("HDD");
        ((string)detMedia.Invoke(null, [dict, 1, "SATA", "Toshiba Harddisk"])!).Should().Be("HDD");
        ((string)detMedia.Invoke(null, [dict, 1, "SATA", "ST2000DM008"])!).Should().Be("HDD");
        ((string)detMedia.Invoke(null, [dict, 1, "SATA", "Generic Flash"])!).Should().Be("SSD");
    }
}

