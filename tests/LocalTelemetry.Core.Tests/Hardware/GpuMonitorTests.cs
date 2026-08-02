using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class GpuMonitorTests
{
    [Fact]
    public void AmdGpuMonitor_MethodsExecuteSafely()
    {
        using var monitor = new AmdGpuMonitor();
        monitor.GetTempC().Should().BeGreaterThanOrEqualTo(0);
        monitor.GetUsagePct().Should().BeGreaterThanOrEqualTo(0);
        monitor.GetClockMHz().Should().BeGreaterThanOrEqualTo(0);
        monitor.GetPowerLimitW().Should().BeGreaterThanOrEqualTo(0);
        monitor.GetPowerDrawW().Should().BeGreaterThanOrEqualTo(0);
        var vram = monitor.GetVram();
        vram.usedMb.Should().BeGreaterThanOrEqualTo(0);
        vram.totalMb.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void IntelGpuMonitor_MethodsExecuteSafely()
    {
        using var monitor = new IntelGpuMonitor();
        monitor.GetTempC().Should().BeGreaterThanOrEqualTo(0);
        monitor.GetClockMHz().Should().BeGreaterThanOrEqualTo(0);
        var vram = monitor.GetVram();
        vram.usedMb.Should().BeGreaterThanOrEqualTo(0);
        vram.totalMb.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void NvGpuMonitor_MethodsExecuteSafely()
    {
        using var monitor = new NvGpuMonitor();
        var data = monitor.Query();
        if (data.HasValue)
        {
            data.Value.TempC.Should().BeGreaterThanOrEqualTo(0);
            data.Value.UsagePct.Should().BeGreaterThanOrEqualTo(0);
            data.Value.VramTotalMb.Should().BeGreaterThanOrEqualTo(0);
            data.Value.VramUsedMb.Should().BeGreaterThanOrEqualTo(0);
            data.Value.CoreClockMHz.Should().BeGreaterThanOrEqualTo(0);
            data.Value.PowerDrawW.Should().BeGreaterThanOrEqualTo(0);
            data.Value.TdpW.Should().BeGreaterThanOrEqualTo(0);
        }
    }
}
