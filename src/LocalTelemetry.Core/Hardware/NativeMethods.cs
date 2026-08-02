using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace LocalTelemetry.Core.Hardware;

[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    // SetupAPI GUIDs
    internal static readonly Guid GUID_DISPLAY = new("{4d36e968-e325-11ce-bfc1-08002be10318}");
    internal static readonly Guid GUID_NET = new("{4d36e972-e325-11ce-bfc1-08002be10318}");

    // SetupAPI constants
    internal const uint DIGCF_PRESENT = 0x00000002;
    internal const uint SPDRP_DEVICEDESC = 0x00000000;
    internal const uint SPDRP_HARDWAREID = 0x00000001;
    internal const uint SPDRP_MFG = 0x0000000B;
    internal const uint SPDRP_LOCATION_INFORMATION = 0x0000000D;

    // CreateFile constants
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint OPEN_EXISTING = 3;
    internal const uint GENERIC_READ = 0x80000000;
    internal const uint GENERIC_WRITE = 0x40000000;

    // Token privilege constants
    internal const uint TOKEN_QUERY = 0x0008;
    internal const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    internal const uint SE_PRIVILEGE_ENABLED = 0x2;

    // CPU power
    internal const int ProcessorInformation = 11;

    // Structs
    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus; // 1=on line, 0=off line, 255=unknown
        public byte BatteryFlag;  // 128=no battery, 1=high, 2=low, 4=critical, 8=charging
        public byte BatteryLifePercent;
        public byte Reserved1;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESSOR_POWER_INFORMATION
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID
    {
        public int LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TOKEN_PRIVILEGES
    {
        public int PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    // SetupAPI
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    internal static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property,
        out uint propertyRegDataType, IntPtr propertyBuffer, uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    // Kernel32
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetSystemFirmwareTable(
        uint firmwareTableProviderSignature, uint firmwareTableID,
        IntPtr firmwareTableBuffer, uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    internal static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        byte[]? lpInBuffer, uint nInBufferSize,
        [Out] byte[]? lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(
        out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemPowerStatus(
        out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    internal enum FIRMWARE_TYPE
    {
        FirmwareTypeUnknown,
        FirmwareTypeBios,
        FirmwareTypeUefi,
        FirmwareTypeMax
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFirmwareType(out FIRMWARE_TYPE firmwareType);

    // PowrProf
    [DllImport("powrprof.dll")]
    internal static extern int CallNtPowerInformation(
        int informationLevel, IntPtr inputBuffer, int inputBufSize,
        IntPtr outputBuffer, int outputBufSize);

    // IphlpApi
    [DllImport("iphlpapi.dll", CharSet = CharSet.Unicode)]
    internal static extern uint ConvertInterfaceLuidToNameW(
        ref ulong InterfaceLuid,
        [Out] char[] Name,
        uint NameLength);

    // AdvApi32
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(
        IntPtr processHandle, uint desiredAccess, out SafeFileHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LookupPrivilegeValue(
        string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AdjustTokenPrivileges(
        SafeFileHandle tokenHandle, bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState, uint bufferLength,
        IntPtr previousState, IntPtr returnLength);
}
