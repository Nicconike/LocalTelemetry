using System.IO;
using System.IO.Pipes;
using System.Text;
using FluentAssertions;
using LocalTelemetry.App.Services;
using Xunit;

namespace LocalTelemetry.App.Tests.Services;

public class NotificationClientTests
{
    [Fact]
    public void NotificationClient_Dispose_ReleasesResources()
    {
        var client = new NotificationClient();
        var act = () => client.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task NotificationClient_SendToastAsync_FastTimeoutRetries()
    {
        // Pass 1ms connect timeout so attempt 0 and 1 retries execute in ~2ms total
        using var client = new NotificationClient("NonExistentPipe_" + Guid.NewGuid().ToString("N"), 1);

        var act = async () => await client.SendToastAsync("Test Title", "Test Body", "Tag1");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotificationClient_SendShutdownAsync_FastTimeoutRetries()
    {
        using var client = new NotificationClient("NonExistentPipe_" + Guid.NewGuid().ToString("N"), 1);

        var act = async () => await client.SendShutdownAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotificationClient_SendToastAsync_HandlesOperationCanceledException()
    {
        using var client = new NotificationClient("NonExistentPipe_" + Guid.NewGuid().ToString("N"), 1000);
        using var cts = new CancellationTokenSource(1); // Cancel immediately

        var act = async () => await client.SendToastAsync("Title", "Body", null, cts.Token);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotificationClient_SendToastAsync_HandlesLockAcquisitionCancellation()
    {
        using var client = new NotificationClient("NonExistentPipe_" + Guid.NewGuid().ToString("N"), 1000);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Already canceled token before entering wait

        var act = async () => await client.SendToastAsync("Title", "Body", null, cts.Token);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotificationClient_SendToastAsync_SuccessfulPipeCommunication()
    {
        string pipeName = "TestPipe_" + Guid.NewGuid().ToString("N");
        string? received = null;

        using var serverReady = new ManualResetEventSlim(false);
        var serverTask = Task.Run(async () =>
        {
            using var server = new NamedPipeServerStream(pipeName, PipeDirection.In);
            serverReady.Set();
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, Encoding.UTF8);
            received = await reader.ReadLineAsync();
        });

        if (serverReady.Wait(TimeSpan.FromSeconds(2)))
        {
            using var client = new NotificationClient(pipeName, 2000);
            await client.SendToastAsync("Test Title", "Test Body", "TestTag");
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2));

            received.Should().NotBeNullOrEmpty();
            received.Should().Contain("ShowToast");
            received.Should().Contain("Test Title");
            received.Should().Contain("Test Body");
        }
    }

    [Fact]
    public async Task NotificationClient_SendShutdownAsync_SuccessfulPipeCommunication()
    {
        string pipeName = "TestShutdownPipe_" + Guid.NewGuid().ToString("N");
        string? received = null;

        using var serverReady = new ManualResetEventSlim(false);
        var serverTask = Task.Run(async () =>
        {
            using var server = new NamedPipeServerStream(pipeName, PipeDirection.In);
            serverReady.Set();
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, Encoding.UTF8);
            received = await reader.ReadLineAsync();
        });

        if (serverReady.Wait(TimeSpan.FromSeconds(2)))
        {
            using var client = new NotificationClient(pipeName, 2000);
            await client.SendShutdownAsync();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2));

            received.Should().NotBeNullOrEmpty();
            received.Should().Contain("Shutdown");
        }
    }

    [Fact]
    public void NotificationClient_MultipleDispose_DoesNotThrow()
    {
        var client = new NotificationClient();
        client.Dispose();
        var act = () => client.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task NotificationClient_ExecuteAttemptAsync_UnexpectedException_HandlesGracefully()
    {
        using var client = new NotificationClient();
        var method = typeof(NotificationClient).GetMethod("ExecuteAttemptAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Should().NotBeNull();

        var msgType = typeof(NotificationClient).GetNestedType("NotificationMessage", System.Reflection.BindingFlags.NonPublic)!;
        var msg = Activator.CreateInstance(msgType)!;
        msgType.GetProperty("Action")?.SetValue(msg, "ShowToast");

        // Passing invalid negative timeout (-5) causes ConnectAsync to throw ArgumentOutOfRangeException, hitting catch (Exception ex)
        var task = (Task)method!.Invoke(client, [msg, 0, -5, CancellationToken.None])!;
        await task;
    }
}
