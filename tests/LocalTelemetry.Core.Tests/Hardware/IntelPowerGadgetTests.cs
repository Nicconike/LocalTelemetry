using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using NSubstitute;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class IntelPowerGadgetTests
{
    [Fact]
    public void IntelPowerGadgetNativePInvoke_Instance_ExecutesSafely()
    {
        var native = IntelPowerGadgetNativePInvoke.Instance;
        try
        {
            // 1. Success hooks
            native.TryLoadLibraryFunc = () => true;
            native.InitializeFunc = () => 0;
            native.ShutdownFunc = () => 0;
            native.GetNumNodesFunc = (ref int n) => { n = 1; return 0; };
            native.GetIAFrequencyFunc = (int node, ref double freq) => { freq = 3600.0; return 0; };

            native.TryLoadLibrary().Should().BeTrue();
            native.Initialize().Should().Be(0);
            native.GetNumNodes(out int count).Should().Be(0);
            count.Should().Be(1);
            native.GetIAFrequency(0, out double f).Should().Be(0);
            f.Should().Be(3600.0);
            native.Shutdown().Should().Be(0);

            // 2. Exception/Failure hooks
            native.TryLoadLibraryFunc = () => throw new InvalidOperationException("load fail");
            native.InitializeFunc = () => throw new InvalidOperationException("init fail");
            native.ShutdownFunc = () => throw new InvalidOperationException("shutdown fail");
            native.GetNumNodesFunc = (ref int n) => throw new InvalidOperationException("nodes fail");
            native.GetIAFrequencyFunc = (int node, ref double freq) => throw new InvalidOperationException("freq fail");

            native.TryLoadLibrary().Should().BeFalse();
            native.Initialize().Should().Be(-1);
            native.GetNumNodes(out _).Should().Be(-1);
            native.GetIAFrequency(0, out _).Should().Be(-1);
            native.Shutdown().Should().Be(-1);
        }
        finally
        {
            native.ResetHooks();
        }
    }

    [Fact]
    public void IntelPowerGadget_FallbackWhenLibraryLoadFails()
    {
        var nativeMock = Substitute.For<IIntelPowerGadgetNative>();
        nativeMock.TryLoadLibrary().Returns(false);

        using var gadget = new IntelPowerGadget(nativeMock);

        gadget.IsAvailable.Should().BeFalse();
        double.IsNaN(gadget.GetFrequency()).Should().BeTrue();
    }

    [Fact]
    public void IntelPowerGadget_FallbackWhenInitFails()
    {
        var nativeMock = Substitute.For<IIntelPowerGadgetNative>();
        nativeMock.TryLoadLibrary().Returns(true);
        nativeMock.Initialize().Returns(-1);

        using var gadget = new IntelPowerGadget(nativeMock);

        gadget.IsAvailable.Should().BeFalse();
        double.IsNaN(gadget.GetFrequency()).Should().BeTrue();
    }

    [Fact]
    public void IntelPowerGadget_FallbackWhenGetNumNodesFails()
    {
        var nativeMock = Substitute.For<IIntelPowerGadgetNative>();
        nativeMock.TryLoadLibrary().Returns(true);
        nativeMock.Initialize().Returns(0);

        nativeMock.GetNumNodes(out Arg.Any<int>()).Returns(x => { x[0] = -1; return -1; });

        using var gadget = new IntelPowerGadget(nativeMock);

        gadget.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void IntelPowerGadget_WithMockNative_ReturnsFrequency()
    {
        var nativeMock = Substitute.For<IIntelPowerGadgetNative>();
        nativeMock.TryLoadLibrary().Returns(true);
        nativeMock.Initialize().Returns(0);

        nativeMock.GetNumNodes(out Arg.Any<int>()).Returns(x => { x[0] = 4; return 0; });
        nativeMock.GetIAFrequency(0, out Arg.Any<double>()).Returns(x => { x[1] = 4500.0; return 0; });

        using var gadget = new IntelPowerGadget(nativeMock);

        gadget.IsAvailable.Should().BeTrue();
        gadget.GetFrequency().Should().Be(4500.0);

        gadget.Dispose();
        nativeMock.Received(1).Shutdown();
    }
}
