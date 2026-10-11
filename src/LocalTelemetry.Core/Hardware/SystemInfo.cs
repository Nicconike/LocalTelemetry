using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using LocalTelemetry.Core.Hardware.PawnIo;
using LocalTelemetry.Core.Models;
using Microsoft.Win32;

namespace LocalTelemetry.Core.Hardware;

[SupportedOSPlatform("windows")]
public static class SystemInfo
{
    // Socket string constants
    private const string SocketLga1851 = "Socket LGA 1851";
    private const string SocketBga2049 = "Socket BGA 2049";
    private const string SocketLga1700 = "Socket LGA 1700";
    private const string SocketBga1744 = "Socket BGA 1744";
    private const string SocketLga1200 = "Socket LGA 1200";
    private const string SocketBga1440 = "Socket BGA 1440";
    private const string SocketLga1151 = "Socket LGA 1151";
    private const string SocketAm5 = "Socket AM5";
    private const string SocketFp8 = "Socket FP8";
    private const string SocketAm4 = "Socket AM4";
    private const string SocketFp7 = "Socket FP7";
    private const string SocketFp6 = "Socket FP6";
    private const string SocketFp5 = "Socket FP5";
    private const string SocketFp4 = "Socket FP4";
    private const string SocketFl1 = "Socket FL1";
    private const string SocketBga2833 = "Socket BGA 2833";
    private const string SocketBga1964 = "Socket BGA 1964";
    private const string SocketBga2551 = "Socket BGA 2551";

    // Vendor and label constants
    private const string IntelVendor = "Intel";
    private const string NvidiaVendor = "NVIDIA";
    private const string UnknownLabel = "Unknown";

    // Caches (populated on first call, never re-read)
    private static byte[]? _smbiosCache;
    private static string? _cpuNameCache;
    private static string? _osVersionCache;
    private static long? _ramTotalBytesCache;
    private static bool? _hasBatteryCache;
    private static (string Name, string Vendor, bool IsDedicated, long? VramBytes, string? DriverVersion)[]? _gpusCache;
    private static (string Name, string Manufacturer)[]? _nicsCache;
    private static List<Dictionary<string, object?>>? _ramModulesCache;
    private static string? _batteryManufacturerCache;
    private static string? _batteryDeviceNameCache;
    private static string? _batteryDesignCapacityCache;
    private static string? _batteryFullChargeCapacityCache;

