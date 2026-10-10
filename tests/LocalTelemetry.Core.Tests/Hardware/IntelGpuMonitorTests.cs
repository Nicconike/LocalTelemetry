using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using NSubstitute;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class IntelGpuMonitorTests
{
    [Fact]
    public void IntelGpuNativePInvoke_Instance_ExecutesSafely()
    {
        var native = IntelGpuNativePInvoke.Instance;
        native.Init(out var handle);
        native.EnumerateDevices(handle, out _);
        native.GetDeviceName(handle, out _);
        native.GetTempSensor(handle, out _);
        native.GetTempC(handle, out _);
        native.GetFreqDomain(handle, out _);
        native.GetClockMHz(handle, out _);
        native.GetMemModule(handle, out _);
        native.GetVram(handle, out _, out _);
        native.Close(handle);
    }

    [Fact]
    public void IntelGpuMonitor_FallbackWhenInitFails()
    {
        var nativeMock = Substitute.For<IIntelGpuNative>();
        nativeMock.Init(out Arg.Any<IntPtr>()).Returns(x => { x[0] = IntPtr.Zero; return -1; });

        using var monitor = new IntelGpuMonitor(nativeMock);

        monitor.IsAvailable.Should().BeFalse();
        monitor.GpuName.Should().BeEmpty();
        monitor.GetTempC().Should().Be(0);
        monitor.GetClockMHz().Should().Be(0);
        monitor.GetVram().Should().Be((0, 0));
    }

    [Fact]
    public void IntelGpuMonitor_FallbackWhenEnumerateDevicesFails()
    {
        var nativeMock = Substitute.For<IIntelGpuNative>();
        nativeMock.Init(out Arg.Any<IntPtr>()).Returns(x => { x[0] = (IntPtr)0x1000; return 0; });
        nativeMock.EnumerateDevices((IntPtr)0x1000, out Arg.Any<IntPtr[]>()!).Returns(x => { x[1] = Array.Empty<IntPtr>(); return -1; });

        using var monitor = new IntelGpuMonitor(nativeMock);

        monitor.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void IntelGpuMonitor_WithMockNative_ReturnsTelemetryMetrics()
    {
        var nativeMock = Substitute.For<IIntelGpuNative>();

        nativeMock.Init(out Arg.Any<IntPtr>()).Returns(x => { x[0] = (IntPtr)0x1000; return 0; });

        nativeMock.EnumerateDevices((IntPtr)0x1000, out Arg.Any<IntPtr[]>()!).Returns(x =>
        {
            x[1] = new IntPtr[] { (IntPtr)0x2000 };
            return 0;
        });

        nativeMock.GetDeviceName((IntPtr)0x2000, out Arg.Any<string>()!).Returns(x => { x[1] = "Intel Arc A770 Graphics"; return 0; });
        nativeMock.GetTempSensor((IntPtr)0x2000, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x3000; return 0; });
        nativeMock.GetFreqDomain((IntPtr)0x2000, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x4000; return 0; });
        nativeMock.GetMemModule((IntPtr)0x2000, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x5000; return 0; });

        nativeMock.GetTempC((IntPtr)0x3000, out Arg.Any<double>()).Returns(x => { x[1] = 58.5; return 0; });
        nativeMock.GetClockMHz((IntPtr)0x4000, out Arg.Any<double>()).Returns(x => { x[1] = 2400.0; return 0; });

        nativeMock.GetVram((IntPtr)0x5000, out Arg.Any<ulong>(), out Arg.Any<ulong>()).Returns(x =>
        {
            x[1] = (ulong)(16L * 1024 * 1024 * 1024);
            x[2] = (ulong)(4L * 1024 * 1024 * 1024);
            return 0;
        });

        using var monitor = new IntelGpuMonitor(nativeMock);

        monitor.IsAvailable.Should().BeTrue();
        monitor.GpuName.Should().Be("Intel Arc A770 Graphics");
        monitor.GetTempC().Should().Be(58.5f);
        monitor.GetClockMHz().Should().Be(2400.0f);

        var (usedMb, totalMb) = monitor.GetVram();
        totalMb.Should().Be(16384.0f);
        usedMb.Should().Be(12288.0f);

        monitor.Dispose();
        nativeMock.Received(1).Close((IntPtr)0x1000);
    }

    [Fact]
    public void IntelGpuNativePInvoke_Structs_CanBeInstantiated()
    {
        var types = typeof(IntelGpuMonitor).Assembly.GetTypes()
            .Where(t => t.FullName!.Contains("Ctl") && t.IsValueType);

        foreach (var t in types)
        {
            var inst = Activator.CreateInstance(t);
            inst.Should().NotBeNull();
        }
    }

    [Fact]
    public void IntelGpuMonitor_Reset_Reinitializes()
    {
        var nativeMock = Substitute.For<IIntelGpuNative>();
        nativeMock.Init(out Arg.Any<IntPtr>()).Returns(x => { x[0] = (IntPtr)0x1000; return 0; });
        nativeMock.EnumerateDevices((IntPtr)0x1000, out Arg.Any<IntPtr[]>()!).Returns(x =>
        {
            x[1] = new IntPtr[] { (IntPtr)0x2000 };
            return 0;
        });

        using var monitor = new IntelGpuMonitor(nativeMock);
        monitor.IsAvailable.Should().BeTrue();

        monitor.Reset();

        monitor.IsAvailable.Should().BeTrue();
        nativeMock.Received(2).Init(out Arg.Any<IntPtr>());
    }

    [Fact]
    public void IntelGpuMonitor_IndividualSensorFailures_ReturnZeroMetrics()
    {
        var nativeMock = Substitute.For<IIntelGpuNative>();
        nativeMock.Init(out Arg.Any<IntPtr>()).Returns(x => { x[0] = (IntPtr)0x1000; return 0; });
        nativeMock.EnumerateDevices((IntPtr)0x1000, out Arg.Any<IntPtr[]>()!).Returns(x =>
        {
            x[1] = new IntPtr[] { (IntPtr)0x2000 };
            return 0;
        });

        // Fail individual sensor queries during Init
        nativeMock.GetDeviceName((IntPtr)0x2000, out Arg.Any<string>()!).Returns(-1);
        nativeMock.GetTempSensor((IntPtr)0x2000, out Arg.Any<IntPtr>()).Returns(-1);
        nativeMock.GetFreqDomain((IntPtr)0x2000, out Arg.Any<IntPtr>()).Returns(-1);
        nativeMock.GetMemModule((IntPtr)0x2000, out Arg.Any<IntPtr>()).Returns(-1);

        using var monitor = new IntelGpuMonitor(nativeMock);
        monitor.IsAvailable.Should().BeTrue();
        monitor.GpuName.Should().BeEmpty();
        monitor.GetTempC().Should().Be(0f);
        monitor.GetClockMHz().Should().Be(0f);
        monitor.GetVram().Should().Be((0f, 0f));
    }

    [Fact]
    public void IntelGpuMonitor_SensorReadsReturningError_ReturnZeroMetrics()
    {
        var nativeMock = Substitute.For<IIntelGpuNative>();
        nativeMock.Init(out Arg.Any<IntPtr>()).Returns(x => { x[0] = (IntPtr)0x1000; return 0; });
        nativeMock.EnumerateDevices((IntPtr)0x1000, out Arg.Any<IntPtr[]>()!).Returns(x =>
        {
            x[1] = new IntPtr[] { (IntPtr)0x2000 };
            return 0;
        });

        nativeMock.GetTempSensor((IntPtr)0x2000, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x3000; return 0; });
        nativeMock.GetFreqDomain((IntPtr)0x2000, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x4000; return 0; });
        nativeMock.GetMemModule((IntPtr)0x2000, out Arg.Any<IntPtr>()).Returns(x => { x[1] = (IntPtr)0x5000; return 0; });

        // Sensor handles exist, but calls return error code
        nativeMock.GetTempC((IntPtr)0x3000, out Arg.Any<double>()).Returns(-1);
        nativeMock.GetClockMHz((IntPtr)0x4000, out Arg.Any<double>()).Returns(-1);
        nativeMock.GetVram((IntPtr)0x5000, out Arg.Any<ulong>(), out Arg.Any<ulong>()).Returns(-1);

        using var monitor = new IntelGpuMonitor(nativeMock);
        monitor.GetTempC().Should().Be(0f);
        monitor.GetClockMHz().Should().Be(0f);
        monitor.GetVram().Should().Be((0f, 0f));
    }
}
