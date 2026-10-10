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
public class NotificationClient : IDisposable
{
    private readonly string _pipeName;
    private readonly int _connectTimeoutMs;
    private const int MaxRetries = 3;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public NotificationClient() : this("LocalTelemetryNotifier", 5000) { }

    internal NotificationClient(string pipeName, int connectTimeoutMs)
    {
        _pipeName = pipeName;
        _connectTimeoutMs = connectTimeoutMs;
    }

    // Public API
    /// <summary>
    /// Sends a toast notification to the notifier process.
    /// </summary>
    /// <param name="title">Toast display title.</param>
    /// <param name="body">Toast body text.</param>
    /// <param name="tag">Optional tag for notification grouping/deduplication.</param>
    /// <param name="cancellationToken">Cancellation token for the pipe operation.</param>
    public virtual async Task SendToastAsync(string title, string body, string? tag = null, CancellationToken cancellationToken = default)
    {
        var msg = new NotificationMessage
        {
            Action = "ShowToast",
            Title = title,
            Body = body,
            Tag = tag
        };

        await SendAsync(msg, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sends a shutdown command to the notifier process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the pipe operation.</param>
    public virtual async Task SendShutdownAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(new NotificationMessage { Action = "Shutdown" }, timeoutMs: 500, maxRetries: 1, cancellationToken: cancellationToken);
    }

    // Pipe Communication
    private async Task SendAsync(NotificationMessage msg, int? timeoutMs = null, int maxRetries = MaxRetries, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        int timeout = timeoutMs ?? _connectTimeoutMs;
        try
        {
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try
                {
                    await pipe.ConnectAsync(timeout, cancellationToken);

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
                        Log.Warn("Notification service connection timed out; retrying...");
                        await Task.Delay(1000, cancellationToken);
                    }
                    else
                    {
                        Log.Error("Failed to connect to notification service (service may not be running)");
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (msg.Action == "Shutdown" && ex is System.IO.IOException) return;
                    Log.Error(ex, "Failed to transmit message to notification service");
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
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lock.Dispose();
        }
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
