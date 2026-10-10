using FluentAssertions;
using LocalTelemetry.Core.Hardware.PawnIo;
using NSubstitute;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware.PawnIo;

public class AmdMsrReaderTests
{
    [Fact]
    public void AmdMsrReader_ReadTemperature_WithNullDevice_ReturnsZero()
    {
        using var reader = new AmdMsrReader(null!);
        reader.IsAvailable.Should().BeFalse();
        reader.ReadTemperature().Should().Be(0f);
    }

    [Fact]
    public void AmdMsrReader_ReadTemperature_WithMockTransport_ParsesTemperature()
    {
        var transport = Substitute.For<IPawnIoTransport>();

        // ioctl_resolve_pm_table -> returns version & base
        transport.Execute("ioctl_resolve_pm_table", null, 2).Returns(new ulong[] { 1, 0x1000 });

        // ioctl_update_pm_table -> returns 1
        transport.Execute("ioctl_update_pm_table", null, 0).Returns(new ulong[] { 1 });

        // ioctl_read_pm_table -> returns table entries with raw temp (e.g. 50000 -> 50.0°C)
        var table = new ulong[128];
        table[0] = 50000; // 50.0°C
        transport.Execute("ioctl_read_pm_table", null, 128).Returns(table);

        using var reader = new AmdMsrReader(transport);
        reader.IsAvailable.Should().BeTrue();

        float temp = reader.ReadTemperature();
        temp.Should().Be(50.0f);
    }
}
