using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using LocalTelemetry.Core.Models;
using Microsoft.Win32;

namespace LocalTelemetry.Core.Hardware;

[SupportedOSPlatform("windows")]
public static class SystemInfo
{
    // Caches (populated on first call, never re-read)
    private static byte[]? _smbiosCache;
    private static string? _cpuNameCache;
    private static string? _osVersionCache;
    private static long? _ramTotalBytesCache;
    private static bool? _hasBatteryCache;
    private static (string Name, string Vendor, bool IsDedicated, long? VramBytes, string? DriverVersion)[]? _gpusCache;
    private static (string Name, string Manufacturer)[]? _nicsCache;
    private static List<Dictionary<string, object?>>? _ramModulesCache;

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
        catch (ManagementException) { Diagnostics.Log.Error($"WMI({query}): WMI query returned no data"); }
        catch (Exception ex) { Diagnostics.Log.Error(ex, $"WMI({query})"); }
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
        catch (ManagementException) { Diagnostics.Log.Error($"WMI({query}): WMI query returned no data"); }
        catch (Exception ex) { Diagnostics.Log.Error(ex, $"WMI({query})"); }
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

        // Step 1: WMI SocketDesignation (works on 99%+ of systems)
        string sd = QueryWmiFirst("SELECT SocketDesignation FROM Win32_Processor", "SocketDesignation");
        if (IsSocketName(sd)) return _cpuSocketCache = sd;

        // Step 2: SMBIOS UpgradeMethod
        int upg = QueryWmiFirstInt("SELECT UpgradeMethod FROM Win32_Processor", "UpgradeMethod");
        if (upg > 0)
        {
            string mapped = SmbiosSocketName(upg);
            if (mapped.Length > 0) return _cpuSocketCache = mapped;
        }

        // Step 3: CPUID (Family, Model) lookup - deterministic, OS-independent
        string cpuidSkt = CpuIdSocketLookup();
        if (cpuidSkt.Length > 0) return _cpuSocketCache = cpuidSkt;

