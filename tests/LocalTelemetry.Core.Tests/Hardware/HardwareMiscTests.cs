using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using LocalTelemetry.Core.Hardware.PawnIo;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class HardwareMiscTests
{
    [Fact]
    public void IntelPowerGadget_MethodsExecuteSafely()
    {
        using var gadget = new IntelPowerGadget();
        // Just verify it doesn't crash.
        double freq = gadget.GetFrequency();
        if (gadget.IsAvailable)
        {
            freq.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void WddmGpuMonitor_MethodsExecuteSafely()
    {
        using var monitor = new WddmGpuMonitor();
        // Since we are in CI, it may or may not find counters
        float usage = monitor.GetUsagePct();
        usage.Should().BeGreaterThanOrEqualTo(0);

        // Test polling twice
        usage = monitor.GetUsagePct();
        usage.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void PawnIoDevice_StaticMethods_ExecuteSafely()
    {
        var bytes = PawnIoDevice.LoadResourceBytes("invalid_resource.bin");
        bytes.Should().BeNull();
    }
}
