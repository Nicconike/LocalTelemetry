using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json.Serialization;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Diagnostics;
using LocalTelemetry.Core.Models;

namespace LocalTelemetry.App.Services;

/// <summary>
/// Checks GitHub Releases API for new application updates.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UpdateCheckService(AppSettings cfg, HttpClient? httpClient = null)
{
    private readonly AppSettings _cfg = cfg;
    private readonly HttpClient _httpClient = httpClient ?? CreateDefaultHttpClient();
#pragma warning disable S1075 // Official repository releases API and web URLs are invariant
    private const string GitHubReleasesUrl = "https://api.github.com/repos/Nicconike/LocalTelemetry/releases?per_page=3";
    private const string GitHubReleasesWebUrl = "https://github.com/Nicconike/LocalTelemetry/releases";

#pragma warning restore S1075

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(8);
        client.DefaultRequestHeaders.Add("User-Agent", "LocalTelemetry-App");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        return client;
    }

    /// <summary>
    /// Evaluates whether a background update check is due based on configured frequency.
    /// </summary>
    public bool IsCheckDue()
    {
        string freq = _cfg.Updates.CheckFrequency?.ToLowerInvariant() ?? "weekly";
        if (freq == "manual") return false;

        DateTime last = _cfg.Updates.LastCheckUtc;
        if (last == DateTime.MinValue) return true;

        TimeSpan elapsed = DateTime.UtcNow - last;
        return freq switch
        {
            "monthly" => elapsed.TotalDays >= 30,
            _ => elapsed.TotalDays >= 7, // Default: weekly
        };
    }

    /// <summary>
    /// Checks GitHub for available releases and returns an <see cref="UpdateCheckResult"/>.
    /// </summary>
    /// <param name="force">If true, ignores frequency cache timers.</param>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool force = false)
    {
        string currentVer = GetCurrentVersionString();

        if (force)
            Log.Info($"Manual update check requested by user (Current: v{currentVer})");
        else
            Log.Debug($"Background update check initiating (Current: v{currentVer})");

        if (!force && !IsCheckDue())
        {
            Log.Debug("Background update check skipped (interval not due)");
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                CurrentVersion = currentVer,
                LatestVersion = currentVer,
                ErrorMessage = null
            };
        }

        try
        {
            _cfg.Updates.LastCheckUtc = DateTime.UtcNow;
            _cfg.Save();

            using var response = await _httpClient.GetAsync(GitHubReleasesUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string statusMsg = $"GitHub API returned HTTP {(int)response.StatusCode} ({response.StatusCode})";
                Log.Warn(statusMsg);
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = currentVer,
                    ErrorMessage = statusMsg
                };
            }

            var releases = await response.Content.ReadFromJsonAsync<List<GitHubReleaseDto>>().ConfigureAwait(false);
            if (releases is null || releases.Count == 0)
            {
                Log.Warn("No releases found on GitHub repository");
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = currentVer,
                    ErrorMessage = "No releases found"
                };
            }

            bool includePrereleases = _cfg.Updates.IncludePrereleases;
            var targetRelease = includePrereleases
                ? releases.FirstOrDefault(r => !r.Draft)
                : releases.FirstOrDefault(r => !r.Draft && !r.Prerelease);

            if (targetRelease is null)
            {
                string desc = includePrereleases ? "No release version found" : "No stable release found";
                Log.Warn($"{desc} (Include Prereleases: {includePrereleases})");
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = currentVer,
                    ErrorMessage = desc
                };
            }

            if (string.IsNullOrWhiteSpace(targetRelease.TagName))
            {
                Log.Warn("No release version found on target release");
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = currentVer,
                    ErrorMessage = "No release version found"
                };
            }

            string latestVer = targetRelease.TagName.TrimStart('v', 'V');
            bool isNewer = IsVersionNewer(currentVer, latestVer);

            Log.Info($"Update check completed: Current=v{currentVer}, Latest=v{latestVer}, Update Available: {isNewer}, Pre-release: {targetRelease.Prerelease}");

            return new UpdateCheckResult
            {
                IsUpdateAvailable = isNewer,
                IsPrerelease = targetRelease.Prerelease,
                CurrentVersion = currentVer,
                LatestVersion = latestVer,
                ReleaseUrl = targetRelease.HtmlUrl ?? GitHubReleasesWebUrl,
                ReleaseNotes = targetRelease.Body ?? string.Empty,
                PublishedAt = targetRelease.PublishedAt ?? string.Empty,
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Request failed due to network or deserialization exception");
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                CurrentVersion = currentVer,
                ErrorMessage = "Could not connect to GitHub."
            };
        }
    }

    /// <summary>
    /// Gets current running assembly version string.
    /// </summary>
    public static string GetCurrentVersionString()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var infoAttr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        string ver = infoAttr?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "0.1.0";

        // Strip commit hash suffix e.g. "0.1.0+a3f910b" -> "0.1.0"
        int plusIdx = ver.IndexOf('+');
        if (plusIdx > 0)
            ver = ver[..plusIdx];

        return ver.TrimStart('v', 'V');
    }

    /// <summary>
    /// Robust version comparison logic supporting Semantic Versioning 2.0 &amp; standard System.Version.
    /// </summary>
    public static bool IsVersionNewer(string currentVerStr, string latestVerStr)
    {
        if (TryParseSemVer(currentVerStr, out var currentVer) &&
            TryParseSemVer(latestVerStr, out var latestVer))
        {
            return CompareSemVer(latestVer, currentVer) > 0;
        }

        string cleanCurrent = CleanVersionForParsing(currentVerStr);
        string cleanLatest = CleanVersionForParsing(latestVerStr);

        if (Version.TryParse(cleanCurrent, out var cVer) &&
            Version.TryParse(cleanLatest, out var lVer))
        {
            return lVer > cVer;
        }

        return string.Compare(latestVerStr, currentVerStr, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private readonly record struct ParsedSemVer(int Major, int Minor, int Patch, string? Prerelease);

    private static bool TryParseSemVer(string ver, out ParsedSemVer parsed)
    {
        parsed = default;
        ver = ver.Trim().TrimStart('v', 'V');

        int plusIdx = ver.IndexOf('+');
        if (plusIdx >= 0)
            ver = ver[..plusIdx];

        if (string.IsNullOrWhiteSpace(ver))
            return false;

        string? prerelease = null;
        int dashIdx = ver.IndexOf('-');
        if (dashIdx >= 0)
        {
            prerelease = ver[(dashIdx + 1)..];
            ver = ver[..dashIdx];
        }

        var parts = ver.Split('.');
        if (parts.Length < 2 || parts.Length > 4)
            return false;

        if (!int.TryParse(parts[0], out int major) ||
            !int.TryParse(parts[1], out int minor))
            return false;

        int patch = 0;
        if (parts.Length >= 3 && !int.TryParse(parts[2], out patch))
            return false;

        parsed = new ParsedSemVer(major, minor, patch, prerelease);
        return true;
    }

    private static int CompareSemVer(ParsedSemVer a, ParsedSemVer b)
    {
        if (a.Major != b.Major) return a.Major.CompareTo(b.Major);
        if (a.Minor != b.Minor) return a.Minor.CompareTo(b.Minor);
        if (a.Patch != b.Patch) return a.Patch.CompareTo(b.Patch);

        // A stable release without a prerelease has higher precedence than a prerelease build
        if (a.Prerelease is null && b.Prerelease is not null) return 1;
        if (a.Prerelease is not null && b.Prerelease is null) return -1;
        if (a.Prerelease is null && b.Prerelease is null) return 0;

        return ComparePrereleaseSegments(a.Prerelease!, b.Prerelease!);
    }

    private static int ComparePrereleaseSegments(string aPrerelease, string bPrerelease)
    {
        var aParts = aPrerelease.Split('.');
        var bParts = bPrerelease.Split('.');
        int minLen = Math.Min(aParts.Length, bParts.Length);

        for (int i = 0; i < minLen; i++)
        {
            int cmp = CompareIdentifier(aParts[i], bParts[i]);
            if (cmp != 0) return cmp;
        }

        return aParts.Length.CompareTo(bParts.Length);
    }

    private static int CompareIdentifier(string aPart, string bPart)
    {
        bool aIsNum = int.TryParse(aPart, out int aNum);
        bool bIsNum = int.TryParse(bPart, out int bNum);

        if (aIsNum && bIsNum) return aNum.CompareTo(bNum);
        if (aIsNum) return -1; // Numeric identifiers have lower precedence than non-numeric
        if (bIsNum) return 1;

        return string.Compare(aPart, bPart, StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanVersionForParsing(string ver)
    {
        ver = ver.TrimStart('v', 'V');
        int dashIdx = ver.IndexOf('-');
        if (dashIdx > 0)
            ver = ver[..dashIdx];
        int plusIdx = ver.IndexOf('+');
        if (plusIdx > 0)
            ver = ver[..plusIdx];
        return ver;
    }

    private sealed record GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; init; } = string.Empty;

        [JsonPropertyName("published_at")]
        public string PublishedAt { get; init; } = string.Empty;

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }
    }
}
