using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using NSubstitute;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class NvGpuMonitorTests
{
    [Fact]
    public void NvGpuNativePInvoke_Instance_ExecutesSafely()
    {
        var native = NvGpuNativePInvoke.Instance;
        try
        {
            // 1. Success hooks
            native.NvmlInitFunc = () => 0;
            native.NvmlShutdownFunc = () => 0;
            native.GetDeviceHandleByIndexFunc = (int idx, out IntPtr dev) => { dev = (IntPtr)0x5000; return 0; };
            native.GetDeviceNameFunc = (IntPtr d, byte[] b, int l) =>
            {
                var bytes = System.Text.Encoding.ASCII.GetBytes("RTX 4090\0");
                Array.Copy(bytes, b, bytes.Length);
                return 0;
            };
            native.GetDriverVersionFunc = (byte[] b, int l) =>
            {
                var bytes = System.Text.Encoding.ASCII.GetBytes("550.54\0");
                Array.Copy(bytes, b, bytes.Length);
                return 0;
            };
            native.GetTemperatureFunc = (IntPtr d, int s, ref uint t) => { t = 65u; return 0; };
            native.GetUtilizationRatesFunc = (IntPtr d, ref NvGpuNativePInvoke.NvmlUtilization u) => { u.gpu = 75u; u.memory = 30u; return 0; };
            native.GetMemoryInfoFunc = (IntPtr d, ref NvGpuNativePInvoke.NvmlMemory m) => { m.total = 1000; m.free = 600; m.used = 400; return 0; };
            native.GetClockInfoFunc = (IntPtr d, int c, ref uint clk) => { clk = 2500u; return 0; };
            native.GetPowerUsageFunc = (IntPtr d, ref uint p) => { p = 300000u; return 0; };
            native.GetPowerManagementLimitFunc = (IntPtr d, ref uint pl) => { pl = 450000u; return 0; };

            native.Init().Should().Be(0);
            native.GetDeviceHandleByIndex(0, out var validDev).Should().Be(0);
            native.GetDeviceName(validDev, out var name).Should().Be(0);
            name.Should().Be("RTX 4090");
            native.GetDriverVersion(out var ver).Should().Be(0);
            ver.Should().Be("550.54");
            native.GetTemperature(validDev, 0, out var temp).Should().Be(0);
            temp.Should().Be(65u);
            native.GetUtilizationRates(validDev, out var gpuU, out var memU).Should().Be(0);
            gpuU.Should().Be(75u);
            memU.Should().Be(30u);
            native.GetMemoryInfo(validDev, out var total, out var free, out var used).Should().Be(0);
            total.Should().Be(1000UL);
            free.Should().Be(600UL);
            used.Should().Be(400UL);
            native.GetClockInfo(validDev, 0, out var clock).Should().Be(0);
            clock.Should().Be(2500u);
            native.GetPowerUsage(validDev, out var pwr).Should().Be(0);
            pwr.Should().Be(300000u);
            native.GetPowerManagementLimit(validDev, out var pwrLim).Should().Be(0);
            pwrLim.Should().Be(450000u);
            native.Shutdown().Should().Be(0);

            // 2. Exception/Failure hooks
            native.NvmlInitFunc = () => throw new InvalidOperationException("init fail");
            native.NvmlShutdownFunc = () => throw new InvalidOperationException("shutdown fail");
            native.GetDeviceHandleByIndexFunc = (int idx, out IntPtr dev) => throw new InvalidOperationException("handle fail");
            native.GetDeviceNameFunc = (IntPtr d, byte[] b, int l) => throw new InvalidOperationException("name fail");
            native.GetDriverVersionFunc = (byte[] b, int l) => throw new InvalidOperationException("ver fail");
            native.GetTemperatureFunc = (IntPtr d, int s, ref uint t) => throw new InvalidOperationException("temp fail");
            native.GetUtilizationRatesFunc = (IntPtr d, ref NvGpuNativePInvoke.NvmlUtilization u) => throw new InvalidOperationException("util fail");
            native.GetMemoryInfoFunc = (IntPtr d, ref NvGpuNativePInvoke.NvmlMemory m) => throw new InvalidOperationException("mem fail");
            native.GetClockInfoFunc = (IntPtr d, int c, ref uint clk) => throw new InvalidOperationException("clock fail");
            native.GetPowerUsageFunc = (IntPtr d, ref uint p) => throw new InvalidOperationException("pwr fail");
            native.GetPowerManagementLimitFunc = (IntPtr d, ref uint pl) => throw new InvalidOperationException("pwrLim fail");

            native.Init().Should().Be(-1);
            native.GetDeviceHandleByIndex(0, out _).Should().Be(-1);
            native.GetDeviceName((IntPtr)0x1, out _).Should().Be(-1);
            native.GetDriverVersion(out _).Should().Be(-1);
            native.GetTemperature((IntPtr)0x1, 0, out _).Should().Be(-1);
            native.GetUtilizationRates((IntPtr)0x1, out _, out _).Should().Be(-1);
            native.GetMemoryInfo((IntPtr)0x1, out _, out _, out _).Should().Be(-1);
            native.GetClockInfo((IntPtr)0x1, 0, out _).Should().Be(-1);
            native.GetPowerUsage((IntPtr)0x1, out _).Should().Be(-1);
            native.GetPowerManagementLimit((IntPtr)0x1, out _).Should().Be(-1);
            native.Shutdown().Should().Be(-1);

            // Zero device handle check
            native.GetDeviceName(IntPtr.Zero, out _).Should().Be(-1);
            native.GetTemperature(IntPtr.Zero, 0, out _).Should().Be(-1);
            native.GetUtilizationRates(IntPtr.Zero, out _, out _).Should().Be(-1);
            native.GetMemoryInfo(IntPtr.Zero, out _, out _, out _).Should().Be(-1);
            native.GetClockInfo(IntPtr.Zero, 0, out _).Should().Be(-1);
            native.GetPowerUsage(IntPtr.Zero, out _).Should().Be(-1);
            native.GetPowerManagementLimit(IntPtr.Zero, out _).Should().Be(-1);
        }
        finally
        {
            native.ResetHooks();
        }
    }

    [Fact]
    public void NvGpuMonitor_FallbackWhenInitFails()
    {
        var nativeMock = Substitute.For<INvGpuNative>();
        nativeMock.Init().Returns(-1);

        using var monitor = new NvGpuMonitor(nativeMock);

        monitor.IsAvailable.Should().BeFalse();
        monitor.GpuName.Should().BeEmpty();
        monitor.Query().Should().BeNull();
    }

    [Fact]
    public void NvGpuMonitor_FallbackWhenGetHandleFails()
    {
        var nativeMock = Substitute.For<INvGpuNative>();
        nativeMock.Init().Returns(0);
        nativeMock.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = IntPtr.Zero; return -1; });

        using var monitor = new NvGpuMonitor(nativeMock);

        monitor.IsAvailable.Should().BeFalse();
        nativeMock.Received(1).Shutdown();
    }

    [Fact]
    public void NvGpuMonitor_WithMockNative_QueriesAllMetrics()
    {
        var nativeMock = Substitute.For<INvGpuNative>();
        nativeMock.Init().Returns(0);

        nativeMock.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x1000; return 0; });
        nativeMock.GetDeviceName((IntPtr)0x1000, out Arg.Any<string>()).Returns(x => { x[1] = "NVIDIA GeForce RTX 4090"; return 0; });
        nativeMock.GetDriverVersion(out Arg.Any<string>()).Returns(x => { x[0] = "550.54"; return 0; });

        nativeMock.GetTemperature((IntPtr)0x1000, 0, out Arg.Any<uint>()).Returns(x => { x[2] = 62u; return 0; });
        nativeMock.GetUtilizationRates((IntPtr)0x1000, out Arg.Any<uint>(), out Arg.Any<uint>()).Returns(x => { x[1] = 85u; x[2] = 40u; return 0; });

        nativeMock.GetMemoryInfo((IntPtr)0x1000, out Arg.Any<ulong>(), out Arg.Any<ulong>(), out Arg.Any<ulong>()).Returns(x =>
        {
            x[1] = (ulong)(24L * 1024 * 1024 * 1024);
            x[2] = (ulong)(14L * 1024 * 1024 * 1024);
            x[3] = (ulong)(10L * 1024 * 1024 * 1024);
            return 0;
        });

        nativeMock.GetClockInfo((IntPtr)0x1000, 0, out Arg.Any<uint>()).Returns(x => { x[2] = 2520u; return 0; });
        nativeMock.GetPowerUsage((IntPtr)0x1000, out Arg.Any<uint>()).Returns(x => { x[1] = 320000u; return 0; });
        nativeMock.GetPowerManagementLimit((IntPtr)0x1000, out Arg.Any<uint>()).Returns(x => { x[1] = 450000u; return 0; });

        var monitor = new NvGpuMonitor(nativeMock);

        monitor.IsAvailable.Should().BeTrue();
        monitor.GpuName.Should().Be("NVIDIA GeForce RTX 4090");
        NvGpuMonitor.LatestDriverVersion.Should().Be("550.54");

        var data = monitor.Query();
        data.Should().NotBeNull();
        data.Value.TempC.Should().Be(62.0f);
        data.Value.UsagePct.Should().Be(85.0f);
        data.Value.VramTotalMb.Should().Be(24576.0f);
        data.Value.VramUsedMb.Should().Be(10240.0f);
        data.Value.CoreClockMHz.Should().Be(2520.0f);
        data.Value.PowerDrawW.Should().Be(320.0f);
        data.Value.TdpW.Should().Be(450.0f);

        monitor.Dispose();
        nativeMock.Received(1).Shutdown();
    }

    [Fact]
    public void NvGpuMonitor_Query_DeviceLost_SetsUnavailable()
    {
        var nativeMock = Substitute.For<INvGpuNative>();
        nativeMock.Init().Returns(0);
        nativeMock.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x1000; return 0; });
        nativeMock.GetDeviceName((IntPtr)0x1000, out Arg.Any<string>()).Returns(x => { x[1] = "NVIDIA GeForce RTX 5060 Laptop GPU"; return 0; });

        // Simulate NVML_ERROR_GPU_IS_LOST (15)
        nativeMock.GetTemperature((IntPtr)0x1000, 0, out Arg.Any<uint>()).Returns(15);

        using var monitor = new NvGpuMonitor(nativeMock);
        monitor.IsAvailable.Should().BeTrue();

        var data = monitor.Query();
        data.Should().BeNull();
        monitor.IsAvailable.Should().BeFalse();
        nativeMock.Received().Shutdown();
    }

    [Fact]
    public void NvGpuMonitor_Reset_Reinitializes()
    {
        var nativeMock = Substitute.For<INvGpuNative>();
        nativeMock.Init().Returns(0);
        nativeMock.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x1000; return 0; });
        nativeMock.GetDeviceName((IntPtr)0x1000, out Arg.Any<string>()).Returns(x => { x[1] = "NVIDIA GeForce RTX 5060 Laptop GPU"; return 0; });

        using var monitor = new NvGpuMonitor(nativeMock);
        monitor.IsAvailable.Should().BeTrue();

        monitor.Reset();

        monitor.IsAvailable.Should().BeTrue();
        nativeMock.Received(2).Init();
    }

    [Fact]
    public void NvGpuMonitor_Query_TransientError999_DoesNotImmediatelyShutdown()
    {
        var nativeMock = Substitute.For<INvGpuNative>();
        nativeMock.Init().Returns(0);
        nativeMock.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x1000; return 0; });
        nativeMock.GetDeviceName((IntPtr)0x1000, out Arg.Any<string>()).Returns(x => { x[1] = "NVIDIA GeForce RTX 5060 Laptop GPU"; return 0; });

        // Simulate single 999 (NVML_ERROR_UNKNOWN) on temperature, but other metrics succeed
        nativeMock.GetTemperature((IntPtr)0x1000, 0, out Arg.Any<uint>()).Returns(999);
        nativeMock.GetUtilizationRates((IntPtr)0x1000, out Arg.Any<uint>(), out Arg.Any<uint>()).Returns(x => { x[1] = 20u; return 0; });

        using var monitor = new NvGpuMonitor(nativeMock);
        monitor.IsAvailable.Should().BeTrue();

        var data = monitor.Query();
        data.Should().NotBeNull();
        data.Value.UsagePct.Should().Be(20f);
        // Did not shut down
        monitor.IsAvailable.Should().BeTrue();
        nativeMock.DidNotReceive().Shutdown();
    }

    [Fact]
    public void NvGpuMonitor_Query_ConsecutiveUnknownFailures_TriggersDeviceLost()
    {
        var nativeMock = Substitute.For<INvGpuNative>();
        nativeMock.Init().Returns(0);
        nativeMock.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x1000; return 0; });
        nativeMock.GetDeviceName((IntPtr)0x1000, out Arg.Any<string>()).Returns(x => { x[1] = "NVIDIA GeForce RTX 5060 Laptop GPU"; return 0; });

        // All calls return 999
        nativeMock.GetTemperature((IntPtr)0x1000, 0, out Arg.Any<uint>()).Returns(999);
        nativeMock.GetUtilizationRates((IntPtr)0x1000, out Arg.Any<uint>(), out Arg.Any<uint>()).Returns(999);
        nativeMock.GetMemoryInfo((IntPtr)0x1000, out Arg.Any<ulong>(), out Arg.Any<ulong>(), out Arg.Any<ulong>()).Returns(999);
        nativeMock.GetClockInfo((IntPtr)0x1000, 0, out Arg.Any<uint>()).Returns(999);
        nativeMock.GetPowerUsage((IntPtr)0x1000, out Arg.Any<uint>()).Returns(999);
        nativeMock.GetPowerManagementLimit((IntPtr)0x1000, out Arg.Any<uint>()).Returns(999);

        using var monitor = new NvGpuMonitor(nativeMock);
        monitor.IsAvailable.Should().BeTrue();

        // 1st tick: returns null, still available
        monitor.Query().Should().BeNull();
        monitor.IsAvailable.Should().BeTrue();

        // 2nd tick: returns null, still available
        monitor.Query().Should().BeNull();
        monitor.IsAvailable.Should().BeTrue();

        // 3rd tick: exceeds threshold, transitions to device lost
        monitor.Query().Should().BeNull();
        monitor.IsAvailable.Should().BeFalse();
        nativeMock.Received(1).Shutdown();
    }

    [Fact]
    public void NvGpuMonitor_DeviceLostBranches_And_Reinit_Coverage()
    {
        // 1. Device lost (15) on each metric
        int[] lostErrors = [1, 15, 16];
        foreach (int err in lostErrors)
        {
            var native = Substitute.For<INvGpuNative>();
            native.Init().Returns(0);
            native.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x1000; return 0; });
            native.GetDeviceName((IntPtr)0x1000, out Arg.Any<string>()!).Returns(x => { x[1] = "RTX 4090"; return 0; });

            // Lost on Temperature
            native.GetTemperature((IntPtr)0x1000, 0, out Arg.Any<uint>()).Returns(err);
            using (var mon = new NvGpuMonitor(native))
            {
                mon.Query().Should().BeNull();
                mon.IsAvailable.Should().BeFalse();
            }

            // Lost on Utilization
            native = Substitute.For<INvGpuNative>();
            native.Init().Returns(0);
            native.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x1000; return 0; });
            native.GetUtilizationRates((IntPtr)0x1000, out Arg.Any<uint>(), out Arg.Any<uint>()).Returns(err);
            using (var mon = new NvGpuMonitor(native))
            {
                mon.Query().Should().BeNull();
                mon.IsAvailable.Should().BeFalse();
            }

            // Lost on Memory
            native = Substitute.For<INvGpuNative>();
            native.Init().Returns(0);
            native.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x1000; return 0; });
            native.GetMemoryInfo((IntPtr)0x1000, out Arg.Any<ulong>(), out Arg.Any<ulong>(), out Arg.Any<ulong>()).Returns(err);
            using (var mon = new NvGpuMonitor(native))
            {
                mon.Query().Should().BeNull();
                mon.IsAvailable.Should().BeFalse();
            }

            // Lost on Clock
            native = Substitute.For<INvGpuNative>();
            native.Init().Returns(0);
            native.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x1000; return 0; });
            native.GetClockInfo((IntPtr)0x1000, 0, out Arg.Any<uint>()).Returns(err);
            using (var mon = new NvGpuMonitor(native))
            {
                mon.Query().Should().BeNull();
                mon.IsAvailable.Should().BeFalse();
            }

            // Lost on Power Usage
            native = Substitute.For<INvGpuNative>();
            native.Init().Returns(0);
            native.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x1000; return 0; });
            native.GetPowerUsage((IntPtr)0x1000, out Arg.Any<uint>()).Returns(err);
            using (var mon = new NvGpuMonitor(native))
            {
                mon.Query().Should().BeNull();
                mon.IsAvailable.Should().BeFalse();
            }

            // Lost on Power Limit
            native = Substitute.For<INvGpuNative>();
            native.Init().Returns(0);
            native.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x1000; return 0; });
            native.GetPowerManagementLimit((IntPtr)0x1000, out Arg.Any<uint>()).Returns(err);
            using (var mon = new NvGpuMonitor(native))
            {
                mon.Query().Should().BeNull();
                mon.IsAvailable.Should().BeFalse();
            }
        }

        // 2. Re-initialization after cooldown
        var reinitNative = Substitute.For<INvGpuNative>();
        reinitNative.Init().Returns(0);
        reinitNative.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x1000; return 0; });

        using (var mon = new NvGpuMonitor(reinitNative))
        {
            // Set uninitialized and expire failure cooldown
            typeof(NvGpuMonitor).GetField("_initialized", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(mon, false);
            typeof(NvGpuMonitor).GetField("_lastFailureTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(mon, DateTime.UtcNow.AddMinutes(-1));

            var res = mon.Query();
            res.Should().NotBeNull();
            mon.IsAvailable.Should().BeTrue();
        }
    }

    [Fact]
    public void NvGpuMonitor_Constructor_And_Reset_WhenNativeThrows_HandlesGracefully()
    {
        // 1. Constructor throws
        var throwingNative = Substitute.For<INvGpuNative>();
        throwingNative.Init().Returns(_ => throw new InvalidOperationException("DLL not found"));

        using var monitor = new NvGpuMonitor(throwingNative);
        monitor.IsAvailable.Should().BeFalse();

        // 2. Reset when Shutdown throws
        var mock = Substitute.For<INvGpuNative>();
        mock.Init().Returns(0);
        mock.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x1000; return 0; });
        mock.Shutdown().Returns(_ => throw new InvalidOperationException("NVML error"));

        using var mon2 = new NvGpuMonitor(mock);
        mon2.IsAvailable.Should().BeTrue();
        var resetAct = () => mon2.Reset();
        resetAct.Should().NotThrow();

        // 3. Reset when Init throws
        mock.Init().Returns(_ => throw new InvalidOperationException("Init failed"));
        resetAct.Should().NotThrow();
        mon2.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void NvGpuMonitor_Query_SensorErrors_And_ZeroMemory_Coverage()
    {
        var mock = Substitute.For<INvGpuNative>();
        mock.Init().Returns(0);
        mock.GetDeviceHandleByIndex(0, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x1000; return 0; });

        // Non-device-lost errors (999) for temperature and utilization
        mock.GetTemperature((IntPtr)0x1000, 0, out Arg.Any<uint>()).Returns(999);
        mock.GetUtilizationRates((IntPtr)0x1000, out Arg.Any<uint>(), out Arg.Any<uint>()).Returns(999);

        // Memory info with total = 0
        mock.GetMemoryInfo((IntPtr)0x1000, out Arg.Any<ulong>(), out Arg.Any<ulong>(), out Arg.Any<ulong>()).Returns(x =>
        {
            x[1] = 0UL;
            x[2] = 0UL;
            x[3] = 0UL;
            return 0;
        });

        // Clock, power usage, power limit errors
        mock.GetClockInfo((IntPtr)0x1000, 0, out Arg.Any<uint>()).Returns(999);
        mock.GetPowerUsage((IntPtr)0x1000, out Arg.Any<uint>()).Returns(999);
        mock.GetPowerManagementLimit((IntPtr)0x1000, out Arg.Any<uint>()).Returns(999);

        using var monitor = new NvGpuMonitor(mock);
        monitor.IsAvailable.Should().BeTrue();

        // Query when no metrics can be read returns null
        monitor.Query().Should().BeNull();

        // Query when native throws an unexpected exception
        mock.GetTemperature((IntPtr)0x1000, 0, out Arg.Any<uint>()).Returns(_ => throw new InvalidOperationException("Access denied"));
        monitor.Query().Should().BeNull();
    }

    [Fact]
    public void NvGpuMonitor_EnsureReady_WhenWithinCooldown_ReturnsFalseImmediately()
    {
        var mock = Substitute.For<INvGpuNative>();
        mock.Init().Returns(-1);

        using var monitor = new NvGpuMonitor(mock);
        monitor.IsAvailable.Should().BeFalse();

        // EnsureReady within 5s cooldown returns false without attempting Init
        monitor.Query().Should().BeNull();
        mock.Received(1).Init(); // Only the constructor call
    }
}
