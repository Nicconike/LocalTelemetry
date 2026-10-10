using System.Net;
using System.Net.Http;
using LocalTelemetry.App.Services;
using LocalTelemetry.Core.Config;
using Xunit;

namespace LocalTelemetry.App.Tests.Services;

public class UpdateCheckServiceTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFunc;

        public MockHttpMessageHandler(HttpResponseMessage response)
        {
            _responseFunc = _ => new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Content.ReadAsStringAsync().GetAwaiter().GetResult(), System.Text.Encoding.UTF8, "application/json")
            };
        }

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFunc)
        {
            _responseFunc = responseFunc;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFunc(request));
        }
    }

    [Fact]
    public void IsVersionNewer_CorrectlyComparesVersions()
    {
        Assert.True(UpdateCheckService.IsVersionNewer("1.0.0", "1.1.0"));
        Assert.True(UpdateCheckService.IsVersionNewer("v1.0.0", "v2.0.0"));
        Assert.True(UpdateCheckService.IsVersionNewer("0.1.0-beta.1", "0.2.0"));
        Assert.False(UpdateCheckService.IsVersionNewer("1.1.0", "1.0.0"));
        Assert.False(UpdateCheckService.IsVersionNewer("1.0.0", "1.0.0"));
        Assert.True(UpdateCheckService.IsVersionNewer("1.0.0", "beta-2.0.0"));

        // Release Candidate (RC) transitions
        Assert.True(UpdateCheckService.IsVersionNewer("1.0.0-beta.4", "1.0.0-rc.1"));
        Assert.True(UpdateCheckService.IsVersionNewer("1.0.0-rc.1", "1.0.0-rc.2"));
        Assert.True(UpdateCheckService.IsVersionNewer("1.0.0-rc.1", "1.0.0"));
        Assert.False(UpdateCheckService.IsVersionNewer("1.0.0", "1.0.0-rc.1"));
        Assert.False(UpdateCheckService.IsVersionNewer("1.0.0-rc.2", "1.0.0-rc.1"));
        Assert.False(UpdateCheckService.IsVersionNewer("1.0.0-rc.1", "1.0.0-rc.1"));
        Assert.True(UpdateCheckService.IsVersionNewer("v1.0.0-rc.1", "v1.0.0-rc.2"));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_NewerReleaseAvailable_ReturnsUpdateAvailable()
    {
        string json = """
        [
          {
            "tag_name": "v99.0.0",
            "html_url": "https://github.com/Nicconike/LocalTelemetry/releases/tag/v99.0.0",
            "body": "New release features",
            "published_at": "2026-08-15T00:00:00Z",
            "prerelease": false,
            "draft": false
          }
        ]
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        var handler = new MockHttpMessageHandler(response);
        using var client = new HttpClient(handler);

        var cfg = new AppSettings();
        var service = new UpdateCheckService(cfg, client);

        var result = await service.CheckForUpdatesAsync(force: true);

        Assert.True(result.IsUpdateAvailable);
        Assert.False(result.IsPrerelease);
        Assert.Equal("99.0.0", result.LatestVersion);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_Prerelease_FiltersCorrectly()
    {
        string json = """
        [
          {
            "tag_name": "v99.0.0-beta.1",
            "html_url": "https://github.com/Nicconike/LocalTelemetry/releases/tag/v99.0.0-beta.1",
            "body": "Beta release",
            "published_at": "2026-08-15T00:00:00Z",
            "prerelease": true,
            "draft": false
          }
        ]
        """;

        var handlerNoPre = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        using var clientNoPre = new HttpClient(handlerNoPre);

        var cfg = new AppSettings();
        cfg.Updates.IncludePrereleases = false;
        var serviceNoPre = new UpdateCheckService(cfg, clientNoPre);

        var resultNoPre = await serviceNoPre.CheckForUpdatesAsync(force: true);
        Assert.False(resultNoPre.IsUpdateAvailable);
        Assert.Equal("No stable release found", resultNoPre.ErrorMessage);

        var handlerPre = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        using var clientPre = new HttpClient(handlerPre);

        cfg.Updates.IncludePrereleases = true;
        var servicePre = new UpdateCheckService(cfg, clientPre);

        var resultPre = await servicePre.CheckForUpdatesAsync(force: true);
        Assert.True(resultPre.IsUpdateAvailable);
        Assert.True(resultPre.IsPrerelease);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_EmptyReleasesOrInvalidTag_ReturnsError()
    {
        string jsonEmpty = "[]";
        var responseEmpty = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonEmpty, System.Text.Encoding.UTF8, "application/json")
        };
        var clientEmpty = new HttpClient(new MockHttpMessageHandler(responseEmpty));

        var cfg = new AppSettings();
        var serviceEmpty = new UpdateCheckService(cfg, clientEmpty);
        var resEmpty = await serviceEmpty.CheckForUpdatesAsync(force: true);
        Assert.Equal("No releases found", resEmpty.ErrorMessage);

        string jsonNullTag = """[ { "tag_name": "", "prerelease": false, "draft": false } ]""";
        var responseNullTag = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonNullTag, System.Text.Encoding.UTF8, "application/json")
        };
        var clientNullTag = new HttpClient(new MockHttpMessageHandler(responseNullTag));
        var serviceNullTag = new UpdateCheckService(cfg, clientNullTag);
        var resNullTag = await serviceNullTag.CheckForUpdatesAsync(force: true);
        Assert.Equal("No release version found", resNullTag.ErrorMessage);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_SameVersion_ReturnsNoUpdate()
    {
        string currentVer = UpdateCheckService.GetCurrentVersionString();
        string json = $$"""
        [
          {
            "tag_name": "v{{currentVer}}",
            "html_url": "https://github.com/Nicconike/LocalTelemetry/releases/tag/v{{currentVer}}",
            "body": "Same release",
            "published_at": "2026-08-15T00:00:00Z",
            "prerelease": false,
            "draft": false
          }
        ]
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        var handler = new MockHttpMessageHandler(response);
        using var client = new HttpClient(handler);

        var cfg = new AppSettings();
        var service = new UpdateCheckService(cfg, client);

        var result = await service.CheckForUpdatesAsync(force: true);

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(currentVer, result.LatestVersion);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_HttpError_ReturnsErrorMessage()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        var handler = new MockHttpMessageHandler(response);
        using var client = new HttpClient(handler);

        var cfg = new AppSettings();
        var service = new UpdateCheckService(cfg, client);

        var result = await service.CheckForUpdatesAsync(force: true);

        Assert.False(result.IsUpdateAvailable);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ThrowsException_HandledGracefully()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("Network failure"));
        using var client = new HttpClient(handler);

        var cfg = new AppSettings();
        var service = new UpdateCheckService(cfg, client);

        var result = await service.CheckForUpdatesAsync(force: true);

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("Could not connect to GitHub.", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_BackgroundNotDue_SkipsCheck()
    {
        var cfg = new AppSettings();
        cfg.Updates.CheckFrequency = "weekly";
        cfg.Updates.LastCheckUtc = DateTime.UtcNow;

        var service = new UpdateCheckService(cfg);
        var result = await service.CheckForUpdatesAsync(force: false);

        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void IsCheckDue_EvaluatesFrequencyCorrectly()
    {
        var cfg = new AppSettings();

        // Weekly with MinValue last check
        cfg.Updates.CheckFrequency = "weekly";
        cfg.Updates.LastCheckUtc = DateTime.MinValue;
        var service = new UpdateCheckService(cfg);
        Assert.True(service.IsCheckDue());

        // Weekly due (8 days ago) vs not due (2 days ago)
        cfg.Updates.LastCheckUtc = DateTime.UtcNow.AddDays(-8);
        Assert.True(service.IsCheckDue());

        cfg.Updates.LastCheckUtc = DateTime.UtcNow.AddDays(-2);
        Assert.False(service.IsCheckDue());

        // Monthly due (35 days ago) vs not due (10 days ago)
        cfg.Updates.CheckFrequency = "monthly";
        cfg.Updates.LastCheckUtc = DateTime.UtcNow.AddDays(-35);
        Assert.True(service.IsCheckDue());

        cfg.Updates.LastCheckUtc = DateTime.UtcNow.AddDays(-10);
        Assert.False(service.IsCheckDue());

        // Manual
        cfg.Updates.CheckFrequency = "manual";
        Assert.False(service.IsCheckDue());
    }

    [Fact]
    public void IsVersionNewer_NumericVsAlphaAndBuildMetadata_Coverage()
    {
        // Numeric vs alphanumeric prerelease identifiers
        Assert.True(UpdateCheckService.IsVersionNewer("1.0.0-1", "1.0.0-alpha"));
        Assert.False(UpdateCheckService.IsVersionNewer("1.0.0-alpha", "1.0.0-1"));

        // Build metadata (+) stripped properly
        Assert.True(UpdateCheckService.IsVersionNewer("v1.0.0+build1", "v1.1.0+build2"));
        Assert.False(UpdateCheckService.IsVersionNewer("v1.1.0+build1", "v1.0.0+build2"));
    }
}
