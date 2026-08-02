using FluentAssertions;
using LocalTelemetry.Core.Hardware.PawnIo;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class PawnIoManagerWrapperTests
{
    [Fact]
    public void PawnIoManagerWrapper_CallsDelegateMethods()
    {
        var wrapper = new PawnIoManagerWrapper();
        var bytes = wrapper.LoadResourceBytes("NonExistent.bin");
        bytes.Should().BeNull();
    }
}
