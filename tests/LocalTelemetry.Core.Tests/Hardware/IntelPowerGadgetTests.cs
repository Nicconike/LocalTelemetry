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
        var act = () =>
        {
            native.TryLoadLibrary();
            native.Initialize();
            native.GetNumNodes(out _);
            native.GetIAFrequency(0, out _);
            native.Shutdown();
        };
        act.Should().NotThrow();
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
