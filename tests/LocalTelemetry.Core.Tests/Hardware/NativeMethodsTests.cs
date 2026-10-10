using System.Reflection;
using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class NativeMethodsTests
{
    private static readonly Type NativeMethodsType = typeof(SystemInfo).Assembly.GetType("LocalTelemetry.Core.Hardware.NativeMethods")!;

    [Fact]
    public void NativeMethods_SetupApiGuids_AreValid()
    {
        var displayGuid = (Guid)NativeMethodsType.GetField("GUID_DISPLAY", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var netGuid = (Guid)NativeMethodsType.GetField("GUID_NET", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        displayGuid.Should().NotBe(Guid.Empty);
        netGuid.Should().NotBe(Guid.Empty);
    }
}
