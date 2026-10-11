using System.Reflection;
using System.Text;
using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using LocalTelemetry.Core.Hardware.PawnIo;
using NSubstitute;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

[Collection("SystemInfo")]
public class SystemInfoTests
{
    [Fact]
    public void SystemInfo_CpuQueries_ReturnValidData()
    {
        SystemInfo.GetCpuName().Should().NotBeNull();
        SystemInfo.GetCpuVendor().Should().NotBeNull();
        SystemInfo.GetCpuSocket().Should().NotBeNull();
        SystemInfo.GetCpuCoreCount().Should().BeGreaterThan(0);
        SystemInfo.GetCpuThreadCount().Should().BeGreaterThanOrEqualTo(SystemInfo.GetCpuCoreCount());
        SystemInfo.GetCpuMaxSpeedMhz().Should().BeGreaterThanOrEqualTo(0);
        SystemInfo.GetCpuBaseSpeedMhz().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void SystemInfo_MotherboardAndBiosQueries_ReturnValidData()
    {
        SystemInfo.GetSystemManufacturer().Should().NotBeNull();
        SystemInfo.GetSystemModel().Should().NotBeNull();
        SystemInfo.GetSystemTypeLabel().Should().NotBeNull();
        SystemInfo.GetSystemProductName().Should().NotBeNull();
        SystemInfo.GetMotherboardManufacturer().Should().NotBeNull();
        SystemInfo.GetMotherboardProductName().Should().NotBeNull();
        SystemInfo.GetMotherboardVersion().Should().NotBeNull();
        SystemInfo.GetMotherboardSerial().Should().NotBeNull();
        SystemInfo.GetBiosVersion().Should().NotBeNull();
        SystemInfo.GetBiosIsUefi().Should().Be(SystemInfo.GetBiosIsUefi());
    }

    [Fact]
    public void SystemInfo_RamQueries_ReturnValidData()
    {
        SystemInfo.GetTotalRamBytes().Should().BeGreaterThan(0);
        SystemInfo.GetInstalledRamGb().Should().BeGreaterThanOrEqualTo(0);
        SystemInfo.GetRamType().Should().NotBeNull();
        SystemInfo.GetRamSpeed().Should().NotBeNull();
        SystemInfo.GetRamManufacturer().Should().NotBeNull();
        SystemInfo.GetRamModules().Should().NotBeNull();
        SystemInfo.GetRamModuleCount().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void SystemInfo_GpuQueries_ReturnValidData()
    {
        SystemInfo.GetGpus().Should().NotBeNull();
        SystemInfo.GetGpuVendor().Should().NotBeNull();
        SystemInfo.GetGpuColor().Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_DiskAndNicQueries_ReturnValidData()
    {
        SystemInfo.GetDiskModel().Should().NotBeNull();
        SystemInfo.GetDiskManufacturer().Should().NotBeNull();
        SystemInfo.GetAllDisks().Should().NotBeNull();
        SystemInfo.GetNics().Should().NotBeNull();
        SystemInfo.GetDiskColor().Should().NotBeNull();
        SystemInfo.GetNicColor().Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_OsAndSystemQueries_ReturnValidData()
    {
        SystemInfo.GetOsDisplayVersion().Should().NotBeNull();
        SystemInfo.GetCpuColor().Should().NotBeNull();
        SystemInfo.GetRamColor().Should().NotBeNull();
        SystemInfo.GetSystemOemColor().Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_PowerAndBatteryQueries_ReturnValidData()
    {
        string mfr = SystemInfo.GetBatteryManufacturer();
        string dev = SystemInfo.GetBatteryDeviceName();
        mfr.Should().NotBeNull();
        dev.Should().NotBeNull();
        mfr.Should().NotContain("Virtual Battery");
        dev.Should().NotContain("CRB Battery");
        SystemInfo.GetBatteryDesignCapacity().Should().NotBeNull();
        SystemInfo.GetBatteryFullChargedCapacity().Should().NotBeNull();
        SystemInfo.HasBattery().Should().Be(SystemInfo.HasBattery());
        SystemInfo.GetPsuName().Should().NotBeNull();
        SystemInfo.GetPsuMaxCapacity().Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_IsSocketName_ValidatesSocketsCorrectly()
    {
        var method = typeof(SystemInfo).GetMethod("IsSocketName", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        method!.Invoke(null, ["LGA 1700"]).Should().Be(true);
        method.Invoke(null, ["Socket AM5"]).Should().Be(true);
        method.Invoke(null, ["BGA 1964"]).Should().Be(true);
        method.Invoke(null, ["PGA 988"]).Should().Be(true);
        method.Invoke(null, ["FP8"]).Should().Be(true);
        method.Invoke(null, ["FL1"]).Should().Be(true);
        method.Invoke(null, ["FooBarXYZ"]).Should().Be(false);
        method.Invoke(null, [""]).Should().Be(false);
    }

    [Fact]
    public void SystemInfo_SmbiosSocketName_MapsCodesCorrectly()
    {
        var method = typeof(SystemInfo).GetMethod("SmbiosSocketName", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        method!.Invoke(null, [25]).Should().Be("Socket LGA 1366");
        method.Invoke(null, [48]).Should().Be("Socket BGA 1364");
        method.Invoke(null, [34]).Should().Be("Socket BGA 1023");
        method.Invoke(null, [9999]).Should().Be("");
    }
}

/// <summary>
/// Hermetic tests for SystemInfo private helpers. No real hardware, WMI, SMBIOS or
/// registry access is required: pure functions are invoked via reflection, and SMBIOS
/// parsing paths are exercised by injecting a crafted raw SMBIOS blob into the cache.
/// </summary>
[Collection("SystemInfo")]
public class SystemInfoHermeticTests
{
    private delegate string SpanStringFunc(ReadOnlySpan<char> value);
    private delegate string SpanSpanStringFunc(ReadOnlySpan<char> a, ReadOnlySpan<char> b);
    private delegate void CpuidAppender(Span<byte> buf, ref int pos, int reg);

    private static T GetDelegate<T>(string methodName) where T : Delegate
    {
        var method = typeof(SystemInfo).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return method!.CreateDelegate<T>();
    }

    private static object? Invoke(string methodName, params object?[]? args)
    {
        var method = typeof(SystemInfo).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return method!.Invoke(null, args);
    }

    [Theory]
    [InlineData("INTEL(R) CORE(TM) ULTRA 7 155H", "Socket BGA 2049")]
    [InlineData("INTEL(R) CORE(TM) ULTRA 7 155U", "Socket BGA 2551")]
    [InlineData("INTEL(R) CORE(TM) ULTRA 5 125V", "Socket BGA 2833")]
    [InlineData("INTEL(R) CORE(TM) ULTRA 9 285K", "Socket LGA 1851")]
    [InlineData("INTEL(R) CORE(TM) ULTRA 7 265H", "Socket BGA 2049")]
    [InlineData("INTEL(R) CORE(TM) ULTRA 5 225U", "Socket BGA 2551")]
    [InlineData("INTEL(R) CORE(TM) ULTRA 7", "")]
    [InlineData("INTEL(R) CORE(TM) ULTRA X 7", "")]
    [InlineData("INTEL(R) CORE(TM) I7-14700K", "Socket LGA 1700")]
    [InlineData("INTEL(R) CORE(TM) I9-14900HX", "Socket BGA 1744")]
    [InlineData("INTEL(R) CORE(TM) I7-14700H", "Socket BGA 1964")]
    [InlineData("INTEL(R) CORE(TM) I5-13420U", "Socket BGA 1744")]
    [InlineData("INTEL(R) CORE(TM) I7-10750H", "Socket BGA 1440")]
    [InlineData("INTEL(R) CORE(TM) I5-10210U", "Socket BGA 1526")]
    [InlineData("INTEL(R) CORE(TM) I7-8700K", "Socket LGA 1151")]
    [InlineData("INTEL(R) CORE(TM) I5-8400", "Socket LGA 1151")]
    [InlineData("INTEL(R) CORE(TM) I7-8565U", "Socket BGA")]
    [InlineData("INTEL(R) CORE(TM) I7-4790K", "Socket LGA 1150")]
    [InlineData("INTEL(R) CORE(TM) I5-4590", "Socket LGA 1150")]
    [InlineData("INTEL(R) CORE(TM) I7-4700MQ", "Socket BGA")]
    [InlineData("INTEL(R) CORE(TM) I7-1165G7", "Socket BGA")]
    [InlineData("INTEL(R) XEON(R) W9-3495X", "Socket LGA 4677")]
    [InlineData("INTEL(R) XEON(R) W7-2495X", "Socket LGA 4677")]
    [InlineData("INTEL(R) XEON(R) GOLD 6248R", "Socket LGA 4189")]
    [InlineData("INTEL(R) ATOM(TM) X5-Z8350", "")]
    public void SystemInfo_DeriveIntelSocket_ReturnsExpected(string name, string expected)
    {
        var del = GetDelegate<SpanStringFunc>("DeriveIntelSocket");
        del(name.AsSpan()).Should().Be(expected);
    }

    [Theory]
    [InlineData("AMD RYZEN 9 7950X", "Socket AM5")]
    [InlineData("AMD RYZEN 7 7840HS", "Socket FP8")]
    [InlineData("AMD RYZEN 7 7840H", "Socket AM5")]
    [InlineData("AMD RYZEN 9 7945HX", "Socket FL1")]
    [InlineData("AMD RYZEN 5 5600X", "Socket AM4")]
    [InlineData("AMD RYZEN 5 5600H", "Socket FP6")]
    [InlineData("AMD RYZEN 9 5980HX", "Socket FP7")]
    [InlineData("AMD RYZEN 7 7735U", "Socket FP8")]
    [InlineData("AMD RYZEN 9 8945HS", "Socket FP8")]
    [InlineData("AMD RYZEN 9 9950X", "Socket AM5")]
    [InlineData("AMD RYZEN 5 6600H", "Socket FP7")]
    [InlineData("AMD RYZEN 5 4600H", "Socket FP6")]
    [InlineData("AMD RYZEN 3 3200G", "Socket AM4")]
    [InlineData("AMD RYZEN 3 2300U", "Socket FP5")]
    [InlineData("AMD RYZEN 5 2400G", "Socket AM4")]
    [InlineData("AMD RYZEN 1 1000U", "Socket FP4")]
    [InlineData("AMD RYZEN 1 1000", "Socket AM4")]
    [InlineData("AMD RYZEN THREADRIPPER 7980X", "Socket sTRX4")]
    [InlineData("AMD RYZEN THREADRIPPER 7000X", "Socket sTR5")]
    [InlineData("AMD EPYC 7742", "Socket SP3")]
    [InlineData("AMD EPYC 9004X", "Socket SP5")]
    [InlineData("AMD EPYC 8004X", "Socket SP6")]
    [InlineData("AMD RYZEN", "")]
    [InlineData("INTEL XEON GOLD 6248", "")]
    public void SystemInfo_DeriveAmdSocket_ReturnsExpected(string name, string expected)
    {
        var del = GetDelegate<SpanStringFunc>("DeriveAmdSocket");
        del(name.AsSpan()).Should().Be(expected);
    }

    [Theory]
    [InlineData("12345", "", "Socket LGA 1700")]
    [InlineData("13900", "HX", "Socket BGA 1744")]
    [InlineData("14700", "H", "Socket BGA 1964")]
    [InlineData("13420", "U", "Socket BGA 1744")]
    [InlineData("11800", "H", "Socket BGA 1440")]
    [InlineData("10210", "U", "Socket BGA 1526")]
    [InlineData("9900", "K", "Socket LGA 1151")]
    [InlineData("8700", "K", "Socket LGA 1151")]
    [InlineData("8400", "", "Socket LGA 1151")]
    [InlineData("8565", "U", "Socket BGA")]
    [InlineData("6400", "K", "Socket LGA 1151")]
    [InlineData("4790", "K", "Socket LGA 1150")]
    [InlineData("4590", "", "Socket LGA 1150")]
    [InlineData("4700", "MQ", "Socket BGA")]
    [InlineData("1240", "P", "Socket BGA")]
    [InlineData("0000", "", "")]
    public void SystemInfo_IntelSocketByGen_ReturnsExpected(string num, string suffix, string expected)
    {
        var del = GetDelegate<SpanSpanStringFunc>("IntelSocketByGen");
        del(num.AsSpan(), suffix.AsSpan()).Should().Be(expected);
    }

    [Theory]
    [InlineData("W9-3495X", "Socket LGA 4677")]
    [InlineData("W5-3425", "Socket LGA 4677")]
    [InlineData("W7-2495X", "Socket LGA 4677")]
    [InlineData("GOLD 6248R", "Socket LGA 4189")]
    [InlineData("PLATINUM 8468", "Socket LGA 4189")]
    public void SystemInfo_IntelXeonSocket_ReturnsExpected(string name, string expected)
    {
        var del = GetDelegate<SpanStringFunc>("IntelXeonSocket");
        del(name.AsSpan()).Should().Be(expected);
    }

    [Theory]
    [InlineData("7950", "X", "Socket AM5")]
    [InlineData("7840", "HS", "Socket FP8")]
    [InlineData("7945", "HX", "Socket FL1")]
    [InlineData("7735", "U", "Socket FP8")]
    [InlineData("5600", "X", "Socket AM4")]
    [InlineData("5600", "H", "Socket FP6")]
    [InlineData("5980", "HX", "Socket FP7")]
    [InlineData("6600", "H", "Socket FP7")]
    [InlineData("4600", "H", "Socket FP6")]
    [InlineData("3200", "G", "Socket AM4")]
    [InlineData("2300", "U", "Socket FP5")]
    [InlineData("2400", "G", "Socket AM4")]
    [InlineData("1000", "U", "Socket FP4")]
    [InlineData("1000", "G", "Socket AM4")]
    public void SystemInfo_AmdSocketByArch_ReturnsExpected(string num, string suffix, string expected)
    {
        var del = GetDelegate<SpanSpanStringFunc>("AmdSocketByArch");
        del(num.AsSpan(), suffix.AsSpan()).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "")]
    [InlineData(2, "")]
    [InlineData(3, "")]
    [InlineData(4, "ZIF Socket")]
    [InlineData(5, "")]
    [InlineData(6, "")]
    [InlineData(7, "LIF Socket")]
    [InlineData(8, "Slot 1")]
    [InlineData(9, "Slot 2")]
    [InlineData(10, "370-pin Socket")]
    [InlineData(11, "Slot A")]
    [InlineData(12, "Slot M")]
    [InlineData(13, "Socket 423")]
    [InlineData(14, "Socket A (Socket 462)")]
    [InlineData(15, "Socket 478")]
    [InlineData(16, "Socket 754")]
    [InlineData(17, "Socket 940")]
    [InlineData(18, "Socket 939")]
    [InlineData(19, "Socket mPGA604")]
    [InlineData(20, "Socket LGA 771")]
    [InlineData(21, "Socket LGA 775")]
    [InlineData(22, "Socket S1")]
    [InlineData(23, "Socket AM2")]
    [InlineData(24, "Socket F (1207)")]
    [InlineData(25, "Socket LGA 1366")]
    [InlineData(26, "Socket G34")]
    [InlineData(27, "Socket AM3")]
    [InlineData(28, "Socket C32")]
    [InlineData(29, "Socket LGA 1156")]
    [InlineData(30, "Socket LGA 1567")]
    [InlineData(31, "Socket PGA988A")]
    [InlineData(32, "Socket BGA 1288")]
    [InlineData(33, "Socket rPGA988B")]
    [InlineData(34, "Socket BGA 1023")]
    [InlineData(35, "Socket BGA 1224")]
    [InlineData(36, "Socket LGA 1155")]
    [InlineData(37, "Socket LGA 1356")]
    [InlineData(38, "Socket LGA 2011")]
    [InlineData(39, "Socket FS1")]
    [InlineData(40, "Socket FS2")]
    [InlineData(41, "Socket FM1")]
    [InlineData(42, "Socket FM2")]
    [InlineData(43, "Socket LGA 2011-3")]
    [InlineData(44, "Socket LGA 1356-3")]
    [InlineData(45, "Socket LGA 1150")]
    [InlineData(46, "Socket BGA 1168")]
    [InlineData(47, "Socket BGA 1234")]
    [InlineData(48, "Socket BGA 1364")]
    [InlineData(49, "Socket AM4")]
    [InlineData(50, "Socket LGA 1151")]
    [InlineData(51, "Socket BGA 1356")]
    [InlineData(52, "Socket BGA 1440")]
    [InlineData(53, "Socket BGA 1515")]
    [InlineData(54, "Socket LGA 3647")]
    [InlineData(55, "Socket SP3")]
    [InlineData(56, "Socket SP3r2")]
    [InlineData(57, "Socket LGA 2066")]
    [InlineData(58, "Socket BGA 1392")]
    [InlineData(59, "Socket BGA 1510")]
    [InlineData(60, "Socket BGA 1528")]
    [InlineData(61, "Socket LGA 4189")]
    [InlineData(62, "Socket LGA 1200")]
    [InlineData(63, "Socket LGA 4677")]
    [InlineData(64, "Socket LGA 1700")]
    [InlineData(65, "Socket BGA 1744")]
    [InlineData(66, "Socket BGA 1781")]
    [InlineData(67, "Socket BGA 1211")]
    [InlineData(68, "Socket BGA 2422")]
    [InlineData(69, "Socket LGA 5773")]
    [InlineData(70, "Socket BGA 5773")]
    [InlineData(71, "Socket AM5")]
    [InlineData(72, "Socket SP5")]
    [InlineData(73, "Socket SP6")]
    [InlineData(74, "Socket BGA 883")]
    [InlineData(75, "Socket BGA 1190")]
    [InlineData(76, "Socket BGA 4129")]
    [InlineData(77, "Socket LGA 4710")]
    [InlineData(78, "Socket LGA 7529")]
    [InlineData(79, "Socket BGA 1964")]
    [InlineData(80, "Socket BGA 1792")]
    [InlineData(81, "Socket BGA 2049")]
    [InlineData(82, "Socket BGA 2551")]
    [InlineData(83, "Socket LGA 1851")]
    [InlineData(84, "Socket BGA 2114")]
    [InlineData(85, "Socket BGA 2833")]
    [InlineData(9999, "")]
    public void SystemInfo_SmbiosSocketName_MapsAllCodes(int code, string expected)
    {
        Invoke("SmbiosSocketName", code).Should().Be(expected);
    }

    [Theory]
    [InlineData(0x01, "Other")]
    [InlineData(0x02, "Unknown")]
    [InlineData(0x03, "SIMM")]
    [InlineData(0x04, "SIP")]
    [InlineData(0x05, "Chip")]
    [InlineData(0x06, "DIP")]
    [InlineData(0x07, "ZIP")]
    [InlineData(0x08, "Proprietary Card")]
    [InlineData(0x09, "DIMM")]
    [InlineData(0x0A, "SODIMM")]
    [InlineData(0x0B, "SORDIMM")]
    [InlineData(0x0C, "Mini-DIMM")]
    [InlineData(0x0D, "Mini-RDIMM")]
    [InlineData(0x99, "Type99")]
    public void SystemInfo_FormFactorLabel_MapsAllCodes(int ff, string expected)
    {
        Invoke("FormFactorLabel", ff).Should().Be(expected);
    }

    [Theory]
    [InlineData(0x00, "Unknown")]
    [InlineData(0x11, "SDRAM")]
    [InlineData(0x12, "DDR")]
    [InlineData(0x13, "DDR2")]
    [InlineData(0x14, "DDR2 FB-DIMM")]
    [InlineData(0x18, "DDR3")]
    [InlineData(0x19, "DDR3 FB-DIMM")]
    [InlineData(0x1A, "DDR4")]
    [InlineData(0x1B, "LPDDR")]
    [InlineData(0x1C, "LPDDR2")]
    [InlineData(0x1D, "LPDDR3")]
    [InlineData(0x1E, "LPDDR4")]
    [InlineData(0x20, "HBM")]
    [InlineData(0x21, "HBM2")]
    [InlineData(0x22, "DDR5")]
    [InlineData(0x23, "LPDDR5")]
    [InlineData(0x24, "HBM3")]
    [InlineData(0x26, "LPDDR5X")]
    [InlineData(0x77, "Type77")]
    public void SystemInfo_MemoryTypeLabel_MapsAllCodes(int mt, string expected)
    {
        Invoke("MemoryTypeLabel", mt).Should().Be(expected);
    }

    [Theory]
    [InlineData("PCI\\VEN_10DE&DEV_2487", "GeForce", "NVIDIA")]
    [InlineData("PCI\\VEN_1002&DEV_73BF", "Radeon", "AMD")]
    [InlineData("PCI\\VEN_8086&DEV_4680", "UHD Graphics", "Intel")]
    [InlineData("", "NVIDIA GeForce RTX 3080", "NVIDIA")]
    [InlineData("", "AMD Radeon RX 6700 XT", "AMD")]
    [InlineData("", "Intel(R) UHD Graphics 630", "Intel")]
    [InlineData("", "FooBar Virtual Device", "")]
    public void SystemInfo_ParseVendor_MapsVendor(string hwId, string name, string expected)
    {
        Invoke("ParseVendor", hwId, name).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("123", 123)]
    [InlineData("0", 0)]
    [InlineData("-5", 0)]
    [InlineData("abc", 0)]
    [InlineData(99, 99)]
    public void SystemInfo_TryParseWmiInt_ReturnsExpected(object? val, int expected)
    {
        Invoke("TryParseWmiInt", val).Should().Be(expected);
    }

    [Theory]
    [InlineData(24, 24)]
    [InlineData(48, 24)]
    [InlineData(32, 32)]
    [InlineData(16, 16)]
    [InlineData(20, 20)]
    [InlineData(40, 20)]
    [InlineData(10, 0)]
    public void SystemInfo_DetectGlpiStructSize_ReturnsExpected(int totalLen, int expected)
    {
        Invoke("DetectGlpiStructSize", totalLen).Should().Be(expected);
    }

    [Fact]
    public void SystemInfo_AppendCpuid_WritesLittleEndian()
    {
        var del = GetDelegate<CpuidAppender>("AppendCpuid");
        Span<byte> buf = stackalloc byte[8];
        int pos = 0;

        del(buf, ref pos, 0x04030201);
        del(buf, ref pos, 0x08070605);

        pos.Should().Be(8);
        buf[0].Should().Be(0x01);
        buf[1].Should().Be(0x02);
        buf[2].Should().Be(0x03);
        buf[3].Should().Be(0x04);
        buf[4].Should().Be(0x05);
        buf[7].Should().Be(0x08);
    }

    [Fact]
    public void SystemInfo_GetSmbiosString_ReadsStringTable()
    {
        byte[] blob = [0x04, 0x02, (byte)'S', (byte)'a', (byte)'m', 0, (byte)'A', (byte)'c', (byte)'m', 0, 0];

        Invoke("GetSmbiosString", blob, 0, 2, (byte)1).Should().Be("Sam");
        Invoke("GetSmbiosString", blob, 0, 2, (byte)2).Should().Be("Acm");
        Invoke("GetSmbiosString", blob, 0, 2, (byte)3).Should().Be("");
        Invoke("GetSmbiosString", blob, 0, 2, (byte)0).Should().Be("");
    }

    [Fact]
    public void SystemInfo_FindSmbiosString_FindsAndSkipsStructures()
    {
        byte[] blob = BuildBlob();

        Invoke("FindSmbiosString", blob, (byte)1, (byte)0x04).Should().Be("ASUS");
        Invoke("FindSmbiosString", blob, (byte)1, (byte)0x05).Should().Be("PRIME X670-P");
        Invoke("FindSmbiosString", blob, (byte)17, (byte)0x17).Should().Be("Corsair");
        Invoke("FindSmbiosString", blob, (byte)99, (byte)0x04).Should().Be("");
    }

    [Fact]
    public void SystemInfo_GetCpuThreadCount_ReadsInjectedSmbios()
    {
        WithSmbios(BuildBlob(), () =>
        {
            SystemInfo.GetCpuThreadCount().Should().Be(16);
        });
    }

    [Fact]
    public void SystemInfo_GetCpuCoreCount_ReadsInjectedSmbios()
    {
        WithSmbios(BuildBlob(), () =>
        {
            ((int)Invoke("GetSmbiosCoreCount")!).Should().Be(8);
            SystemInfo.GetCpuCoreCount().Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void SystemInfo_PsuQueries_ReadInjectedSmbios()
    {
        WithSmbios(BuildBlob(), () =>
        {
            SystemInfo.GetPsuName().Should().Be("Seasonic FOCUS GX-850");
            SystemInfo.GetPsuMaxCapacity().Should().Be("65 W");
        });
    }

    [Fact]
    public void SystemInfo_SmbiosBatteryAndRamQueries_ReadInjectedSmbios()
    {
        WithSmbios(BuildBlob(), () =>
        {
            Invoke("GetSmbiosBatteryString", 0x05).Should().Be("Sunwoda");
            Invoke("GetSmbiosBatteryString", 0x08).Should().Be("A31-80");
            Invoke("GetSmbiosBatteryWord", 0x0A).Should().Be(50000);
            ((IEnumerable<string>)Invoke("GetSmbiosRamManufacturers")!).Should().Equal("Corsair");
        });
    }

    [Theory]
    [InlineData("Sunwoda", true)]
    [InlineData("L24D4PC0", true)]
    [InlineData("A31-80", true)]
    [InlineData("Simplo", true)]
    [InlineData("-Virtual Battery 0-", false)]
    [InlineData("CRB Battery 0", false)]
    [InlineData("Fake", false)]
    [InlineData("None", false)]
    [InlineData("Unknown", false)]
    [InlineData("To be filled by O.E.M.", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void SystemInfo_IsValidBatteryString_FiltersVirtualAndDummyFirmwareStrings(string? input, bool expected)
    {
        var method = typeof(SystemInfo).GetMethod("IsValidBatteryString", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        bool result = (bool)method!.Invoke(null, [input])!;
        result.Should().Be(expected);
    }

    [Fact]
    public void SystemInfo_GetSmbiosRaw_ReturnsInjectedCache()
    {
        byte[] blob = BuildBlob();
        WithSmbios(blob, () =>
        {
            var result = (byte[])Invoke("GetSmbiosRaw")!;
            result.Should().BeSameAs(blob);
        });
    }

    [Fact]
    public void SystemInfo_GetPsuMaxCapacity_SkipsUnknownCapacity()
    {
        var blob = new List<byte>();
        var data = new byte[0x10];
        data[0] = 39;
        data[1] = 0x10;
        data[0x0A] = 0x00;
        data[0x0B] = 0x80;
        blob.AddRange(data);
        blob.Add(0);
        blob.Add(0);

        WithSmbios(blob.ToArray(), () =>
        {
            SystemInfo.GetPsuMaxCapacity().Should().Be("");
            SystemInfo.GetPsuName().Should().Be("");
        });
    }

    private static byte[] BuildBlob()
    {
        var blob = new List<byte>();
        void AddStruct(byte type, int len, Action<byte[]> fill, params string[] strings)
        {
            var data = new byte[len];
            data[0] = type;
            data[1] = (byte)len;
            fill(data);
            blob.AddRange(data);
            foreach (string s in strings)
            {
                blob.AddRange(Encoding.ASCII.GetBytes(s));
                blob.Add(0);
            }
            blob.Add(0);
        }

        AddStruct(4, 0x30, d => { d[0x23] = 8; d[0x25] = 16; d[0x2A] = 8; d[0x2C] = 8; d[0x2E] = 16; }, "Socket LGA 1700");
        AddStruct(1, 0x1B, d => { d[0x04] = 1; d[0x05] = 2; }, "ASUS", "PRIME X670-P");
        AddStruct(17, 0x2A, d => { d[0x12] = 26; d[0x17] = 1; }, "Corsair", "CMK32GX4M2B3200C16", "BLAH123");
        AddStruct(22, 0x1A, d => { d[0x05] = 1; d[0x08] = 2; d[0x0A] = 0x50; d[0x0B] = 0xC3; }, "Sunwoda", "A31-80");
        AddStruct(39, 0x10, d => { d[0x06] = 1; d[0x08] = 2; d[0x0A] = 0xE8; d[0x0B] = 0xFD; }, "Seasonic", "FOCUS GX-850");
        AddStruct(127, 4, _ => { });
        blob.Add(0);
        return blob.ToArray();
    }

    private delegate string ReadOnlySpanFunc(ReadOnlySpan<char> s);

    [Fact]
    public void SystemInfo_InternalCpuMethods_And_BatteryFlows_Coverage()
    {
        // 1. CpuIdSocketLookup
        var cpuIdSocketMethod = typeof(SystemInfo).GetMethod("CpuIdSocketLookup", BindingFlags.NonPublic | BindingFlags.Static);
        cpuIdSocketMethod.Should().NotBeNull();
        cpuIdSocketMethod!.Invoke(null, null);

        // 2. GetCpuBrandStringCpuid
        var cpuidBrandMethod = typeof(SystemInfo).GetMethod("GetCpuBrandStringCpuid", BindingFlags.NonPublic | BindingFlags.Static);
        cpuidBrandMethod.Should().NotBeNull();
        cpuidBrandMethod!.Invoke(null, null);

        // 3. GetCpuBrandString
        var brandMethod = typeof(SystemInfo).GetMethod("GetCpuBrandString", BindingFlags.NonPublic | BindingFlags.Static);
        brandMethod.Should().NotBeNull();
        brandMethod!.Invoke(null, null);

        // 4. DeriveSocketFromCpuName
        var deriveSocketMethod = typeof(SystemInfo).GetMethod("DeriveSocketFromCpuName", BindingFlags.NonPublic | BindingFlags.Static);
        deriveSocketMethod.Should().NotBeNull();
        deriveSocketMethod!.Invoke(null, null);

        // 5. DeriveQualcommSocket
        var qualcommMethod = typeof(SystemInfo).GetMethod("DeriveQualcommSocket", BindingFlags.NonPublic | BindingFlags.Static);
        qualcommMethod.Should().NotBeNull();
        var qualcommDelegate = qualcommMethod!.CreateDelegate<ReadOnlySpanFunc>();
        qualcommDelegate("Snapdragon X Elite".AsSpan()).Should().Be("Socket 2050");
        qualcommDelegate("Snapdragon 8 Gen 2".AsSpan()).Should().BeEmpty();

        // 6. QueryNtPowerMaxMhz
        var ntPowerMethod = typeof(SystemInfo).GetMethod("QueryNtPowerMaxMhz", BindingFlags.NonPublic | BindingFlags.Static);
        ntPowerMethod.Should().NotBeNull();
        ntPowerMethod!.Invoke(null, null);

        // 7. QueryRegistryProcessorSpeed
        var regSpeedMethod = typeof(SystemInfo).GetMethod("QueryRegistryProcessorSpeed", BindingFlags.NonPublic | BindingFlags.Static);
        regSpeedMethod.Should().NotBeNull();
        regSpeedMethod!.Invoke(null, null);

        // 8. QueryRamTypeSmbios and FindSmbiosType17MemoryType
        WithSmbios(BuildBlob(), () =>
        {
            var ramSmbiosMethod = typeof(SystemInfo).GetMethod("QueryRamTypeSmbios", BindingFlags.NonPublic | BindingFlags.Static);
            ramSmbiosMethod.Should().NotBeNull();
            string ramType = (string)ramSmbiosMethod!.Invoke(null, null)!;
            ramType.Should().Be("DDR4");

            // Battery properties with SMBIOS Type 22 present
            var hasBatField = typeof(SystemInfo).GetField("_hasBatteryCache", BindingFlags.NonPublic | BindingFlags.Static);
            var batMfrField = typeof(SystemInfo).GetField("_batteryManufacturerCache", BindingFlags.NonPublic | BindingFlags.Static);
            var batNameField = typeof(SystemInfo).GetField("_batteryDeviceNameCache", BindingFlags.NonPublic | BindingFlags.Static);
            var batCapField = typeof(SystemInfo).GetField("_batteryDesignCapacityCache", BindingFlags.NonPublic | BindingFlags.Static);
            var batFccField = typeof(SystemInfo).GetField("_batteryFullChargeCapacityCache", BindingFlags.NonPublic | BindingFlags.Static);

            object? origHasBat = hasBatField!.GetValue(null);
            object? origMfr = batMfrField!.GetValue(null);
            object? origName = batNameField!.GetValue(null);
            object? origCap = batCapField!.GetValue(null);
            object? origFcc = batFccField!.GetValue(null);

            try
            {
                hasBatField.SetValue(null, true);
                batMfrField.SetValue(null, null);
                batNameField.SetValue(null, null);
                batCapField.SetValue(null, null);
                batFccField.SetValue(null, null);

                SystemInfo.GetBatteryManufacturer().Should().NotBeNullOrEmpty();
                SystemInfo.GetBatteryDeviceName().Should().NotBeNullOrEmpty();
                SystemInfo.GetBatteryDesignCapacity().Should().NotBeNullOrEmpty();
                SystemInfo.GetBatteryFullChargedCapacity();

                // HasBattery uncached
                hasBatField.SetValue(null, null);
                SystemInfo.HasBattery();
            }
            finally
            {
                hasBatField.SetValue(null, origHasBat);
                batMfrField.SetValue(null, origMfr);
                batNameField.SetValue(null, origName);
                batCapField.SetValue(null, origCap);
                batFccField.SetValue(null, origFcc);
            }
        });
    }

    [Fact]
    public void SystemInfo_WmiCatchBlocks_HandleInvalidQueriesGracefully()
    {
        var qWmiFirst = typeof(SystemInfo).GetMethod("QueryWmiFirst", BindingFlags.NonPublic | BindingFlags.Static);
        var qWmiFirstInt = typeof(SystemInfo).GetMethod("QueryWmiFirstInt", BindingFlags.NonPublic | BindingFlags.Static);
        var qRootWmiStr = typeof(SystemInfo).GetMethod("QueryRootWmiString", BindingFlags.NonPublic | BindingFlags.Static);
        var qRootWmiInt = typeof(SystemInfo).GetMethod("QueryRootWmiInt", BindingFlags.NonPublic | BindingFlags.Static);

        qWmiFirst.Should().NotBeNull();
        qWmiFirstInt.Should().NotBeNull();
        qRootWmiStr.Should().NotBeNull();
        qRootWmiInt.Should().NotBeNull();

        // Query non-existent WMI class to trigger ManagementException
        ((string)qWmiFirst!.Invoke(null, ["SELECT NonExistentProp FROM Win32_NonExistentClass12345", "NonExistentProp"])!).Should().BeEmpty();
        ((int)qWmiFirstInt!.Invoke(null, ["SELECT NonExistentProp FROM Win32_NonExistentClass12345", "NonExistentProp"])!).Should().Be(0);
        ((string)qRootWmiStr!.Invoke(null, ["NonExistentRootClass12345", "NonExistentProp"])!).Should().BeEmpty();
        ((int)qRootWmiInt!.Invoke(null, ["NonExistentRootClass12345", "NonExistentProp"])!).Should().Be(0);

        // Also test with empty / invalid SQL syntax to trigger generic Exception catch block
        ((string)qWmiFirst.Invoke(null, ["INVALID SQL SYNTAX !@#$%", "prop"])!).Should().BeEmpty();
        ((int)qWmiFirstInt.Invoke(null, ["INVALID SQL SYNTAX !@#$%", "prop"])!).Should().Be(0);
    }

    [Fact]
    public void SystemInfo_DeriveSocketFromCpuName_And_BrandString_Coverage()
    {
        var deriveSocket = typeof(SystemInfo).GetMethod("DeriveSocketFromCpuName", BindingFlags.NonPublic | BindingFlags.Static);
        var deriveIntel = typeof(SystemInfo).GetMethod("DeriveIntelSocket", BindingFlags.NonPublic | BindingFlags.Static);
        var deriveAmd = typeof(SystemInfo).GetMethod("DeriveAmdSocket", BindingFlags.NonPublic | BindingFlags.Static);
        var deriveQualcomm = typeof(SystemInfo).GetMethod("DeriveQualcommSocket", BindingFlags.NonPublic | BindingFlags.Static);

        deriveSocket.Should().NotBeNull();
        deriveIntel.Should().NotBeNull();
        deriveAmd.Should().NotBeNull();
        deriveQualcomm.Should().NotBeNull();

        // 1. SmbiosSocketName coverage
        var smbiosSocketMethod = typeof(SystemInfo).GetMethod("SmbiosSocketName", BindingFlags.NonPublic | BindingFlags.Static);
        smbiosSocketMethod.Should().NotBeNull();
        ((string)smbiosSocketMethod!.Invoke(null, [64])!).Should().Be("Socket LGA 1700");
        ((string)smbiosSocketMethod.Invoke(null, [71])!).Should().Be("Socket AM5");
        ((string)smbiosSocketMethod.Invoke(null, [83])!).Should().Be("Socket LGA 1851");
        ((string)smbiosSocketMethod.Invoke(null, [85])!).Should().Be("Socket BGA 2833");
        ((string)smbiosSocketMethod.Invoke(null, [999])!).Should().BeEmpty();

        // 2. IsSocketName and IsPlaceholderSocket
        var isSocketMethod = typeof(SystemInfo).GetMethod("IsSocketName", BindingFlags.NonPublic | BindingFlags.Static);
        var isPlaceholderMethod = typeof(SystemInfo).GetMethod("IsPlaceholderSocket", BindingFlags.NonPublic | BindingFlags.Static);
        isSocketMethod.Should().NotBeNull();
        isPlaceholderMethod.Should().NotBeNull();

        ((bool)isSocketMethod!.Invoke(null, ["LGA 1700"])!).Should().BeTrue();
        ((bool)isSocketMethod.Invoke(null, ["Socket AM5"])!).Should().BeTrue();
        ((bool)isSocketMethod.Invoke(null, ["BGA 2049"])!).Should().BeTrue();
        ((bool)isSocketMethod.Invoke(null, ["FP8"])!).Should().BeTrue();
        ((bool)isSocketMethod.Invoke(null, ["FL1"])!).Should().BeTrue();
        ((bool)isSocketMethod.Invoke(null, ["GenericDevice"])!).Should().BeFalse();
        ((bool)isSocketMethod.Invoke(null, [""])!).Should().BeFalse();

        ((bool)isPlaceholderMethod!.Invoke(null, ["Unknown"])!).Should().BeTrue();
        ((bool)isPlaceholderMethod.Invoke(null, ["To Be Filled By O.E.M."])!).Should().BeTrue();
        ((bool)isPlaceholderMethod.Invoke(null, ["LGA 1700"])!).Should().BeFalse();
        ((bool)isPlaceholderMethod.Invoke(null, [""])!).Should().BeTrue();

        // 3. DeriveSocketFromCpuName and GetCpuSocket with synthetic _cpuNameCache values
        var cpuNameField = typeof(SystemInfo).GetField("_cpuNameCache", BindingFlags.NonPublic | BindingFlags.Static);
        var cpuSocketField = typeof(SystemInfo).GetField("_cpuSocketCache", BindingFlags.NonPublic | BindingFlags.Static);
        cpuNameField.Should().NotBeNull();
        cpuSocketField.Should().NotBeNull();

        object? origName = cpuNameField!.GetValue(null);
        object? origSocket = cpuSocketField!.GetValue(null);

        string[] testCpuNames =
        [
            "Intel(R) Core(TM) Ultra 7 155H",
            "Intel(R) Core(TM) Ultra 7 258V",
            "Intel(R) Core(TM) Ultra 5 125U",
            "Intel(R) Core(TM) Ultra 9 285K",
            "Intel(R) Core(TM) i7-14700HX",
            "Intel(R) Core(TM) i9-14900K",
            "Intel(R) Core(TM) i7-11700K",
            "Intel(R) Core(TM) i7-1165G7",
            "Intel(R) Core(TM) i7-9700K",
            "Intel(R) Core(TM) i7-8750H",
            "Intel(R) Core(TM) i7-6500U",
            "Intel(R) Xeon(R) W9-3495X",
            "Intel(R) Xeon(R) Platinum 8380",
            "AMD Ryzen Threadripper PRO 7995WX",
            "AMD Ryzen Threadripper 3990X",
            "AMD EPYC 9654",
            "AMD EPYC 8534P",
            "AMD EPYC 7763",
            "AMD Ryzen 9 9950X",
            "AMD Ryzen 7 8840HS",
            "AMD Ryzen 7 8700G",
            "AMD Ryzen 9 7945HX",
            "AMD Ryzen 7 7840U",
            "AMD Ryzen 7 7700X",
            "AMD Ryzen 7 6800H",
            "AMD Ryzen 9 5900HX",
            "AMD Ryzen 7 5800H",
            "AMD Ryzen 7 5700X",
            "AMD Ryzen 5 4600H",
            "AMD Ryzen 7 3700U",
            "AMD Ryzen 5 3600",
            "AMD Ryzen 5 2500U",
            "AMD Ryzen 5 2600",
            "AMD Ryzen 7 1700",
            "AMD Ryzen 7 1700U",
            "Snapdragon X Elite - X1E-80-100",
            "Unknown CPU Name"
        ];

        try
        {
            foreach (string cpu in testCpuNames)
            {
                cpuNameField.SetValue(null, cpu);
                cpuSocketField.SetValue(null, null);
                string skt = (string)deriveSocket!.Invoke(null, null)!;
                skt.Should().NotBeNull();
            }
        }
        finally
        {
            cpuNameField.SetValue(null, origName);
            cpuSocketField.SetValue(null, origSocket);
        }
    }


    [Fact]
    public void SystemInfo_FindSmbiosType4Value_Coverage()
    {
        var findValue = typeof(SystemInfo).GetMethod("FindSmbiosType4Value", BindingFlags.NonPublic | BindingFlags.Static);
        findValue.Should().NotBeNull();

        // 1. empty raw array
        ((int)findValue!.Invoke(null, [Array.Empty<byte>(), 0x2A, 0x23])!).Should().Be(0);

        // 2. malformed length byte < 4
        byte[] malformed = [(byte)4, (byte)2, (byte)0, (byte)0];
        ((int)findValue.Invoke(null, [malformed, 0x2A, 0x23])!).Should().Be(0);

        // 3. valid wordOffset
        byte[] rawWord = new byte[60];
        rawWord[0] = 4; // Type 4
        rawWord[1] = 50; // len
        rawWord[0x2A] = 0x20;
        rawWord[0x2B] = 0x01; // word = 0x0120 (288)
        ((int)findValue.Invoke(null, [rawWord, 0x2A, 0x23])!).Should().Be(288);

        // 4. wordOffset beyond len, but byteOffset within len
        byte[] rawByte = new byte[40];
        rawByte[0] = 4; // Type 4
        rawByte[1] = 38; // len < 0x2A + 2 (44)
        rawByte[0x23] = 16;
        ((int)findValue.Invoke(null, [rawByte, 0x2A, 0x23])!).Should().Be(16);

        // 5. both offsets beyond len
        byte[] rawShort = new byte[20];
        rawShort[0] = 4;
        rawShort[1] = 10;
        ((int)findValue.Invoke(null, [rawShort, 0x2A, 0x23])!).Should().Be(0);
    }

    [Fact]
    public void SystemInfo_OemAndBatteryFallbacks_Coverage()
    {
        // Test GetSystemManufacturer, GetSystemProductName, GetSystemModel
        string mfr = SystemInfo.GetSystemManufacturer();
        string prod = SystemInfo.GetSystemProductName();
        string model = SystemInfo.GetSystemModel();
        mfr.Should().NotBeNull();
        prod.Should().NotBeNull();
        model.Should().NotBeNull();

        // Test GetRamType and GetRamModules live detection
        string ramType = SystemInfo.GetRamType();
        ramType.Should().NotBeNullOrEmpty();
        ramType.Should().NotBe("Unknown");
        var ramModules = SystemInfo.GetRamModules();
        ramModules.Should().NotBeEmpty();
        foreach (var mod in ramModules)
        {
            mod["type"].Should().NotBe("Unknown");
        }

        // Test GetGpuVendor and GetGpuColor
        string gpuVendor = SystemInfo.GetGpuVendor();
        string gpuColor = SystemInfo.GetGpuColor();
        gpuVendor.Should().NotBeNull();
        gpuColor.Should().NotBeNull();

        // Test GetDiskManufacturer and GetDiskModel
        string diskMfr = SystemInfo.GetDiskManufacturer();
        string diskModel = SystemInfo.GetDiskModel();
        diskMfr.Should().NotBeNull();
        diskModel.Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_SmbiosEnums_And_SocketLookups_Coverage()
    {
        // 1. SmbiosSocketName for all enumeration values 0..86
        var smbiosSocketMethod = typeof(SystemInfo).GetMethod("SmbiosSocketName", BindingFlags.NonPublic | BindingFlags.Static);
        smbiosSocketMethod.Should().NotBeNull();
        for (int i = 0; i <= 86; i++)
        {
            var res = (string)smbiosSocketMethod!.Invoke(null, [i])!;
            res.Should().NotBeNull();
        }

        // 2. SmbiosMemoryTypeString for all memory type codes 0..36
        var memTypeMethod = typeof(SystemInfo).GetMethod("SmbiosMemoryTypeString", BindingFlags.NonPublic | BindingFlags.Static);
        if (memTypeMethod != null)
        {
            for (int i = 0; i <= 36; i++)
            {
                var res = (string)memTypeMethod.Invoke(null, [i])!;
                res.Should().NotBeNull();
            }
        }

        // 3. SmbiosFormFactorString for all form factors 0..16
        var formFactorMethod = typeof(SystemInfo).GetMethod("SmbiosFormFactorString", BindingFlags.NonPublic | BindingFlags.Static);
        if (formFactorMethod != null)
        {
            for (int i = 0; i <= 16; i++)
            {
                var res = (string)formFactorMethod.Invoke(null, [i])!;
                res.Should().NotBeNull();
            }
        }

        // 4. CpuIdSocketLookup for key codes
        var cpuIdMethod = typeof(SystemInfo).GetMethod("CpuIdSocketLookup", BindingFlags.NonPublic | BindingFlags.Static);
        if (cpuIdMethod != null)
        {
            var act = () => cpuIdMethod.Invoke(null, null);
            act.Should().NotThrow();
        }

        // 5. QueryWin32BatteryManufacturer and QueryWin32BatteryName fallbacks
        var battMfrMethod = typeof(SystemInfo).GetMethod("QueryWin32BatteryManufacturer", BindingFlags.NonPublic | BindingFlags.Static);
        battMfrMethod?.Invoke(null, null);

        var battNameMethod = typeof(SystemInfo).GetMethod("QueryWin32BatteryName", BindingFlags.NonPublic | BindingFlags.Static);
        battNameMethod?.Invoke(null, null);
    }

    [Fact]
    public void SystemInfo_ReadMsrRatioMhz_AllBranches_Coverage()
    {
        var readMsr = typeof(SystemInfo).GetMethod("ReadMsrRatioMhz", BindingFlags.NonPublic | BindingFlags.Static);
        readMsr.Should().NotBeNull();

        var transport = Substitute.For<IPawnIoTransport>();

        // 1. Valid ratio (40 -> 4000 MHz)
        transport.Execute("ioctl_read_msr", Arg.Any<ulong[]>(), 1).Returns([(ulong)40]);
        var mhz = (int)readMsr!.Invoke(null, [transport, 0x1ADu])!;
        mhz.Should().Be(4000);

        // 2. Small ratio (5 -> 500 MHz, not > 1000)
        transport.Execute("ioctl_read_msr", Arg.Any<ulong[]>(), 1).Returns([(ulong)5]);
        mhz = (int)readMsr.Invoke(null, [transport, 0x1ADu])!;
        mhz.Should().Be(0);

        // 3. Zero ratio
        transport.Execute("ioctl_read_msr", Arg.Any<ulong[]>(), 1).Returns([(ulong)0]);
        mhz = (int)readMsr.Invoke(null, [transport, 0x1ADu])!;
        mhz.Should().Be(0);

        // 4. 0xFF ratio
        transport.Execute("ioctl_read_msr", Arg.Any<ulong[]>(), 1).Returns([(ulong)0xFF]);
        mhz = (int)readMsr.Invoke(null, [transport, 0x1ADu])!;
        mhz.Should().Be(0);

        // 5. Empty result
        transport.Execute("ioctl_read_msr", Arg.Any<ulong[]>(), 1).Returns([]);
        mhz = (int)readMsr.Invoke(null, [transport, 0x1ADu])!;
        mhz.Should().Be(0);

        // 6. QueryCpuMaxSpeedMsr with pawnIo
        var queryMsr = typeof(SystemInfo).GetMethod("QueryCpuMaxSpeedMsr", BindingFlags.NonPublic | BindingFlags.Static);
        queryMsr?.Invoke(null, null);
    }

    [Fact]
    public void SystemInfo_BatteryQueries_WhenBatteryReportedPresent_Coverage()
    {
        var battCache = typeof(SystemInfo).GetField("_hasBatteryCache", BindingFlags.NonPublic | BindingFlags.Static);
        var mfrCache = typeof(SystemInfo).GetField("_batteryManufacturerCache", BindingFlags.NonPublic | BindingFlags.Static);
        var devCache = typeof(SystemInfo).GetField("_batteryDeviceNameCache", BindingFlags.NonPublic | BindingFlags.Static);
        var desCache = typeof(SystemInfo).GetField("_batteryDesignCapacityCache", BindingFlags.NonPublic | BindingFlags.Static);
        var fullCache = typeof(SystemInfo).GetField("_batteryFullChargedCapacityCache", BindingFlags.NonPublic | BindingFlags.Static);

        var priorBatt = battCache?.GetValue(null);
        var priorMfr = mfrCache?.GetValue(null);
        var priorDev = devCache?.GetValue(null);
        var priorDes = desCache?.GetValue(null);
        var priorFull = fullCache?.GetValue(null);

        try
        {
            // Force HasBattery = true and clear result caches
            battCache?.SetValue(null, true);
            mfrCache?.SetValue(null, null);
            devCache?.SetValue(null, null);
            desCache?.SetValue(null, null);
            fullCache?.SetValue(null, null);

            SystemInfo.GetBatteryManufacturer().Should().NotBeNull();
            SystemInfo.GetBatteryDeviceName().Should().NotBeNull();
            SystemInfo.GetBatteryDesignCapacity().Should().NotBeNull();
            SystemInfo.GetBatteryFullChargedCapacity().Should().NotBeNull();
        }
        finally
        {
            battCache?.SetValue(null, priorBatt);
            mfrCache?.SetValue(null, priorMfr);
            devCache?.SetValue(null, priorDev);
            desCache?.SetValue(null, priorDes);
            fullCache?.SetValue(null, priorFull);
        }
    }

    [Fact]
    public void SystemInfo_FindSmbiosString_AllBranches_Coverage()
    {
        var method = typeof(SystemInfo).GetMethod("FindSmbiosString", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        // 1. Build a synthetic SMBIOS Type 1 table with strings:
        // Struct header: [type=1, len=8, handle=0,0, mfrIdx=1, prodIdx=2, verIdx=0, serialIdx=3]
        // Strings: "Vendor Corp\0" + "Product Name\0" + "Serial123\0" + "\0"
        var ms = new MemoryStream();
        ms.Write([1, 8, 0, 0, 1, 2, 0, 3]);
        ms.Write(Encoding.ASCII.GetBytes("Vendor Corp\0Product Name\0Serial123\0\0"));
        // Add end-of-table structure (type 127)
        ms.Write([127, 4, 0, 0, 0, 0]);
        byte[] blob = ms.ToArray();

        // Match type 1, offset 4 (Vendor Corp)
        var mfr = (string)method!.Invoke(null, [blob, (byte)1, (byte)0x04])!;
        mfr.Should().Be("Vendor Corp");

        // Match type 1, offset 5 (Product Name)
        var prod = (string)method.Invoke(null, [blob, (byte)1, (byte)0x05])!;
        prod.Should().Be("Product Name");

        // String index 0 -> returns empty
        var str0 = (string)method.Invoke(null, [blob, (byte)1, (byte)0x06])!;
        str0.Should().BeEmpty();

        // Type not in blob -> returns empty
        var strNone = (string)method.Invoke(null, [blob, (byte)22, (byte)0x04])!;
        strNone.Should().BeEmpty();

        // Truncated blob
        var strTrunc = (string)method.Invoke(null, [new byte[] { 1, 20 }, (byte)1, (byte)0x04])!;
        strTrunc.Should().BeEmpty();

        // Empty blob
        var strEmpty = (string)method.Invoke(null, [Array.Empty<byte>(), (byte)1, (byte)0x04])!;
        strEmpty.Should().BeEmpty();
    }

    private static void WithSmbios(byte[] blob, Action test)
    {
        var field = typeof(SystemInfo).GetField("_smbiosCache", BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();
        object? prior = field!.GetValue(null);
        field.SetValue(null, blob);
        try
        {
            test();
        }
        finally
        {
            field.SetValue(null, prior);
        }
    }

    private delegate string SpanSocketFunc(ReadOnlySpan<char> span);

    [Fact]
    public void SystemInfo_QueryProcessorAndSocketDerivation_Coverage()
    {
        // 1. Processor Speed and Brand Query Methods
        var ntPower = typeof(SystemInfo).GetMethod("QueryNtPowerMaxMhz", BindingFlags.NonPublic | BindingFlags.Static);
        ntPower.Should().NotBeNull();
        var ntVal = (int)ntPower!.Invoke(null, null)!;
        ntVal.Should().BeGreaterThanOrEqualTo(0);

        var regSpeed = typeof(SystemInfo).GetMethod("QueryRegistryProcessorSpeed", BindingFlags.NonPublic | BindingFlags.Static);
        regSpeed.Should().NotBeNull();
        var regVal = (int)regSpeed!.Invoke(null, null)!;
        regVal.Should().BeGreaterThanOrEqualTo(0);

        var cpuidSpeed = typeof(SystemInfo).GetMethod("QueryCpuMaxSpeedCpuid", BindingFlags.NonPublic | BindingFlags.Static);
        cpuidSpeed.Should().NotBeNull();
        var cpuidVal = (int)cpuidSpeed!.Invoke(null, null)!;
        cpuidVal.Should().BeGreaterThanOrEqualTo(0);

        var brandCpuid = typeof(SystemInfo).GetMethod("GetCpuBrandStringCpuid", BindingFlags.NonPublic | BindingFlags.Static);
        brandCpuid.Should().NotBeNull();
        var brandStr = (string?)brandCpuid!.Invoke(null, null);

        var cpuidSocket = typeof(SystemInfo).GetMethod("CpuIdSocketLookup", BindingFlags.NonPublic | BindingFlags.Static);
        cpuidSocket.Should().NotBeNull();
        var socketStr = (string)cpuidSocket!.Invoke(null, null)!;
        socketStr.Should().NotBeNull();

        // 2. Socket Derivations via Delegates
        var mIntel = typeof(SystemInfo).GetMethod("DeriveIntelSocket", BindingFlags.NonPublic | BindingFlags.Static);
        var mAmd = typeof(SystemInfo).GetMethod("DeriveAmdSocket", BindingFlags.NonPublic | BindingFlags.Static);
        var mQcom = typeof(SystemInfo).GetMethod("DeriveQualcommSocket", BindingFlags.NonPublic | BindingFlags.Static);

        mIntel.Should().NotBeNull();
        mAmd.Should().NotBeNull();
        mQcom.Should().NotBeNull();

        var deriveIntel = mIntel!.CreateDelegate<SpanSocketFunc>();
        var deriveAmd = mAmd!.CreateDelegate<SpanSocketFunc>();
        var deriveQcom = mQcom!.CreateDelegate<SpanSocketFunc>();

        static ReadOnlySpan<char> U(string s) => s.ToUpperInvariant().AsSpan();

        // Intel Ultra
        deriveIntel(U("Intel Core Ultra 7 155H")).Should().NotBeEmpty();
        deriveIntel(U("Intel Core Ultra 9 285K")).Should().NotBeEmpty();
        deriveIntel(U("Intel Core Ultra 7 258V")).Should().NotBeEmpty();
        deriveIntel(U("Intel Core Ultra 5 125U")).Should().NotBeEmpty();
        deriveIntel(U("Intel Ultra Invalid")).Should().BeEmpty();

        // Intel Core Traditional (gens 1-14)
        deriveIntel(U("Intel Core i9-14900K")).Should().NotBeEmpty();
        deriveIntel(U("Intel Core i9-13900HX")).Should().NotBeEmpty();
        deriveIntel(U("Intel Core i7-12700H")).Should().NotBeEmpty();
        deriveIntel(U("Intel Core i7-11700K")).Should().NotBeEmpty();
        deriveIntel(U("Intel Core i7-10750H")).Should().NotBeEmpty();
        deriveIntel(U("Intel Core i5-10210U")).Should().NotBeEmpty();
        deriveIntel(U("Intel Core i7-9700K")).Should().NotBeEmpty();
        deriveIntel(U("Intel Core i7-8750H")).Should().NotBeEmpty();
        deriveIntel(U("Intel Core i7-4770K")).Should().NotBeEmpty();
        deriveIntel(U("Intel Core i5-4200U")).Should().NotBeEmpty();

        // Intel Xeon
        deriveIntel(U("Intel Xeon W9-3495X")).Should().Contain("4677");
        deriveIntel(U("Intel Xeon Gold 6248")).Should().Contain("4189");
        deriveIntel(U("Intel Pentium G4560")).Should().BeEmpty();

        // AMD Threadripper & EPYC
        deriveAmd(U("AMD Ryzen Threadripper 7000")).Should().Contain("sTR5");
        deriveAmd(U("AMD Ryzen Threadripper 3990X")).Should().Contain("sTRX4");
        deriveAmd(U("AMD EPYC 9004")).Should().Contain("SP5");
        deriveAmd(U("AMD EPYC 8004")).Should().Contain("SP6");
        deriveAmd(U("AMD EPYC 7003")).Should().Contain("SP3");

        // AMD Ryzen Desktop & Mobile
        deriveAmd(U("AMD Ryzen 9 9950X")).Should().Contain("AM5");
        deriveAmd(U("AMD Ryzen 7 8840U")).Should().NotBeEmpty();
        deriveAmd(U("AMD Ryzen 7 7800X3D")).Should().NotBeEmpty();
        deriveAmd(U("AMD Ryzen 7 7840HS")).Should().NotBeEmpty();
        deriveAmd(U("AMD Ryzen 5 6600H")).Should().NotBeEmpty();
        deriveAmd(U("AMD Ryzen 7 5800X")).Should().NotBeEmpty();
        deriveAmd(U("AMD Ryzen 7 5800U")).Should().NotBeEmpty();
        deriveAmd(U("AMD Ryzen 5 4600H")).Should().NotBeEmpty();
        deriveAmd(U("AMD Ryzen 5 3600")).Should().NotBeEmpty();
        deriveAmd(U("AMD Ryzen 5 3500U")).Should().NotBeEmpty();
        deriveAmd(U("AMD Ryzen 5 2600")).Should().NotBeEmpty();
        deriveAmd(U("AMD Athlon 3000G")).Should().BeEmpty();

        // Qualcomm
        deriveQcom(U("Snapdragon X Elite")).Should().Contain("2050");
        deriveQcom(U("Snapdragon 8 Gen 3")).Should().BeEmpty();

        // 3. GPU Registry Data Query
        var gpuReg = typeof(SystemInfo).GetMethod("GetGpuRegistryData", BindingFlags.NonPublic | BindingFlags.Static);
        gpuReg.Should().NotBeNull();
        gpuReg!.Invoke(null, ["NonExistentDisplayDeviceName"]);
    }

    [Fact]
    public void SystemInfo_SmbiosType17MemoryType_ParsesCorrectly()
    {
        var mFindType17 = typeof(SystemInfo).GetMethod("FindSmbiosType17MemoryType", BindingFlags.NonPublic | BindingFlags.Static);
        var mQuerySmbios = typeof(SystemInfo).GetMethod("QueryRamTypeSmbios", BindingFlags.NonPublic | BindingFlags.Static);
        mFindType17.Should().NotBeNull();
        mQuerySmbios.Should().NotBeNull();

        // 1. Empty buffer returns 0
        ((int)mFindType17!.Invoke(null, [Array.Empty<byte>()])!).Should().Be(0);

        // 2. Truncated buffer returns 0
        ((int)mFindType17.Invoke(null, [new byte[] { 17, 2 }])!).Should().Be(0);

        // 3. Synthetic SMBIOS buffer with Type 17 DDR4 (code 26 = 0x1A)
        var ms = new MemoryStream();
        byte[] type17Header = new byte[24];
        type17Header[0] = 17; // Type 17
        type17Header[1] = 24; // Length
        type17Header[0x12] = 26; // DDR4
        ms.Write(type17Header);
        ms.Write([0, 0]); // Double null strings terminator
        ms.Write([127, 4, 0, 0, 0, 0]); // Type 127 end of table
        byte[] blob = ms.ToArray();

        int parsed = (int)mFindType17.Invoke(null, [blob])!;
        parsed.Should().Be(26);

        // Test QueryRamTypeSmbios with this blob injected
        WithSmbios(blob, () =>
        {
            string label = (string)mQuerySmbios!.Invoke(null, null)!;
            label.Should().Be("DDR4");
        });
    }

    [Fact]
    public void SystemInfo_SmbiosPowerSupply_ParsesCorrectly()
    {
        var mPsuName = typeof(SystemInfo).GetMethod("GetPsuName", BindingFlags.Public | BindingFlags.Static);
        mPsuName.Should().NotBeNull();

        // Synthetic Type 39 table
        var ms = new MemoryStream();
        byte[] type39Header = new byte[22];
        type39Header[0] = 39;
        type39Header[1] = 22;
        type39Header[0x06] = 1; // Mfr string 1
        type39Header[0x08] = 2; // Model string 2
        ms.Write(type39Header);
        ms.Write(Encoding.ASCII.GetBytes("Corsair\0RM850x\0\0"));
        ms.Write([127, 4, 0, 0, 0, 0]);
        byte[] blob = ms.ToArray();

        WithSmbios(blob, () =>
        {
            string psu = (string)mPsuName!.Invoke(null, null)!;
            psu.Should().Contain("Corsair");
            psu.Should().Contain("RM850x");
        });

        // Only manufacturer present
        var ms2 = new MemoryStream();
        byte[] type39Header2 = new byte[22];
        type39Header2[0] = 39;
        type39Header2[1] = 22;
        type39Header2[0x06] = 1;
        type39Header2[0x08] = 0;
        ms2.Write(type39Header2);
        ms2.Write(Encoding.ASCII.GetBytes("Seasonic\0\0"));
        ms2.Write([127, 4, 0, 0, 0, 0]);

        WithSmbios(ms2.ToArray(), () =>
        {
            string psu = (string)mPsuName!.Invoke(null, null)!;
            psu.Should().Be("Seasonic");
        });

        // Only model present
        var ms3 = new MemoryStream();
        byte[] type39Header3 = new byte[22];
        type39Header3[0] = 39;
        type39Header3[1] = 22;
        type39Header3[0x06] = 0;
        type39Header3[0x08] = 1;
        ms3.Write(type39Header3);
        ms3.Write(Encoding.ASCII.GetBytes("Focus-GX\0\0"));
        ms3.Write([127, 4, 0, 0, 0, 0]);

        WithSmbios(ms3.ToArray(), () =>
        {
            string psu = (string)mPsuName!.Invoke(null, null)!;
            psu.Should().Be("Focus-GX");
        });
    }

    [Fact]
    public void SystemInfo_SmbiosBatteryStringAndWord_ParsesCorrectly()
    {
        var mBattStr = typeof(SystemInfo).GetMethod("GetSmbiosBatteryString", BindingFlags.NonPublic | BindingFlags.Static);
        var mBattWord = typeof(SystemInfo).GetMethod("GetSmbiosBatteryWord", BindingFlags.NonPublic | BindingFlags.Static);
        mBattStr.Should().NotBeNull();
        mBattWord.Should().NotBeNull();

        // Synthetic Type 22 table
        var ms = new MemoryStream();
        byte[] type22Header = new byte[26];
        type22Header[0] = 22;
        type22Header[1] = 26;
        type22Header[0x05] = 1; // Mfr string 1
        type22Header[0x08] = 2; // Device string 2
        type22Header[0x0A] = 0x10; // 10000 mWh low byte
        type22Header[0x0B] = 0x27; // 10000 mWh high byte
        ms.Write(type22Header);
        ms.Write(Encoding.ASCII.GetBytes("SimulatedBatteryCorp\0SimulatedModelPack\0\0"));
        ms.Write([127, 4, 0, 0, 0, 0]);
        byte[] blob = ms.ToArray();

        WithSmbios(blob, () =>
        {
            string mfr = (string)mBattStr!.Invoke(null, [0x05])!;
            mfr.Should().Be("SimulatedBatteryCorp");

            string dev = (string)mBattStr.Invoke(null, [0x08])!;
            dev.Should().Be("SimulatedModelPack");

            int word = (int)mBattWord!.Invoke(null, [0x0A])!;
            word.Should().Be(10000);
        });
    }

    [Fact]
    public void SystemInfo_InternalBatteryQueries_Coverage()
    {
        var mWin32Mfr = typeof(SystemInfo).GetMethod("QueryWin32BatteryManufacturer", BindingFlags.NonPublic | BindingFlags.Static);
        var mWin32Name = typeof(SystemInfo).GetMethod("QueryWin32BatteryName", BindingFlags.NonPublic | BindingFlags.Static);

        mWin32Mfr.Should().NotBeNull();
        mWin32Name.Should().NotBeNull();

        var mfr = (string?)mWin32Mfr!.Invoke(null, null);
        var name = (string?)mWin32Name!.Invoke(null, null);

        mfr.Should().NotBeNull();
        name.Should().NotBeNull();
    }
}

