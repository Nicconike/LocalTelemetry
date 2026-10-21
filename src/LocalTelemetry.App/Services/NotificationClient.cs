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

    private enum AttemptResult
    {
        Success,
        Retry,
        Stop
    }

    // Pipe Communication
    private async Task SendAsync(NotificationMessage msg, int? timeoutMs = null, int maxRetries = MaxRetries, CancellationToken cancellationToken = default)
    {
        try
        {
            await _lock.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        int timeout = timeoutMs ?? _connectTimeoutMs;
        try
        {
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                var result = await ExecuteAttemptAsync(msg, attempt, timeout, cancellationToken);
                if (result != AttemptResult.Retry)
                {
                    return;
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<AttemptResult> ExecuteAttemptAsync(NotificationMessage msg, int attempt, int timeout, CancellationToken cancellationToken)
    {
        using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(timeout, cancellationToken);

            string json = JsonSerializer.Serialize(msg) + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await pipe.WriteAsync(bytes, cancellationToken);
            await pipe.FlushAsync(cancellationToken);
            return AttemptResult.Success;
        }
        catch (TimeoutException)
        {
            if (attempt == 0)
            {
                Log.Warn("Notification service connection timed out; retrying...");
                await Task.Delay(1000, cancellationToken);
                return AttemptResult.Retry;
            }
            Log.Error("Failed to connect to notification service (service may not be running)");
            return AttemptResult.Stop;
        }
        catch (OperationCanceledException)
        {
            return AttemptResult.Stop;
        }
        catch (Exception ex)
        {
            if (msg.Action != "Shutdown" || ex is not System.IO.IOException)
            {
                Log.Error(ex, "Failed to transmit message to notification service");
            }
            return AttemptResult.Stop;
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
