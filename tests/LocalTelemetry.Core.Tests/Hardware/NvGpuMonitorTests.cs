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
        native.Init();
        native.GetDeviceHandleByIndex(0, out var dev);
        native.GetDeviceName(dev, out _);
        native.GetDriverVersion(out _);
        native.GetTemperature(dev, 0, out _);
        native.GetUtilizationRates(dev, out _, out _);
        native.GetMemoryInfo(dev, out _, out _, out _);
        native.GetClockInfo(dev, 0, out _);
        native.GetPowerUsage(dev, out _);
        native.GetPowerManagementLimit(dev, out _);
        native.Shutdown();
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
        nativeMock.GetDeviceName((IntPtr)0x1000, out Arg.Any<string>()!).Returns(x => { x[1] = "NVIDIA GeForce RTX 4090"; return 0; });
        nativeMock.GetDriverVersion(out Arg.Any<string>()!).Returns(x => { x[0] = "550.54"; return 0; });

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

        using var monitor = new NvGpuMonitor(nativeMock);

        monitor.IsAvailable.Should().BeTrue();
        monitor.GpuName.Should().Be("NVIDIA GeForce RTX 4090");
        NvGpuMonitor.LatestDriverVersion.Should().Be("550.54");

        var data = monitor.Query();
        data.Should().NotBeNull();
        data!.Value.TempC.Should().Be(62.0f);
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
        nativeMock.GetDeviceName((IntPtr)0x1000, out Arg.Any<string>()!).Returns(x => { x[1] = "NVIDIA GeForce RTX 5060 Laptop GPU"; return 0; });

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
        nativeMock.GetDeviceName((IntPtr)0x1000, out Arg.Any<string>()!).Returns(x => { x[1] = "NVIDIA GeForce RTX 5060 Laptop GPU"; return 0; });

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
        nativeMock.GetDeviceName((IntPtr)0x1000, out Arg.Any<string>()!).Returns(x => { x[1] = "NVIDIA GeForce RTX 5060 Laptop GPU"; return 0; });

        // Simulate single 999 (NVML_ERROR_UNKNOWN) on temperature, but other metrics succeed
        nativeMock.GetTemperature((IntPtr)0x1000, 0, out Arg.Any<uint>()).Returns(999);
        nativeMock.GetUtilizationRates((IntPtr)0x1000, out Arg.Any<uint>(), out Arg.Any<uint>()).Returns(x => { x[1] = 20u; return 0; });

        using var monitor = new NvGpuMonitor(nativeMock);
        monitor.IsAvailable.Should().BeTrue();

        var data = monitor.Query();
        data.Should().NotBeNull();
        data!.Value.UsagePct.Should().Be(20f);
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
        nativeMock.GetDeviceName((IntPtr)0x1000, out Arg.Any<string>()!).Returns(x => { x[1] = "NVIDIA GeForce RTX 5060 Laptop GPU"; return 0; });

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
}
