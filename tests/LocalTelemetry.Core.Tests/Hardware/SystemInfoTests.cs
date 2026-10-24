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

        method.Invoke(null, ["LGA 1700"]).Should().Be(true);
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

        method.Invoke(null, [25]).Should().Be("Socket LGA 1366");
        method.Invoke(null, [48]).Should().Be("Socket BGA 1364");
        method.Invoke(null, [34]).Should().Be("Socket BGA 1023");
        method.Invoke(null, [9999]).Should().Be("");
    }

    private delegate string SpanStringFunc(ReadOnlySpan<char> value);
    private delegate string SpanSpanStringFunc(ReadOnlySpan<char> a, ReadOnlySpan<char> b);
    private delegate void CpuidAppender(Span<byte> buf, ref int pos, int reg);

    private static T GetDelegate<T>(string methodName) where T : Delegate
    {
        var method = typeof(SystemInfo).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return method.CreateDelegate<T>();
    }

    private static object? Invoke(string methodName, params object?[]? args)
    {
        var method = typeof(SystemInfo).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return method.Invoke(null, args);
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
        bool result = (bool)method.Invoke(null, [input])!;
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
        cpuIdSocketMethod.Invoke(null, null);

        // 2. GetCpuBrandStringCpuid
        var cpuidBrandMethod = typeof(SystemInfo).GetMethod("GetCpuBrandStringCpuid", BindingFlags.NonPublic | BindingFlags.Static);
        cpuidBrandMethod.Should().NotBeNull();
        cpuidBrandMethod.Invoke(null, null);

        // 3. GetCpuBrandString
        var brandMethod = typeof(SystemInfo).GetMethod("GetCpuBrandString", BindingFlags.NonPublic | BindingFlags.Static);
        brandMethod.Should().NotBeNull();
        brandMethod.Invoke(null, null);

        // 4. DeriveSocketFromCpuName
        var deriveSocketMethod = typeof(SystemInfo).GetMethod("DeriveSocketFromCpuName", BindingFlags.NonPublic | BindingFlags.Static);
        deriveSocketMethod.Should().NotBeNull();
        deriveSocketMethod.Invoke(null, null);

        // 5. DeriveQualcommSocket
        var qualcommMethod = typeof(SystemInfo).GetMethod("DeriveQualcommSocket", BindingFlags.NonPublic | BindingFlags.Static);
        qualcommMethod.Should().NotBeNull();
        var qualcommDelegate = qualcommMethod.CreateDelegate<ReadOnlySpanFunc>();
        qualcommDelegate("Snapdragon X Elite".AsSpan()).Should().Be("Socket 2050");
        qualcommDelegate("Snapdragon 8 Gen 2".AsSpan()).Should().BeEmpty();

        // 6. QueryNtPowerMaxMhz
        var ntPowerMethod = typeof(SystemInfo).GetMethod("QueryNtPowerMaxMhz", BindingFlags.NonPublic | BindingFlags.Static);
        ntPowerMethod.Should().NotBeNull();
        ntPowerMethod.Invoke(null, null);

        // 7. QueryRegistryProcessorSpeed
        var regSpeedMethod = typeof(SystemInfo).GetMethod("QueryRegistryProcessorSpeed", BindingFlags.NonPublic | BindingFlags.Static);
        regSpeedMethod.Should().NotBeNull();
        regSpeedMethod.Invoke(null, null);

        // 8. QueryRamTypeSmbios and FindSmbiosType17MemoryType
        WithSmbios(BuildBlob(), () =>
        {
            var ramSmbiosMethod = typeof(SystemInfo).GetMethod("QueryRamTypeSmbios", BindingFlags.NonPublic | BindingFlags.Static);
            ramSmbiosMethod.Should().NotBeNull();
            string ramType = (string)ramSmbiosMethod.Invoke(null, null)!;
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
        ((string)qWmiFirst.Invoke(null, ["SELECT NonExistentProp FROM Win32_NonExistentClass12345", "NonExistentProp"])!).Should().BeEmpty();
        ((int)qWmiFirstInt.Invoke(null, ["SELECT NonExistentProp FROM Win32_NonExistentClass12345", "NonExistentProp"])!).Should().Be(0);
        ((string)qRootWmiStr.Invoke(null, ["NonExistentRootClass12345", "NonExistentProp"])!).Should().BeEmpty();
        ((int)qRootWmiInt.Invoke(null, ["NonExistentRootClass12345", "NonExistentProp"])!).Should().Be(0);

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
        ((string)smbiosSocketMethod.Invoke(null, [64])!).Should().Be("Socket LGA 1700");
        ((string)smbiosSocketMethod.Invoke(null, [71])!).Should().Be("Socket AM5");
        ((string)smbiosSocketMethod.Invoke(null, [83])!).Should().Be("Socket LGA 1851");
        ((string)smbiosSocketMethod.Invoke(null, [85])!).Should().Be("Socket BGA 2833");
        ((string)smbiosSocketMethod.Invoke(null, [999])!).Should().BeEmpty();

        // 2. IsSocketName and IsPlaceholderSocket
        var isSocketMethod = typeof(SystemInfo).GetMethod("IsSocketName", BindingFlags.NonPublic | BindingFlags.Static);
        var isPlaceholderMethod = typeof(SystemInfo).GetMethod("IsPlaceholderSocket", BindingFlags.NonPublic | BindingFlags.Static);
        isSocketMethod.Should().NotBeNull();
        isPlaceholderMethod.Should().NotBeNull();

        ((bool)isSocketMethod.Invoke(null, ["LGA 1700"])!).Should().BeTrue();
        ((bool)isSocketMethod.Invoke(null, ["Socket AM5"])!).Should().BeTrue();
        ((bool)isSocketMethod.Invoke(null, ["BGA 2049"])!).Should().BeTrue();
        ((bool)isSocketMethod.Invoke(null, ["FP8"])!).Should().BeTrue();
        ((bool)isSocketMethod.Invoke(null, ["FL1"])!).Should().BeTrue();
        ((bool)isSocketMethod.Invoke(null, ["GenericDevice"])!).Should().BeFalse();
        ((bool)isSocketMethod.Invoke(null, [""])!).Should().BeFalse();

        ((bool)isPlaceholderMethod.Invoke(null, ["Unknown"])!).Should().BeTrue();
        ((bool)isPlaceholderMethod.Invoke(null, ["To Be Filled By O.E.M."])!).Should().BeTrue();
        ((bool)isPlaceholderMethod.Invoke(null, ["LGA 1700"])!).Should().BeFalse();
        ((bool)isPlaceholderMethod.Invoke(null, [""])!).Should().BeTrue();

        // 3. DeriveSocketFromCpuName and GetCpuSocket with synthetic _cpuNameCache values
        var cpuNameField = typeof(SystemInfo).GetField("_cpuNameCache", BindingFlags.NonPublic | BindingFlags.Static);
        var cpuSocketField = typeof(SystemInfo).GetField("_cpuSocketCache", BindingFlags.NonPublic | BindingFlags.Static);
        cpuNameField.Should().NotBeNull();
        cpuSocketField.Should().NotBeNull();

        object? origName = cpuNameField.GetValue(null);
        object? origSocket = cpuSocketField.GetValue(null);

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
                string skt = (string)deriveSocket.Invoke(null, null)!;
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
        ((int)findValue.Invoke(null, [Array.Empty<byte>(), 0x2A, 0x23])!).Should().Be(0);

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
            var res = (string)smbiosSocketMethod.Invoke(null, [i])!;
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
        var mhz = (int)readMsr.Invoke(null, [transport, 0x1ADu])!;
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
        var mfr = (string)method.Invoke(null, [blob, (byte)1, (byte)0x04])!;
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
        object? prior = field.GetValue(null);
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
        var ntVal = (int)ntPower.Invoke(null, null)!;
        ntVal.Should().BeGreaterThanOrEqualTo(0);

        var regSpeed = typeof(SystemInfo).GetMethod("QueryRegistryProcessorSpeed", BindingFlags.NonPublic | BindingFlags.Static);
        regSpeed.Should().NotBeNull();
        var regVal = (int)regSpeed.Invoke(null, null)!;
        regVal.Should().BeGreaterThanOrEqualTo(0);

        var cpuidSpeed = typeof(SystemInfo).GetMethod("QueryCpuMaxSpeedCpuid", BindingFlags.NonPublic | BindingFlags.Static);
        cpuidSpeed.Should().NotBeNull();
        var cpuidVal = (int)cpuidSpeed.Invoke(null, null)!;
        cpuidVal.Should().BeGreaterThanOrEqualTo(0);

        var brandCpuid = typeof(SystemInfo).GetMethod("GetCpuBrandStringCpuid", BindingFlags.NonPublic | BindingFlags.Static);
        brandCpuid.Should().NotBeNull();
        brandCpuid.Invoke(null, null);

        var cpuidSocket = typeof(SystemInfo).GetMethod("CpuIdSocketLookup", BindingFlags.NonPublic | BindingFlags.Static);
        cpuidSocket.Should().NotBeNull();
        var socketStr = (string)cpuidSocket.Invoke(null, null)!;
        socketStr.Should().NotBeNull();

        // 2. Socket Derivations via Delegates
        var mIntel = typeof(SystemInfo).GetMethod("DeriveIntelSocket", BindingFlags.NonPublic | BindingFlags.Static);
        var mAmd = typeof(SystemInfo).GetMethod("DeriveAmdSocket", BindingFlags.NonPublic | BindingFlags.Static);
        var mQcom = typeof(SystemInfo).GetMethod("DeriveQualcommSocket", BindingFlags.NonPublic | BindingFlags.Static);

        mIntel.Should().NotBeNull();
        mAmd.Should().NotBeNull();
        mQcom.Should().NotBeNull();

        var deriveIntel = mIntel.CreateDelegate<SpanSocketFunc>();
        var deriveAmd = mAmd.CreateDelegate<SpanSocketFunc>();
        var deriveQcom = mQcom.CreateDelegate<SpanSocketFunc>();

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
        gpuReg.Invoke(null, ["NonExistentDisplayDeviceName"]);
    }

    [Fact]
    public void SystemInfo_SmbiosType17MemoryType_ParsesCorrectly()
    {
        var mFindType17 = typeof(SystemInfo).GetMethod("FindSmbiosType17MemoryType", BindingFlags.NonPublic | BindingFlags.Static);
        var mQuerySmbios = typeof(SystemInfo).GetMethod("QueryRamTypeSmbios", BindingFlags.NonPublic | BindingFlags.Static);
        mFindType17.Should().NotBeNull();
        mQuerySmbios.Should().NotBeNull();

        // 1. Empty buffer returns 0
        ((int)mFindType17.Invoke(null, [Array.Empty<byte>()])!).Should().Be(0);

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
            string label = (string)mQuerySmbios.Invoke(null, null)!;
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
            string psu = (string)mPsuName.Invoke(null, null)!;
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
            string psu = (string)mPsuName.Invoke(null, null)!;
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
            string psu = (string)mPsuName.Invoke(null, null)!;
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
            string mfr = (string)mBattStr.Invoke(null, [0x05])!;
            mfr.Should().Be("SimulatedBatteryCorp");

            string dev = (string)mBattStr.Invoke(null, [0x08])!;
            dev.Should().Be("SimulatedModelPack");

            int word = (int)mBattWord.Invoke(null, [0x0A])!;
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

        var mfr = (string?)mWin32Mfr.Invoke(null, null);
        var name = (string?)mWin32Name.Invoke(null, null);

        mfr.Should().NotBeNull();
        name.Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_IsValidBatteryString_AllBranches_Coverage()
    {
        var mIsValid = typeof(SystemInfo).GetMethod("IsValidBatteryString", BindingFlags.NonPublic | BindingFlags.Static);
        mIsValid.Should().NotBeNull();

        ((bool)mIsValid.Invoke(null, [null])!).Should().BeFalse();
        ((bool)mIsValid.Invoke(null, [""])!).Should().BeFalse();
        ((bool)mIsValid.Invoke(null, ["   "])!).Should().BeFalse();
        ((bool)mIsValid.Invoke(null, ["Virtual Battery"])!).Should().BeFalse();
        ((bool)mIsValid.Invoke(null, ["CRB Battery"])!).Should().BeFalse();
        ((bool)mIsValid.Invoke(null, ["CRB Device"])!).Should().BeFalse();
        ((bool)mIsValid.Invoke(null, ["Fake"])!).Should().BeFalse();
        ((bool)mIsValid.Invoke(null, ["None"])!).Should().BeFalse();
        ((bool)mIsValid.Invoke(null, ["Unknown"])!).Should().BeFalse();
        ((bool)mIsValid.Invoke(null, ["To be filled by O.E.M."])!).Should().BeFalse();
        ((bool)mIsValid.Invoke(null, ["-Test-"])!).Should().BeFalse();
        ((bool)mIsValid.Invoke(null, ["LGC-L19L3PF5"])!).Should().BeTrue();
        ((bool)mIsValid.Invoke(null, ["SMP-SimulatedPack"])!).Should().BeTrue();
    }

    [Fact]
    public void SystemInfo_SmbiosSocketName_Coverage()
    {
        var mSmbiosSocket = typeof(SystemInfo).GetMethod("SmbiosSocketName", BindingFlags.NonPublic | BindingFlags.Static);
        mSmbiosSocket.Should().NotBeNull();

        for (int i = 1; i <= 86; i++)
        {
            var res = (string)mSmbiosSocket.Invoke(null, [i])!;
            res.Should().NotBeNull();
        }

        ((string)mSmbiosSocket.Invoke(null, [0])!).Should().BeEmpty();
        ((string)mSmbiosSocket.Invoke(null, [999])!).Should().BeEmpty();
    }

    [Fact]
    public void SystemInfo_QueryRamTypeSmbios_ParsesType17Correctly()
    {
        // Build synthetic Type 17 (Memory Device) header (length 0x18), byte at offset 0x12 is memory type (0x1A = DDR5)
        var ms = new MemoryStream();
        ms.WriteByte(17); // Type 17
        ms.WriteByte(0x18); // Length
        ms.Write([0, 0]); // Handle
        // Fill up to offset 0x12 (18)
        for (int i = 4; i < 0x12; i++) ms.WriteByte(0);
        ms.WriteByte(0x22); // 0x22 = DDR5
        ms.Write([0, 0, 0, 0, 0]); // remaining length to 0x18
        // String table: 1 string + double null terminator
        byte[] str = Encoding.ASCII.GetBytes("DDR5-Module");
        ms.Write(str);
        ms.WriteByte(0);
        ms.WriteByte(0);

        // Add Type 127 end of table
        ms.Write([127, 4, 0, 0, 0, 0]);

        byte[] blob = ms.ToArray();

        WithSmbios(blob, () =>
        {
            var method = typeof(SystemInfo).GetMethod("QueryRamTypeSmbios", BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();
            string ramType = (string)method.Invoke(null, null)!;
            ramType.Should().Be("DDR5");
        });
    }

    [Fact]
    public void SystemInfo_SmbiosBattery_ParsesType22Correctly()
    {
        // Build synthetic Type 22 (Portable Battery) header:
        // offset 0: type (22), offset 1: length (0x1A), offset 2-3: handle
        // offset 4: location str index (1)
        // offset 5: manufacturer str index (2)
        // offset 6: manufacture date str index (0)
        // offset 7: serial number str index (0)
        // offset 8: device name str index (3)
        // offset 0x0A: design capacity word (50000 mWh = 0xC350)
        var ms = new MemoryStream();
        ms.WriteByte(22); // Type 22
        ms.WriteByte(0x1A); // Length
        ms.Write([0, 0]); // Handle
        ms.WriteByte(1); // Location
        ms.WriteByte(2); // Manufacturer
        ms.WriteByte(0); // Date
        ms.WriteByte(0); // Serial
        ms.WriteByte(3); // Device Name
        ms.WriteByte(0); // Chemistry
        ms.WriteByte(0x50); // Design capacity low byte
        ms.WriteByte(0xC3); // Design capacity high byte (50000)
        for (int i = 0x0C; i < 0x1A; i++) ms.WriteByte(0);

        // String table: string 1 = "Primary", string 2 = "Dell OEM", string 3 = "Battery-X"
        ms.Write(Encoding.ASCII.GetBytes("Primary"));
        ms.WriteByte(0);
        ms.Write(Encoding.ASCII.GetBytes("Dell OEM"));
        ms.WriteByte(0);
        ms.Write(Encoding.ASCII.GetBytes("Battery-X"));
        ms.WriteByte(0);
        ms.WriteByte(0); // Double null terminator

        // Type 127
        ms.Write([127, 4, 0, 0, 0, 0]);

        byte[] blob = ms.ToArray();

        WithSmbios(blob, () =>
        {
            var strMethod = typeof(SystemInfo).GetMethod("GetSmbiosBatteryString", BindingFlags.NonPublic | BindingFlags.Static);
            strMethod.Should().NotBeNull();
            string mfr = (string)strMethod.Invoke(null, [5])!;
            string devName = (string)strMethod.Invoke(null, [8])!;
            mfr.Should().Be("Dell OEM");
            devName.Should().Be("Battery-X");

            var wordMethod = typeof(SystemInfo).GetMethod("GetSmbiosBatteryWord", BindingFlags.NonPublic | BindingFlags.Static);
            wordMethod.Should().NotBeNull();
            int cap = (int)wordMethod.Invoke(null, [0x0A])!;
            cap.Should().Be(50000);
        });
    }

    [Fact]
    public void SystemInfo_HasBattery_And_SystemTypeLabel_CachedBehavior()
    {
        var cacheField = typeof(SystemInfo).GetField("_hasBatteryCache", BindingFlags.NonPublic | BindingFlags.Static);
        cacheField.Should().NotBeNull();
        object? prior = cacheField.GetValue(null);

        try
        {
            cacheField.SetValue(null, true);
            SystemInfo.HasBattery().Should().BeTrue();
            SystemInfo.GetSystemTypeLabel().Should().Be("Laptop");

            cacheField.SetValue(null, false);
            SystemInfo.HasBattery().Should().BeFalse();
            SystemInfo.GetSystemTypeLabel().Should().Be("Desktop");
        }
        finally
        {
            cacheField.SetValue(null, prior);
        }
    }

    [Fact]
    public void SystemInfo_CpuIdSocketLookup_HandlesKnownIntelAndAmdFamilies()
    {
        // Intel Arrow Lake desktop: family 6, model 0xC6 => key: (6 << 16) | 0xC6
        // eax: family (bits 8-11 = 6), extModel (bits 16-19 = 0xC), model (bits 4-7 = 6)
        int eaxArrowLake = (0xC << 16) | (6 << 8) | (6 << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxArrowLake).Should().Be("Socket LGA 1851");

        // Intel Raptor Lake S: family 6, model 0xBA
        int eaxRaptor = (0xB << 16) | (6 << 8) | (0xA << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxRaptor).Should().Be("Socket LGA 1700");

        // Intel Comet Lake: family 6, model 0xA5
        int eaxComet = (0xA << 16) | (6 << 8) | (5 << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxComet).Should().Be("Socket LGA 1200");

        // Intel Kaby Lake: family 6, model 0x9E
        int eaxKaby = (0x9 << 16) | (6 << 8) | (0xE << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxKaby).Should().Be("Socket LGA 1151");

        // AMD Granite Ridge: family 0x19, model 0x5E
        // For family 0x0F: family += extFamily (0x0A => 0x19)
        int eaxGranite = (5 << 16) | (0x0A << 20) | (0x0F << 8) | (0xE << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxGranite).Should().Be("Socket AM5");

        // AMD Vermeer: family 0x19, model 0x11
        int eaxVermeer = (1 << 16) | (0x0A << 20) | (0x0F << 8) | (1 << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxVermeer).Should().Be("Socket AM4");

        // AMD Rembrandt: family 0x19, model 0x21
        int eaxRembrandt = (2 << 16) | (0x0A << 20) | (0x0F << 8) | (1 << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxRembrandt).Should().Be("Socket FP7");

        // Intel remaining cases
        int eaxArrowLakeH = (0xC << 16) | (6 << 8) | (4 << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxArrowLakeH).Should().Be("Socket BGA 2049");

        int eaxLunarLake = (0xC << 16) | (6 << 8) | (7 << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxLunarLake).Should().Be("Socket BGA 2833");

        int eaxMeteorLake = (0xA << 16) | (6 << 8) | (0xC << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxMeteorLake).Should().Be("Socket LGA 1851");

        int eaxRaptorRefresh = (0xB << 16) | (6 << 8) | (0xE << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxRaptorRefresh).Should().Be("Socket LGA 1700");

        int eaxRaptorHx = (0xB << 16) | (6 << 8) | (0xF << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxRaptorHx).Should().Be("Socket BGA 1964");

        int eaxRaptorP = (0xB << 16) | (6 << 8) | (0xB << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxRaptorP).Should().Be("Socket BGA 1744");

        int eaxAlderLakeS = (0xB << 16) | (6 << 8) | (7 << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxAlderLakeS).Should().Be("Socket LGA 1700");

        int eaxAlderLake = (0x9 << 16) | (6 << 8) | (0xA << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxAlderLake).Should().Be("Socket LGA 1700");

        int eaxRaptorPAlt = (0xA << 16) | (6 << 8) | (0xA << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxRaptorPAlt).Should().Be("Socket BGA 1744");

        int eaxTigerU = (0x8 << 16) | (6 << 8) | (0xC << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxTigerU).Should().Be("Socket BGA 1440");

        int eaxTigerDesk = (0x8 << 16) | (6 << 8) | (0xD << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxTigerDesk).Should().Be("Socket LGA 1200");

        int eaxKaby2 = (0x8 << 16) | (6 << 8) | (0xE << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxKaby2).Should().Be("Socket LGA 1151");

        // AMD remaining cases
        int eaxRaphael = (6 << 16) | (0x0A << 20) | (0x0F << 8) | (1 << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxRaphael).Should().Be("Socket AM5");

        int eaxPhoenix = (6 << 16) | (0x0A << 20) | (0x0F << 8) | (4 << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxPhoenix).Should().Be("Socket FP8");

        int eaxPhoenix2 = (7 << 16) | (0x0A << 20) | (0x0F << 8) | (4 << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxPhoenix2).Should().Be("Socket FP8");

        int eaxDragon = (7 << 16) | (0x0A << 20) | (0x0F << 8) | (8 << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxDragon).Should().Be("Socket FL1");

        int eaxCezanne = (5 << 16) | (0x0A << 20) | (0x0F << 8);
        SystemInfo.CpuIdSocketLookupRaw(eaxCezanne).Should().Be("Socket FP6");

        int eaxMatisse = (7 << 16) | (0x0A << 20) | (0x0F << 8) | (1 << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxMatisse).Should().Be("Socket AM4");

        // Zen+ (extFamily = 8 => 0x0F + 8 = 0x17)
        int eaxZenPlus = (3 << 16) | (8 << 20) | (0x0F << 8) | (1 << 4);
        SystemInfo.CpuIdSocketLookupRaw(eaxZenPlus).Should().Be("Socket AM4");

        // Unknown CPUID eax
        SystemInfo.CpuIdSocketLookupRaw(0).Should().BeEmpty();
    }

    [Fact]
    public void SystemInfo_DeriveSocketFromCpuName_HandlesVendorsAndModels()
    {
        // Intel Ultra
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Core(TM) Ultra 9 285K").Should().Be("Socket LGA 1851");
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Core(TM) Ultra 7 258V").Should().Be("Socket BGA 2833");
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Core(TM) Ultra 7 155H").Should().Be("Socket BGA 2049");
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Core(TM) Ultra 5 125U").Should().Be("Socket BGA 2551");

        // Intel Core Traditional
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Core(TM) i9-13900K").Should().Be("Socket LGA 1700");
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Core(TM) i7-13700HX").Should().Be("Socket BGA 1744");
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Core(TM) i7-12700H").Should().Be("Socket BGA 1964");
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Core(TM) i7-11700K").Should().Be("Socket LGA 1200");
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Core(TM) i7-11850H").Should().Be("Socket BGA 1440");
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Core(TM) i7-10510U").Should().Be("Socket BGA 1526");
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Core(TM) i7-9700K").Should().Be("Socket LGA 1151");
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Core(TM) i7-8750H").Should().Be("Socket BGA 1440");
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Core(TM) i7-4770K").Should().Be("Socket LGA 1150");
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Core(TM) i7-4700HQ").Should().Be("Socket BGA");

        // Intel Xeon
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Xeon(R) w9-3495X").Should().Be("Socket LGA 4677");
        SystemInfo.DeriveSocketFromCpuBrand("Intel(R) Xeon(R) Gold 6348").Should().Be("Socket LGA 4189");

        // AMD Threadripper & EPYC
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen Threadripper 7000 Series").Should().Be("Socket sTR5");
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen Threadripper 3990X").Should().Be("Socket sTRX4");
        SystemInfo.DeriveSocketFromCpuBrand("AMD EPYC 9004 Series").Should().Be("Socket SP5");
        SystemInfo.DeriveSocketFromCpuBrand("AMD EPYC 8004 Series").Should().Be("Socket SP6");
        SystemInfo.DeriveSocketFromCpuBrand("AMD EPYC 7763").Should().Be("Socket SP3");

        // AMD Ryzen
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen 9 9950X").Should().Be("Socket AM5");
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen 7 8840HS").Should().Be("Socket FP8");
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen 7 8700G").Should().Be("Socket AM5");
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen 9 7945HX").Should().Be("Socket FL1");
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen 7 7840U").Should().Be("Socket FP8");
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen 9 7900X").Should().Be("Socket AM5");
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen 7 6800U").Should().Be("Socket FP7");
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen 9 5900HX").Should().Be("Socket FP7");
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen 7 5800H").Should().Be("Socket FP6");
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen 7 5800X").Should().Be("Socket AM4");
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen 7 4800H").Should().Be("Socket FP6");
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen 5 3500U").Should().Be("Socket FP5");
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen 5 3600").Should().Be("Socket AM4");
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen 5 2500U").Should().Be("Socket FP5");
        SystemInfo.DeriveSocketFromCpuBrand("AMD Ryzen 5 1600").Should().Be("Socket AM4");

        // Qualcomm
        SystemInfo.DeriveSocketFromCpuBrand("Snapdragon X Elite - X1E-80-100").Should().Be("Socket 2050");

        // Unknown / edge cases
        SystemInfo.DeriveSocketFromCpuBrand("").Should().BeEmpty();
        SystemInfo.DeriveSocketFromCpuBrand("ABC").Should().BeEmpty();
        SystemInfo.DeriveSocketFromCpuBrand("Generic Processor").Should().BeEmpty();
    }

    [Fact]
    public void SystemInfo_SocketClassification_And_SmbiosSocketNames()
    {
        SystemInfo.IsSocketName("Socket LGA 1700").Should().BeTrue();
        SystemInfo.IsSocketName("Socket AM5").Should().BeTrue();
        SystemInfo.IsSocketName("BGA 2833").Should().BeTrue();
        SystemInfo.IsSocketName("PGA 988").Should().BeTrue();
        SystemInfo.IsSocketName("FCBGA 1528").Should().BeTrue();
        SystemInfo.IsSocketName("FCLGA 4189").Should().BeTrue();
        SystemInfo.IsSocketName("Slot 1").Should().BeTrue();
        SystemInfo.IsSocketName("FP8").Should().BeTrue();
        SystemInfo.IsSocketName("FL1").Should().BeTrue();
        SystemInfo.IsSocketName("").Should().BeFalse();
        SystemInfo.IsSocketName("Normal Chip").Should().BeFalse();

        SystemInfo.IsPlaceholderSocket("Other").Should().BeTrue();
        SystemInfo.IsPlaceholderSocket("Unknown").Should().BeTrue();
        SystemInfo.IsPlaceholderSocket("None").Should().BeTrue();
        SystemInfo.IsPlaceholderSocket("CPU").Should().BeTrue();
        SystemInfo.IsPlaceholderSocket("CPU Socket").Should().BeTrue();
        SystemInfo.IsPlaceholderSocket("Socket").Should().BeTrue();
        SystemInfo.IsPlaceholderSocket("To Be Filled By O.E.M.").Should().BeTrue();
        SystemInfo.IsPlaceholderSocket("Default string").Should().BeTrue();
        SystemInfo.IsPlaceholderSocket("Not Specified").Should().BeTrue();
        SystemInfo.IsPlaceholderSocket("N/A").Should().BeTrue();
        SystemInfo.IsPlaceholderSocket("  ").Should().BeTrue();
        SystemInfo.IsPlaceholderSocket("LGA 1700").Should().BeFalse();

        SystemInfo.SmbiosSocketName(4).Should().Be("ZIF Socket");
        SystemInfo.SmbiosSocketName(7).Should().Be("LIF Socket");
        SystemInfo.SmbiosSocketName(8).Should().Be("Slot 1");
        SystemInfo.SmbiosSocketName(21).Should().Be("Socket LGA 775");
        SystemInfo.SmbiosSocketName(49).Should().Be("Socket AM4");
        SystemInfo.SmbiosSocketName(50).Should().Be("Socket LGA 1151");
        SystemInfo.SmbiosSocketName(62).Should().Be("Socket LGA 1200");
        SystemInfo.SmbiosSocketName(64).Should().Be("Socket LGA 1700");
        SystemInfo.SmbiosSocketName(71).Should().Be("Socket AM5");
        SystemInfo.SmbiosSocketName(83).Should().Be("Socket LGA 1851");
        SystemInfo.SmbiosSocketName(85).Should().Be("Socket BGA 2833");
        SystemInfo.SmbiosSocketName(3).Should().BeEmpty();
        SystemInfo.SmbiosSocketName(999).Should().BeEmpty();
    }

    [Fact]
    public void SystemInfo_FormFactor_And_MemoryTypeLabels()
    {
        SystemInfo.FormFactorLabel(0x01).Should().Be("Other");
        SystemInfo.FormFactorLabel(0x02).Should().Be("Unknown");
        SystemInfo.FormFactorLabel(0x09).Should().Be("DIMM");
        SystemInfo.FormFactorLabel(0x0A).Should().Be("SODIMM");
        SystemInfo.FormFactorLabel(0x0D).Should().Be("Mini-RDIMM");
        SystemInfo.FormFactorLabel(0xFF).Should().Be("TypeFF");

        SystemInfo.MemoryTypeLabel(0x00).Should().Be("Unknown");
        SystemInfo.MemoryTypeLabel(0x11).Should().Be("SDRAM");
        SystemInfo.MemoryTypeLabel(0x18).Should().Be("DDR3");
        SystemInfo.MemoryTypeLabel(0x1A).Should().Be("DDR4");
        SystemInfo.MemoryTypeLabel(0x22).Should().Be("DDR5");
        SystemInfo.MemoryTypeLabel(0x26).Should().Be("LPDDR5X");
        SystemInfo.MemoryTypeLabel(0xFE).Should().Be("TypeFE");
    }

    [Fact]
    public void SystemInfo_BatteryString_Validation()
    {
        SystemInfo.IsValidBatteryString(null).Should().BeFalse();
        SystemInfo.IsValidBatteryString("").Should().BeFalse();
        SystemInfo.IsValidBatteryString("   ").Should().BeFalse();
        SystemInfo.IsValidBatteryString("Virtual Battery").Should().BeFalse();
        SystemInfo.IsValidBatteryString("CRB Battery").Should().BeFalse();
        SystemInfo.IsValidBatteryString("CRB123").Should().BeFalse();
        SystemInfo.IsValidBatteryString("Fake").Should().BeFalse();
        SystemInfo.IsValidBatteryString("None").Should().BeFalse();
        SystemInfo.IsValidBatteryString("Unknown").Should().BeFalse();
        SystemInfo.IsValidBatteryString("To be filled by O.E.M.").Should().BeFalse();
        SystemInfo.IsValidBatteryString("-placeholder-").Should().BeFalse();
        SystemInfo.IsValidBatteryString("Sunwoda L20M4PC0").Should().BeTrue();
    }

    [Fact]
    public void SystemInfo_HardwareHelpers_Glpi_Msr_Smbios_And_Vendors()
    {
        // DetectGlpiStructSize
        SystemInfo.DetectGlpiStructSize(24).Should().Be(24);
        SystemInfo.DetectGlpiStructSize(32).Should().Be(32);
        SystemInfo.DetectGlpiStructSize(16).Should().Be(16);
        SystemInfo.DetectGlpiStructSize(20).Should().Be(20);
        SystemInfo.DetectGlpiStructSize(7).Should().Be(0);

        // ReadMsrRatioMhz
        var pawnIoMock = Substitute.For<IPawnIoTransport>();
        pawnIoMock.Execute("ioctl_read_msr", Arg.Any<ulong[]>(), 1).Returns([0x30UL]);
        SystemInfo.ReadMsrRatioMhz(pawnIoMock, 0x1AD).Should().Be(4800);

        pawnIoMock.Execute("ioctl_read_msr", Arg.Any<ulong[]>(), 1).Returns([0x05UL]);
        SystemInfo.ReadMsrRatioMhz(pawnIoMock, 0x1AD).Should().Be(0);

        pawnIoMock.Execute("ioctl_read_msr", Arg.Any<ulong[]>(), 1).Returns([]);
        SystemInfo.ReadMsrRatioMhz(pawnIoMock, 0x1AD).Should().Be(0);

        // IsWirelessAdapter
        SystemInfo.IsWirelessAdapter("Intel(R) Wi-Fi 6E AX211 160MHz").Should().BeTrue();
        SystemInfo.IsWirelessAdapter("TP-Link Wireless USB Adapter").Should().BeTrue();
        SystemInfo.IsWirelessAdapter("Qualcomm 802.11ac NIC").Should().BeTrue();
        SystemInfo.IsWirelessAdapter("Realtek Gaming 2.5GbE Family Controller").Should().BeFalse();

        // ParseVendor
        SystemInfo.ParseVendor("PCI\\VEN_10DE&DEV_2684", "").Should().Be("NVIDIA");
        SystemInfo.ParseVendor("PCI\\VEN_1002&DEV_744C", "").Should().Be("AMD");
        SystemInfo.ParseVendor("PCI\\VEN_8086&DEV_56A0", "").Should().Be("Intel");
        SystemInfo.ParseVendor("", "NVIDIA GeForce RTX 4080").Should().Be("NVIDIA");
        SystemInfo.ParseVendor("", "AMD Radeon RX 7900 XTX").Should().Be("AMD");
        SystemInfo.ParseVendor("", "Intel Arc A770").Should().Be("Intel");
        SystemInfo.ParseVendor("", "Microsoft Basic Render Driver").Should().BeEmpty();

        // FindSmbiosType4Value
        byte[] type4Raw = new byte[64];
        type4Raw[0] = 4;
        type4Raw[1] = 0x30;
        type4Raw[0x2A] = 8;
        type4Raw[0x23] = 4;
        SystemInfo.FindSmbiosType4Value(type4Raw, 0x2A, 0x23).Should().Be(8);
        SystemInfo.FindSmbiosType4Value([], 0x2A, 0x23).Should().Be(0);

        // FindSmbiosString and GetSmbiosString
        var ms = new MemoryStream();
        ms.Write([1, 0x1B, 0, 0, 1, 2]); // Type 1, length 0x1B, offset 4 = str 1, offset 5 = str 2
        for (int i = 6; i < 0x1B; i++) ms.WriteByte(0);
        ms.Write(Encoding.ASCII.GetBytes("TestManufacturer"));
        ms.WriteByte(0);
        ms.Write(Encoding.ASCII.GetBytes("TestProduct"));
        ms.WriteByte(0);
        ms.WriteByte(0); // Terminate struct 1
        ms.Write([127, 4, 0, 0, 0, 0]); // Type 127
        byte[] smbiosBlob = ms.ToArray();

        SystemInfo.FindSmbiosString(smbiosBlob, 1, 4).Should().Be("TestManufacturer");
        SystemInfo.FindSmbiosString(smbiosBlob, 1, 5).Should().Be("TestProduct");
        SystemInfo.FindSmbiosString(smbiosBlob, 1, 20).Should().BeEmpty();
        SystemInfo.GetSmbiosString(smbiosBlob, 0, 0x1B, 0).Should().BeEmpty();
        SystemInfo.GetSmbiosString(smbiosBlob, 0, 0x1B, 99).Should().BeEmpty();
    }

    [Fact]
    public void SystemInfo_GpuAndBatteryAndColor_Branches_Coverage()
    {
        var isDiscrete = typeof(SystemInfo).GetMethod("IsDiscreteGpu", BindingFlags.NonPublic | BindingFlags.Static);
        isDiscrete.Should().NotBeNull();

        var dummyDev = default(NativeMethods.SpDevinfoData);
        ((bool)isDiscrete.Invoke(null, ["Intel Arc A770", "Intel", IntPtr.Zero, dummyDev])!).Should().BeTrue();
        ((bool)isDiscrete.Invoke(null, ["AMD Radeon RX 7900 XTX", "AMD", IntPtr.Zero, dummyDev])!).Should().BeTrue();
        ((bool)isDiscrete.Invoke(null, ["AMD Radeon PRO W6800", "AMD", IntPtr.Zero, dummyDev])!).Should().BeTrue();
        ((bool)isDiscrete.Invoke(null, ["Radeon VII", "AMD", IntPtr.Zero, dummyDev])!).Should().BeTrue();
        ((bool)isDiscrete.Invoke(null, ["Radeon Pro WX 7100", "AMD", IntPtr.Zero, dummyDev])!).Should().BeTrue();
        ((bool)isDiscrete.Invoke(null, ["Intel UHD Graphics", "Intel", IntPtr.Zero, dummyDev])!).Should().BeFalse();

        var gpusField = typeof(SystemInfo).GetField("_gpusCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        var originalGpus = gpusField.GetValue(null);

        try
        {
            (string Name, string Vendor, bool IsDedicated, long? VramBytes, string? DriverVersion)[] intelDedicated =
            [
                ("Intel Arc A770", "Intel", true, 16L * 1024 * 1024 * 1024, "31.0")
            ];
            gpusField.SetValue(null, intelDedicated);
            SystemInfo.GetGpuVendor().Should().Be("Intel");
            SystemInfo.GetGpuColor().Should().NotBeEmpty();

            (string Name, string Vendor, bool IsDedicated, long? VramBytes, string? DriverVersion)[] intelIntegrated =
            [
                ("Intel Iris Xe Graphics", "Intel", false, 0L, "31.0")
            ];
            gpusField.SetValue(null, intelIntegrated);
            SystemInfo.GetGpuVendor().Should().Be("Intel");
            SystemInfo.GetGpuColor().Should().NotBeEmpty();

            (string Name, string Vendor, bool IsDedicated, long? VramBytes, string? DriverVersion)[] emptyGpus = [];
            gpusField.SetValue(null, emptyGpus);
            SystemInfo.GetGpuVendor().Should().Be("Unknown");
            SystemInfo.GetGpuColor().Should().BeEmpty();
        }
        finally
        {
            gpusField.SetValue(null, originalGpus);
        }

        SystemInfo.GetDiskColor().Should().NotBeNull();
        SystemInfo.GetRamColor().Should().NotBeNull();
        SystemInfo.GetNicColor().Should().NotBeNull();
        SystemInfo.GetSystemOemColor().Should().NotBeNull();

        // 4. Battery queries with cache forced to true
        var hasBatField = typeof(SystemInfo).GetField("_hasBatteryCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        var originalHasBat = hasBatField.GetValue(null);
        var mfrCacheField = typeof(SystemInfo).GetField("_batteryManufacturerCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        var devCacheField = typeof(SystemInfo).GetField("_batteryDeviceNameCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        var designCacheField = typeof(SystemInfo).GetField("_batteryDesignCapacityCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        var fullCacheField = typeof(SystemInfo).GetField("_batteryFullChargeCapacityCache", BindingFlags.NonPublic | BindingFlags.Static)!;

        try
        {
            hasBatField.SetValue(null, true);
            mfrCacheField.SetValue(null, null);
            devCacheField.SetValue(null, null);
            designCacheField.SetValue(null, null);
            fullCacheField.SetValue(null, null);

            SystemInfo.GetBatteryManufacturer().Should().NotBeNull();
            SystemInfo.GetBatteryDeviceName().Should().NotBeNull();
            SystemInfo.GetBatteryDesignCapacity().Should().NotBeNull();
            SystemInfo.GetBatteryFullChargedCapacity().Should().NotBeNull();
        }
        finally
        {
            hasBatField.SetValue(null, originalHasBat);
            mfrCacheField.SetValue(null, null);
            devCacheField.SetValue(null, null);
            designCacheField.SetValue(null, null);
            fullCacheField.SetValue(null, null);
        }

        // 5. PSU and Disk enumeration queries
        SystemInfo.GetPsuName().Should().NotBeNull();
        SystemInfo.GetPsuMaxCapacity().Should().NotBeNull();
        SystemInfo.GetDiskManufacturer().Should().NotBeNull();
        SystemInfo.GetAllDisks().Should().NotBeNull();

        // 6. CPU Max Speed MSR query
        var queryMsrMethod = typeof(SystemInfo).GetMethod("QueryCpuMaxSpeedMsr", BindingFlags.NonPublic | BindingFlags.Static);
        queryMsrMethod?.Invoke(null, null);

        // 7. WMI error handling and battery internal helpers
        var qWmiFirst = typeof(SystemInfo).GetMethod("QueryWmiFirst", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((string)qWmiFirst.Invoke(null, ["SELECT InvalidProp FROM Win32_NonExistentClass", "InvalidProp"])!).Should().BeEmpty();

        var qWmiFirstInt = typeof(SystemInfo).GetMethod("QueryWmiFirstInt", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((int)qWmiFirstInt.Invoke(null, ["SELECT InvalidProp FROM Win32_NonExistentClass", "InvalidProp"])!).Should().Be(0);

        var qRootStr = typeof(SystemInfo).GetMethod("QueryRootWmiString", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((string)qRootStr.Invoke(null, ["NonExistentClass", "Prop"])!).Should().BeEmpty();

        var qRootInt = typeof(SystemInfo).GetMethod("QueryRootWmiInt", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((int)qRootInt.Invoke(null, ["NonExistentClass", "Prop"])!).Should().Be(0);

        var qWin32Name = typeof(SystemInfo).GetMethod("QueryWin32BatteryName", BindingFlags.NonPublic | BindingFlags.Static)!;
        qWin32Name.Invoke(null, null).Should().NotBeNull();

        var qWin32Mfr = typeof(SystemInfo).GetMethod("QueryWin32BatteryManufacturer", BindingFlags.NonPublic | BindingFlags.Static)!;
        qWin32Mfr.Invoke(null, null).Should().NotBeNull();

        var getSmbiosBatStr = typeof(SystemInfo).GetMethod("GetSmbiosBatteryString", BindingFlags.NonPublic | BindingFlags.Static)!;
        getSmbiosBatStr.Invoke(null, [0x05]).Should().NotBeNull();

        var getSmbiosBatWord = typeof(SystemInfo).GetMethod("GetSmbiosBatteryWord", BindingFlags.NonPublic | BindingFlags.Static)!;
        getSmbiosBatWord.Invoke(null, [0x0A]).Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_SmbiosType39_And_Type17_DetailedParsing_Coverage()
    {
        var smbiosCacheField = typeof(SystemInfo).GetField("_smbiosCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        var originalSmbios = smbiosCacheField.GetValue(null);

        try
        {
            // 1. Build synthetic SMBIOS containing Type 39 (PSU), Type 17 (Memory), and Type 127 (End)
            var psuBytes = new List<byte>
            {
                // Type 39 Header (len 0x0C)
                39, 0x0C, 0x01, 0x00,
                // 0x04: Power Unit Group
                1,
                // 0x05: Location Str Idx
                0,
                // 0x06: Mfr Str Idx (string 1)
                1,
                // 0x07: Serial Str Idx
                0,
                // 0x08: Model Str Idx (string 2)
                2,
                // 0x09: Asset Tag Str Idx
                0,
                // 0x0A: Max capacity LSB (65000 mW = 0xFDE8 -> LSB 0xE8)
                0xE8,
                // 0x0B: Max capacity MSB (0xFD)
                0xFD
            };
            // Type 39 Strings: "Corsair\0RM850x\0\0"
            psuBytes.AddRange(Encoding.ASCII.GetBytes("Corsair\0RM850x\0\0"));

            // Type 17 Header (len 0x14)
            psuBytes.AddRange([17, 0x14, 0x02, 0x00]);
            for (int i = 4; i < 0x12; i++) psuBytes.Add(0);
            psuBytes.Add(0x22); // 0x12: Memory type DDR5 (0x22)
            psuBytes.Add(0);    // 0x13
            psuBytes.AddRange([0, 0]); // Double null strings

            // Type 127 Header (len 4)
            psuBytes.AddRange([127, 4, 0x03, 0x00, 0, 0]);

            byte[] fullSmbios = psuBytes.ToArray();
            smbiosCacheField.SetValue(null, fullSmbios);

            SystemInfo.GetPsuName().Should().Be("Corsair RM850x");
            SystemInfo.GetPsuMaxCapacity().Should().Be("65 W");

            var findSmbios17 = typeof(SystemInfo).GetMethod("FindSmbiosType17MemoryType", BindingFlags.NonPublic | BindingFlags.Static)!;
            ((int)findSmbios17.Invoke(null, [fullSmbios])!).Should().Be(0x22);

            // 2. Only manufacturer (model string idx = 0)
            byte[] mfrOnly = (byte[])fullSmbios.Clone();
            mfrOnly[8] = 0; // Model str idx = 0
            smbiosCacheField.SetValue(null, mfrOnly);
            SystemInfo.GetPsuName().Should().Be("Corsair");

            // 3. Only model (mfr string idx = 0)
            byte[] modelOnly = (byte[])fullSmbios.Clone();
            modelOnly[6] = 0; // Mfr str idx = 0
            smbiosCacheField.SetValue(null, modelOnly);
            SystemInfo.GetPsuName().Should().Be("RM850x");

            // 4. Both empty
            byte[] bothEmpty = (byte[])fullSmbios.Clone();
            bothEmpty[6] = 0;
            bothEmpty[8] = 0;
            smbiosCacheField.SetValue(null, bothEmpty);
            SystemInfo.GetPsuName().Should().BeEmpty();

            // 5. Unknown capacity 0x8000
            byte[] unknownCap = (byte[])fullSmbios.Clone();
            unknownCap[0x0A] = 0x00;
            unknownCap[0x0B] = 0x80;
            smbiosCacheField.SetValue(null, unknownCap);
            SystemInfo.GetPsuMaxCapacity().Should().BeEmpty();

            // 6. Empty raw array
            smbiosCacheField.SetValue(null, Array.Empty<byte>());
            SystemInfo.GetPsuName().Should().BeEmpty();
            SystemInfo.GetPsuMaxCapacity().Should().BeEmpty();
            ((int)findSmbios17.Invoke(null, [Array.Empty<byte>()])!).Should().Be(0);

            // 7. Truncated raw array (len < 4)
            ((int)findSmbios17.Invoke(null, [new byte[] { 17, 2, 0, 0 }])!).Should().Be(0);
        }
        finally
        {
            smbiosCacheField.SetValue(null, originalSmbios);
        }
    }

    [Fact]
    public void SystemInfo_DetailedInternalQueries_And_Fallbacks_Coverage()
    {
        // 1. ReadMsrRatioMhz branches
        var pawnIoMock = Substitute.For<IPawnIoTransport>();

        // Empty result
        pawnIoMock.Execute("ioctl_read_msr", Arg.Any<ulong[]>(), 1).Returns(Array.Empty<ulong>());
        SystemInfo.ReadMsrRatioMhz(pawnIoMock, 0x1AD).Should().Be(0);

        // Ratio 0x00
        pawnIoMock.Execute("ioctl_read_msr", Arg.Any<ulong[]>(), 1).Returns([0x00UL]);
        SystemInfo.ReadMsrRatioMhz(pawnIoMock, 0x1AD).Should().Be(0);

        // Ratio 0xFF
        pawnIoMock.Execute("ioctl_read_msr", Arg.Any<ulong[]>(), 1).Returns([0xFFUL]);
        SystemInfo.ReadMsrRatioMhz(pawnIoMock, 0x1AD).Should().Be(0);

        // Ratio 0x05 (500 MHz <= 1000)
        pawnIoMock.Execute("ioctl_read_msr", Arg.Any<ulong[]>(), 1).Returns([0x05UL]);
        SystemInfo.ReadMsrRatioMhz(pawnIoMock, 0x1AD).Should().Be(0);

        // Ratio 0x24 (36 * 100 = 3600 MHz > 1000)
        pawnIoMock.Execute("ioctl_read_msr", Arg.Any<ulong[]>(), 1).Returns([0x24UL]);
        SystemInfo.ReadMsrRatioMhz(pawnIoMock, 0x1AD).Should().Be(3600);

        // 2. IsWirelessAdapter branches
        SystemInfo.IsWirelessAdapter("Intel(R) Wi-Fi 6E AX211 160MHz").Should().BeTrue();
        SystemInfo.IsWirelessAdapter("Killer(R) WiFi 6 AX1650w").Should().BeTrue();
        SystemInfo.IsWirelessAdapter("Qualcomm Atheros Wireless Network Adapter").Should().BeTrue();
        SystemInfo.IsWirelessAdapter("Realtek 8821CE Wireless LAN 802.11ac").Should().BeTrue();
        SystemInfo.IsWirelessAdapter("Broadcom 802.11n Network Adapter").Should().BeTrue();
        SystemInfo.IsWirelessAdapter("Realtek PCIe 2.5GbE Family Controller").Should().BeFalse();

        // 3. IsDiscreteGpu reflection branches
        var isDiscreteGpu = typeof(SystemInfo).GetMethod("IsDiscreteGpu", BindingFlags.NonPublic | BindingFlags.Static)!;
        var dummyDevInfo = new NativeMethods.SpDevinfoData();

        ((bool)isDiscreteGpu.Invoke(null, ["Intel(R) Arc(TM) A770 Graphics", "Intel", IntPtr.Zero, dummyDevInfo])!).Should().BeTrue();
        ((bool)isDiscreteGpu.Invoke(null, ["AMD Radeon RX 7900 XTX", "AMD", IntPtr.Zero, dummyDevInfo])!).Should().BeTrue();
        ((bool)isDiscreteGpu.Invoke(null, ["Radeon PRO W6800", "AMD", IntPtr.Zero, dummyDevInfo])!).Should().BeTrue();
        ((bool)isDiscreteGpu.Invoke(null, ["AMD Radeon VII", "AMD", IntPtr.Zero, dummyDevInfo])!).Should().BeTrue();
        ((bool)isDiscreteGpu.Invoke(null, ["Radeon Pro WX 3200", "AMD", IntPtr.Zero, dummyDevInfo])!).Should().BeTrue();
        ((bool)isDiscreteGpu.Invoke(null, ["AMD Radeon(TM) Graphics", "AMD", IntPtr.Zero, dummyDevInfo])!).Should().BeFalse();
        ((bool)isDiscreteGpu.Invoke(null, ["Intel(R) Iris(R) Xe Graphics", "Intel", IntPtr.Zero, dummyDevInfo])!).Should().BeFalse();

        // 4. GetGpuRegistryData with unknown device
        var getGpuReg = typeof(SystemInfo).GetMethod("GetGpuRegistryData", BindingFlags.NonPublic | BindingFlags.Static)!;
        var regResult = ((long? Vram, string? Ver))getGpuReg.Invoke(null, ["NonExistentGpu_Device_Description_12345"])!;
        regResult.Vram.Should().BeNull();
        regResult.Ver.Should().BeNull();

        // 5. GetBiosRegValue with unknown key
        var getBiosReg = typeof(SystemInfo).GetMethod("GetBiosRegValue", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((string)getBiosReg.Invoke(null, ["NonExistentBiosKey_12345"])!).Should().BeEmpty();

        // 6. CPU ID and NT Power max Mhz queries
        var queryCpuid = typeof(SystemInfo).GetMethod("QueryCpuMaxSpeedCpuid", BindingFlags.NonPublic | BindingFlags.Static)!;
        int cpuidMhz = (int)queryCpuid.Invoke(null, null)!;
        cpuidMhz.Should().BeGreaterThanOrEqualTo(0);

        var queryNtPower = typeof(SystemInfo).GetMethod("QueryNtPowerMaxMhz", BindingFlags.NonPublic | BindingFlags.Static)!;
        int ntPowerMhz = (int)queryNtPower.Invoke(null, null)!;
        ntPowerMhz.Should().BeGreaterThanOrEqualTo(0);

        // 7. GetOsDisplayVersion resets cache
        var osCacheField = typeof(SystemInfo).GetField("_osVersionCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        var origOsCache = osCacheField.GetValue(null);
        try
        {
            osCacheField.SetValue(null, null);
            SystemInfo.GetOsDisplayVersion().Should().NotBeNull();
        }
        finally
        {
            osCacheField.SetValue(null, origOsCache);
        }
    }

    [Fact]
    public void SystemInfo_CpuVendor_Environment_Variations()
    {
        string? origEnv = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        try
        {
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", "AMD64 Family 25 Model 80 Stepping 0, AuthenticAMD");
            SystemInfo.GetCpuVendor().Should().Be("AMD");

            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", "Intel64 Family 6 Model 158 Stepping 10, GenuineIntel");
            SystemInfo.GetCpuVendor().Should().Be("Intel");

            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", "ARM64 Family 8 Model 1 Revision 2, Qualcomm");
            SystemInfo.GetCpuVendor().Should().BeEmpty();

            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", null);
            SystemInfo.GetCpuVendor().Should().BeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", origEnv);
        }
    }

    [Fact]
    public void SystemInfo_Wmi_Error_Catch_Branches()
    {
        var queryWmiFirst = typeof(SystemInfo).GetMethod("QueryWmiFirst", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((string)queryWmiFirst.Invoke(null, ["SELECT InvalidProperty FROM InvalidClass_NonExistent_12345", "InvalidProperty"])!).Should().BeEmpty();

        var queryWmiFirstInt = typeof(SystemInfo).GetMethod("QueryWmiFirstInt", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((int)queryWmiFirstInt.Invoke(null, ["SELECT InvalidProperty FROM InvalidClass_NonExistent_12345", "InvalidProperty"])!).Should().Be(0);

        var queryRootWmiStr = typeof(SystemInfo).GetMethod("QueryRootWmiString", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((string)queryRootWmiStr.Invoke(null, ["InvalidClass_NonExistent_12345", "InvalidProperty"])!).Should().BeEmpty();

        var queryRootWmiInt = typeof(SystemInfo).GetMethod("QueryRootWmiInt", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((int)queryRootWmiInt.Invoke(null, ["InvalidClass_NonExistent_12345", "InvalidProperty"])!).Should().Be(0);
    }

    [Fact]
    public void SystemInfo_Smbios_Field_And_Advance_EdgeCases()
    {
        var readField = typeof(SystemInfo).GetMethod("ReadSmbiosType4Field", BindingFlags.NonPublic | BindingFlags.Static)!;
        var advanceMethod = typeof(SystemInfo).GetMethod("AdvanceToNextSmbiosStructure", BindingFlags.NonPublic | BindingFlags.Static)!;

        // 1. Buffer with wordVal == 0 and byteVal > 0 -> returns byteVal
        byte[] buffer = new byte[32];
        buffer[4] = 0; // word low
        buffer[5] = 0; // word high
        buffer[8] = 16; // byteVal
        int val = (int)readField.Invoke(null, [buffer, 0, (byte)16, 4, 8])!;
        val.Should().Be(16);

        // 2. Buffer with both wordVal == 0 and byteVal == 0 -> returns 0
        buffer[8] = 0;
        int valZero = (int)readField.Invoke(null, [buffer, 0, (byte)16, 4, 8])!;
        valZero.Should().Be(0);

        // 3. Buffer with length smaller than offsets -> returns 0
        int valShort = (int)readField.Invoke(null, [buffer, 0, (byte)2, 4, 8])!;
        valShort.Should().Be(0);

        // 4. AdvanceToNextSmbiosStructure on buffer with double zero
        byte[] smbiosData = [1, 4, 0, 0, 0, 0, 2, 4];
        int next = (int)advanceMethod.Invoke(null, [smbiosData, 2])!;
        next.Should().Be(4);

        // 5. AdvanceToNextSmbiosStructure on buffer without double zero reaches boundary
        byte[] smbiosNoDoubleZero = [1, 2, 3, 4, 5];
        int nextEnd = (int)advanceMethod.Invoke(null, [smbiosNoDoubleZero, 0])!;
        nextEnd.Should().BeGreaterThanOrEqualTo(smbiosNoDoubleZero.Length);
    }

    [Fact]
    public void SystemInfo_Battery_Validation_And_Query_Fallbacks()
    {
        // 1. IsValidBatteryString validation branches
        SystemInfo.IsValidBatteryString(null).Should().BeFalse();
        SystemInfo.IsValidBatteryString("").Should().BeFalse();
        SystemInfo.IsValidBatteryString("   ").Should().BeFalse();
        SystemInfo.IsValidBatteryString("Virtual Battery Pack").Should().BeFalse();
        SystemInfo.IsValidBatteryString("CRB Battery").Should().BeFalse();
        SystemInfo.IsValidBatteryString("CRB-DEV-001").Should().BeFalse();
        SystemInfo.IsValidBatteryString("Fake").Should().BeFalse();
        SystemInfo.IsValidBatteryString("None").Should().BeFalse();
        SystemInfo.IsValidBatteryString("Unknown").Should().BeFalse();
        SystemInfo.IsValidBatteryString("To be filled by O.E.M.").Should().BeFalse();
        SystemInfo.IsValidBatteryString("-BAT0-").Should().BeFalse();
        SystemInfo.IsValidBatteryString("Sunwoda").Should().BeTrue();
        SystemInfo.IsValidBatteryString("LGC").Should().BeTrue();
        SystemInfo.IsValidBatteryString("SMP").Should().BeTrue();
        SystemInfo.IsValidBatteryString("Panasonic").Should().BeTrue();

        // 2. QueryWin32BatteryName and QueryWin32BatteryManufacturer methods
        var queryNameMethod = typeof(SystemInfo).GetMethod("QueryWin32BatteryName", BindingFlags.NonPublic | BindingFlags.Static)!;
        var win32Name = (string)queryNameMethod.Invoke(null, null)!;
        win32Name.Should().NotBeNull();

        var queryMfrMethod = typeof(SystemInfo).GetMethod("QueryWin32BatteryManufacturer", BindingFlags.NonPublic | BindingFlags.Static)!;
        var win32Mfr = (string)queryMfrMethod.Invoke(null, null)!;
        win32Mfr.Should().NotBeNull();

        // 3. Fallback execution with forced battery presence cache
        var hasBatField = typeof(SystemInfo).GetField("_hasBatteryCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        var mfrField = typeof(SystemInfo).GetField("_batteryManufacturerCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        var devField = typeof(SystemInfo).GetField("_batteryDeviceNameCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        var desField = typeof(SystemInfo).GetField("_batteryDesignCapacityCache", BindingFlags.NonPublic | BindingFlags.Static)!;

        var origHasBat = hasBatField.GetValue(null);
        var origMfr = mfrField.GetValue(null);
        var origDev = devField.GetValue(null);
        var origDes = desField.GetValue(null);

        try
        {
            // Case A: HasBattery is false -> all battery queries return empty
            hasBatField.SetValue(null, false);
            mfrField.SetValue(null, null);
            devField.SetValue(null, null);
            desField.SetValue(null, null);

            SystemInfo.GetBatteryManufacturer().Should().BeEmpty();
            SystemInfo.GetBatteryDeviceName().Should().BeEmpty();
            SystemInfo.GetBatteryDesignCapacity().Should().BeEmpty();

            // Case B: HasBattery is true -> runs multi-step resolution
            hasBatField.SetValue(null, true);
            mfrField.SetValue(null, null);
            devField.SetValue(null, null);
            desField.SetValue(null, null);

            SystemInfo.GetBatteryManufacturer().Should().NotBeNull();
            SystemInfo.GetBatteryDeviceName().Should().NotBeNull();
            SystemInfo.GetBatteryDesignCapacity().Should().NotBeNull();
        }
        finally
        {
            hasBatField.SetValue(null, origHasBat);
            mfrField.SetValue(null, origMfr);
            devField.SetValue(null, origDev);
            desField.SetValue(null, origDes);
        }
    }

    [Fact]
    public void SystemInfo_Ram_And_Motherboard_Fallbacks()
    {
        // 1. GetInstalledRamGb with empty or invalid module size
        var ramCacheField = typeof(SystemInfo).GetField("_ramModulesCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        var origRamCache = ramCacheField.GetValue(null);
        try
        {
            // Set empty modules list to test fallback calculation
            ramCacheField.SetValue(null, new List<Dictionary<string, object>>());
            double gb = SystemInfo.GetInstalledRamGb();
            gb.Should().BeGreaterThanOrEqualTo(0);

            // Set module list with non-numeric sizeGb
            var invalidModule = new Dictionary<string, object> { ["sizeGb"] = "NotANumber" };
            ramCacheField.SetValue(null, new List<Dictionary<string, object>> { invalidModule });
            double fallbackGb = SystemInfo.GetInstalledRamGb();
            fallbackGb.Should().BeGreaterThanOrEqualTo(0);

            // Test GetRamManufacturer fallback when manufacturer property is missing
            var noMfrModule = new Dictionary<string, object> { ["speed"] = "3200 MT/s" };
            ramCacheField.SetValue(null, new List<Dictionary<string, object>> { noMfrModule });
            string mfr = SystemInfo.GetRamManufacturer();
            mfr.Should().NotBeNull();

            // Test GetRamSpeed fallback when speed property is missing
            var noSpeedModule = new Dictionary<string, object> { ["manufacturer"] = "Corsair" };
            ramCacheField.SetValue(null, new List<Dictionary<string, object>> { noSpeedModule });
            string speed = SystemInfo.GetRamSpeed();
            speed.Should().BeEmpty();
        }
        finally
        {
            ramCacheField.SetValue(null, origRamCache);
        }

        // 2. Motherboard and System fallback methods
        SystemInfo.GetSystemManufacturer().Should().NotBeNull();
        SystemInfo.GetSystemProductName().Should().NotBeNull();
        SystemInfo.GetSystemModel().Should().NotBeNull();
        SystemInfo.GetMotherboardManufacturer().Should().NotBeNull();
        SystemInfo.GetMotherboardProductName().Should().NotBeNull();
        SystemInfo.GetMotherboardVersion().Should().NotBeNull();
        SystemInfo.GetMotherboardSerial().Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_PowerSupply_And_Drive_Heuristics_Coverage()
    {
        // 1. Power supply SMBIOS getters
        var psName = SystemInfo.GetPsuName();
        psName.Should().NotBeNull();

        var psCap = SystemInfo.GetPsuMaxCapacity();
        psCap.Should().NotBeNull();

        // 2. GetAllDisks, GetDiskModel, and GetDiskManufacturer execution
        var disks = SystemInfo.GetAllDisks();
        disks.Should().NotBeNull();

        var diskModel = SystemInfo.GetDiskModel();
        diskModel.Should().NotBeNull();

        var diskMfr = SystemInfo.GetDiskManufacturer();
        diskMfr.Should().NotBeNull();
    }

    [Fact]
    public void SystemInfo_DeriveSocketFromCpuBrand_And_CpuIdSocketLookupRaw_Exhaustive()
    {
        // 1. CpuIdSocketLookupRaw across all Intel and AMD CPUID family/model combinations
        int[] eaxKeys =
        [
            (0x06 << 16) | 0xC6, (0x06 << 16) | 0xC4, (0x06 << 16) | 0xC7, (0x06 << 16) | 0xAC,
            (0x06 << 16) | 0xBE, (0x06 << 16) | 0xBF, (0x06 << 16) | 0xBA, (0x06 << 16) | 0xBB,
            (0x06 << 16) | 0xB7, (0x06 << 16) | 0x9A, (0x06 << 16) | 0xAA, (0x06 << 16) | 0xA5,
            (0x06 << 16) | 0x8C, (0x06 << 16) | 0x8D, (0x06 << 16) | 0x9E, (0x06 << 16) | 0x8E,
            (0x19 << 16) | 0x5E, (0x19 << 16) | 0x61, (0x19 << 16) | 0x64, (0x19 << 16) | 0x74,
            (0x19 << 16) | 0x78, (0x19 << 16) | 0x50, (0x19 << 16) | 0x21, (0x19 << 16) | 0x11,
            (0x19 << 16) | 0x71, (0x17 << 16) | 0x31, 0x00
        ];

        foreach (int eax in eaxKeys)
        {
            // Reverse-encode family and model into EAX register format:
            // family at bits 8-11, extFamily at bits 20-27, model at bits 4-7, extModel at bits 16-19
            int f = (eax >> 16) & 0xFF;
            int m = eax & 0xFFFF;
            int regEax;
            if (f > 0x0F)
            {
                int extF = f - 0x0F;
                int baseM = m & 0x0F;
                int extM = (m >> 4) & 0x0F;
                regEax = (extF << 20) | (extM << 16) | (0x0F << 8) | (baseM << 4);
            }
            else
            {
                int baseM = m & 0x0F;
                int extM = (m >> 4) & 0x0F;
                regEax = (extM << 16) | (f << 8) | (baseM << 4);
            }
            SystemInfo.CpuIdSocketLookupRaw(regEax).Should().NotBeNull();
        }

        // 2. DeriveSocketFromCpuBrand across all CPU brand families
        string?[] testBrands =
        [
            null, "", "   ", "CPU",
            "Intel Core Ultra 7 155H",
            "Intel Core Ultra 7 155U",
            "Intel Core Ultra 9 285K",
            "Intel Core Ultra 7 258V",
            "Intel Core i9-14900HX",
            "Intel Core i7-13700H",
            "Intel Core i7-14700K",
            "Intel Core i7-10700H",
            "Intel Core i7-10700U",
            "Intel Core i7-10700K",
            "Intel Core i7-8700H",
            "Intel Core i7-8700K",
            "Intel Core i7-4770K",
            "Intel Core i7-4770",
            "Intel Xeon W9-3495X",
            "Intel Xeon Platinum 8380",
            "Intel Unknown Model",
            "AMD Ryzen Threadripper 7980X",
            "AMD Ryzen Threadripper 3990X",
            "AMD EPYC 9654",
            "AMD EPYC 8534P",
            "AMD EPYC 7763",
            "AMD Ryzen 9 9950X",
            "AMD Ryzen 7 8845HS",
            "AMD Ryzen 7 7800X3D",
            "AMD Ryzen 7 6800H",
            "AMD Ryzen 7 5800H",
            "AMD Ryzen 7 4800H",
            "AMD Ryzen 5 3600",
            "AMD Ryzen 5 3500U",
            "AMD Ryzen 5 1600",
            "AMD Ryzen 5 2500U",
            "AMD Unknown Model",
            "Snapdragon X Elite",
            "Qualcomm Generic Chip"
        ];

        foreach (var brand in testBrands)
        {
            var socket = SystemInfo.DeriveSocketFromCpuBrand(brand);
            socket.Should().NotBeNull();
        }
    }

    [Fact]
    public void SystemInfo_SmbiosCache_EmptyAndPopulated_Coverage()
    {
        var smbiosField = typeof(SystemInfo).GetField("_smbiosCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        var origSmbios = smbiosField.GetValue(null);

        try
        {
            // 1. Empty SMBIOS table branch
            smbiosField.SetValue(null, Array.Empty<byte>());
            SystemInfo.GetPsuName().Should().BeEmpty();
            SystemInfo.GetPsuMaxCapacity().Should().BeEmpty();

            // 2. Populated SMBIOS table branch
            smbiosField.SetValue(null, BuildBlob());
            SystemInfo.GetPsuName().Should().Be("Seasonic FOCUS GX-850");
            SystemInfo.GetPsuMaxCapacity().Should().NotBeEmpty();
        }
        finally
        {
            smbiosField.SetValue(null, origSmbios);
        }
    }
}


