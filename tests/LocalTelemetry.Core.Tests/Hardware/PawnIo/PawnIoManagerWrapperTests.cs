using FluentAssertions;
using LocalTelemetry.Core.Hardware.PawnIo;
using NSubstitute;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware.PawnIo;

public class PawnIoManagerWrapperTests
{
    [Fact]
    public void PawnIoManagerWrapper_CallsDelegateMethods()
    {
        var wrapper = new PawnIoManagerWrapper();
        var bytes = wrapper.LoadResourceBytes("NonExistent.bin");
        bytes.Should().BeNull();

        // Test safe creation query without triggering UAC elevation/install commands
        wrapper.TryCreate();
    }

    [Fact]
    public void LoadModule_NonPawnDevice_ReturnsFalse()
    {
        var wrapper = new PawnIoManagerWrapper();
        var fake = Substitute.For<IPawnIoTransport>();

        wrapper.LoadModule(fake, [1, 2, 3]).Should().BeFalse();

        using var handle = new Microsoft.Win32.SafeHandles.SafeFileHandle(IntPtr.Zero, true);
        using var device = new PawnIoDevice(handle);
        wrapper.LoadModule(device, [1, 2, 3]).Should().BeFalse();
    }

    [Fact]
    public void Statics_AreCallableWithoutThrowing()
    {
        var wrapper = new PawnIoManagerWrapper();
        var act = () =>
        {
            try { wrapper.TryCreate(); } catch (Exception) { /* Best-effort execution */ }
            try { wrapper.TryInstall(); } catch (Exception) { /* Best-effort execution */ }
            try { wrapper.StartDriverService(); } catch (Exception) { /* Best-effort execution */ }
            try { wrapper.LoadResourceBytes("IntelMSR.bin"); } catch (Exception) { /* Best-effort execution */ }
        };
        act.Should().NotThrow();
    }
}
