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
        try
        {
            // 1. Success hooks
            native.InitFunc = (ref IntelGpuNativePInvoke.CtlInitArgs desc, out IntPtr handle) => { handle = (IntPtr)0x2000; return 0; };
            native.CloseFunc = h => 0;
            native.EnumerateDevicesFunc = (IntPtr h, ref uint c, IntPtr[]? d) =>
            {
                c = 1;
                if (d != null && d.Length > 0) d[0] = (IntPtr)0x3000;
                return 0;
            };
            native.GetDevicePropertiesFunc = (IntPtr h, ref IntelGpuNativePInvoke.CtlDeviceAdapterProperties props) =>
            {
                props.name = "Intel Arc A770";
                return 0;
            };
            native.EnumTemperatureSensorsFunc = (IntPtr h, ref uint c, IntPtr[]? t) =>
            {
                c = 1;
                if (t != null && t.Length > 0) t[0] = (IntPtr)0x4000;
                return 0;
            };
            native.TemperatureGetStateFunc = (IntPtr h, ref double temp) => { temp = 58.5; return 0; };
            native.EnumFrequencyDomainsFunc = (IntPtr h, ref uint c, IntPtr[]? f) =>
            {
                c = 1;
                if (f != null && f.Length > 0) f[0] = (IntPtr)0x5000;
                return 0;
            };
            native.FrequencyGetStateFunc = (IntPtr h, ref IntelGpuNativePInvoke.CtlFreqState s) => { s.actual = 2100.0; return 0; };
            native.EnumMemoryModulesFunc = (IntPtr h, ref uint c, IntPtr[]? m) =>
            {
                c = 1;
                if (m != null && m.Length > 0) m[0] = (IntPtr)0x6000;
                return 0;
            };
            native.MemoryGetStateFunc = (IntPtr h, ref IntelGpuNativePInvoke.CtlMemState s) => { s.size = 16000000; s.free = 8000000; return 0; };

            native.Init(out var apiHandle).Should().Be(0);
            apiHandle.Should().Be((IntPtr)0x2000);
            native.EnumerateDevices(apiHandle, out var devs).Should().Be(0);
            devs.Should().ContainSingle();
            native.GetDeviceName(devs[0], out var name).Should().Be(0);
            name.Should().Be("Intel Arc A770");
            native.GetTempSensor(devs[0], out var tSensor).Should().Be(0);
            native.GetTempC(tSensor, out var tempC).Should().Be(0);
            tempC.Should().Be(58.5);
            native.GetFreqDomain(devs[0], out var fDomain).Should().Be(0);
            native.GetClockMHz(fDomain, out var clk).Should().Be(0);
            clk.Should().Be(2100.0);
            native.GetMemModule(devs[0], out var mMod).Should().Be(0);
            native.GetVram(mMod, out var total, out var free).Should().Be(0);
            total.Should().Be(16000000UL);
            free.Should().Be(8000000UL);
            native.Close(apiHandle).Should().Be(0);

            // Empty count paths
            native.EnumerateDevicesFunc = (IntPtr h, ref uint c, IntPtr[]? d) => { c = 0; return 0; };
            native.EnumerateDevices(apiHandle, out _).Should().Be(-1);
            native.EnumTemperatureSensorsFunc = (IntPtr h, ref uint c, IntPtr[]? t) => { c = 0; return 0; };
            native.GetTempSensor(devs[0], out _).Should().Be(-1);
            native.EnumFrequencyDomainsFunc = (IntPtr h, ref uint c, IntPtr[]? f) => { c = 0; return 0; };
            native.GetFreqDomain(devs[0], out _).Should().Be(-1);
            native.EnumMemoryModulesFunc = (IntPtr h, ref uint c, IntPtr[]? m) => { c = 0; return 0; };
            native.GetMemModule(devs[0], out _).Should().Be(-1);

            // 2. Exception/Failure hooks
            native.InitFunc = (ref IntelGpuNativePInvoke.CtlInitArgs desc, out IntPtr handle) => throw new InvalidOperationException("init fail");
            native.CloseFunc = h => throw new InvalidOperationException("close fail");
            native.EnumerateDevicesFunc = (IntPtr h, ref uint c, IntPtr[]? d) => throw new InvalidOperationException("enum fail");
            native.GetDevicePropertiesFunc = (IntPtr h, ref IntelGpuNativePInvoke.CtlDeviceAdapterProperties props) => throw new InvalidOperationException("props fail");
            native.EnumTemperatureSensorsFunc = (IntPtr h, ref uint c, IntPtr[]? t) => throw new InvalidOperationException("temp fail");
            native.TemperatureGetStateFunc = (IntPtr h, ref double temp) => throw new InvalidOperationException("temp state fail");
            native.EnumFrequencyDomainsFunc = (IntPtr h, ref uint c, IntPtr[]? f) => throw new InvalidOperationException("freq fail");
            native.FrequencyGetStateFunc = (IntPtr h, ref IntelGpuNativePInvoke.CtlFreqState s) => throw new InvalidOperationException("freq state fail");
            native.EnumMemoryModulesFunc = (IntPtr h, ref uint c, IntPtr[]? m) => throw new InvalidOperationException("mem fail");
            native.MemoryGetStateFunc = (IntPtr h, ref IntelGpuNativePInvoke.CtlMemState s) => throw new InvalidOperationException("mem state fail");

            native.Init(out _).Should().Be(-1);
            native.Close(apiHandle).Should().Be(-1);
            native.EnumerateDevices(apiHandle, out _).Should().Be(-1);
            native.GetDeviceName(apiHandle, out _).Should().Be(-1);
            native.GetTempSensor(apiHandle, out _).Should().Be(-1);
            native.GetTempC(apiHandle, out _).Should().Be(-1);
            native.GetFreqDomain(apiHandle, out _).Should().Be(-1);
            native.GetClockMHz(apiHandle, out _).Should().Be(-1);
            native.GetMemModule(apiHandle, out _).Should().Be(-1);
            native.GetVram(apiHandle, out _, out _).Should().Be(-1);
        }
        finally
        {
            native.ResetHooks();
        }
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
        nativeMock.EnumerateDevices((IntPtr)0x1000, out Arg.Any<IntPtr[]>()).Returns(x => { x[1] = Array.Empty<IntPtr>(); return -1; });

        using var monitor = new IntelGpuMonitor(nativeMock);

        monitor.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void IntelGpuMonitor_WithMockNative_ReturnsTelemetryMetrics()
    {
        var nativeMock = Substitute.For<IIntelGpuNative>();

        nativeMock.Init(out Arg.Any<IntPtr>()).Returns(x => { x[0] = (IntPtr)0x1000; return 0; });

        nativeMock.EnumerateDevices((IntPtr)0x1000, out Arg.Any<IntPtr[]>()).Returns(x =>
        {
            x[1] = new IntPtr[] { (IntPtr)0x2000 };
            return 0;
        });

        nativeMock.GetDeviceName((IntPtr)0x2000, out Arg.Any<string>()).Returns(x => { x[1] = "Intel Arc A770 Graphics"; return 0; });
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

        var monitor = new IntelGpuMonitor(nativeMock);

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
        nativeMock.EnumerateDevices((IntPtr)0x1000, out Arg.Any<IntPtr[]>()).Returns(x =>
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
        nativeMock.EnumerateDevices((IntPtr)0x1000, out Arg.Any<IntPtr[]>()).Returns(x =>
        {
            x[1] = new IntPtr[] { (IntPtr)0x2000 };
            return 0;
        });

        // Fail individual sensor queries during Init
        nativeMock.GetDeviceName((IntPtr)0x2000, out Arg.Any<string>()).Returns(-1);
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
        nativeMock.EnumerateDevices((IntPtr)0x1000, out Arg.Any<IntPtr[]>()).Returns(x =>
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

    [Fact]
    public void IntelGpuMonitor_WhenUnavailable_MethodsReturnZero()
    {
        var nativeMock = Substitute.For<IIntelGpuNative>();
        nativeMock.Init(out Arg.Any<IntPtr>()).Returns(-1);

        using var monitor = new IntelGpuMonitor(nativeMock);
        monitor.IsAvailable.Should().BeFalse();

        monitor.GetTempC().Should().Be(0f);
        monitor.GetClockMHz().Should().Be(0f);
        monitor.GetVram().Should().Be((0f, 0f));
    }

    [Fact]
    public void IntelGpuMonitor_Reset_WhenNativeThrows_HandlesGracefully()
    {
        var nativeMock = Substitute.For<IIntelGpuNative>();
        nativeMock.Init(out Arg.Any<IntPtr>()).Returns(x => { x[0] = (IntPtr)0x1000; return 0; });
        nativeMock.EnumerateDevices((IntPtr)0x1000, out Arg.Any<IntPtr[]>()).Returns(x =>
        {
            x[1] = new IntPtr[] { (IntPtr)0x2000 };
            return 0;
        });

        using var monitor = new IntelGpuMonitor(nativeMock);
        monitor.IsAvailable.Should().BeTrue();

        // Close throws
        nativeMock.Close(Arg.Any<IntPtr>()).Returns(_ => throw new InvalidOperationException("Close failed"));
        var actReset1 = () => monitor.Reset();
        actReset1.Should().NotThrow();

        // Init throws
        nativeMock.Init(out Arg.Any<IntPtr>()).Returns(_ => throw new InvalidOperationException("Init crashed"));
        var actReset2 = () => monitor.Reset();
        actReset2.Should().NotThrow();
        monitor.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void IntelGpuMonitor_Constructor_WhenInitThrows_HandlesGracefully()
    {
        var nativeMock = Substitute.For<IIntelGpuNative>();
        nativeMock.Init(out Arg.Any<IntPtr>()).Returns(_ => throw new InvalidOperationException("Init crashed"));

        using var monitor = new IntelGpuMonitor(nativeMock);
        monitor.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void IntelGpuMonitor_Dispose_MultipleCalls_AreIdempotent()
    {
        var nativeMock = Substitute.For<IIntelGpuNative>();
        nativeMock.Init(out Arg.Any<IntPtr>()).Returns(x => { x[0] = (IntPtr)0x1000; return 0; });
        nativeMock.EnumerateDevices((IntPtr)0x1000, out Arg.Any<IntPtr[]>()).Returns(x =>
        {
            x[1] = new IntPtr[] { (IntPtr)0x2000 };
            return 0;
        });

        var monitor = new IntelGpuMonitor(nativeMock);
        Action disposeAction = monitor.Dispose;
        disposeAction();
        disposeAction.Should().NotThrow();

        nativeMock.Received(1).Close((IntPtr)0x1000);
    }
}
