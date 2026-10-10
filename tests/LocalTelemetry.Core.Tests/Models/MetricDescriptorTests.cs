using FluentAssertions;
using LocalTelemetry.Core.Models;
using Xunit;

namespace LocalTelemetry.Core.Tests.Models;

public class MetricDescriptorTests
{
    [Fact]
    public void MetricDescriptor_Properties_ReturnCorrectValues()
    {
        var desc = new MetricDescriptor("cpu_pct", "CPU", "CPU Usage", "%", "cpu");

        desc.Id.Should().Be("cpu_pct");
        desc.ShortLabel.Should().Be("CPU");
        desc.FullLabel.Should().Be("CPU Usage");
        desc.Unit.Should().Be("%");
        desc.Group.Should().Be("cpu");
    }
}
