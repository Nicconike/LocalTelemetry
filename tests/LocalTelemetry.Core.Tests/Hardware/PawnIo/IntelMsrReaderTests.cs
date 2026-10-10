using FluentAssertions;
using LocalTelemetry.Core.Hardware.PawnIo;
using NSubstitute;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware.PawnIo;

public class IntelMsrReaderTests
{
    [Fact]
    public void IntelMsrReader_ReadTemperature_WithNullDevice_ReturnsZero()
    {
        using var reader = new IntelMsrReader(null!);
        reader.IsAvailable.Should().BeFalse();
        reader.ReadTemperature().Should().Be(0f);
    }

    [Fact]
    public void IntelMsrReader_ReadTemperature_WithMockTransport_CalculatesTjMaxMinusReadout()
    {
        var transport = Substitute.For<IPawnIoTransport>();

        // MSR_IA32_TEMPERATURE_TARGET (0x1A2): tjMax = 100 in bits 16..23 (100 << 16 = 0x640000)
        transport.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a.Length == 1 && a[0] == 0x1A2), 1)
                 .Returns(new ulong[] { 100UL << 16 });

        // MSR_IA32_PACKAGE_THERM_STATUS (0x1B1): digitalReadout = 40 in bits 16..23 (40 << 16 = 0x280000)
        transport.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a.Length == 1 && a[0] == 0x1B1), 1)
                 .Returns(new ulong[] { 40UL << 16 });

        using var reader = new IntelMsrReader(transport);
        reader.IsAvailable.Should().BeTrue();

        float temp = reader.ReadTemperature();
        temp.Should().Be(60.0f); // 100 - 40 = 60°C
    }
}
