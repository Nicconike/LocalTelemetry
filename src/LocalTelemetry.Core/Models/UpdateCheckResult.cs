namespace LocalTelemetry.Core.Models;

/// <summary>
/// Result object returned by the update checking service.
/// </summary>
public sealed record UpdateCheckResult
{
    /// <summary>Whether a newer application version is available on GitHub.</summary>
    public bool IsUpdateAvailable { get; init; }

    /// <summary>Whether the latest detected version is a pre-release or Release Candidate (RC).</summary>
    public bool IsPrerelease { get; init; }

    /// <summary>The currently running application version string.</summary>
    public string CurrentVersion { get; init; } = string.Empty;

    /// <summary>The latest published release tag/version string on GitHub.</summary>
    public string LatestVersion { get; init; } = string.Empty;

    /// <summary>The direct web URL to the GitHub release page.</summary>
    public string ReleaseUrl { get; init; } = string.Empty;

    /// <summary>Brief summary or release notes of the latest release.</summary>
    public string ReleaseNotes { get; init; } = string.Empty;

    /// <summary>UTC ISO timestamp when the release was published.</summary>
    public string PublishedAt { get; init; } = string.Empty;

    /// <summary>Error message if the update check failed, or null on success.</summary>
    public string? ErrorMessage { get; init; }
}
