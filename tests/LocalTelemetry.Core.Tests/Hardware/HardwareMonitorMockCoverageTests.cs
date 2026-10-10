using System.Reflection;
using FluentAssertions;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Hardware;
using LocalTelemetry.Core.Hardware.PawnIo;
using LocalTelemetry.Core.Models;
using Microsoft.Win32.SafeHandles;
using NSubstitute;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

[Collection("TrafficHistory")]
public class HardwareMonitorMockCoverageTests
{
    [Fact]
    public void HardwareMonitor_MockedPawnIo_RaplAndMperf_Coverage()
    {
        var cfg = new AppSettings();
        cfg.Monitoring.EnableCpu = true;
        cfg.Monitoring.EnableGpu = true;
        cfg.Monitoring.EnableNet = true;
        cfg.Monitoring.EnableDisk = true;
        cfg.Monitoring.EnableBattery = true;

        var sysInfoMock = Substitute.For<ISystemInfo>();
        sysInfoMock.GetCpuName().Returns("Intel Core i9-13900K");
        sysInfoMock.GetCpuVendor().Returns("Intel");
        sysInfoMock.GetCpuCoreCount().Returns(24);
        sysInfoMock.GetCpuThreadCount().Returns(32);
        sysInfoMock.GetCpuMaxSpeedMhz().Returns(5800);
        sysInfoMock.HasBattery().Returns(true);
        sysInfoMock.GetGpus().Returns([(Name: "NVIDIA GeForce RTX 4090", Vendor: "NVIDIA", IsDedicated: true, VramBytes: 25769803776L, DriverVersion: "550.00")]);
        sysInfoMock.GetNics().Returns([(Name: "Ethernet", Manufacturer: "Intel")]);

        var transportMock = Substitute.For<IPawnIoTransport>();
        // RAPL power unit (0x606): energy unit divisor
        transportMock.Execute("ioctl_read_msr", Arg.Is<ulong[]>(x => x.Length > 0 && x[0] == 0x606), 1)
            .Returns([0x000A1003UL]);
        // RAPL SKU (0x614): TDP
        transportMock.Execute("ioctl_read_msr", Arg.Is<ulong[]>(x => x.Length > 0 && x[0] == 0x614), 1)
            .Returns([0x000003E8UL]);
        // RAPL energy status (0x611)
        transportMock.Execute("ioctl_read_msr", Arg.Is<ulong[]>(x => x.Length > 0 && x[0] == 0x611), 1)
            .Returns([1_000_000UL], [1_200_000UL]);
        // APERF (0xE8)
        transportMock.Execute("ioctl_read_msr", Arg.Is<ulong[]>(x => x.Length > 0 && x[0] == 0xE8), 1)
            .Returns([1_000_000UL], [2_000_000UL]);
        // MPERF (0xE7)
        transportMock.Execute("ioctl_read_msr", Arg.Is<ulong[]>(x => x.Length > 0 && x[0] == 0xE7), 1)
            .Returns([1_000_000UL], [2_000_000UL]);

        var pawnIoMock = Substitute.For<IPawnIoManager>();
        pawnIoMock.TryCreate().Returns(transportMock);

        var lifecycleMock = Substitute.For<IHardwareLifecycleManager>();

        using var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock, lifecycleMock);

        // Inject transport directly into private fields
        var fieldTransport = typeof(HardwareMonitor).GetField("_pawnIoDevice", BindingFlags.NonPublic | BindingFlags.Instance);
        var fieldMaxMhz = typeof(HardwareMonitor).GetField("_maxCpuMhz", BindingFlags.NonPublic | BindingFlags.Instance);
        var fieldCpuVendor = typeof(HardwareMonitor).GetField("_cpuVendor", BindingFlags.NonPublic | BindingFlags.Instance);

        fieldTransport!.SetValue(monitor, transportMock);
        fieldMaxMhz!.SetValue(monitor, 5800u);
        fieldCpuVendor!.SetValue(monitor, "Intel");

