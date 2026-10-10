using FluentAssertions;
using LocalTelemetry.Core.Models;
using Xunit;

namespace LocalTelemetry.Core.Tests.Models;

public class UpdateCheckResultTests
{
    [Fact]
    public void UpdateCheckResult_Properties_ReturnExpectedValues()
    {
        var result = new UpdateCheckResult
        {
            IsUpdateAvailable = true,
            IsPrerelease = false,
            CurrentVersion = "1.0.0",
            LatestVersion = "1.1.0",
            ReleaseUrl = "https://github.com/Nicconike/LocalTelemetry/releases/tag/v1.1.0",
            ReleaseNotes = "Performance improvements",
            PublishedAt = "2026-09-01T00:00:00Z",
            ErrorMessage = null
        };

        result.IsUpdateAvailable.Should().BeTrue();
        result.IsPrerelease.Should().BeFalse();
        result.CurrentVersion.Should().Be("1.0.0");
        result.LatestVersion.Should().Be("1.1.0");
        result.ReleaseUrl.Should().Be("https://github.com/Nicconike/LocalTelemetry/releases/tag/v1.1.0");
        result.ReleaseNotes.Should().Be("Performance improvements");
        result.PublishedAt.Should().Be("2026-09-01T00:00:00Z");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void UpdateCheckResult_Defaults_InitializeCorrectly()
    {
        var result = new UpdateCheckResult();

        result.IsUpdateAvailable.Should().BeFalse();
        result.IsPrerelease.Should().BeFalse();
        result.CurrentVersion.Should().BeEmpty();
        result.LatestVersion.Should().BeEmpty();
        result.ReleaseUrl.Should().BeEmpty();
        result.ReleaseNotes.Should().BeEmpty();
        result.PublishedAt.Should().BeEmpty();
        result.ErrorMessage.Should().BeNull();
    }
}