    private static string QueryWmiFirst(string query, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            foreach (ManagementBaseObject mo in results)
            {
                object? val = mo[property];
                if (val is not null)
                {
                    string s = val.ToString()?.Trim() ?? "";
                    if (s.Length > 0) return s;
                }
            }
        }
        catch (ManagementException) { Diagnostics.Log.Debug($"WMI({query}): query returned no data"); }
        catch (Exception ex) { Diagnostics.Log.Debug(ex, $"Failed to query system management interface for {query}"); }
        return "";
    }

    private static int QueryWmiFirstInt(string query, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            foreach (ManagementBaseObject mo in results)
            {
                object? val = mo[property];
                if (val is not null)
                {
                    string s = val.ToString()?.Trim() ?? "";
                    if (s.Length > 0 && int.TryParse(s, out int n)) return n;
                }
            }
        }
        catch (ManagementException) { Diagnostics.Log.Debug($"WMI({query}): query returned no data"); }
        catch (Exception ex) { Diagnostics.Log.Debug(ex, $"WMI({query})"); }
        return 0;
    }

    // CPU Socket
    private static string? _cpuSocketCache;

    /// <summary>
    /// Gets the CPU socket name via WMI, SMBIOS, CPUID or brand string derivation.
    /// </summary>
    /// <returns>Socket name such as <c>LGA 1700</c> or <c>AM5</c> or empty if unknown.</returns>
    public static string GetCpuSocket()
    {
        if (_cpuSocketCache is not null) return _cpuSocketCache;

        // Step 1: WMI SocketDesignation (SMBIOS Type 4 Socket Designation) - brand-agnostic
        string sd = QueryWmiFirst("SELECT SocketDesignation FROM Win32_Processor", "SocketDesignation");
        if (IsSocketName(sd) && !IsPlaceholderSocket(sd)) return _cpuSocketCache = sd;

        // Step 2: SMBIOS UpgradeMethod - brand-agnostic; Other/Unknown placeholders skipped
        int upg = QueryWmiFirstInt("SELECT UpgradeMethod FROM Win32_Processor", "UpgradeMethod");
        if (upg > 0)
        {
            string mapped = SmbiosSocketName(upg);
            if (mapped.Length > 0 && !IsPlaceholderSocket(mapped)) return _cpuSocketCache = mapped;
        }

        // Step 3: Derive from CPU brand string (Intel / AMD / Qualcomm, no per-model dictionary)
        string derived = DeriveSocketFromCpuName();
        if (derived.Length > 0)
            return _cpuSocketCache = derived;

        // Step 4: CPUID (Family, Model) lookup - x86 only, last-resort fallback
        string cpuidSkt = CpuIdSocketLookup();
        if (cpuidSkt.Length > 0) return _cpuSocketCache = cpuidSkt;

        return _cpuSocketCache = sd.Length > 0 ? sd : "";
    }

    /// <summary>Looks up socket by CPUID Family + Model from known CPUs.</summary>
    private static string CpuIdSocketLookup()
    {
        if (!X86Base.IsSupported) return "";
        try
        {
            var (eax, _, _, _) = X86Base.CpuId(0x01, 0);
            int family = (eax >> 8) & 0xF;
            int extFamily = (eax >> 20) & 0xFF;
            int model = (eax >> 4) & 0xF;
            int extModel = (eax >> 16) & 0xF;
            if (family == 0x06 || family == 0x0F) // Intel uses family 6, AMD uses extended family
            {
                if (family == 0x0F)
                    family += extFamily;
                model |= extModel << 4;
            }

            int key = (family << 16) | (model & 0xFFFF);

            // Intel
            return key switch
            {
                (0x06 << 16) | 0xC6 => SocketLga1851, // Arrow Lake desktop
                (0x06 << 16) | 0xC4 => SocketBga2049, // Arrow Lake H
                (0x06 << 16) | 0xC7 => SocketBga2833, // Lunar Lake
                (0x06 << 16) | 0xAC => SocketLga1851, // Meteor Lake desktop
                (0x06 << 16) | 0xBE => SocketLga1700, // Raptor Lake Refresh desktop
                (0x06 << 16) | 0xBF => SocketBga1964, // Raptor Lake HX
                (0x06 << 16) | 0xBA => SocketLga1700, // Raptor Lake S desktop
                (0x06 << 16) | 0xBB => SocketBga1744, // Raptor Lake P
                (0x06 << 16) | 0xB7 => SocketLga1700, // Alder Lake S desktop
                (0x06 << 16) | 0x9A => SocketLga1700, // Alder Lake desktop
                (0x06 << 16) | 0xAA => SocketBga1744, // Raptor Lake P
                (0x06 << 16) | 0xA5 => SocketLga1200, // Comet Lake desktop
                (0x06 << 16) | 0x8C => SocketBga1440, // Tiger Lake U
                (0x06 << 16) | 0x8D => SocketLga1200, // Tiger Lake desktop
                (0x06 << 16) | 0x9E => SocketLga1151, // Kaby Lake desktop
                (0x06 << 16) | 0x8E => SocketLga1151, // Kaby Lake desktop

                // AMD
                (0x19 << 16) | 0x5E => SocketAm5, // Granite Ridge (Ryzen 9000)
                (0x19 << 16) | 0x61 => SocketAm5, // Raphael (Ryzen 7000)
                (0x19 << 16) | 0x64 => SocketFp8, // Phoenix (Ryzen 7040)
                (0x19 << 16) | 0x74 => SocketFp8, // Phoenix 2
                (0x19 << 16) | 0x78 => SocketFl1, // Dragon Range (Ryzen 7045 HX)
                (0x19 << 16) | 0x50 => SocketFp6, // Cezanne (Ryzen 5000 mobile)
                (0x19 << 16) | 0x21 => SocketFp7, // Rembrandt (Ryzen 6000)
                (0x19 << 16) | 0x11 => SocketAm4, // Vermeer (Ryzen 5000 desktop)
                (0x19 << 16) | 0x71 => SocketAm4, // Matisse (Ryzen 3000 desktop)
                (0x17 << 16) | 0x31 => SocketAm4, // Zen+ desktop
                _ => ""
            };
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to resolve CPU socket via CPUID"); return ""; }
    }

    /// <summary>Reads CPU brand string via CPUID leaf 0x80000002-0x80000004 (fastest, most reliable).</summary>
    private static string? GetCpuBrandStringCpuid()
    {
        if (!X86Base.IsSupported) return null;
        try
        {
            Span<byte> buf = stackalloc byte[48];
            int pos = 0;
            for (uint leaf = 0x80000002; leaf <= 0x80000004; leaf++)
            {
                var (eax, ebx, ecx, edx) = X86Base.CpuId((int)leaf, 0);
                AppendCpuid(buf, ref pos, eax);
                AppendCpuid(buf, ref pos, ebx);
                AppendCpuid(buf, ref pos, ecx);
                AppendCpuid(buf, ref pos, edx);
            }
            if (pos == 0) return null;
            return Encoding.ASCII.GetString(buf[..pos]).Trim();
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to read CPU brand string via CPUID"); return null; }
    }

    private static void AppendCpuid(Span<byte> buf, ref int pos, int reg)
    {
        uint u = (uint)reg;
        buf[pos++] = (byte)(u & 0xFF);
        buf[pos++] = (byte)((u >> 8) & 0xFF);
        buf[pos++] = (byte)((u >> 16) & 0xFF);
        buf[pos++] = (byte)((u >> 24) & 0xFF);
    }

    private static string GetCpuBrandString()
    {
        string? cpuid = GetCpuBrandStringCpuid();
        if (!string.IsNullOrEmpty(cpuid)) return cpuid;
        string reg = GetCpuName();
        if (!string.IsNullOrEmpty(reg)) return reg;
        return QueryWmiFirst("SELECT Name FROM Win32_Processor", "Name");
    }

    private static string DeriveSocketFromCpuName()
    {
        string name = GetCpuBrandString();
        if (string.IsNullOrEmpty(name)) return "";

        ReadOnlySpan<char> s = name.AsSpan().Trim();
        if (s.Length < 4) return "";

        // Detect vendor from brand string
        if (s.IndexOf("INTEL", StringComparison.OrdinalIgnoreCase) >= 0)
            return DeriveIntelSocket(s);
        if (s.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0)
            return DeriveAmdSocket(s);
        if (s.IndexOf("SNAPDRAGON", StringComparison.OrdinalIgnoreCase) >= 0)
            return DeriveQualcommSocket(s);

        // Try uppercased fallback in case vendor is embedded differently
        ReadOnlySpan<char> u = name.ToUpperInvariant().AsSpan().Trim();
        if (u.IndexOf("INTEL") >= 0) return DeriveIntelSocket(u);
        if (u.IndexOf("AMD") >= 0) return DeriveAmdSocket(u);
        if (u.IndexOf("SNAPDRAGON") >= 0) return DeriveQualcommSocket(u);

        return "";
    }

    private static string DeriveIntelSocket(ReadOnlySpan<char> s)
    {
        if (s.IndexOf("ULTRA") >= 0)
            return DeriveIntelUltraSocket(s);

        string traditionalSocket = DeriveIntelCoreTraditionalSocket(s);
        if (!string.IsNullOrEmpty(traditionalSocket))
            return traditionalSocket;

        if (s.IndexOf("XEON") >= 0)
            return IntelXeonSocket(s);

        return "";
    }

    private static string DeriveIntelUltraSocket(ReadOnlySpan<char> s)
    {
        int ultraIdx = s.IndexOf("ULTRA");
        if (ultraIdx < 0) return "";

        if (!TryParseUltraModel(s, ultraIdx + 5, out var numStr, out var suffix) || numStr.Length == 0)
            return "";

        if (suffix.IndexOf('V') >= 0) return SocketBga2833;

        int genDigit = numStr[0] - '0';
        if (genDigit >= 1)
        {
            if (suffix.IndexOf('H') >= 0) return SocketBga2049;
            if (suffix.IndexOf('U') >= 0) return SocketBga2551;
            return SocketLga1851;
        }
        return "";
    }

    private static bool TryParseUltraModel(ReadOnlySpan<char> s, int startIndex, out ReadOnlySpan<char> numStr, out ReadOnlySpan<char> suffix)
    {
        numStr = default;
        suffix = default;

        int i = startIndex;
        while (i < s.Length && s[i] == ' ') i++;
        if (i < s.Length && char.IsDigit(s[i])) i++;
        while (i < s.Length && s[i] == ' ') i++;
        if (i >= s.Length || !char.IsDigit(s[i])) return false;

        int numStart = i;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        int numEnd = i;

        int sufStart = i;
        while (i < s.Length && char.IsLetter(s[i])) i++;
        int sufEnd = i;

        if (numEnd <= numStart) return false;
        numStr = s[numStart..numEnd];
        suffix = s[sufStart..sufEnd];
        return true;
    }

    private static string DeriveIntelCoreTraditionalSocket(ReadOnlySpan<char> s)
    {
        for (int idx = 0; idx < s.Length - 2; idx++)
        {
            if ((s[idx] == 'I' || s[idx] == 'i') && char.IsDigit(s[idx + 1])
                && TryParseCoreModel(s, idx + 2, out var num, out var suffix))
            {
                return IntelSocketByGen(num, suffix);
            }
        }
        return "";
    }

    private static bool TryParseCoreModel(ReadOnlySpan<char> s, int sepIndex, out ReadOnlySpan<char> num, out ReadOnlySpan<char> suffix)
    {
        num = default;
        suffix = default;

        int sep = sepIndex;
        while (sep < s.Length && s[sep] != '-' && s[sep] != ' ') sep++;
        if (sep >= s.Length - 1) return false;

        int numStart = sep + 1;
        int numEnd = numStart;
        while (numEnd < s.Length && char.IsDigit(s[numEnd])) numEnd++;
        if (numEnd <= numStart) return false;

        int sufStart = numEnd;
        int sufEnd = sufStart;
        while (sufEnd < s.Length && char.IsLetter(s[sufEnd])) sufEnd++;

        num = s[numStart..numEnd];
        suffix = s[sufStart..sufEnd];
        return true;
    }

    private static string IntelSocketByGen(ReadOnlySpan<char> num, ReadOnlySpan<char> suffix)
    {
        int gen = num.Length >= 5 && num[0] == '1'
            ? (num[0] - '0') * 10 + (num[1] - '0')
            : num[0] - '0';

        return gen switch
        {
            >= 12 and <= 14 => ResolveIntelGen12To14Socket(suffix),
            10 or 11 => ResolveIntelGen10To11Socket(suffix),
            >= 6 and <= 9 => ResolveIntelGen6To9Socket(suffix),
            >= 1 and <= 5 => (suffix.Length == 0 || suffix.IndexOf('K') >= 0) ? "Socket LGA 1150" : "Socket BGA",
            _ => ""
        };
    }

    private static string ResolveIntelGen12To14Socket(ReadOnlySpan<char> suffix)
    {
        if (suffix.IndexOf("HX") >= 0 || suffix.IndexOf("HK") >= 0 || suffix.IndexOf('U') >= 0 || suffix.IndexOf('P') >= 0)
            return SocketBga1744;
        if (suffix.IndexOf('H') >= 0)
            return SocketBga1964;
        return SocketLga1700;
    }

    private static string ResolveIntelGen10To11Socket(ReadOnlySpan<char> suffix)
    {
        if (suffix.IndexOf('H') >= 0) return SocketBga1440;
        if (suffix.IndexOf('U') >= 0) return "Socket BGA 1526";
        return SocketLga1200;
    }

    private static string ResolveIntelGen6To9Socket(ReadOnlySpan<char> suffix)
    {
        if (suffix.IndexOf('H') >= 0) return SocketBga1440;
        if (suffix.Length == 0 || suffix.IndexOf('K') >= 0) return SocketLga1151;
        return "Socket BGA";
    }

    private static string IntelXeonSocket(ReadOnlySpan<char> s)
    {
        if (s.IndexOf("W9") >= 0 || s.IndexOf("W5") >= 0 || s.IndexOf("W7") >= 0) return "Socket LGA 4677";
        return "Socket LGA 4189";
    }

    private static string DeriveAmdSocket(ReadOnlySpan<char> s)
    {
        if (s.IndexOf("THREADRIPPER") >= 0)
            return (s.IndexOf("7000") >= 0 || s.IndexOf("7005") >= 0) ? "Socket sTR5" : "Socket sTRX4";

        if (s.IndexOf("EPYC") >= 0)
        {
            if (s.IndexOf("9004") >= 0 || s.IndexOf("9005") >= 0) return "Socket SP5";
            return s.IndexOf("8004") >= 0 ? "Socket SP6" : "Socket SP3";
        }

        return DeriveAmdRyzenSocket(s);
    }

    private static string DeriveAmdRyzenSocket(ReadOnlySpan<char> s)
    {
        int ryzenIdx = s.IndexOf("RYZEN");
        if (ryzenIdx < 0) return "";

        for (int i = ryzenIdx + 5; i < s.Length - 3; i++)
        {
            if (char.IsDigit(s[i]) && char.IsDigit(s[i + 1])
                && char.IsDigit(s[i + 2]) && char.IsDigit(s[i + 3]))
            {
                ReadOnlySpan<char> numStr = s[i..(i + 4)];
                int sufStart = i + 4;
                int sufEnd = sufStart;
                while (sufEnd < s.Length && char.IsLetter(s[sufEnd])) sufEnd++;
                ReadOnlySpan<char> suffix = s[sufStart..sufEnd];

                return AmdSocketByArch(numStr, suffix);
            }
        }
        return "";
    }

    private static string DeriveQualcommSocket(ReadOnlySpan<char> s)
    {
        if (s.IndexOf("SNAPDRAGON X", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Socket 2050";
        return "";
    }

    private static string AmdSocketByArch(ReadOnlySpan<char> num, ReadOnlySpan<char> suffix)
    {
        int arch = num[0] - '0';

        return arch switch
        {
            9 => SocketAm5,
            8 => ResolveZen4MobileSocket(suffix),
            7 => ResolveZen4OrMobileSocket(suffix),
            6 => SocketFp7,
            5 => ResolveZen3MobileSocket(suffix),
            4 => SocketFp6,
            3 or 2 => suffix.IndexOf('U') >= 0 ? SocketFp5 : SocketAm4,
            1 => suffix.IndexOf('U') >= 0 ? SocketFp4 : SocketAm4,
            _ => ""
        };
    }

    private static string ResolveZen4MobileSocket(ReadOnlySpan<char> suffix)
    {
        if (suffix.IndexOf('U') >= 0 || suffix.IndexOf("HS") >= 0 || suffix.IndexOf('H') >= 0)
            return SocketFp8;
        return SocketAm5;
    }

    private static string ResolveZen4OrMobileSocket(ReadOnlySpan<char> suffix)
    {
        if (suffix.IndexOf("HX") >= 0) return SocketFl1;
        if (suffix.IndexOf("HS") >= 0 || suffix.IndexOf('U') >= 0) return SocketFp8;
        return SocketAm5;
    }

    private static string ResolveZen3MobileSocket(ReadOnlySpan<char> suffix)
    {
        if (suffix.IndexOf("HX") >= 0) return SocketFp7;
        if (suffix.IndexOf('H') >= 0 || suffix.IndexOf("HS") >= 0 || suffix.IndexOf('U') >= 0) return SocketFp6;
        return SocketAm4;
    }

    private static bool IsSocketName(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        return s.Contains("LGA", StringComparison.OrdinalIgnoreCase)
            || s.Contains("Socket", StringComparison.OrdinalIgnoreCase)
            || s.Contains("BGA", StringComparison.OrdinalIgnoreCase)
            || s.Contains("PGA", StringComparison.OrdinalIgnoreCase)
            || s.Contains("FCBGA", StringComparison.OrdinalIgnoreCase)
            || s.Contains("FCLGA", StringComparison.OrdinalIgnoreCase)
            || s.Contains("Slot", StringComparison.OrdinalIgnoreCase)
            || s.Contains("AM", StringComparison.OrdinalIgnoreCase)
            || s.Contains("TR", StringComparison.OrdinalIgnoreCase)
            || s.Contains("SP", StringComparison.OrdinalIgnoreCase)
            || s.Contains("sWRX", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("FP", StringComparison.OrdinalIgnoreCase)  // AMD mobile FP5-FP8
            || s.StartsWith("FL", StringComparison.OrdinalIgnoreCase); // AMD FL1
    }

    /// <summary>True for SMBIOS/OEM placeholder strings that are not real socket names.</summary>
    private static bool IsPlaceholderSocket(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        string t = s.Trim();
        return t.Equals("Other", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            || t.Equals("None", StringComparison.OrdinalIgnoreCase)
            || t.Equals("CPU", StringComparison.OrdinalIgnoreCase)
            || t.Equals("CPU Socket", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Socket", StringComparison.OrdinalIgnoreCase)
            || t.Equals("To Be Filled By O.E.M.", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Default string", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Not Specified", StringComparison.OrdinalIgnoreCase)
            || t.Equals("N/A", StringComparison.OrdinalIgnoreCase);
    }

    private static string SmbiosSocketName(int code)
    {
        // Values per the official SMBIOS "Processor Upgrade" enumeration
        // (Microsoft Win32_Processor.UpgradeMethod / DMTF Processor Information Type 4).
        // Codes 1 (Other) and 2 (Unknown) are placeholders and intentionally return "".
        return code switch
        {
            3 => "", // Daughter Board (not a socket)
            4 => "ZIF Socket",
            5 => "", // Replacement/Piggy Back (not a socket)
            6 => "", // None
            7 => "LIF Socket",
            8 => "Slot 1",
            9 => "Slot 2",
            10 => "370-pin Socket",
            11 => "Slot A",
            12 => "Slot M",
            13 => "Socket 423",
            14 => "Socket A (Socket 462)",
            15 => "Socket 478",
            16 => "Socket 754",
            17 => "Socket 940",
            18 => "Socket 939",
            19 => "Socket mPGA604",
            20 => "Socket LGA 771",
            21 => "Socket LGA 775",
            22 => "Socket S1",
            23 => "Socket AM2",
            24 => "Socket F (1207)",
            25 => "Socket LGA 1366",
            26 => "Socket G34",
            27 => "Socket AM3",
            28 => "Socket C32",
            29 => "Socket LGA 1156",
            30 => "Socket LGA 1567",
            31 => "Socket PGA988A",
            32 => "Socket BGA 1288",
            33 => "Socket rPGA988B",
            34 => "Socket BGA 1023",
            35 => "Socket BGA 1224",
            36 => "Socket LGA 1155",
            37 => "Socket LGA 1356",
            38 => "Socket LGA 2011",
            39 => "Socket FS1",
            40 => "Socket FS2",
            41 => "Socket FM1",
            42 => "Socket FM2",
            43 => "Socket LGA 2011-3",
            44 => "Socket LGA 1356-3",
            45 => "Socket LGA 1150",
            46 => "Socket BGA 1168",
            47 => "Socket BGA 1234",
            48 => "Socket BGA 1364",
            49 => "Socket AM4",
            50 => "Socket LGA 1151",
            51 => "Socket BGA 1356",
            52 => "Socket BGA 1440",
            53 => "Socket BGA 1515",
            54 => "Socket LGA 3647",
            55 => "Socket SP3",
            56 => "Socket SP3r2",
            57 => "Socket LGA 2066",
            58 => "Socket BGA 1392",
            59 => "Socket BGA 1510",
            60 => "Socket BGA 1528",
            61 => "Socket LGA 4189",
            62 => "Socket LGA 1200",
            63 => "Socket LGA 4677",
            64 => "Socket LGA 1700",
            65 => "Socket BGA 1744",
            66 => "Socket BGA 1781",
            67 => "Socket BGA 1211",
            68 => "Socket BGA 2422",
            69 => "Socket LGA 5773",
            70 => "Socket BGA 5773",
            71 => "Socket AM5",
            72 => "Socket SP5",
            73 => "Socket SP6",
            74 => "Socket BGA 883",
            75 => "Socket BGA 1190",
            76 => "Socket BGA 4129",
            77 => "Socket LGA 4710",
            78 => "Socket LGA 7529",
            79 => "Socket BGA 1964",
            80 => "Socket BGA 1792",
            81 => "Socket BGA 2049",
            82 => "Socket BGA 2551",
            83 => "Socket LGA 1851",
            84 => "Socket BGA 2114",
            85 => "Socket BGA 2833",
            _ => ""
        };
    }

    // CPU

    /// <summary>Gets the full CPU processor name from the registry.</summary>
    /// <returns>e.g. <c>Intel(R) Core(TM) i7-14700K</c> or empty if unavailable.</returns>
    public static string GetCpuName()
    {
        if (_cpuNameCache is not null) return _cpuNameCache;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            _cpuNameCache = key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? string.Empty;
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to determine processor model name"); _cpuNameCache = string.Empty; }
        return _cpuNameCache;
    }

    /// <summary>Gets the CPU vendor name from the <c>PROCESSOR_IDENTIFIER</c> environment variable.</summary>
    /// <summary>Gets the CPU vendor string from the environment variable.</summary>
    /// <returns><c>Intel</c>, <c>AMD</c> or empty if unknown.</returns>
    public static string GetCpuVendor()
    {
        string? id = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (id is null) return "";
        if (id.Contains(IntelVendor, StringComparison.OrdinalIgnoreCase)) return IntelVendor;
        if (id.Contains("AMD", StringComparison.OrdinalIgnoreCase)) return "AMD";
        return "";
    }

    /// <summary>Gets the number of physical CPU cores via SMBIOS or GLPI fallback.</summary>
    /// <returns>Physical core count or <see cref="Environment.ProcessorCount"/> as fallback.</returns>
    public static int GetCpuCoreCount()
    {
        int smbios = GetSmbiosCoreCount();
        int glpi = GetPhysicalCoreCountFallback();
        if (smbios > 0 && smbios != glpi)
        {
            Diagnostics.Log.Info($"CPU core count mismatch: SMBIOS={smbios}, GLPI={glpi}; preferring GLPI");
            return glpi;
        }
        return smbios > 0 ? smbios : glpi;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformation(IntPtr buffer, ref uint returnLength);

    private static int GetPhysicalCoreCountFallback()
    {
        try
        {
            uint len = 0;
            GetLogicalProcessorInformation(IntPtr.Zero, ref len);
            if (Marshal.GetLastWin32Error() != 122 || len == 0) return Environment.ProcessorCount;
            IntPtr buf = Marshal.AllocHGlobal((int)len);
            try
            {
                if (!GetLogicalProcessorInformation(buf, ref len))
                    return Environment.ProcessorCount;
                int structSize = DetectGlpiStructSize((int)len);
                if (structSize == 0) return Environment.ProcessorCount;
                int cores = 0;
                for (int off = 0; off + structSize <= (int)len; off += structSize)
                {
                    int relationship = Marshal.ReadInt32(buf, off + IntPtr.Size);
                    if (relationship == 0) cores++;
                }
                return cores > 0 ? cores : Environment.ProcessorCount;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to determine physical core count via logical processor information"); return Environment.ProcessorCount; }
    }

    private static readonly int[] GlpiCandidateSizes = [24, 32, 16, 20];

    private static int DetectGlpiStructSize(int totalLen)
    {
        return GlpiCandidateSizes.FirstOrDefault(sz => totalLen >= sz && totalLen % sz == 0);
    }

    private static int GetSmbiosCoreCount()
    {
        try
        {
            byte[] raw = GetSmbiosRaw();
            return FindSmbiosType4Value(raw, 0x2A, 0x23);
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to determine processor core count"); }
        return 0;
    }

    /// <summary>Gets the number of CPU threads (logical processors) via SMBIOS.</summary>
    /// <returns>Thread count or <see cref="Environment.ProcessorCount"/> as fallback.</returns>
    public static int GetCpuThreadCount()
    {
        try
        {
            byte[] raw = GetSmbiosRaw();
            int threads = FindSmbiosType4Value(raw, 0x2E, 0x25);
            if (threads > 0) return threads;
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to determine processor thread count"); }
        return Environment.ProcessorCount;
    }

    private static int FindSmbiosType4Value(byte[] raw, int wordOffset, int byteOffset)
    {
        if (raw.Length == 0) return 0;
        int p = 0;
        while (p < raw.Length)
        {
            byte t = raw[p];
            byte len = raw[p + 1];
            if (len < 4 || p + len > raw.Length) break;
            if (t == 4)
            {
                if (len >= wordOffset + 2)
                {
                    int wordVal = raw[p + wordOffset] | (raw[p + wordOffset + 1] << 8);
                    if (wordVal > 0) return wordVal;
                }
                if (len >= byteOffset + 1)
                {
                    int byteVal = raw[p + byteOffset];
                    if (byteVal > 0) return byteVal;
                }
            }
            p += len;
            while (p < raw.Length && !(raw[p] == 0 && raw[p + 1] == 0)) p++;
            p += 2;
            if (t == 127) break;
        }
        return 0;
    }

    /// <summary>Gets the maximum (turbo) CPU frequency in MHz via MSR, CPUID or WMI.</summary>
    /// <returns>Max frequency in MHz or 0 if unavailable.</returns>
    public static int GetCpuMaxSpeedMhz()
    {
        int msrMhz = QueryCpuMaxSpeedMsr();
        if (msrMhz > 0) return msrMhz;

        int cpuidMhz = QueryCpuMaxSpeedCpuid();
        if (cpuidMhz > 0) return cpuidMhz;

        try
        {
            byte[] raw = GetSmbiosRaw();
            int smbiosMhz = FindSmbiosType4Value(raw, 0x14, 0x14);
            if (smbiosMhz > 0) return smbiosMhz;
        }
        catch (Exception ex) { Diagnostics.Log.Debug(ex, "Failed to query CPU max speed via SMBIOS"); }

        int ntMhz = QueryNtPowerMaxMhz();
        int wmiMhz = QueryWmiFirstInt("SELECT MaxClockSpeed FROM Win32_Processor", "MaxClockSpeed");
        if (ntMhz > 0 && ntMhz != wmiMhz && ntMhz > 1000) return ntMhz;
        if (wmiMhz > 0) return wmiMhz;

        return QueryRegistryProcessorSpeed();
    }

    private static int QueryCpuMaxSpeedMsr()
    {
        try
        {
            var pawnIo = HardwareMonitor.SharedPawnIoDevice;
            if (pawnIo is null || GetCpuVendor() != IntelVendor) return 0;

            int mhz = ReadMsrRatioMhz(pawnIo, 0x1AD);
            if (mhz > 0) return mhz;

            return ReadMsrRatioMhz(pawnIo, 0x771);
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query maximum processor frequency via MSR"); }
        return 0;
    }

    private static int ReadMsrRatioMhz(IPawnIoTransport pawnIo, uint msr)
    {
        var result = pawnIo.Execute("ioctl_read_msr", [msr], 1);
        if (result.Length >= 1)
        {
            uint ratio = (uint)(result[0] & 0xFF);
            if (ratio is > 0 and < 0xFF)
            {
                int mhz = (int)ratio * 100;
                if (mhz > 1000) return mhz;
            }
        }
        return 0;
    }

    private static int QueryCpuMaxSpeedCpuid()
    {
        if (!X86Base.IsSupported) return 0;
        try
        {
            var (eax, ebx, ecx, _) = X86Base.CpuId(0x16, 0);
            if (ebx is > 0 and < 0xFFFF && ebx > eax) return ebx;
            if (ecx is > 0 and < 0xFFFF && ecx > eax) return ecx;
            if (ebx is > 0 and < 0xFFFF) return ebx;
            if (eax is > 0 and < 0xFFFF) return eax;
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query maximum processor frequency via CPUID"); }
        return 0;
    }

    private static int QueryNtPowerMaxMhz()
    {
        try
        {
            int count = Environment.ProcessorCount;
            int size = Marshal.SizeOf<NativeMethods.ProcessorPowerInformation>() * count;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                int ret = NativeMethods.CallNtPowerInformation(
                    NativeMethods.ProcessorInformation, IntPtr.Zero, 0, buf, size);
                if (ret == 0)
                {
                    uint max = 0;
                    for (int i = 0; i < count; i++)
                    {
                        var ppi = Marshal.PtrToStructure<NativeMethods.ProcessorPowerInformation>(
                            buf + i * Marshal.SizeOf<NativeMethods.ProcessorPowerInformation>());
                        if (ppi.MaxMhz > max) max = ppi.MaxMhz;
                    }
                    if (max is > 0 and < 0xFFFF) return (int)max;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query processor power information"); }
        return 0;
    }

    private static int QueryRegistryProcessorSpeed()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key is not null)
            {
                if (key.GetValue("~MHz") is int mhz && mhz is > 0 and < 0xFFFF)
                    return mhz;
                if (key.GetValue("ProcessorMaxTurboBoostMHz") is int turbo && turbo is > 0 and < 0xFFFF)
                    return turbo;
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query processor frequency from registry"); }
        return 0;
    }

    /// <summary>Gets the base CPU frequency in MHz from WMI.</summary>
    /// <returns>Base frequency in MHz or 0 if unavailable.</returns>
    public static int GetCpuBaseSpeedMhz()
    {
        return QueryWmiFirstInt("SELECT MaxClockSpeed FROM Win32_Processor", "MaxClockSpeed");
    }

    // GPU
    /// <summary>Enumerates all GPUs via Setup API and registry.</summary>
    /// <returns>Array of GPU info tuples (Name, Vendor, IsDedicated, VramBytes, DriverVersion).</returns>
    public static (string Name, string Vendor, bool IsDedicated, long? VramBytes, string? DriverVersion)[] GetGpus()
    {
        if (_gpusCache is not null) return _gpusCache;
        var results = new List<(string, string, bool, long?, string?)>();
        var displayGuid = NativeMethods.GUID_DISPLAY;
        IntPtr devInfoSet = NativeMethods.SetupDiGetClassDevs(ref displayGuid, IntPtr.Zero,
            IntPtr.Zero, NativeMethods.DIGCF_PRESENT);
        if (devInfoSet.ToInt64() == -1) return [];

        try
        {
            NativeMethods.SpDevinfoData devInfo = new();
            devInfo.cbSize = (uint)Marshal.SizeOf<NativeMethods.SpDevinfoData>();
            uint i = 0;

            while (NativeMethods.SetupDiEnumDeviceInfo(devInfoSet, i++, ref devInfo))
            {
                string name = GetDevPropStr(devInfoSet, devInfo, NativeMethods.SPDRP_DEVICEDESC);
                if (string.IsNullOrEmpty(name)) continue;

                string hwId = GetDevPropStr(devInfoSet, devInfo, NativeMethods.SPDRP_HARDWAREID);
                string vendor = ParseVendor(hwId, name);
                if (string.IsNullOrEmpty(vendor)) continue;

                bool dedicated = vendor == NvidiaVendor || IsDiscreteGpu(name, vendor, devInfoSet, devInfo);
                (long? vram, string? driver) = GetGpuRegistryData(name);
                results.Add((name, vendor, dedicated, vram, driver));
            }
        }
        finally { NativeMethods.SetupDiDestroyDeviceInfoList(devInfoSet); }

        _gpusCache = results.ToArray();
        return _gpusCache;
    }

    private static (long? VramBytes, string? DriverVersion) GetGpuRegistryData(string deviceDesc)
    {
        const string baseKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(baseKey);
            if (root is null) return (null, null);

            foreach (var subName in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(subName);
                if (sub is null) continue;

                string? driverDesc = sub.GetValue("DriverDesc") as string;
                if (string.IsNullOrEmpty(driverDesc)) continue;

                if (!driverDesc.Equals(deviceDesc, StringComparison.OrdinalIgnoreCase))
                    continue;

                long? vram = null;
                if (sub.GetValue("HardwareInformation.qwMemorySize") is byte[] qwordBytes && qwordBytes.Length >= 8)
                    vram = BitConverter.ToInt64(qwordBytes, 0);

                string? driverVer = sub.GetValue("DriverVersion") as string;
                if (string.IsNullOrEmpty(driverVer)) driverVer = null;

                return (vram, driverVer);
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query GPU details from registry"); }
        return (null, null);
    }

    // RAM
    /// <summary>Gets the total physical RAM in bytes via <c>GlobalMemoryStatusEx</c>.</summary>
    /// <returns>Total physical memory in bytes.</returns>
    public static long GetTotalRamBytes()
    {
        if (_ramTotalBytesCache.HasValue) return _ramTotalBytesCache.Value;
        var mem = new NativeMethods.MemoryStatusEx();
        mem.dwLength = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>();
        if (NativeMethods.GlobalMemoryStatusEx(ref mem))
            _ramTotalBytesCache = (long)mem.ullTotalPhys;
        else
            _ramTotalBytesCache = 0;
        return _ramTotalBytesCache.Value;
    }

    /// <summary>Calculates total installed RAM in GB from all physical memory modules.</summary>
    /// <returns>Total RAM in GB, rounded to one decimal place.</returns>
    public static double GetInstalledRamGb()
    {
        try
        {
            double total = 0;
            var modules = GetRamModules();
            foreach (var m in modules)
            {
                if (m.TryGetValue("sizeGb", out var size) && size is double d)
                    total += d;
            }
            if (total > 0) return Math.Round(total, 1);
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query total installed physical memory"); }
        return GetTotalRamBytes() / (1024.0 * 1024.0 * 1024.0);
    }

    /// <summary>Gets the manufacturer name of the first detected RAM module.</summary>
    /// <returns>Manufacturer name (e.g. <c>Corsair</c>, <c>Samsung</c>) or empty if unknown.</returns>
    public static string GetRamManufacturer()
    {
        try
        {
            var modules = GetRamModules();
            foreach (var m in modules)
            {
                if (m.TryGetValue("manufacturer", out var mfr) && mfr is string s && !string.IsNullOrEmpty(s))
                    return s;
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query memory module manufacturer details"); }

        var smbiosMfrs = GetSmbiosRamManufacturers();
        string? firstSmbios = smbiosMfrs.FirstOrDefault(mfr => !string.IsNullOrEmpty(mfr));
        if (!string.IsNullOrEmpty(firstSmbios)) return firstSmbios;

        return "";
    }

    /// <summary>Gets the number of installed physical memory modules.</summary>
    public static int GetRamModuleCount() => GetRamModules().Count;

    /// <summary>Gets the speed of the first detected RAM module (e.g. <c>4800 MT/s</c>).</summary>
    /// <returns>Speed string or empty if unknown.</returns>
    public static string GetRamSpeed()
    {
        try
        {
            var modules = GetRamModules();
            foreach (var m in modules)
            {
                if (m.TryGetValue("speed", out var speed) && speed is string s && !string.IsNullOrEmpty(s))
                    return s;
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query memory module speed"); }
        return "";
    }

    // Motherboard
    private static string GetBiosRegValue(string name)
    {
        try { using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"); return k?.GetValue(name)?.ToString()?.Trim() ?? ""; } catch (Exception ex) { Diagnostics.Log.Error(ex, $"Failed to query BIOS registry value '{name}'"); return ""; }
    }

    /// <summary>Gets the motherboard manufacturer, from registry then WMI fallback.</summary>
    public static string GetMotherboardManufacturer()
    {
        string reg = GetBiosRegValue("BaseBoardManufacturer");
        if (reg.Length > 0) return reg;
        return QueryWmiFirst("SELECT Manufacturer FROM Win32_BaseBoard", "Manufacturer");
    }

    /// <summary>Gets the motherboard product name, from registry then WMI fallback.</summary>
    public static string GetMotherboardProductName()
    {
        string reg = GetBiosRegValue("BaseBoardProduct");
        if (reg.Length > 0) return reg;
        return QueryWmiFirst("SELECT Product FROM Win32_BaseBoard", "Product");
    }

    /// <summary>Gets the motherboard version, from registry then WMI fallback.</summary>
    public static string GetMotherboardVersion()
    {
        string reg = GetBiosRegValue("BaseBoardVersion");
        if (reg.Length > 0) return reg;
        return QueryWmiFirst("SELECT Version FROM Win32_BaseBoard", "Version");
    }

    /// <summary>Gets the motherboard serial number, from registry then WMI fallback.</summary>
    public static string GetMotherboardSerial()
    {
        string reg = GetBiosRegValue("BaseBoardSerial");
        if (reg.Length > 0) return reg;
        return QueryWmiFirst("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber");
    }

    /// <summary>Gets the BIOS version and release date from the registry.</summary>
    /// <returns>e.g. <c>F15 (04/15/2024)</c> or empty if unavailable.</returns>
    public static string GetBiosVersion()
    {
        string ver = GetBiosRegValue("BIOSVersion");
        string date = GetBiosRegValue("BIOSReleaseDate");
        if (string.IsNullOrEmpty(ver) && string.IsNullOrEmpty(date)) return "";

        if (!string.IsNullOrEmpty(date) && DateTime.TryParseExact(date, ["MM/dd/yyyy", "M/d/yyyy", "MM/dd/yy", "yyyyMMdd"],
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
        {
            date = parsedDate.ToString("dd-MM-yyyy");
        }

        return string.IsNullOrEmpty(date) ? ver : $"{ver} ({date})";
    }

    /// <summary>Determines whether the system is booting in UEFI mode.</summary>
    /// <returns><see langword="true"/> if UEFI, <see langword="false"/> if legacy BIOS or unknown.</returns>
    public static bool GetBiosIsUefi()
    {
        try
        {
            return NativeMethods.GetFirmwareType(out var ft)
                && ft == NativeMethods.FirmwareType.Uefi;
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to determine UEFI firmware boot mode"); return false; }
    }

    // System (SMBIOS Type 1)
    /// <summary>Gets the system manufacturer (OEM) name via WMI or SMBIOS.</summary>
    /// <returns>e.g. <c>Dell Inc.</c>, <c>ASUS</c> or empty if unavailable.</returns>
    public static string GetSystemManufacturer()
    {
        string wmi = QueryWmiFirst("SELECT Manufacturer FROM Win32_ComputerSystem", "Manufacturer");
        if (wmi.Length > 0) return wmi;
        try
        {
            byte[] raw = GetSmbiosRaw();
            if (raw.Length == 0) return "";
            return FindSmbiosString(raw, 1, 0x04);
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query system manufacturer name"); }
        return "";
    }

    /// <summary>Gets the system product name (model) via WMI or SMBIOS.</summary>
    /// <returns>e.g. <c>Precision 7920</c> or empty if unavailable.</returns>
    public static string GetSystemProductName()
    {
        string wmi = QueryWmiFirst("SELECT Model FROM Win32_ComputerSystem", "Model");
        if (wmi.Length > 0) return wmi;
        try
        {
            byte[] raw = GetSmbiosRaw();
            if (raw.Length == 0) return "";
            return FindSmbiosString(raw, 1, 0x05);
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query system product name"); }
        return "";
    }

    /// <summary>Gets a combined system model string (manufacturer + product name).</summary>
    /// <returns>e.g. <c>Dell Inc. XPS 15 9500</c> or the manufacturer alone or empty.</returns>
    public static string GetSystemModel()
    {
        string mfr = GetSystemManufacturer();
        string product = GetSystemProductName();
        if (string.IsNullOrEmpty(mfr)) return product;
        if (string.IsNullOrEmpty(product)) return mfr;
        return $"{mfr} {product}";
    }

    // Memory Modules
    /// <summary>Gets detailed information about all installed physical memory modules via WMI.</summary>
    /// <returns>List of dictionaries with keys: manufacturer, partNumber, serial, sizeGb, speed, ecc, formFactor, type.</returns>
    public static List<Dictionary<string, object?>> GetRamModules()
    {
        if (_ramModulesCache is not null)
            return _ramModulesCache;

        return _ramModulesCache = QueryWmiRamModules();
    }

    private static List<Dictionary<string, object?>> QueryWmiRamModules()
    {
        var results = new List<Dictionary<string, object?>>();
        var smbiosMfrs = GetSmbiosRamManufacturers();
        int moduleIdx = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
            foreach (ManagementBaseObject mo in searcher.Get())
            {
                ulong capacity = Convert.ToUInt64(mo["Capacity"] ?? 0UL);
                if (capacity == 0) continue;

                string mfr = moduleIdx < smbiosMfrs.Count ? smbiosMfrs[moduleIdx] : "";
                if (string.IsNullOrEmpty(mfr))
                    mfr = (mo["Manufacturer"] as string ?? "").Trim();
                moduleIdx++;

                string partNumber = (mo["PartNumber"] as string ?? "").Trim();
                string serial = (mo["SerialNumber"] as string ?? "").Trim();
                uint speed = Convert.ToUInt32(mo["Speed"] ?? 0U);
                ushort ff = Convert.ToUInt16(mo["FormFactor"] ?? (ushort)0);
                ushort mt = Convert.ToUInt16(mo["MemoryType"] ?? (ushort)0);
                if (mt == 0)
                    mt = Convert.ToUInt16(mo["SMBIOSMemoryType"] ?? (ushort)0);

                results.Add(new()
                {
                    ["manufacturer"] = mfr,
                    ["partNumber"] = partNumber,
                    ["serial"] = serial,
                    ["sizeGb"] = capacity / (1024.0 * 1024.0 * 1024.0),
                    ["speed"] = speed > 0 ? $"{speed} MT/s" : "",
                    ["ecc"] = false,
                    ["formFactor"] = FormFactorLabel(ff),
                    ["type"] = MemoryTypeLabel(mt),
                });
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query physical memory modules via system management"); }
        return results;
    }

    private static List<string> GetSmbiosRamManufacturers()
    {
        var mfrs = new List<string>();
        try
        {
            byte[] raw = GetSmbiosRaw();
            if (raw.Length == 0) return mfrs;

            int p = 0;
            while (p < raw.Length)
            {
                byte t = raw[p];
                byte len = raw[p + 1];
                if (len < 4 || p + len > raw.Length) break;

                if (t == 17 && len > 0x17)
                {
                    byte idx = raw[p + 0x17];
                    mfrs.Add(GetSmbiosString(raw, p, len, idx));
                }

                p += len;
                while (p < raw.Length && !(raw[p] == 0 && raw[p + 1] == 0)) p++;
                p += 2;
                if (t == 127) break;
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query memory module manufacturers via SMBIOS"); }
        return mfrs;
    }

    private static string FormFactorLabel(int ff) => ff switch
    {
        0x01 => "Other",
        0x02 => UnknownLabel,
        0x03 => "SIMM",
        0x04 => "SIP",
        0x05 => "Chip",
        0x06 => "DIP",
        0x07 => "ZIP",
        0x08 => "Proprietary Card",
        0x09 => "DIMM",
        0x0A => "SODIMM",
        0x0B => "SORDIMM",
        0x0C => "Mini-DIMM",
        0x0D => "Mini-RDIMM",
        _ => $"Type{ff:X}",
    };

    private static string MemoryTypeLabel(int mt) => mt switch
    {
        0x00 => UnknownLabel,
        0x11 => "SDRAM",
        0x12 => "DDR",
        0x13 => "DDR2",
        0x14 => "DDR2 FB-DIMM",
        0x18 => "DDR3",
        0x19 => "DDR3 FB-DIMM",
        0x1A => "DDR4",
        0x1B => "LPDDR",
        0x1C => "LPDDR2",
        0x1D => "LPDDR3",
        0x1E => "LPDDR4",
        0x20 => "HBM",
        0x21 => "HBM2",
        0x22 => "DDR5",
        0x23 => "LPDDR5",
        0x24 => "HBM3",
        0x26 => "LPDDR5X",
        _ => $"Type{mt:X}",
    };

    /// <summary>Gets the RAM technology type (e.g. <c>DDR5</c>, <c>DDR4</c>) from WMI, falling back to SMBIOS Type 17.</summary>
    /// <returns>Memory type label or <c>Unknown</c> if detection fails.</returns>
    public static string GetRamType()
    {
        string wmiType = QueryRamTypeWmi();
        if (!string.IsNullOrEmpty(wmiType)) return wmiType;

        string smbiosType = QueryRamTypeSmbios();
        if (!string.IsNullOrEmpty(smbiosType)) return smbiosType;

        return MemoryTypeLabel(0);
    }

    private static string QueryRamTypeWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT MemoryType, SMBIOSMemoryType FROM Win32_PhysicalMemory");
            using var results = searcher.Get();
            foreach (ManagementBaseObject mo in results)
            {
                ushort mt = Convert.ToUInt16(mo["MemoryType"] ?? (ushort)0);
                if (mt == 0)
                    mt = Convert.ToUInt16(mo["SMBIOSMemoryType"] ?? (ushort)0);
                if (mt != 0) return MemoryTypeLabel(mt);
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to detect memory type via system management"); }
        return "";
    }

    private static string QueryRamTypeSmbios()
    {
        try
        {
            byte[] raw = GetSmbiosRaw();
            int mt = FindSmbiosType17MemoryType(raw);
            if (mt > 0) return MemoryTypeLabel(mt);
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to detect memory type via SMBIOS tables"); }
        return "";
    }

    private static int FindSmbiosType17MemoryType(byte[] raw)
    {
        if (raw.Length == 0) return 0;
        int p = 0;
        while (p < raw.Length)
        {
            byte t = raw[p];
            byte len = raw[p + 1];
            if (len < 4 || p + len > raw.Length) break;
            if (t == 17 && len > 0x12 && raw[p + 0x12] > 0)
                return raw[p + 0x12];

            p = AdvanceSmbiosPointer(raw, p, len);
            if (t == 127) break;
        }
        return 0;
    }

    private static int AdvanceSmbiosPointer(byte[] raw, int p, byte len)
    {
        p += len;
        while (p < raw.Length && !(raw[p] == 0 && raw[p + 1] == 0)) p++;
        return p + 2;
    }

    // System Type
    /// <summary>Gets whether the system is a laptop (has battery) or desktop.</summary>
    /// <returns><c>Laptop</c> or <c>Desktop</c>.</returns>
    public static string GetSystemTypeLabel()
    {
        return HasBattery() ? "Laptop" : "Desktop";
    }

    /// <summary>Detects whether the system has a battery (laptop/tablet).</summary>
    public static bool HasBattery()
    {
        if (_hasBatteryCache.HasValue) return _hasBatteryCache.Value;

        bool hasBattery = false;
        try
        {
            if (NativeMethods.GetSystemPowerStatus(out var sps))
                hasBattery = sps.BatteryFlag != 128;
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query battery status via system power API"); }

        // WMI check as secondary detection - some laptops have ACPI batteries
        // that GetSystemPowerStatus doesn't enumerate correctly
        if (!hasBattery)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                using var results = searcher.Get();
                if (results.Count > 0)
                {
                    hasBattery = true;
                    Diagnostics.Log.Info("Battery detected via system management (system power API reported no battery)");
                }
            }
            catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query battery presence via system management"); }
        }

        _hasBatteryCache = hasBattery;
        return hasBattery;
    }

    // Battery (SMBIOS Type 22)
    private static string GetSmbiosBatteryString(int offset)
    {
        byte[] raw = GetSmbiosRaw();
        if (raw.Length == 0) return "";
        return FindSmbiosString(raw, 22, (byte)offset);
    }

    private static int GetSmbiosBatteryWord(int offset)
    {
        byte[] raw = GetSmbiosRaw();
        if (raw.Length == 0) return 0;
        int p = 0;
        while (p < raw.Length)
        {
            byte t = raw[p];
            byte len = raw[p + 1];
            if (len < 4 || p + len > raw.Length) break;
            if (t == 22 && offset + 2 <= len)
            {
                int val = raw[p + offset] | (raw[p + offset + 1] << 8);
                if (val > 0) return val;
            }
            p = AdvanceSmbiosPointer(raw, p, len);
            if (t == 127) break;
        }
        return 0;
    }

    // Battery helpers
    private static int TryParseWmiInt(object? val)
    {
        if (val is null) return 0;
        string s = val.ToString()?.Trim() ?? "";
        return s.Length > 0 && int.TryParse(s, out int n) && n > 0 ? n : 0;
    }

    /// <summary>Queries a WMI class in ROOT\WMI and returns the first non-empty string property value.</summary>
    private static string QueryRootWmiString(string className, string property)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\WMI");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT {property} FROM {className}"));
            using var results = searcher.Get();
            foreach (ManagementBaseObject mo in results)
            {
                string val = (mo[property] as string ?? "").Trim();
                if (val.Length > 0) return val;
            }
        }
        catch (ManagementException ex)
        {
            Diagnostics.Log.Debug($"Failed to query '{property}' from ROOT\\WMI class '{className}': {ex.Message}");
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, $"Failed to query '{property}' from ROOT\\WMI class '{className}'");
        }
        return "";
    }

    /// <summary>Queries a WMI class in ROOT\WMI and returns the first positive int property value.</summary>
    private static int QueryRootWmiInt(string className, string property)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\WMI");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT {property} FROM {className}"));
            using var results = searcher.Get();
            var mo = results.Cast<ManagementBaseObject>().FirstOrDefault();
            if (mo is not null)
                return TryParseWmiInt(mo[property]);
        }
        catch (ManagementException ex)
        {
            Diagnostics.Log.Debug($"Failed to query integer '{property}' from ROOT\\WMI class '{className}': {ex.Message}");
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, $"Failed to query integer '{property}' from ROOT\\WMI class '{className}'");
        }
        return 0;
    }

    private static bool IsValidBatteryString(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        string trimmed = s.Trim();
        if (trimmed.Length == 0) return false;
        if (trimmed.Contains("Virtual Battery", StringComparison.OrdinalIgnoreCase)) return false;
        if (trimmed.Contains("CRB Battery", StringComparison.OrdinalIgnoreCase)) return false;
        if (trimmed.StartsWith("CRB", StringComparison.OrdinalIgnoreCase)) return false;
        if (trimmed.Equals("Fake", StringComparison.OrdinalIgnoreCase)) return false;
        if (trimmed.Equals("None", StringComparison.OrdinalIgnoreCase)) return false;
        if (trimmed.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return false;
        if (trimmed.Contains("To be filled", StringComparison.OrdinalIgnoreCase)) return false;
        if (trimmed.StartsWith('-') && trimmed.EndsWith('-')) return false;
        return true;
    }

    private static string QueryWin32BatteryName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Battery");
            using var results = searcher.Get();
            foreach (ManagementBaseObject mo in results)
            {
                string name = (mo["Name"] as string ?? "").Trim();
                if (name.Length > 0) return name;
            }
        }
        catch (ManagementException ex) { Diagnostics.Log.Debug($"Failed to query battery device name from system management: {ex.Message}"); }
        catch (Exception ex) { Diagnostics.Log.Debug(ex, "Failed to query battery device name from system management"); }
        return "";
    }

    private static string QueryWin32BatteryManufacturer()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_Battery");
            using var results = searcher.Get();
            foreach (ManagementBaseObject mo in results)
            {
                string devId = (mo["DeviceID"] as string ?? "").Trim();
                if (devId.Length == 0) continue;

                var matchedBrand = BrandColorDefaults.BatteryColors.Keys
                    .FirstOrDefault(brand => devId.Contains(brand, StringComparison.OrdinalIgnoreCase));
                if (matchedBrand != null)
                    return matchedBrand;
            }
        }
        catch (ManagementException ex) { Diagnostics.Log.Debug($"Failed to query battery manufacturer from Win32_Battery: {ex.Message}"); }
        catch (Exception ex) { Diagnostics.Log.Debug(ex, "Failed to query battery manufacturer from Win32_Battery"); }
        return "";
    }

    /// <summary>Gets the battery manufacturer name from WMI or SMBIOS.</summary>
    /// <returns>e.g. <c>Sunwoda</c>, <c>LGC</c> or empty if no battery present.</returns>
    public static string GetBatteryManufacturer()
    {
        if (!HasBattery()) return "";
        if (_batteryManufacturerCache is not null) return _batteryManufacturerCache;

        // 1. Live ACPI battery static data from root\wmi (queried from battery BMS)
        string wmi = QueryRootWmiString("BatteryStaticData", "ManufactureName");
        if (IsValidBatteryString(wmi)) return _batteryManufacturerCache = wmi;

        // 2. SMBIOS Type 22 (Portable Battery) if valid and not a virtual/dummy placeholder
        string smbios = GetSmbiosBatteryString(0x05);
        if (IsValidBatteryString(smbios)) return _batteryManufacturerCache = smbios;

        // 3. Fallback: Parse manufacturer from Win32_Battery DeviceID (e.g. "818SunwodaL24D4PC0")
        string win32Mfr = QueryWin32BatteryManufacturer();
        if (IsValidBatteryString(win32Mfr)) return _batteryManufacturerCache = win32Mfr;

        return _batteryManufacturerCache = "";
    }

    /// <summary>Gets the battery device name from WMI or SMBIOS.</summary>
    /// <returns>Device name or empty if no battery present.</returns>
    public static string GetBatteryDeviceName()
    {
        if (!HasBattery()) return "";
        if (_batteryDeviceNameCache is not null) return _batteryDeviceNameCache;

        // 1. Live ACPI battery static data from root\wmi
        string staticName = QueryRootWmiString("BatteryStaticData", "DeviceName");
        if (IsValidBatteryString(staticName)) return _batteryDeviceNameCache = staticName;

        // 2. Win32_Battery Name
        string win32Name = QueryWin32BatteryName();
        if (IsValidBatteryString(win32Name)) return _batteryDeviceNameCache = win32Name;

        // 3. SMBIOS Type 22 (Portable Battery) if valid and not a virtual/dummy placeholder
        string smbios = GetSmbiosBatteryString(0x08);
        if (IsValidBatteryString(smbios)) return _batteryDeviceNameCache = smbios;

        return _batteryDeviceNameCache = "";
    }

    /// <summary>Gets the battery design capacity in mWh.</summary>
    /// <returns>e.g. <c>50000 mWh</c> or empty if unavailable.</returns>
    public static string GetBatteryDesignCapacity()
    {
        if (!HasBattery()) return "";
        if (_batteryDesignCapacityCache is not null) return _batteryDesignCapacityCache;

        // 1. Live ACPI battery static data from root\wmi
        int staticMwh = QueryRootWmiInt("BatteryStaticData", "DesignedCapacity");
        if (staticMwh > 0) return _batteryDesignCapacityCache = $"{staticMwh} mWh";

        // 2. SMBIOS Type 22 (Portable Battery)
        int smbiosMwh = GetSmbiosBatteryWord(0x0A);
        if (smbiosMwh > 0) return _batteryDesignCapacityCache = $"{smbiosMwh} mWh";

        return _batteryDesignCapacityCache = "";
    }

    /// <summary>Gets the battery full charged capacity in mWh from WMI.</summary>
    /// <returns>e.g. <c>45000 mWh</c> or empty if unavailable.</returns>
    public static string GetBatteryFullChargedCapacity()
    {
        if (!HasBattery()) return "";
        if (_batteryFullChargeCapacityCache is not null) return _batteryFullChargeCapacityCache;
        int fccWmi = QueryRootWmiInt("BatteryFullChargedCapacity", "FullChargedCapacity");
        if (fccWmi > 0) return _batteryFullChargeCapacityCache = $"{fccWmi} mWh";
        return _batteryFullChargeCapacityCache = "";
    }

    // PSU (SMBIOS Type 39)
    /// <summary>Gets the PSU (power supply) name from SMBIOS Type 39.</summary>
    /// <returns>e.g. <c>Seasonic FOCUS GX-850</c> or empty if unavailable.</returns>
    public static string GetPsuName()
    {
        try
        {
            byte[] raw = GetSmbiosRaw();
            if (raw.Length == 0) return "";
            string mfr = FindSmbiosString(raw, 39, 0x06);
            string model = FindSmbiosString(raw, 39, 0x08);
            if (string.IsNullOrEmpty(mfr) && string.IsNullOrEmpty(model)) return "";
            if (string.IsNullOrEmpty(mfr)) return model;
            if (string.IsNullOrEmpty(model)) return mfr;
            return $"{mfr} {model}";
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query power supply name from SMBIOS"); return ""; }
    }

    /// <summary>Gets the PSU maximum capacity in watts from SMBIOS Type 39.</summary>
    /// <returns>e.g. <c>850 W</c> or empty if unavailable.</returns>
    public static string GetPsuMaxCapacity()
    {
        try
        {
            byte[] raw = GetSmbiosRaw();
            if (raw.Length == 0) return "";
            int p = 0;
            while (p < raw.Length)
            {
                byte t = raw[p];
                byte len = raw[p + 1];
                if (len < 4 || p + len > raw.Length) break;
                if (t == 39 && len >= 0x0B)
                {
                    int cap = raw[p + 0x0A] | (raw[p + 0x0B] << 8);
                    if (cap > 0 && cap != 0x8000)
                        return $"{cap / 1000.0:F0} W";
                }
                p = AdvanceSmbiosPointer(raw, p, len);
                if (t == 127) break;
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to query power supply capacity from SMBIOS"); }
        return "";
    }

    // Disk
    /// <summary>Gets the model of the primary (disk 0) physical drive.</summary>
    /// <returns>Model string or empty if unavailable.</returns>
    public static string GetDiskModel()
    {
        return DiskQuery.QueryPhysicalDrive0()?.Model ?? "";
    }

    /// <summary>Gets the manufacturer/vendor of the primary (disk 0) physical drive.</summary>
    /// <returns>Manufacturer name or empty if unavailable.</returns>
    public static string GetDiskManufacturer()
    {
        var drive = DiskQuery.QueryPhysicalDrive0();
        if (drive is null) return "";
        if (!string.IsNullOrEmpty(drive.Vendor)) return drive.Vendor;
        // WMI rarely populates Vendor; extract from model name instead.
        string model = drive.Model;
        if (string.IsNullOrEmpty(model)) return "";
        var match = BrandColorDefaults.DiskColors.Keys.FirstOrDefault(k => model.Contains(k, StringComparison.OrdinalIgnoreCase));
        return match ?? "";
    }

    // Disk enumeration
    /// <summary>Enumerates all physical disk drives via WMI.</summary>
    /// <returns>List of <see cref="DiskInfo"/> records.</returns>
    public static List<DiskInfo> GetAllDisks()
    {
        try
        {
            return DiskQuery.QueryAllDrives()
                .OrderBy(d => d.DiskIndex)
                .ToList();
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error(ex, "Failed to query all physical disk drives");
            return [];
        }
    }

    // NIC
    /// <summary>Enumerates physical network adapters via Setup API, filtering out virtual adapters.</summary>
    /// <returns>Array of NIC info tuples (Name, Manufacturer).</returns>
    public static (string Name, string Manufacturer)[] GetNics()
    {
        if (_nicsCache is not null) return _nicsCache;
        var results = new List<(string, string)>();
        var netGuid = NativeMethods.GUID_NET;
        IntPtr devInfoSet = NativeMethods.SetupDiGetClassDevs(ref netGuid, IntPtr.Zero,
            IntPtr.Zero, NativeMethods.DIGCF_PRESENT);
        if (devInfoSet.ToInt64() == -1) return [];

        try
        {
            NativeMethods.SpDevinfoData devInfo = new();
            devInfo.cbSize = (uint)Marshal.SizeOf<NativeMethods.SpDevinfoData>();
            uint i = 0;

            while (NativeMethods.SetupDiEnumDeviceInfo(devInfoSet, i++, ref devInfo))
            {
                string name = GetDevPropStr(devInfoSet, devInfo, NativeMethods.SPDRP_DEVICEDESC);
                if (string.IsNullOrEmpty(name)) continue;

                string hwId = GetDevPropStr(devInfoSet, devInfo, NativeMethods.SPDRP_HARDWAREID);
                if (hwId.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase) ||
                    hwId.StartsWith("SWD\\", StringComparison.OrdinalIgnoreCase) ||
                    hwId.StartsWith("WAN\\", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (name.Contains("Miniport", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Teredo", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("IP-HTTPS", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Kernel Debug", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
                    continue;

                string mfr = GetDevPropStr(devInfoSet, devInfo, NativeMethods.SPDRP_MFG);
                results.Add((name, mfr));
            }
        }
        finally { NativeMethods.SetupDiDestroyDeviceInfoList(devInfoSet); }

        // Sort wired adapters first; OrderBy is stable, so SetupDi order is preserved within each group
        _nicsCache = results.OrderBy(r => IsWirelessAdapter(r.Item1)).ToArray();
        return _nicsCache;
    }

    /// <summary>True if the adapter name identifies a wireless (Wi-Fi) NIC.</summary>
    private static bool IsWirelessAdapter(string name)
    {
        return name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
            || name.Contains("WiFi", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Wireless", StringComparison.OrdinalIgnoreCase)
            || name.Contains("WLAN", StringComparison.OrdinalIgnoreCase)
            || name.Contains("802.11", StringComparison.OrdinalIgnoreCase);
    }

    // OS
    /// <summary>Gets the human-readable Windows version string (e.g. <c>Windows 11 Pro 23H2 (build 22631)</c>).</summary>
    /// <returns>OS display version or empty if unavailable.</returns>
    public static string GetOsDisplayVersion()
    {
        if (_osVersionCache is not null) return _osVersionCache;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is null) return "";
            string? product = key.GetValue("ProductName")?.ToString();
            string? display = key.GetValue("DisplayVersion")?.ToString();
            string? build = key.GetValue("CurrentBuild")?.ToString();
            if (string.IsNullOrEmpty(product)) return "";
            string osName = product;
            if (int.TryParse(build, out int buildNumber) && buildNumber >= 22000 &&
                product.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
            {
                osName = "Windows 11" + product["Windows 10".Length..];
            }
            _osVersionCache = string.IsNullOrEmpty(display)
                ? $"{osName} (build {build})"
                : $"{osName} {display} (build {build})";
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "Failed to retrieve Windows operating system version"); _osVersionCache = string.Empty; }
        return _osVersionCache;
    }

    // Brand color lookup helpers
    /// <summary>Gets the brand color hex for the CPU vendor.</summary>
    /// <returns>Hex color string (e.g. <c>#0071C5</c>) or empty if unknown.</returns>
    public static string GetCpuColor()
    {
        string vendor = GetCpuVendor();
        return BrandColorDefaults.CpuColors.TryGetValue(vendor, out var c) ? c : "";
    }

    /// <summary>Gets the brand color hex for the primary (preferably dedicated) GPU vendor.</summary>
    /// <returns>Hex color string or empty if unknown.</returns>
    public static string GetGpuColor()
    {
        var gpus = GetGpus();
        var ded = gpus.FirstOrDefault(g => g.IsDedicated);
        if (!string.IsNullOrEmpty(ded.Vendor))
            return BrandColorDefaults.GpuColors.TryGetValue(ded.Vendor, out var c) ? c : "";

        foreach (var g in gpus)
            if (BrandColorDefaults.GpuColors.TryGetValue(g.Vendor, out var c))
                return c;
        return "";
    }

    /// <summary>Gets the name of the primary GPU vendor.</summary>
    public static string GetGpuVendor()
    {
        var gpus = GetGpus();
        var ded = gpus.FirstOrDefault(g => g.IsDedicated);
        if (!string.IsNullOrEmpty(ded.Vendor)) return ded.Vendor;
        return gpus.Length > 0 ? gpus[0].Vendor : UnknownLabel;
    }

    /// <summary>Gets the brand color hex for the primary disk manufacturer.</summary>
    /// <returns>Hex color string or empty if unknown.</returns>
    public static string GetDiskColor()
    {
        var drive = DiskQuery.QueryPhysicalDrive0();
        return BrandColorDefaults.ResolveDiskColor(drive?.Vendor ?? "", drive?.Model ?? "");
    }

    /// <summary>Gets the brand color hex for the RAM manufacturer.</summary>
    /// <returns>Hex color string or empty if unknown.</returns>
    public static string GetRamColor()
    {
        string mfr = GetRamManufacturer();
        var match = BrandColorDefaults.RamColors.FirstOrDefault(kvp => mfr.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase));
        return match.Value ?? "";
    }

    /// <summary>Gets the brand color hex for the first matched NIC manufacturer.</summary>
    /// <returns>Hex color string or empty if unknown.</returns>
    public static string GetNicColor()
    {
        var nics = GetNics();
        foreach (var (_, mfr) in nics)
        {
            var match = BrandColorDefaults.NicColors.FirstOrDefault(kvp => mfr.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match.Value))
                return match.Value;
        }
        return "";
    }

    /// <summary>Gets the brand color hex for the system OEM manufacturer.</summary>
    /// <returns>Hex color string or empty if unknown.</returns>
    public static string GetSystemOemColor()
    {
        string mfr = GetSystemManufacturer();
        var match = BrandColorDefaults.SystemOemColors.FirstOrDefault(kvp => mfr.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase));
        return match.Value ?? "";
    }

    // Private helpers
    private static string ParseVendor(string hwId, string name)
    {
        if (hwId?.Contains("VEN_10DE") == true) return NvidiaVendor;
        if (hwId?.Contains("VEN_1002") == true) return "AMD";
        if (hwId?.Contains("VEN_8086") == true) return IntelVendor;
        if (name.Contains(NvidiaVendor, StringComparison.OrdinalIgnoreCase)) return NvidiaVendor;
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) return "AMD";
        if (name.Contains(IntelVendor, StringComparison.OrdinalIgnoreCase)) return IntelVendor;
        return "";
    }

    private static bool IsDiscreteGpu(string name, string vendor, IntPtr devInfoSet, NativeMethods.SpDevinfoData devInfo)
    {
        if (vendor == IntelVendor && name.Contains("Arc", StringComparison.OrdinalIgnoreCase)) return true;
        if (vendor == "AMD" && (name.Contains("RX", StringComparison.OrdinalIgnoreCase)
                || name.Contains("PRO", StringComparison.OrdinalIgnoreCase)
                || name.Contains("VII", StringComparison.OrdinalIgnoreCase)
                || name.Contains("WX", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string loc = GetDevPropStr(devInfoSet, devInfo, NativeMethods.SPDRP_LOCATION_INFORMATION);
        if (!string.IsNullOrEmpty(loc))
        {
            var m = Regex.Match(loc, @"(?i)PCI\s*bus\s*(\d+)", RegexOptions.None, TimeSpan.FromMilliseconds(250));
            if (m.Success && int.TryParse(m.Groups[1].Value, out int bus))
                return bus > 0;
        }
        return false;
    }

    private static string GetDevPropStr(IntPtr devInfoSet, NativeMethods.SpDevinfoData devInfo, uint prop)
    {
        IntPtr buf = Marshal.AllocHGlobal(2048);
        try
        {
            if (NativeMethods.SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfo, prop,
                    out _, buf, 2048, out _))
            {
                return Marshal.PtrToStringUni(buf) ?? "";
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return "";
    }

    private const uint SMBIOS_SIGNATURE = 0x52534D42; // 'RSMB' (Raw SMBIOS)

    private static byte[] GetSmbiosRaw()
    {
        if (_smbiosCache is not null) return _smbiosCache;
        uint size = NativeMethods.GetSystemFirmwareTable(SMBIOS_SIGNATURE, 0, IntPtr.Zero, 0);
        if (size == 0) return [];
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            uint ret = NativeMethods.GetSystemFirmwareTable(SMBIOS_SIGNATURE, 0, buf, size);
            if (ret <= 8) return [];
            byte[] raw = new byte[ret - 8];
            Marshal.Copy(IntPtr.Add(buf, 8), raw, 0, (int)(ret - 8));
            _smbiosCache = raw;
            return _smbiosCache;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static string FindSmbiosString(byte[] smbios, byte type, byte offset)
    {
        int p = 0;
        while (p < smbios.Length)
        {
            byte t = smbios[p];
            byte len = smbios[p + 1];
            if (len < 4 || p + len > smbios.Length) break;

            if (t == type && offset < len)
            {
                byte idx = smbios[p + offset];
                return GetSmbiosString(smbios, p, len, idx);
            }

            p += len;
            while (p < smbios.Length && !(smbios[p] == 0 && smbios[p + 1] == 0)) p++;
            p += 2;
            if (t == 127) break;
        }
        return "";
    }

    private static string GetSmbiosString(byte[] smbios, int structStart, int dataLen, byte stringIdx)
    {
        if (stringIdx == 0) return "";
        int p = structStart + dataLen;
        int idx = 1;
        while (p < smbios.Length)
        {
            int end = Array.IndexOf<byte>(smbios, 0, p);
            if (end < 0 || end == p) break;
            if (idx == stringIdx)
                return Encoding.ASCII.GetString(smbios, p, end - p);
            p = end + 1;
            idx++;
        }
        return "";
    }
}
