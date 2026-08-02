using FluentAssertions;
using LocalTelemetry.Core.Hardware.PawnIo;
using NSubstitute;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class PawnIoTests
{
    [Fact]
    public void IntelMsrReader_ReadTemperature_WithNullDevice_ReturnsZero()
    {
        using var reader = new IntelMsrReader(null!);
        reader.IsAvailable.Should().BeFalse();
        reader.ReadTemperature().Should().Be(0f);
    }

    [Fact]
    public void IntelMsrReader_ReadTemperature_CalculatesTemperatureFromMsr()
    {
        var mockDevice = Substitute.For<IPawnIoTransport>();

        // MSR_IA32_TEMPERATURE_TARGET (0x1A2) returns tjMax = 100 in bits 16-23 -> (100 << 16) = 0x640000
        mockDevice.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == 0x1A2), 1)
                  .Returns([0x640000UL]);

        // MSR_IA32_PACKAGE_THERM_STATUS (0x1B1) returns digitalReadout = 40 in bits 16-23 -> (40 << 16) = 0x280000
        mockDevice.Execute("ioctl_read_msr", Arg.Is<ulong[]>(a => a[0] == 0x1B1), 1)
                  .Returns([0x280000UL]);

        using var reader = new IntelMsrReader(mockDevice);
        reader.IsAvailable.Should().BeTrue();

        float temp = reader.ReadTemperature();
        temp.Should().Be(60f); // 100 - 40 = 60
    }

    [Fact]
    public void AmdMsrReader_ReadTemperature_WithNullDevice_ReturnsZero()
    {
        using var reader = new AmdMsrReader(null!);
        reader.IsAvailable.Should().BeFalse();
        reader.ReadTemperature().Should().Be(0f);
    }

    [Fact]
    public void AmdMsrReader_ReadTemperature_CalculatesTemperatureFromPmTable()
    {
        var mockDevice = Substitute.For<IPawnIoTransport>();

        // ioctl_resolve_pm_table returns [version, baseAddress]
        mockDevice.Execute("ioctl_resolve_pm_table", null, 2)
                  .Returns([1UL, 0x1000UL]);

        // ioctl_update_pm_table returns [1] on success
        mockDevice.Execute("ioctl_update_pm_table", null, 0)
                  .Returns([1UL]);

        // ioctl_read_pm_table returns the power management table entries
        // TryDecodeTemperature divides by 1000f if between 10000 and 200000
        // e.g. 50000 -> 50.0 °C
        ulong entry1 = 50000UL;
        ulong entry2 = (60000UL << 32) | 40000UL; // High: 60C, Low: 40C
        mockDevice.Execute("ioctl_read_pm_table", null, 128)
                  .Returns([entry1, entry2]);

        using var reader = new AmdMsrReader(mockDevice);
        reader.IsAvailable.Should().BeTrue();

        float temp = reader.ReadTemperature();
        temp.Should().Be(60f); // The max temperature found in entry2 (60000 / 1000)
    }

    [Fact]
    public void PawnIoDevice_TryCreate_SafelyAttemptsCreation()
    {
        var dev = PawnIoDevice.TryCreate();
        dev?.Dispose();
    }
}
