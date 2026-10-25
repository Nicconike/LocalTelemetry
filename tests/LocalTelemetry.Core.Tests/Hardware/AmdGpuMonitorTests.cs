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
        try
        {
            // 1. Success hooks
            native.CreateFunc = () => 0;
            native.DestroyFunc = () => 0;
            native.AdapterInfoFunc = (AmdAdlNativePInvoke.AdlAdapterInfo[] info, ref int count) =>
            {
                count = 1;
                info[0].iAdapterIndex = 0;
                info[0].strAdapterName = "AMD Radeon RX 7900 XTX";
                return 0;
            };
            native.TemperatureFunc = (int idx, int tc, ref AmdAdlNativePInvoke.AdlTemperature temp) => { temp.iTemperature = 55000; return 0; };
            native.ActivityFunc = (int idx, ref AmdAdlNativePInvoke.AdlPmActivity act) => { act.iActivityPercent = 80; act.iCoreClock = 25000; return 0; };
            native.MemoryInfoFunc = (int idx, ref AmdAdlNativePInvoke.AdlMemoryInfo mem) => { mem.iMemorySize = 24000; mem.iFreeMemorySize = 14000; return 0; };
            native.PowerLimitFunc = (int idx, int def, ref AmdAdlNativePInvoke.AdlPowerLimitInfo pli) => { pli.iMax = 350000; pli.iDefault = 300000; return 0; };

            native.Create().Should().Be(0);
            native.GetAdapterInfo(out int adIdx, out string adName).Should().Be(0);
            adIdx.Should().Be(0);
            adName.Should().Be("AMD Radeon RX 7900 XTX");
            native.GetTemperature(0, out int tempC).Should().Be(0);
            tempC.Should().Be(55000);
            native.GetActivity(0, out int actPct, out int clk).Should().Be(0);
            actPct.Should().Be(80);
            clk.Should().Be(25000);
            native.GetMemoryInfo(0, out long total, out long free).Should().Be(0);
            total.Should().Be(24000L);
            free.Should().Be(14000L);
            native.GetPowerLimit(0, out int maxMw, out int defMw).Should().Be(0);
            maxMw.Should().Be(350000);
            defMw.Should().Be(300000);
            native.Destroy().Should().Be(0);

            // Empty count check in GetAdapterInfo
            native.AdapterInfoFunc = (AmdAdlNativePInvoke.AdlAdapterInfo[] info, ref int count) => { count = 0; return 0; };
            native.GetAdapterInfo(out _, out _).Should().Be(-1);

            // 2. Exception/Failure hooks
            native.CreateFunc = () => throw new InvalidOperationException("create fail");
            native.DestroyFunc = () => throw new InvalidOperationException("destroy fail");
            native.AdapterInfoFunc = (AmdAdlNativePInvoke.AdlAdapterInfo[] info, ref int count) => throw new InvalidOperationException("adapter fail");
            native.TemperatureFunc = (int idx, int tc, ref AmdAdlNativePInvoke.AdlTemperature temp) => throw new InvalidOperationException("temp fail");
            native.ActivityFunc = (int idx, ref AmdAdlNativePInvoke.AdlPmActivity act) => throw new InvalidOperationException("activity fail");
            native.MemoryInfoFunc = (int idx, ref AmdAdlNativePInvoke.AdlMemoryInfo mem) => throw new InvalidOperationException("mem fail");
            native.PowerLimitFunc = (int idx, int def, ref AmdAdlNativePInvoke.AdlPowerLimitInfo pli) => throw new InvalidOperationException("power fail");

            native.Create().Should().Be(-1);
            native.GetAdapterInfo(out _, out _).Should().Be(-1);
            native.GetTemperature(0, out _).Should().Be(-1);
            native.GetActivity(0, out _, out _).Should().Be(-1);
            native.GetMemoryInfo(0, out _, out _).Should().Be(-1);
            native.GetPowerLimit(0, out _, out _).Should().Be(-1);
            native.Destroy().Should().Be(-1);
        }
        finally
        {
            native.ResetHooks();
        }
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

        nativeMock.GetAdapterInfo(out Arg.Any<int>(), out Arg.Any<string>()).Returns(x =>
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

        nativeMock.GetAdapterInfo(out Arg.Any<int>(), out Arg.Any<string>()).Returns(x =>
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
        nativeMock.GetAdapterInfo(out Arg.Any<int>(), out Arg.Any<string>()).Returns(x =>
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

    [Fact]
    public void AmdGpuMonitor_NativeSensorErrors_ReturnZeroMetrics()
    {
        var nativeMock = Substitute.For<IAmdAdlNative>();
        nativeMock.Create().Returns(0);
        nativeMock.GetAdapterInfo(out Arg.Any<int>(), out Arg.Any<string>()).Returns(x =>
        {
            x[0] = 0;
            x[1] = "AMD Radeon RX 7900 XTX";
            return 0;
        });

        // Configure sensor reads to return failure status
        nativeMock.GetTemperature(0, out Arg.Any<int>()).Returns(-1);
        nativeMock.GetActivity(0, out Arg.Any<int>(), out Arg.Any<int>()).Returns(-1);
        nativeMock.GetMemoryInfo(0, out Arg.Any<long>(), out Arg.Any<long>()).Returns(-1);
        nativeMock.GetPowerLimit(0, out Arg.Any<int>(), out Arg.Any<int>()).Returns(-1);

        using var monitor = new AmdGpuMonitor(nativeMock);
        monitor.IsAvailable.Should().BeTrue();

        monitor.GetTempC().Should().Be(0f);
        monitor.GetUsagePct().Should().Be(0f);
        monitor.GetClockMHz().Should().Be(0f);
        monitor.GetVram().Should().Be((0f, 0f));
        monitor.GetPowerLimitW().Should().Be(0f);
        monitor.GetPowerDrawW().Should().Be(0f);
    }

    [Fact]
    public void AmdGpuMonitor_Reset_WhenNativeThrows_HandlesGracefully()
    {
        var nativeMock = Substitute.For<IAmdAdlNative>();
        nativeMock.Create().Returns(0);
        nativeMock.GetAdapterInfo(out Arg.Any<int>(), out Arg.Any<string>()).Returns(x =>
        {
            x[0] = 0;
            x[1] = "AMD Radeon RX 7900 XTX";
            return 0;
        });

        using var monitor = new AmdGpuMonitor(nativeMock);
        monitor.IsAvailable.Should().BeTrue();

        // Destroy throws during reset
        nativeMock.Destroy().Returns(_ => throw new InvalidOperationException("Destroy failure"));
        var actReset = () => monitor.Reset();
        actReset.Should().NotThrow();

        // Init throws during reset
        nativeMock.Create().Returns(_ => throw new InvalidOperationException("Init failure"));
        var actReset2 = () => monitor.Reset();
        actReset2.Should().NotThrow();
        monitor.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void AmdGpuMonitor_Constructor_WhenInitThrows_HandlesGracefully()
    {
        var nativeMock = Substitute.For<IAmdAdlNative>();
        nativeMock.Create().Returns(_ => throw new InvalidOperationException("Native create crashed"));

        using var monitor = new AmdGpuMonitor(nativeMock);
        monitor.IsAvailable.Should().BeFalse();
    }
}
