using System.Reflection;
using System.Text;
using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

[Collection("SystemInfo")]
public class SystemInfoDirectSmbiosCoverageTests
{
    private static void WithSmbios(byte[] blob, Action action)
    {
        var field = typeof(SystemInfo).GetField("_smbiosCache", BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();
        object? prior = field!.GetValue(null);
        field.SetValue(null, blob);
        try
        {
            action();
        }
        finally
        {
            field.SetValue(null, prior);
        }
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
        // struct header: [type=17, len=24, handle=0,0 ... offset 0x12 = 26]
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
        // struct header: [type=39, len=22, handle=0,0, ... offset 6 = str1 (mfr), offset 8 = str2 (model)]
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
        // struct header: [type=22, len=26, handle=0,0, ... offset 5 = str1, offset 8 = str2, offset 0x0A = capacity word (0x2710 = 10000 mWh)]
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
