using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using NSubstitute;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public sealed class HardwareInventoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _stateFilePath;

    public HardwareInventoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HwInventoryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _stateFilePath = Path.Combine(_tempDir, "hardware_state.json");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
    }

    private static ISystemInfo CreateMockSysInfo(
        string cpu = "Intel Core i7-10750H",
        string gpu = "NVIDIA GeForce RTX 2060",
        int diskCount = 2)
    {
        var mock = Substitute.For<ISystemInfo>();
        mock.GetSystemModel().Returns("GL65 Leopard 10SEK");
        mock.GetOsDisplayVersion().Returns("Windows 11 Home 25H2 (build 26200)");
        mock.GetSystemTypeLabel().Returns("Laptop");
        mock.GetMotherboardManufacturer().Returns("Micro-Star International Co., Ltd.");
        mock.GetMotherboardProductName().Returns("MS-16U7");
        mock.GetMotherboardVersion().Returns("REV:1.0");
        mock.GetBiosVersion().Returns("E16U7IMS.10C");
        mock.GetBiosIsUefi().Returns(true);

        mock.GetCpuName().Returns(cpu);
        mock.GetCpuCoreCount().Returns(6);
        mock.GetCpuThreadCount().Returns(12);
        mock.GetCpuBaseSpeedMhz().Returns(2592);
        mock.GetCpuMaxSpeedMhz().Returns(5000);
        mock.GetCpuSocket().Returns("BGA1440");

        mock.GetGpus().Returns([(Name: gpu, Vendor: "NVIDIA", IsDedicated: true, VramBytes: 6442450944, DriverVersion: "550.00")]);

        var disks = new List<DiskInfo>();
        if (diskCount >= 1)
        {
            disks.Add(new DiskInfo
            {
                DiskIndex = 0,
                Model = "ST1000LM048-2E7172",
                DisplayType = "SATA HDD",
                BusType = "SATA",
                MediaType = "HDD",
                SizeBytes = 1000204886016, // 1 TB
                IsBootDrive = false
            });
        }
        if (diskCount >= 2)
        {
            disks.Add(new DiskInfo
            {
                DiskIndex = 1,
                Model = "WDC PC SN530 SDBPNPZ-256G-1032",
                DisplayType = "NVMe SSD",
                BusType = "NVMe",
                MediaType = "SSD",
                SizeBytes = 256060514304, // 256 GB
                IsBootDrive = true
            });
        }
        mock.GetAllDisks().Returns(disks);

        mock.GetInstalledRamGb().Returns(16.0);
        mock.GetRamType().Returns("DDR4");
        mock.GetRamSpeed().Returns("2933");
        mock.GetRamModuleCount().Returns(2);

        mock.HasBattery().Returns(true);
        mock.GetBatteryManufacturer().Returns("MSI");
        mock.GetBatteryDeviceName().Returns("BTY-M6J");
        mock.GetBatteryDesignCapacity().Returns("51000 mWh");
        mock.GetBatteryFullChargedCapacity().Returns("48500 mWh");

        return mock;
    }

    [Fact]
    public void LogHardwareConfiguration_FirstRun_CreatesStateFileAndUses1BasedDrives()
    {
        var sysInfo = CreateMockSysInfo();

        var state = HardwareInventory.LogHardwareConfiguration(sysInfo, _stateFilePath);

        state.Should().NotBeNull();
        File.Exists(_stateFilePath).Should().BeTrue();
        state.Fingerprint.Should().NotBeNullOrEmpty();
        state.SystemModel.Should().Contain("GL65 Leopard 10SEK");
        state.Processor.Should().Contain("Intel Core i7-10750H");
        state.Graphics.Should().ContainSingle().Which.Should().Contain("NVIDIA GeForce RTX 2060");
        state.Memory.Should().Contain("16.0 GB");
        state.Memory.Should().Contain("DDR4");
        state.Battery.Should().Contain("MSI BTY-M6J");

        // Verify 1-based drive numbering in storage list
        state.Storage.Should().HaveCount(2);
        state.Storage[0].Should().StartWith("Drive 1 - ");
        state.Storage[1].Should().StartWith("Drive 2 - ");
        state.Storage[1].Should().Contain("Boot Drive");
    }

    [Fact]
    public void LogHardwareConfiguration_SubsequentRun_UnchangedHardware_KeepsSameFingerprint()
    {
        var sysInfo = CreateMockSysInfo();

        // First run
        var state1 = HardwareInventory.LogHardwareConfiguration(sysInfo, _stateFilePath);
        DateTime firstDetected = state1.LastDetectedUtc;

        // Second run with identical hardware
        var state2 = HardwareInventory.LogHardwareConfiguration(sysInfo, _stateFilePath);

        state2.Fingerprint.Should().Be(state1.Fingerprint);
        state2.LastDetectedUtc.Should().BeOnOrAfter(firstDetected);
    }

    [Fact]
    public void LogHardwareConfiguration_HardwareChanged_GeneratesNewFingerprintAndUpdatesState()
    {
        var sysInfoInitial = CreateMockSysInfo(cpu: "Intel Core i7-10750H");
        var state1 = HardwareInventory.LogHardwareConfiguration(sysInfoInitial, _stateFilePath);

        // Simulate hardware upgrade (e.g. replaced CPU)
        var sysInfoUpgraded = CreateMockSysInfo(cpu: "Intel Core i9-10900K");
        var state2 = HardwareInventory.LogHardwareConfiguration(sysInfoUpgraded, _stateFilePath);

        state2.Fingerprint.Should().NotBe(state1.Fingerprint);
        state2.Processor.Should().Contain("Intel Core i9-10900K");
    }

    [Fact]
    public void LogHardwareConfiguration_BatteryFullChargedCapacityChanged_KeepsSameFingerprint()
    {
        var sysInfo1 = CreateMockSysInfo();
        sysInfo1.GetBatteryFullChargedCapacity().Returns("48500 mWh");
        var state1 = HardwareInventory.LogHardwareConfiguration(sysInfo1, _stateFilePath);

        // Simulate battery wear recalibration (+50 mWh shift in full charge capacity)
        var sysInfo2 = CreateMockSysInfo();
        sysInfo2.GetBatteryFullChargedCapacity().Returns("48550 mWh");
        var state2 = HardwareInventory.LogHardwareConfiguration(sysInfo2, _stateFilePath);

        state2.Fingerprint.Should().Be(state1.Fingerprint);
        state2.Battery.Should().Contain("full charge 48550 mWh");
    }

    [Fact]
    public void LogHardwareConfiguration_NullOrEmptyPath_DoesNotThrow()
    {
        var sysInfo = CreateMockSysInfo();

        var act = () => HardwareInventory.LogHardwareConfiguration(sysInfo, stateFilePath: string.Empty);

        act.Should().NotThrow();
    }

    [Fact]
    public void LogHardwareConfiguration_DesktopWithoutBattery_SetsNoneBatteryLine()
    {
        var sysInfo = CreateMockSysInfo();
        sysInfo.HasBattery().Returns(false);
        var state = HardwareInventory.LogHardwareConfiguration(sysInfo, _stateFilePath);
        state.Battery.Should().Be("None (AC Mains / Desktop)");
    }

    [Fact]
    public void LogHardwareConfiguration_CorruptStateFile_RecoversGracefully()
    {
        File.WriteAllText(_stateFilePath, "{ invalid json content");
        var sysInfo = CreateMockSysInfo();
        var act = () => HardwareInventory.LogHardwareConfiguration(sysInfo, _stateFilePath);
        act.Should().NotThrow();
    }

    [Fact]
    public void LogHardwareConfiguration_SaveStateError_CatchesException()
    {
        var sysInfo = CreateMockSysInfo();
        string invalidPath = "Z:\\NonExistent_Invalid_Dir_123\\state.json";
        var act = () => HardwareInventory.LogHardwareConfiguration(sysInfo, invalidPath);
        act.Should().NotThrow();
    }
}