        // 1. ReadCpuFreqMsr (call twice: first primes, second calculates delta)
        var mFreqMsr = typeof(HardwareMonitor).GetMethod("ReadCpuFreqMsr", BindingFlags.NonPublic | BindingFlags.Instance);
        mFreqMsr.Should().NotBeNull();
        float freqPrimed = (float)mFreqMsr!.Invoke(monitor, null)!;
        freqPrimed.Should().Be(0f);
        float freqDelta = (float)mFreqMsr.Invoke(monitor, null)!;
        freqDelta.Should().BeGreaterThanOrEqualTo(0f);

        // 2. ReadCpuTdp
        var mTdp = typeof(HardwareMonitor).GetMethod("ReadCpuTdp", BindingFlags.NonPublic | BindingFlags.Instance);
        mTdp.Should().NotBeNull();
        var tdp = (uint?)mTdp!.Invoke(monitor, null);
        tdp.Should().NotBeNull();

        // 3. ReadCpuPowerRapl (call twice: first primes, second calculates delta)
        var mPowerRapl = typeof(HardwareMonitor).GetMethod("ReadCpuPowerRapl", BindingFlags.NonPublic | BindingFlags.Instance);
        mPowerRapl.Should().NotBeNull();
        float powerPrimed = (float)mPowerRapl!.Invoke(monitor, null)!;
        powerPrimed.Should().Be(0f);
        float powerDelta = (float)mPowerRapl.Invoke(monitor, null)!;
        powerDelta.Should().BeGreaterThanOrEqualTo(0f);
    }

    [Fact]
    public void HardwareMonitor_MockedCpuTemps_AllBranches_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        var fieldVendor = typeof(HardwareMonitor).GetField("_cpuVendor", BindingFlags.NonPublic | BindingFlags.Instance);
        var fieldIntelMsr = typeof(HardwareMonitor).GetField("_intelMsr", BindingFlags.NonPublic | BindingFlags.Instance);
        var fieldAmdMsr = typeof(HardwareMonitor).GetField("_amdMsr", BindingFlags.NonPublic | BindingFlags.Instance);

        var mReadTemp = typeof(HardwareMonitor).GetMethod("ReadCpuTemp", BindingFlags.NonPublic | BindingFlags.Instance);
        var mIntelTemp = typeof(HardwareMonitor).GetMethod("ReadIntelCpuTemp", BindingFlags.NonPublic | BindingFlags.Instance);
        var mAmdTemp = typeof(HardwareMonitor).GetMethod("ReadAmdCpuTemp", BindingFlags.NonPublic | BindingFlags.Instance);

        mReadTemp.Should().NotBeNull();
        mIntelTemp.Should().NotBeNull();
        mAmdTemp.Should().NotBeNull();

        // 1. Unknown vendor
        fieldVendor!.SetValue(monitor, "UnknownVendor");
        ((float)mReadTemp!.Invoke(monitor, null)!).Should().Be(0f);

        // 2. Intel vendor with null reader
        fieldVendor.SetValue(monitor, "Intel");
        fieldIntelMsr!.SetValue(monitor, null);
        ((float)mIntelTemp!.Invoke(monitor, null)!).Should().Be(0f);

        // 3. AMD vendor with null reader
        fieldVendor.SetValue(monitor, "AMD");
        fieldAmdMsr!.SetValue(monitor, null);
        ((float)mAmdTemp!.Invoke(monitor, null)!).Should().Be(0f);
    }

    [Fact]
    public void HardwareMonitor_GpuUsageSources_And_Reset_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        var mReadGpuPct = typeof(HardwareMonitor).GetMethod("ReadGpuPct", BindingFlags.NonPublic | BindingFlags.Instance);
        mReadGpuPct.Should().NotBeNull();

        // 1. WDDM source configured
        cfg.Monitoring.GpuUsageSource = "wddm";
        float pctWddm = (float)mReadGpuPct!.Invoke(monitor, [null])!;
        pctWddm.Should().BeInRange(0f, 100f);

        // 2. Driver source configured (NVML / fallback)
        cfg.Monitoring.GpuUsageSource = "driver";
        float pctDriver = (float)mReadGpuPct.Invoke(monitor, [null])!;
        pctDriver.Should().BeInRange(0f, 100f);

        // 3. ResetGpuMonitors with mock state
        var mResetGpu = typeof(HardwareMonitor).GetMethod("ResetGpuMonitors", BindingFlags.NonPublic | BindingFlags.Instance);
        mResetGpu.Should().NotBeNull();
        mResetGpu!.Invoke(monitor, ["Unit test driver recovery simulation"]);
    }

    [Fact]
    public void HardwareMonitor_NetworkSelection_And_Logging_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        // 1. SelectActiveNic
        var mSelectNic = typeof(HardwareMonitor).GetMethod("SelectActiveNic", BindingFlags.NonPublic | BindingFlags.Static);
        mSelectNic.Should().NotBeNull();
        mSelectNic!.Invoke(null, ["auto"]);
        mSelectNic.Invoke(null, ["NonExistentNicNameXYZ"]);

        // 2. EnsureNicCounters with specific preferred NIC
        var mEnsureNic = typeof(HardwareMonitor).GetMethod("EnsureNicCounters", BindingFlags.NonPublic | BindingFlags.Instance);
        mEnsureNic.Should().NotBeNull();

        cfg.Monitoring.PreferredNic = "NonExistentNicNameXYZ";
        mEnsureNic!.Invoke(monitor, null);
        // Second call hits cache / same preferred NIC branch
        mEnsureNic.Invoke(monitor, null);

        // 3. LogTickMetrics branch exercises
        var mLogTick = typeof(HardwareMonitor).GetMethod("LogTickMetrics", BindingFlags.NonPublic | BindingFlags.Instance);
        mLogTick.Should().NotBeNull();

        var mc = cfg.Monitoring;
        mc.EnableCpu = true;
        mc.EnableRam = true;
        mc.EnableDisk = true;
        mc.EnableGpu = true;
        mc.EnableBattery = true;

        // Charging battery
        mLogTick!.Invoke(monitor, [
            mc,
            (50f, 65f, 4.5f, 85f),
            (60f, 16f, 32f),
            (100.0, 50.0, 2),
            (75f, 70f, 250f, 2100f),
            (85f, true, true, 15f)
        ]);

        // Discharging battery
        mLogTick.Invoke(monitor, [
            mc,
            (40f, 55f, 3.8f, 45f),
            (50f, 8f, 16f),
            (10.0, 5.0, 1),
            (30f, 50f, 100f, 1500f),
            (50f, false, false, 25f)
        ]);

        // Battery percentage 0 (skipped)
        mLogTick.Invoke(monitor, [
            mc,
            (20f, 45f, 2.5f, 25f),
            (40f, 4f, 16f),
            (0.0, 0.0, 1),
            (10f, 40f, 50f, 800f),
            (0f, false, false, 0f)
        ]);
    }

    [Fact]
    public void HardwareMonitor_LifecycleEvents_And_Disposal_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        var pawnIoMock = Substitute.For<IPawnIoManager>();
        var lifecycleMock = Substitute.For<IHardwareLifecycleManager>();

        var monitor = new HardwareMonitor(cfg, sysInfoMock, pawnIoMock, lifecycleMock);

        // Raise each lifecycle event on the mock
        lifecycleMock.DisplayOrDriverChanged += Raise.Event<Action>();
        lifecycleMock.PowerSuspended += Raise.Event<Action>();
        lifecycleMock.PowerResumed += Raise.Event<Action>();
        lifecycleMock.NetworkTopologyChanged += Raise.Event<Action>();
        lifecycleMock.DevicePnPChanged += Raise.Event<Action>();

        // Double dispose to cover idempotency branch
        monitor.Dispose();
        monitor.Dispose();
    }

    [Fact]
    public void PawnIoDevice_LoadModule_Execute_Disposal_Coverage()
    {
        // SafeFileHandle with invalid handle
        var invalidHandle = new SafeFileHandle(new IntPtr(-1), ownsHandle: false);
        var device = new PawnIoDevice(invalidHandle);

        // 1. LoadModule edge cases
        device.LoadModule(null!).Should().BeFalse();
        device.LoadModule([]).Should().BeFalse();
        device.LoadModule(new byte[16]).Should().BeFalse();

        // 2. Execute with invalid handle
        device.Execute(null!, null, 0).Should().BeEmpty();
        device.Execute("test_fn", [10, 20], 1).Should().BeEmpty();

        // 3. Dispose
        device.Dispose();
        device.Dispose(); // idempotent

        // Post-disposal guards
        device.LoadModule(new byte[16]).Should().BeFalse();
        device.Execute("test_fn", null, 1).Should().BeEmpty();
    }

    [Fact]
    public void HardwareMonitor_GpuMetricReaders_And_BatteryCapacityDelta_Coverage()
    {
        var cfg = new AppSettings();
        var sysInfoMock = Substitute.For<ISystemInfo>();
        using var monitor = new HardwareMonitor(cfg, sysInfoMock, null);

        // 1. GPU metric readers with NvGpuData passed in directly
        var gpuData = new NvGpuMonitor.NvGpuData
        {
            UsagePct = 55f,
            TempC = 68f,
            VramUsedMb = 6144f,
            VramTotalMb = 16384f,
            CoreClockMHz = 2450f,
            PowerDrawW = 180f,
            TdpW = 300f
        };

        var mReadGpuPct = typeof(HardwareMonitor).GetMethod("ReadGpuPct", BindingFlags.NonPublic | BindingFlags.Instance);
        var mReadTemp = typeof(HardwareMonitor).GetMethod("ReadGpuTemp", BindingFlags.NonPublic | BindingFlags.Instance);
        var mReadVram = typeof(HardwareMonitor).GetMethod("ReadGpuVramUsedMb", BindingFlags.NonPublic | BindingFlags.Instance);
        var mReadFreq = typeof(HardwareMonitor).GetMethod("ReadGpuFreqMHz", BindingFlags.NonPublic | BindingFlags.Instance);
        var mReadPower = typeof(HardwareMonitor).GetMethod("ReadGpuPowerW", BindingFlags.NonPublic | BindingFlags.Instance);

        ((float)mReadGpuPct!.Invoke(monitor, [gpuData])!).Should().Be(55f);
        ((float)mReadTemp!.Invoke(monitor, [gpuData])!).Should().Be(68f);
        ((float)mReadVram!.Invoke(monitor, [gpuData])!).Should().Be(6144f);
        ((float)mReadFreq!.Invoke(monitor, [gpuData])!).Should().Be(2450f);
        ((float)mReadPower!.Invoke(monitor, [gpuData])!).Should().Be(180f);

        // 2. Battery rate from capacity delta
        var mDelta = typeof(HardwareMonitor).GetMethod("ComputeBatteryRateFromCapacityDelta", BindingFlags.NonPublic | BindingFlags.Instance);
        mDelta.Should().NotBeNull();

        // Null / 0 capacity
        ((float)mDelta!.Invoke(monitor, [null])!).Should().Be(0f);
        ((float)mDelta.Invoke(monitor, [0L])!).Should().Be(0f);

        // First call primes capacity
        ((float)mDelta.Invoke(monitor, [50000L])!).Should().Be(0f);

        // Second call calculates delta
        float rate = (float)mDelta.Invoke(monitor, [48000L])!;
        rate.Should().NotBe(0f);

        // 3. Network delta calculation
        var mNetDelta = typeof(HardwareMonitor).GetMethod("CalculateNetBytesDelta", BindingFlags.NonPublic | BindingFlags.Instance);
        mNetDelta.Should().NotBeNull();

        // Reset primed state
        typeof(HardwareMonitor).GetField("_nicPrimed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(monitor, false);
        var deltaPrimed = ((double down, double up))mNetDelta!.Invoke(monitor, [1000L, 2000L])!;
        deltaPrimed.down.Should().Be(0);

        // Second call calculates throughput
        var deltaCalc = ((double down, double up))mNetDelta.Invoke(monitor, [5000L, 8000L])!;
        deltaCalc.down.Should().BeGreaterThanOrEqualTo(0);
    }
}
