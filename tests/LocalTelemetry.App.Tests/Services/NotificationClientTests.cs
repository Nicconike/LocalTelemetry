using LocalTelemetry.App.Services;
using Xunit;

namespace LocalTelemetry.App.Tests.Services;

public class NotificationClientTests
{
    [Fact]
    public async Task NotificationClient_SendToastAsync_HandlesNoPipeServerGracefully()
    {
        using var client = new NotificationClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await client.SendToastAsync("Test Title", "Test Body", "Tag1", cts.Token);
    }

    [Fact]
    public async Task NotificationClient_SendShutdownAsync_HandlesNoPipeServerGracefully()
    {
        using var client = new NotificationClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await client.SendShutdownAsync(cts.Token);
    }
}
