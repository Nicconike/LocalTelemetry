using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using NSubstitute;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class AmdGpuMonitorTests
{
    [Fact]
    public void AmdAdlNativePInvoke_Instance_ExecutesSafely()
    {
        var native = AmdAdlNativePInvoke.Instance;
        var act = () =>
        {
            native.Create();
            native.GetAdapterInfo(out _, out _);
            native.GetTemperature(0, out _);
            native.GetActivity(0, out _, out _);
            native.GetMemoryInfo(0, out _, out _);
            native.GetPowerLimit(0, out _, out _);
            native.Destroy();
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void AmdGpuMonitor_FallbackWhenNativeCreateFails()
    {
        var nativeMock = Substitute.For<IAmdAdlNative>();
        nativeMock.Create().Returns(-1);

        using var monitor = new AmdGpuMonitor(nativeMock);

        monitor.IsAvailable.Should().BeFalse();
        monitor.GpuName.Should().BeEmpty();
        monitor.GetTempC().Should().Be(0);
        monitor.GetUsagePct().Should().Be(0);
        monitor.GetClockMHz().Should().Be(0);
        monitor.GetVram().Should().Be((0, 0));
        monitor.GetPowerLimitW().Should().Be(0);
        monitor.GetPowerDrawW().Should().Be(0);
    }

    [Fact]
    public void AmdGpuMonitor_FallbackWhenAdapterInfoFails()
    {
        var nativeMock = Substitute.For<IAmdAdlNative>();
        nativeMock.Create().Returns(0);

        nativeMock.GetAdapterInfo(out Arg.Any<int>(), out Arg.Any<string>()!).Returns(x =>
        {
            x[0] = -1;
            x[1] = string.Empty;
            return -1;
        });

        using var monitor = new AmdGpuMonitor(nativeMock);

        monitor.IsAvailable.Should().BeFalse();
        nativeMock.Received(1).Destroy();
    }

    [Fact]
    public void AmdGpuMonitor_WithMockNative_ReturnsAllTelemetryMetrics()
    {
        var nativeMock = Substitute.For<IAmdAdlNative>();
        nativeMock.Create().Returns(0);

        nativeMock.GetAdapterInfo(out Arg.Any<int>(), out Arg.Any<string>()!).Returns(x =>
        {
            x[0] = 0;
            x[1] = "AMD Radeon RX 7900 XTX";
            return 0;
        });

        nativeMock.GetTemperature(0, out Arg.Any<int>()).Returns(x =>
        {
            x[1] = 65000;
            return 0;
        });

        nativeMock.GetActivity(0, out Arg.Any<int>(), out Arg.Any<int>()).Returns(x =>
        {
            x[1] = 92;
            x[2] = 250000;
            return 0;
        });

        nativeMock.GetMemoryInfo(0, out Arg.Any<long>(), out Arg.Any<long>()).Returns(x =>
        {
            x[1] = 24L * 1024 * 1024 * 1024;
            x[2] = 8L * 1024 * 1024 * 1024;
            return 0;
        });

        nativeMock.GetPowerLimit(0, out Arg.Any<int>(), out Arg.Any<int>()).Returns(x =>
        {
            x[1] = 355000;
            x[2] = 300000;
            return 0;
        });

        using var monitor = new AmdGpuMonitor(nativeMock);

        monitor.IsAvailable.Should().BeTrue();
        monitor.GpuName.Should().Be("AMD Radeon RX 7900 XTX");
        monitor.GetTempC().Should().Be(65.0f);
        monitor.GetUsagePct().Should().Be(92.0f);
        monitor.GetClockMHz().Should().Be(2500.0f);

        var (usedMb, totalMb) = monitor.GetVram();
        totalMb.Should().Be(24576.0f);
        usedMb.Should().Be(16384.0f);

        monitor.GetPowerLimitW().Should().Be(355.0f);
        monitor.GetPowerDrawW().Should().Be(300.0f);
    }

    [Fact]
    public void AmdAdlNativePInvoke_Structs_CanBeInstantiated()
    {
        var types = typeof(AmdGpuMonitor).Assembly.GetTypes()
            .Where(t => t.FullName!.Contains("Adl") && t.IsValueType);

        foreach (var t in types)
        {
            var inst = Activator.CreateInstance(t);
            inst.Should().NotBeNull();
        }

        var alloc = typeof(AmdAdlNativePInvoke).GetMethod("ADL_Main_Memory_Alloc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (alloc != null)
        {
            var ptr = (IntPtr)alloc.Invoke(null, [64])!;
            ptr.Should().NotBe(IntPtr.Zero);
            System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public void AmdGpuMonitor_Reset_Reinitializes()
    {
        var nativeMock = Substitute.For<IAmdAdlNative>();
        nativeMock.Create().Returns(0);
        nativeMock.GetAdapterInfo(out Arg.Any<int>(), out Arg.Any<string>()!).Returns(x =>
        {
            x[0] = 0;
            x[1] = "AMD Radeon RX 7900 XTX";
            return 0;
        });

        using var monitor = new AmdGpuMonitor(nativeMock);
        monitor.IsAvailable.Should().BeTrue();

        monitor.Reset();

        monitor.IsAvailable.Should().BeTrue();
        nativeMock.Received(2).Create();
    }
}
