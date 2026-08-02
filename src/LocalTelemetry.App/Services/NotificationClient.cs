using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using LocalTelemetry.Core.Diagnostics;

namespace LocalTelemetry.App.Services;

/// <summary>
/// Named pipe client that sends toast notification requests to the
/// non-elevated <c>LocalTelemetry.Notifier</c> helper process.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NotificationClient : IDisposable
{
    private const string PipeName = "LocalTelemetryNotifier";
    private const int ConnectTimeoutMs = 5000;
    private const int MaxRetries = 3;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Public API
    /// <summary>
    /// Sends a toast notification to the notifier process.
    /// </summary>
    /// <param name="title">Toast display title.</param>
    /// <param name="body">Toast body text.</param>
    /// <param name="tag">Optional tag for notification grouping/deduplication.</param>
    /// <param name="cancellationToken">Cancellation token for the pipe operation.</param>
    public async Task SendToastAsync(string title, string body, string? tag = null, CancellationToken cancellationToken = default)
    {
        var msg = new NotificationMessage
        {
            Action = "ShowToast",
            Title = title,
            Body = body,
            Tag = tag
        };

        await SendAsync(msg, cancellationToken);
    }

    /// <summary>
    /// Sends a shutdown command to the notifier process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the pipe operation.</param>
    public async Task SendShutdownAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(new NotificationMessage { Action = "Shutdown" }, cancellationToken);
    }

    // Pipe Communication
    private async Task SendAsync(NotificationMessage msg, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                try
                {
                    await pipe.ConnectAsync(ConnectTimeoutMs, cancellationToken);

                    string json = JsonSerializer.Serialize(msg) + "\n";
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    await pipe.WriteAsync(bytes, cancellationToken);
                    await pipe.FlushAsync(cancellationToken);
                    return;
                }
                catch (TimeoutException)
                {
                    if (attempt == 0)
                    {
                        Log.Error("Notifier pipe: connection timeout, retrying...");
                        await Task.Delay(1000, cancellationToken);
                    }
                    else
                    {
                        Log.Error("Notifier pipe: connection timeout (notifier not running?)");
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Notifier pipe: failed");
                    return;
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    // Disposal
    /// <summary>Releases the pipe connection semaphore.</summary>
    public void Dispose()
    {
        _lock.Dispose();
    }

    // DTOs
    private sealed record NotificationMessage
    {
        /// <summary>The command action to perform (e.g. "ShowToast" or "Shutdown").</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>Optional toast title.</summary>
        public string? Title { get; set; }

        /// <summary>Optional toast body text.</summary>
        public string? Body { get; set; }

        /// <summary>Optional tag for notification grouping and deduplication.</summary>
        public string? Tag { get; set; }
    }
}
