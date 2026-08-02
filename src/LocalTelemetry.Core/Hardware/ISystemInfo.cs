using System.Runtime.Versioning;

namespace LocalTelemetry.Core.Hardware;

[SupportedOSPlatform("windows")]
public interface ISystemInfo
{
    string GetCpuSocket();
    string GetCpuName();
    string GetCpuVendor();
    int GetCpuCoreCount();
    int GetCpuThreadCount();
    int GetCpuMaxSpeedMhz();
    int GetCpuBaseSpeedMhz();
    (string Name, string Vendor, bool IsDedicated, long? VramBytes, string? DriverVersion)[] GetGpus();
    long GetTotalRamBytes();
    double GetInstalledRamGb();
    string GetRamManufacturer();
    int GetRamModuleCount();
    string GetRamSpeed();
    string GetMotherboardManufacturer();
    string GetMotherboardProductName();
    string GetMotherboardVersion();
    string GetMotherboardSerial();
    string GetBiosVersion();
    bool GetBiosIsUefi();
    string GetSystemManufacturer();
    string GetSystemProductName();
    string GetSystemModel();
    List<Dictionary<string, object?>> GetRamModules();
    string GetRamType();
    string GetSystemTypeLabel();
    bool HasBattery();
    string GetBatteryManufacturer();
    string GetBatteryDeviceName();
    string GetBatteryDesignCapacity();
    string GetBatteryFullChargedCapacity();
    string GetPsuName();
    string GetPsuMaxCapacity();
    string GetDiskModel();
    string GetDiskManufacturer();
    List<(string Model, string Vendor, string BusType, long? SizeBytes, int DiskIndex, bool IsBootDrive)> GetAllDisks();
    (string Name, string Manufacturer)[] GetNics();
    string GetOsDisplayVersion();
    string GetCpuColor();
    string GetGpuColor();
    string GetGpuVendor();
    string GetDiskColor();
    string GetRamColor();
    string GetNicColor();
    string GetSystemOemColor();
}