        // Step 4: Derive from CPU brand string (CPUID → Registry → WMI)
        string derived = DeriveSocketFromCpuName();
        if (derived.Length > 0)
            return _cpuSocketCache = derived;

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
                if (family == 0x06)
                    model |= extModel << 4;
                else
                    model |= extModel << 4;
            }

            int key = (family << 16) | (model & 0xFFFF);

            // Intel
            return key switch
            {
                (0x06 << 16) | 0xC6 => "Socket LGA 1851", // Arrow Lake desktop
                (0x06 << 16) | 0xC4 => "Socket BGA 2049", // Arrow Lake H
                (0x06 << 16) | 0xC7 => "Socket BGA 2833", // Lunar Lake
                (0x06 << 16) | 0xAC => "Socket LGA 1851", // Meteor Lake desktop
                (0x06 << 16) | 0xBE => "Socket LGA 1700", // Raptor Lake Refresh desktop
                (0x06 << 16) | 0xBF => "Socket BGA 1964", // Raptor Lake HX
                (0x06 << 16) | 0xBA => "Socket LGA 1700", // Raptor Lake S desktop
                (0x06 << 16) | 0xBB => "Socket BGA 1744", // Raptor Lake P
                (0x06 << 16) | 0xB7 => "Socket LGA 1700", // Alder Lake S desktop
                (0x06 << 16) | 0x9A => "Socket LGA 1700", // Alder Lake desktop
                (0x06 << 16) | 0xAA => "Socket BGA 1744", // Raptor Lake P
                (0x06 << 16) | 0xA5 => "Socket LGA 1200", // Comet Lake desktop
                (0x06 << 16) | 0x8C => "Socket BGA 1440", // Tiger Lake U
                (0x06 << 16) | 0x8D => "Socket LGA 1200", // Tiger Lake desktop
                (0x06 << 16) | 0x9E => "Socket LGA 1151", // Kaby Lake desktop
                (0x06 << 16) | 0x8E => "Socket LGA 1151", // Kaby Lake desktop

                // AMD
                (0x19 << 16) | 0x5E => "Socket AM5", // Granite Ridge (Ryzen 9000)
                (0x19 << 16) | 0x61 => "Socket AM5", // Raphael (Ryzen 7000)
                (0x19 << 16) | 0x64 => "Socket FP8", // Phoenix (Ryzen 7040)
                (0x19 << 16) | 0x74 => "Socket FP8", // Phoenix 2
                (0x19 << 16) | 0x78 => "Socket FL1", // Dragon Range (Ryzen 7045 HX)
                (0x19 << 16) | 0x50 => "Socket FP6", // Cezanne (Ryzen 5000 mobile)
                (0x19 << 16) | 0x21 => "Socket FP7", // Rembrandt (Ryzen 6000)
                (0x19 << 16) | 0x11 => "Socket AM4", // Vermeer (Ryzen 5000 desktop)
                (0x19 << 16) | 0x71 => "Socket AM4", // Matisse (Ryzen 3000 desktop)
                (0x17 << 16) | 0x31 => "Socket AM4", // Zen+ desktop
                _ => ""
            };
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "CpuIdSocketLookup"); return ""; }
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
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetCpuBrandStringCpuid"); return null; }
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

        // Try uppercased fallback in case vendor is embedded differently
        ReadOnlySpan<char> u = name.ToUpperInvariant().AsSpan().Trim();
        if (u.IndexOf("INTEL") >= 0) return DeriveIntelSocket(u);
        if (u.IndexOf("AMD") >= 0) return DeriveAmdSocket(u);

        return "";
    }

    private static string DeriveIntelSocket(ReadOnlySpan<char> s)
    {
        // Core Ultra naming (Meteor/Arrow/Lunar Lake)
        // Find "Ultra N MMMsuffix" pattern
        int ultraIdx = s.IndexOf("ULTRA");
        if (ultraIdx >= 0)
        {
            int i = ultraIdx + 5;
            // Skip whitespace and tier digit (e.g. "7" in "Ultra 7")
            while (i < s.Length && s[i] == ' ') i++;
            if (i < s.Length && char.IsDigit(s[i])) i++;
            while (i < s.Length && s[i] == ' ') i++;
            if (i >= s.Length || !char.IsDigit(s[i])) return "";

            int numStart = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            int numEnd = i;

            int sufStart = i;
            while (i < s.Length && char.IsLetter(s[i])) i++;
            int sufEnd = i;

            if (numEnd <= numStart) return "";
            ReadOnlySpan<char> numStr = s[numStart..numEnd];
            ReadOnlySpan<char> suffix = s[sufStart..sufEnd];

            if (numStr.Length == 0) return "";
            int genDigit = numStr[0] - '0';

            // Lunar Lake (200V series) → BGA 2833
            if (suffix.IndexOf('V') >= 0) return "Socket BGA 2833";

            if (genDigit >= 2) // Arrow Lake (200) or newer
            {
                if (suffix.IndexOf('H') >= 0) return "Socket BGA 2049";
                if (suffix.IndexOf('U') >= 0) return "Socket BGA 2551";
                return "Socket LGA 1851";
            }
            if (genDigit == 1) // Meteor Lake (100)
            {
                if (suffix.IndexOf('H') >= 0) return "Socket BGA 2049";
                if (suffix.IndexOf('U') >= 0) return "Socket BGA 2551";
                return "Socket LGA 1851";
            }
            return "";
        }

        // Traditional naming: i7-14700HX / i9-13900K / i5-1240P
        for (int idx = 0; idx < s.Length - 2; idx++)
        {
            if ((s[idx] == 'I' || s[idx] == 'i') && char.IsDigit(s[idx + 1]))
            {
                // Find separator after I#
                int sep = idx + 2;
                while (sep < s.Length && s[sep] != '-' && s[sep] != ' ') sep++;
                if (sep >= s.Length - 1) continue;

                int numStart = sep + 1;
                int numEnd = numStart;
                while (numEnd < s.Length && char.IsDigit(s[numEnd])) numEnd++;
                if (numEnd <= numStart) continue;

                // Suffix (letters after number)
                int sufStart = numEnd;
                int sufEnd = sufStart;
                while (sufEnd < s.Length && char.IsLetter(s[sufEnd])) sufEnd++;

                ReadOnlySpan<char> num = s[numStart..numEnd];
                ReadOnlySpan<char> suffix = s[sufStart..sufEnd];

                return IntelSocketByGen(num, suffix);
            }
        }

        // Xeon
        if (s.IndexOf("XEON") >= 0) return IntelXeonSocket(s);
        return "";
    }

    private static string IntelSocketByGen(ReadOnlySpan<char> num, ReadOnlySpan<char> suffix)
    {
        // Parse generation from model number
        // e.g. "14700" → gen=14, "13900" → gen=13, "11900" → gen=11, "9900" → gen=9
        int gen;
        if (num.Length >= 5 && num[0] == '1')
            gen = (num[0] - '0') * 10 + (num[1] - '0'); // 10-19
        else
            gen = num[0] - '0'; // 1-9

        // Gen 12-14 (Alder/Raptor Lake)
        if (gen >= 12 && gen <= 14)
        {
            if (suffix.IndexOf("HX") >= 0 || suffix.IndexOf("HK") >= 0) return "Socket BGA 1744";
            if (suffix.IndexOf('H') >= 0) return "Socket BGA 1964";
            if (suffix.IndexOf('U') >= 0 || suffix.IndexOf('P') >= 0) return "Socket BGA 1744";
            return "Socket LGA 1700";
        }

        // Gen 10-11 (Comet/Rocket Lake desktop, Ice/Tiger Lake mobile)
        if (gen == 10 || gen == 11)
        {
            if (suffix.IndexOf('H') >= 0) return "Socket BGA 1440";
            if (suffix.IndexOf('U') >= 0) return "Socket BGA 1526";
            return "Socket LGA 1200";
        }

        // Gen 6-9 (Skylake → Coffee Lake)
        if (gen >= 6 && gen <= 9)
        {
            if (suffix.Length == 0 || suffix.IndexOf('K') >= 0 || suffix.IndexOf("KF") >= 0)
                return "Socket LGA 1151";
            return "Socket BGA"; // generic mobile
        }

        // Gen 1-5
        if (gen >= 1 && gen <= 5)
        {
            if (suffix.Length == 0 || suffix.IndexOf('K') >= 0)
                return "Socket LGA 1150";
            return "Socket BGA";
        }

        return "";
    }

    private static string IntelXeonSocket(ReadOnlySpan<char> s)
    {
        if (s.IndexOf("W9") >= 0 || s.IndexOf("W5") >= 0 || s.IndexOf("W7") >= 0) return "Socket LGA 4677";
        return "Socket LGA 4189";
    }

    private static string DeriveAmdSocket(ReadOnlySpan<char> s)
    {
        // Threadripper
        if (s.IndexOf("THREADRIPPER") >= 0)
        {
            if (s.IndexOf("7000") >= 0 || s.IndexOf("7005") >= 0) return "Socket sTR5";
            return "Socket sTRX4";
        }

        // EPYC
        if (s.IndexOf("EPYC") >= 0)
        {
            if (s.IndexOf("9004") >= 0 || s.IndexOf("9005") >= 0) return "Socket SP5";
            if (s.IndexOf("8004") >= 0) return "Socket SP6";
            return "Socket SP3";
        }

        // Ryzen: find first 4-digit model number after "RYZEN"
        int ryzenIdx = s.IndexOf("RYZEN");
        if (ryzenIdx < 0) return "";

        // Scan for a 4-digit number starting from after "RYZEN"
        for (int i = ryzenIdx + 5; i < s.Length - 3; i++)
        {
            if (char.IsDigit(s[i]) && char.IsDigit(s[i + 1])
                && char.IsDigit(s[i + 2]) && char.IsDigit(s[i + 3])
                && (i == 0 || !char.IsDigit(s[i - 1]))) // not preceded by digit
            {
                ReadOnlySpan<char> numStr = s[i..(i + 4)];
                // Suffix follows the model number
                int sufStart = i + 4;
                int sufEnd = sufStart;
                while (sufEnd < s.Length && char.IsLetter(s[sufEnd])) sufEnd++;
                ReadOnlySpan<char> suffix = s[sufStart..sufEnd];

                return AmdSocketByArch(numStr, suffix);
            }
        }
        return "";
    }

    private static string AmdSocketByArch(ReadOnlySpan<char> num, ReadOnlySpan<char> suffix)
    {
        int arch = num[0] - '0';

        if (arch == 9) return "Socket AM5";

        if (arch == 8)
        {
            if (suffix.IndexOf('U') >= 0 || suffix.IndexOf("HS") >= 0 || suffix.IndexOf('H') >= 0)
                return "Socket FP8";
            return "Socket AM5";
        }

        if (arch == 7)
        {
            if (suffix.IndexOf("HX") >= 0) return "Socket FL1";
            if (suffix.IndexOf("HS") >= 0 || suffix.IndexOf('U') >= 0) return "Socket FP8";
            return "Socket AM5";
        }

        if (arch == 6) return "Socket FP7";

        if (arch == 5)
        {
            if (suffix.IndexOf("HX") >= 0) return "Socket FP7";
            if (suffix.IndexOf('H') >= 0 || suffix.IndexOf("HS") >= 0 || suffix.IndexOf('U') >= 0)
                return "Socket FP6";
            return "Socket AM4";
        }

        if (arch == 4) return "Socket FP6";

        if (arch == 3)
        {
            if (suffix.IndexOf('U') >= 0) return "Socket FP5";
            return "Socket AM4";
        }

        if (arch == 2)
        {
            if (suffix.IndexOf('U') >= 0) return "Socket FP5";
            return "Socket AM4";
        }

        if (arch == 1)
        {
            if (suffix.IndexOf('U') >= 0) return "Socket FP4";
            return "Socket AM4";
        }

        return "";
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

    private static string SmbiosSocketName(int code)
    {
        return code switch
        {
            1 => "Other",
            2 => "Unknown",
            4 => "ZIF Socket",
            7 => "LIF Socket",
            8 => "Slot 1",
            9 => "Slot 2",
            10 => "370-pin Socket",
            11 => "Slot A",
            12 => "Slot M",
            13 => "Socket 423",
            14 => "Socket 478",
            15 => "Socket 754",
            16 => "Socket 940",
            17 => "Socket 939",
            18 => "Socket mPGA604",
            19 => "Socket LGA 771",
            20 => "Socket LGA 775",
            21 => "Socket 989",
            22 => "Socket 1207",
            23 => "Socket LGA 1366",
            24 => "Socket LGA 1156",
            25 => "Socket LGA 1700",
            26 => "Socket LGA 1200",
            27 => "Socket LGA 1155",
            28 => "Socket LGA 1150",
            29 => "Socket LGA 2011",
            30 => "Socket LGA 2011-3",
            31 => "Socket LGA 1356",
            32 => "Socket LGA 1151",
            33 => "Socket LGA 3647",
            34 => "Socket SP3",
            35 => "Socket SP3r2",
            36 => "Socket LGA 2066",
            37 => "Socket BGA 1440",
            38 => "Socket BGA 1526",
            39 => "Socket LGA 4677",
            40 => "Socket LGA 4189",
            44 => "Socket BGA 1744",
            45 => "Socket BGA 1781",
            46 => "Socket BGA 1792",
            47 => "Socket BGA 1964",
            48 => "Socket LGA 1851",
            49 => "Socket BGA 2116",
            50 => "Socket LGA 4710",
            51 => "Socket LGA 7529",
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
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetCpuName"); _cpuNameCache = string.Empty; }
        return _cpuNameCache;
    }

    /// <summary>Gets the CPU vendor name from the <c>PROCESSOR_IDENTIFIER</c> environment variable.</summary>
    /// <returns><c>Intel</c>, <c>AMD</c> or empty if unknown.</returns>
    public static string GetCpuVendor()
    {
        string? id = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (id is null) return "";
        if (id.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return "Intel";
        if (id.Contains("AMD", StringComparison.OrdinalIgnoreCase)) return "AMD";
        return "";
    }

    /// <summary>Gets the number of physical CPU cores via SMBIOS or GLPI fallback.</summary>
    /// <returns>Physical core count or <see cref="Environment.ProcessorCount"/> as fallback.</returns>
    public static int GetCpuCoreCount()
    {
        int smbios = 0;
        try
        {
            byte[] raw = GetSmbiosRaw();
            if (raw.Length > 0)
            {
                int p = 0;
                while (p < raw.Length)
                {
                    byte t = raw[p];
                    byte len = raw[p + 1];
                    if (len < 4) break;
                    if (t == 4 && len >= 0x2C)
                    {
                        int cores2 = raw[p + 0x2A] | (raw[p + 0x2B] << 8);
                        if (cores2 > 0) { smbios = cores2; break; }
                    }
                    if (t == 4 && len >= 0x24)
                    {
                        int cores = raw[p + 0x23];
                        if (cores > 0) { smbios = cores; break; }
                    }
                    p += len;
                    while (p < raw.Length && !(raw[p] == 0 && raw[p + 1] == 0)) p++;
                    p += 2;
                    if (t == 127) break;
                }
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetCpuCoreCount"); }
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
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetPhysicalCoreCountFallback"); return Environment.ProcessorCount; }
    }

    private static int DetectGlpiStructSize(int totalLen)
    {
        foreach (int sz in new[] { 24, 32, 16, 20 })
        {
            if (totalLen >= sz && totalLen % sz == 0)
                return sz;
        }
        return 0;
    }

    /// <summary>Gets the number of CPU threads (logical processors) via SMBIOS.</summary>
    /// <returns>Thread count or <see cref="Environment.ProcessorCount"/> as fallback.</returns>
    public static int GetCpuThreadCount()
    {
        try
        {
            byte[] raw = GetSmbiosRaw();
            if (raw.Length == 0) return Environment.ProcessorCount;
            int p = 0;
            while (p < raw.Length)
            {
                byte t = raw[p];
                byte len = raw[p + 1];
                if (len < 4) break;
                if (t == 4 && len >= 0x2E)
                {
                    int threads2 = raw[p + 0x2C] | (raw[p + 0x2D] << 8);
                    if (threads2 > 0) return threads2;
                }
                if (t == 4 && len >= 0x26)
                {
                    int threads = raw[p + 0x25];
                    if (threads > 0) return threads;
                }
                p += len;
                while (p < raw.Length && !(raw[p] == 0 && raw[p + 1] == 0)) p++;
                p += 2;
                if (t == 127) break;
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetCpuThreadCount"); }
        return Environment.ProcessorCount;
    }

    /// <summary>Gets the maximum (turbo) CPU frequency in MHz via MSR, CPUID or WMI.</summary>
    /// <returns>Max frequency in MHz or 0 if unavailable.</returns>
    public static int GetCpuMaxSpeedMhz()
    {
        try
        {
            var pawnIo = HardwareMonitor.SharedPawnIoDevice;
            if (pawnIo is not null && GetCpuVendor() == "Intel")
            {
                // Try MSR TURBO_RATIO_LIMIT (0x1AD) first - works on all modern Intel
                var result = pawnIo.Execute("ioctl_read_msr", [0x1AD], 1);
                if (result.Length >= 1)
                {
                    uint ratio = (uint)(result[0] & 0xFF);
                    if (ratio > 0 && ratio < 0xFF)
                    {
                        int mhz = (int)ratio * 100;
                        if (mhz > 1000) return mhz;
                    }
                }
                // Fallback: MSR IA32_HWP_CAPABILITIES (0x771)
                var result2 = pawnIo.Execute("ioctl_read_msr", [0x771], 1);
                if (result2.Length >= 1)
                {
                    uint ratio = (uint)(result2[0] & 0xFF);
                    if (ratio > 0 && ratio < 0xFF)
                    {
                        int mhz = (int)ratio * 100;
                        if (mhz > 1000) return mhz;
                    }
                }
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetCpuMaxSpeedMhz(MSR)"); }

        // 2) CPUID leaf 0x16 (Processor Frequency Info)
        try
        {
            if (X86Base.IsSupported)
            {
                var (eax, ebx, ecx, _) = X86Base.CpuId(0x16, 0);
                if (ebx > 0 && ebx < 0xFFFF && ebx > eax) return ebx;
                if (ecx > 0 && ecx < 0xFFFF && ecx > eax) return ecx;
                if (ebx > 0 && ebx < 0xFFFF) return ebx;
                if (eax > 0 && eax < 0xFFFF) return eax;
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetCpuMaxSpeedMhz(CPUID)"); }

        // 5) WMI ExtMaxClockSpeed (better than MaxClockSpeed on some systems)
        int ext = QueryWmiFirstInt("SELECT ExtMaxClockSpeed FROM Win32_Processor", "ExtMaxClockSpeed");
        if (ext > 0) return ext;

        // 6) WMI MaxClockSpeed + CallNtPowerInformation - compare to detect turbo
        int wmiMhz = QueryWmiFirstInt("SELECT MaxClockSpeed FROM Win32_Processor", "MaxClockSpeed");
        int ntMhz = 0;
        try
        {
            int count = Environment.ProcessorCount;
            int size = Marshal.SizeOf<NativeMethods.PROCESSOR_POWER_INFORMATION>() * count;
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
                        var ppi = Marshal.PtrToStructure<NativeMethods.PROCESSOR_POWER_INFORMATION>(
                            buf + i * Marshal.SizeOf<NativeMethods.PROCESSOR_POWER_INFORMATION>());
                        if (ppi.MaxMhz > max) max = ppi.MaxMhz;
                    }
                    if (max > 0 && max < 0xFFFF) ntMhz = (int)max;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetCpuMaxSpeedMhz(CallNt)"); }
        if (ntMhz > 0 && ntMhz != wmiMhz && ntMhz > 1000) return ntMhz;
        if (wmiMhz > 0) return wmiMhz;

        // 7) Registry fallback
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key is not null)
            {
                var mhzVal = key.GetValue("~MHz");
                if (mhzVal is int mhz && mhz > 0 && mhz < 0xFFFF)
                    return mhz;
                var turboVal = key.GetValue("ProcessorMaxTurboBoostMHz");
                if (turboVal is int turbo && turbo > 0 && turbo < 0xFFFF)
                    return turbo;
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetCpuMaxSpeedMhz(reg)"); }

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
            NativeMethods.SP_DEVINFO_DATA devInfo = new();
            devInfo.cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>();
            uint i = 0;

            while (NativeMethods.SetupDiEnumDeviceInfo(devInfoSet, i++, ref devInfo))
            {
                string name = GetDevPropStr(devInfoSet, devInfo, NativeMethods.SPDRP_DEVICEDESC);
                if (string.IsNullOrEmpty(name)) continue;

                string hwId = GetDevPropStr(devInfoSet, devInfo, NativeMethods.SPDRP_HARDWAREID);
                string vendor = ParseVendor(hwId, name);
                if (string.IsNullOrEmpty(vendor)) continue;

                bool dedicated = vendor == "NVIDIA" || IsDiscreteGpu(name, vendor, devInfoSet, devInfo);
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
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetGpuRegistryData"); }
        return (null, null);
    }

    // RAM
    /// <summary>Gets the total physical RAM in bytes via <c>GlobalMemoryStatusEx</c>.</summary>
    /// <returns>Total physical memory in bytes.</returns>
    public static long GetTotalRamBytes()
    {
        if (_ramTotalBytesCache.HasValue) return _ramTotalBytesCache.Value;
        var mem = new NativeMethods.MEMORYSTATUSEX();
        mem.dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>();
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
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetInstalledRamGb"); }
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
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetRamManufacturer"); }

        var smbiosMfrs = GetSmbiosRamManufacturers();
        foreach (var mfr in smbiosMfrs)
        {
            if (!string.IsNullOrEmpty(mfr))
                return mfr;
        }

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
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetRamSpeed"); }
        return "";
    }

    // Motherboard
    private static string GetBiosRegValue(string name)
    {
        try { using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"); return k?.GetValue(name)?.ToString()?.Trim() ?? ""; } catch (Exception ex) { Diagnostics.Log.Error(ex, $"GetBiosRegValue({name})"); return ""; }
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
        return string.IsNullOrEmpty(date) ? ver : $"{ver} ({date})";
    }

    /// <summary>Determines whether the system is booting in UEFI mode.</summary>
    /// <returns><see langword="true"/> if UEFI, <see langword="false"/> if legacy BIOS or unknown.</returns>
    public static bool GetBiosIsUefi()
    {
        try
        {
            return NativeMethods.GetFirmwareType(out var ft)
                && ft == NativeMethods.FIRMWARE_TYPE.FirmwareTypeUefi;
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetBiosIsUefi"); return false; }
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
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetSystemManufacturer"); }
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
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetSystemProductName"); }
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
        catch (Exception ex) { Diagnostics.Log.Error(ex, "QueryWmiRamModules"); }
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
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetSmbiosRamManufacturers"); }
        return mfrs;
    }

    private static string FormFactorLabel(int ff) => ff switch
    {
        0x01 => "Other",
        0x02 => "Unknown",
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
        0x00 => "Unknown",
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
        _ => $"Type{mt:X}",
    };

    /// <summary>Gets the RAM technology type (e.g. <c>DDR5</c>, <c>DDR4</c>) from WMI, falling back to SMBIOS Type 17.</summary>
    /// <returns>Memory type label or <c>Unknown</c> if detection fails.</returns>
    public static string GetRamType()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT MemoryType FROM Win32_PhysicalMemory");
            using var results = searcher.Get();
            foreach (ManagementBaseObject mo in results)
            {
                ushort mt = Convert.ToUInt16(mo["MemoryType"] ?? (ushort)0);
                if (mt != 0) return MemoryTypeLabel(mt);
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetRamType(WMI)"); }
        try
        {
            byte[] raw = GetSmbiosRaw();
            if (raw.Length > 0)
            {
                int p = 0;
                while (p < raw.Length)
                {
                    byte t = raw[p];
                    if (t == 17) // Type 17 - Memory Device
                    {
                        int len = raw[p + 1];
                        if (len > 0x12)
                        {
                            int mt = raw[p + 0x12];
                            if (mt > 0) return MemoryTypeLabel(mt);
                        }
                        p += len;
                        while (p < raw.Length && !(raw[p] == 0 && raw[p + 1] == 0)) p++;
                        p += 2;
                    }
                    else
                    {
                        p += raw[p + 1];
                        while (p < raw.Length && !(raw[p] == 0 && raw[p + 1] == 0)) p++;
                        p += 2;
                    }
                }
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetRamType(SMBIOS)"); }
        return MemoryTypeLabel(0);
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
        catch (Exception ex) { Diagnostics.Log.Error(ex, "HasBattery(GetSystemPowerStatus)"); }

        // WMI check as secondary detection - some laptops have ACPI batteries
        // that GetSystemPowerStatus doesn't enumerate correctly
        if (!hasBattery)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                using var results = searcher.Get();
                foreach (ManagementBaseObject mo in results)
                {
                    _ = mo;
                    hasBattery = true;
                    Diagnostics.Log.Info("HasBattery: detected via Win32_Battery (GetSystemPowerStatus reported no battery)");
                    break;
                }
            }
            catch (Exception ex) { Diagnostics.Log.Error(ex, "HasBattery(WMI)"); }
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
            p += len;
            while (p < raw.Length && !(raw[p] == 0 && raw[p + 1] == 0)) p++;
            p += 2;
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
            Diagnostics.Log.Error($"QueryRootWmiString({className}.{property}): {ex.Message}");
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error(ex, $"QueryRootWmiString({className}.{property})");
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
            foreach (ManagementBaseObject mo in results)
                return TryParseWmiInt(mo[property]);
        }
        catch (ManagementException ex)
        {
            Diagnostics.Log.Error($"QueryRootWmiInt({className}.{property}): {ex.Message}");
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error(ex, $"QueryRootWmiInt({className}.{property})");
        }
        return 0;
    }

    /// <summary>Gets the battery manufacturer name from SMBIOS or WMI.</summary>
    /// <returns>e.g. <c>Sunwoda</c>, <c>LGC</c> or empty if no battery present.</returns>
    public static string GetBatteryManufacturer()
    {
        if (!HasBattery()) return "";
        string smbios = GetSmbiosBatteryString(0x05);
        if (smbios.Length > 0) return smbios;
        return QueryRootWmiString("BatteryStaticData", "ManufactureName");
    }

    /// <summary>Gets the battery device name from WMI.</summary>
    /// <returns>Device name or empty if no battery present.</returns>
    public static string GetBatteryDeviceName()
    {
        if (!HasBattery()) return "";
        string smbios = GetSmbiosBatteryString(0x08);
        if (smbios.Length > 0) return smbios;
        string staticName = QueryRootWmiString("BatteryStaticData", "DeviceName");
        if (staticName.Length > 0) return staticName;
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
        catch (ManagementException ex) { Diagnostics.Log.Error($"GetBatteryDeviceName(WMI): {ex.Message}"); }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetBatteryDeviceName(WMI)"); }
        return "";
    }

    /// <summary>Gets the battery design capacity in mWh.</summary>
    /// <returns>e.g. <c>50000 mWh</c> or empty if unavailable.</returns>
    public static string GetBatteryDesignCapacity()
    {
        if (!HasBattery()) return "";
        int smbiosMwh = GetSmbiosBatteryWord(0x0A);
        if (smbiosMwh > 0) return $"{smbiosMwh} mWh";
        int staticMwh = QueryRootWmiInt("BatteryStaticData", "DesignedCapacity");
        if (staticMwh > 0) return $"{staticMwh} mWh";
        return "";
    }

    /// <summary>Gets the battery full charged capacity in mWh from WMI.</summary>
    /// <returns>e.g. <c>45000 mWh</c> or empty if unavailable.</returns>
    public static string GetBatteryFullChargedCapacity()
    {
        if (!HasBattery()) return "";
        int fccWmi = QueryRootWmiInt("BatteryFullChargedCapacity", "FullChargedCapacity");
        if (fccWmi > 0) return $"{fccWmi} mWh";
        return "";
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
            return string.IsNullOrEmpty(mfr) ? model : string.IsNullOrEmpty(model) ? mfr : $"{mfr} {model}";
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetPsuName"); return ""; }
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
                p += len;
                while (p < raw.Length && !(raw[p] == 0 && raw[p + 1] == 0)) p++;
                p += 2;
                if (t == 127) break;
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetPsuMaxCapacity"); }
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
        foreach (var kvp in BrandColorDefaults.DiskColors)
            if (model.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Key;
        return "";
    }

    // Disk enumeration
    /// <summary>Enumerates all physical disk drives via WMI.</summary>
    /// <returns>List of disk info tuples (Model, Vendor, BusType, SizeBytes, DiskIndex, IsBootDrive).</returns>
    public static List<(string Model, string Vendor, string BusType, long? SizeBytes, int DiskIndex, bool IsBootDrive)> GetAllDisks()
    {
        var results = new List<(string, string, string, long?, int, bool)>();
        try
        {
            var disks = DiskQuery.QueryAllDrives()
                .OrderByDescending(d => d.IsBootDrive)
                .ThenBy(d => d.DiskIndex)
                .ToList();
            foreach (var d in disks)
            {
                string vendor = d.Vendor;
                if (string.IsNullOrEmpty(vendor) || vendor.Contains("Standard", StringComparison.OrdinalIgnoreCase))
                    vendor = string.Empty;
                string model = d.Model;
                results.Add((model, d.Vendor, d.BusType ?? string.Empty, d.SizeBytes, d.DiskIndex, d.IsBootDrive));
            }
        }
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetAllDisks"); }
        return results;
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
            NativeMethods.SP_DEVINFO_DATA devInfo = new();
            devInfo.cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>();
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

        _nicsCache = results.ToArray();
        return _nicsCache;
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
        catch (Exception ex) { Diagnostics.Log.Error(ex, "GetOsDisplayVersion"); _osVersionCache = string.Empty; }
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
        return gpus.Length > 0 ? gpus[0].Vendor : "Unknown";
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
        foreach (var kvp in BrandColorDefaults.RamColors)
            if (mfr.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        return "";
    }

    /// <summary>Gets the brand color hex for the first matched NIC manufacturer.</summary>
    /// <returns>Hex color string or empty if unknown.</returns>
    public static string GetNicColor()
    {
        var nics = GetNics();
        foreach (var (_, mfr) in nics)
            foreach (var kvp in BrandColorDefaults.NicColors)
                if (mfr.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
        return "";
    }

    /// <summary>Gets the brand color hex for the system OEM manufacturer.</summary>
    /// <returns>Hex color string or empty if unknown.</returns>
    public static string GetSystemOemColor()
    {
        string mfr = GetSystemManufacturer();
        foreach (var kvp in BrandColorDefaults.SystemOemColors)
            if (mfr.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        return "";
    }

    // Private helpers
    private static string ParseVendor(string hwId, string name)
    {
        if (hwId?.Contains("VEN_10DE") == true) return "NVIDIA";
        if (hwId?.Contains("VEN_1002") == true) return "AMD";
        if (hwId?.Contains("VEN_8086") == true) return "Intel";
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return "NVIDIA";
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) return "AMD";
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return "Intel";
        return "";
    }

    private static bool IsDiscreteGpu(string name, string vendor, IntPtr devInfoSet, NativeMethods.SP_DEVINFO_DATA devInfo)
    {
        if (vendor == "Intel" && name.Contains("Arc", StringComparison.OrdinalIgnoreCase)) return true;
        if (vendor == "AMD")
        {
            if (name.Contains("RX", StringComparison.OrdinalIgnoreCase)
                || name.Contains("PRO", StringComparison.OrdinalIgnoreCase)
                || name.Contains("VII", StringComparison.OrdinalIgnoreCase)
                || name.Contains("WX", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        string loc = GetDevPropStr(devInfoSet, devInfo, NativeMethods.SPDRP_LOCATION_INFORMATION);
        if (!string.IsNullOrEmpty(loc))
        {
            var m = Regex.Match(loc, @"(?i)PCI\s*bus\s*(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int bus))
                return bus > 0;
        }
        return false;
    }

    private static string GetDevPropStr(IntPtr devInfoSet, NativeMethods.SP_DEVINFO_DATA devInfo, uint prop)
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
            if (ret == 0) return [];
            byte[] raw = new byte[ret];
            Marshal.Copy(buf, raw, 0, (int)ret);
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
