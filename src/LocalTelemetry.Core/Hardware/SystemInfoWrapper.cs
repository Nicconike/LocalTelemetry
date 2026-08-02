using System.Runtime.Versioning;

namespace LocalTelemetry.Core.Hardware;

[SupportedOSPlatform("windows")]
public sealed class SystemInfoWrapper : ISystemInfo
{
    public string GetCpuSocket() => SystemInfo.GetCpuSocket();
    public string GetCpuName() => SystemInfo.GetCpuName();
    public string GetCpuVendor() => SystemInfo.GetCpuVendor();
    public int GetCpuCoreCount() => SystemInfo.GetCpuCoreCount();
    public int GetCpuThreadCount() => SystemInfo.GetCpuThreadCount();
    public int GetCpuMaxSpeedMhz() => SystemInfo.GetCpuMaxSpeedMhz();
    public int GetCpuBaseSpeedMhz() => SystemInfo.GetCpuBaseSpeedMhz();
    public (string Name, string Vendor, bool IsDedicated, long? VramBytes, string? DriverVersion)[] GetGpus() => SystemInfo.GetGpus();
    public long GetTotalRamBytes() => SystemInfo.GetTotalRamBytes();
    public double GetInstalledRamGb() => SystemInfo.GetInstalledRamGb();
    public string GetRamManufacturer() => SystemInfo.GetRamManufacturer();
    public int GetRamModuleCount() => SystemInfo.GetRamModuleCount();
    public string GetRamSpeed() => SystemInfo.GetRamSpeed();
    public string GetMotherboardManufacturer() => SystemInfo.GetMotherboardManufacturer();
    public string GetMotherboardProductName() => SystemInfo.GetMotherboardProductName();
    public string GetMotherboardVersion() => SystemInfo.GetMotherboardVersion();
    public string GetMotherboardSerial() => SystemInfo.GetMotherboardSerial();
    public string GetBiosVersion() => SystemInfo.GetBiosVersion();
    public bool GetBiosIsUefi() => SystemInfo.GetBiosIsUefi();
    public string GetSystemManufacturer() => SystemInfo.GetSystemManufacturer();
    public string GetSystemProductName() => SystemInfo.GetSystemProductName();
    public string GetSystemModel() => SystemInfo.GetSystemModel();
    public List<Dictionary<string, object?>> GetRamModules() => SystemInfo.GetRamModules();
    public string GetRamType() => SystemInfo.GetRamType();
    public string GetSystemTypeLabel() => SystemInfo.GetSystemTypeLabel();
    public bool HasBattery() => SystemInfo.HasBattery();
    public string GetBatteryManufacturer() => SystemInfo.GetBatteryManufacturer();
    public string GetBatteryDeviceName() => SystemInfo.GetBatteryDeviceName();
    public string GetBatteryDesignCapacity() => SystemInfo.GetBatteryDesignCapacity();
    public string GetBatteryFullChargedCapacity() => SystemInfo.GetBatteryFullChargedCapacity();
    public string GetPsuName() => SystemInfo.GetPsuName();
    public string GetPsuMaxCapacity() => SystemInfo.GetPsuMaxCapacity();
    public string GetDiskModel() => SystemInfo.GetDiskModel();
    public string GetDiskManufacturer() => SystemInfo.GetDiskManufacturer();
    public List<(string Model, string Vendor, string BusType, long? SizeBytes, int DiskIndex, bool IsBootDrive)> GetAllDisks() => SystemInfo.GetAllDisks();
    public (string Name, string Manufacturer)[] GetNics() => SystemInfo.GetNics();
    public string GetOsDisplayVersion() => SystemInfo.GetOsDisplayVersion();
    public string GetCpuColor() => SystemInfo.GetCpuColor();
    public string GetGpuColor() => SystemInfo.GetGpuColor();
    public string GetGpuVendor() => SystemInfo.GetGpuVendor();
    public string GetDiskColor() => SystemInfo.GetDiskColor();
    public string GetRamColor() => SystemInfo.GetRamColor();
    public string GetNicColor() => SystemInfo.GetNicColor();
    public string GetSystemOemColor() => SystemInfo.GetSystemOemColor();
}
