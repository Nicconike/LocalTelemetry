using FluentAssertions;
using LocalTelemetry.App.Win32;
using Xunit;

namespace LocalTelemetry.App.Tests.Win32;

public class NativeMethodsTests
{
    [Fact]
    public void NativeMethods_GetDoubleClickTime_ExecutesSafely()
    {
        uint time = NativeMethods.GetDoubleClickTime();
        time.Should().BeGreaterThan(0);
    }
}
