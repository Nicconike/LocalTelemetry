using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace LocalTelemetry.Notifier;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private const string PipeName = "LocalTelemetryNotifier";

    internal static int ReconnectDelayMs = 1000;
    internal static Func<INotifierPipeServer>? PipeServerFactory;
    internal static Func<string, string, ToastForm> ToastFactory = static (title, body) => new ToastForm(title, body);

    [STAThread]
    private static void Main(string[] args)
    {
        int parentPid = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 0;

        if (parentPid > 0)
            NotifierLog.Info($"Started (parent PID: {parentPid})");
        else
            NotifierLog.Warn("Started without parent PID - standalone mode");

        using var shutdownCts = new CancellationTokenSource();

        // Monitor parent process
        if (parentPid > 0)
        {
            _ = Task.Run(() => MonitorParentProcessAsync(
                () =>
                {
                    try
                    {
                        using var parent = Process.GetProcessById(parentPid);
                        parent.WaitForExit();
                    }
                    catch (ArgumentException) { NotifierLog.Warn("Parent process not found (already exited)"); }
                    return Task.CompletedTask;
                },
                shutdownCts));
        }

        // Hidden form to provide Windows message pump (required for WinForms/WPF)
        using var hiddenForm = new HiddenForm();
        hiddenForm.Load += (_, _) =>
        {
            _ = Task.Run(() => RunPipeServerAsync(shutdownCts, (title, body) =>
                hiddenForm.BeginInvoke(() => ShowToast(title, body))));
        };
        hiddenForm.FormClosing += (_, _) => NotifierLog.Info("Message pump exiting");

        _ = Task.Run(() =>
        {
            try { shutdownCts.Token.WaitHandle.WaitOne(); }
            catch (OperationCanceledException) { }
            hiddenForm.BeginInvoke(() => hiddenForm.Close());
        });

        Application.Run(hiddenForm);
        NotifierLog.Info("Exited");
    }

    internal static async Task MonitorParentProcessAsync(Func<Task> waitForParentExit, CancellationTokenSource cts)
    {
        try
        {
            await waitForParentExit();
        }
        catch (ArgumentException) { NotifierLog.Warn("Parent process not found (already exited)"); }
        NotifierLog.Info("Parent exited, shutting down");
        cts.Cancel();
    }

    internal static async Task RunPipeServerAsync(CancellationTokenSource cts, Action<string, string> toastDispatcher)
    {
        CancellationToken ct = cts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = CreatePipeServer();

                NotifierLog.Info("Waiting for connection...");
                await server.WaitForConnectionAsync(ct);
                NotifierLog.Info("Client connected");

                using var reader = new StreamReader(server.Stream, Encoding.UTF8);
                await ProcessMessageStreamAsync(reader, cts, toastDispatcher);

                NotifierLog.Info("Client disconnected");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                NotifierLog.Error(ex, "Pipe server error");
                if (!ct.IsCancellationRequested)
                {
                    NotifierLog.Warn($"Re-establishing pipe server in {ReconnectDelayMs}ms...");
                    await Task.Delay(ReconnectDelayMs, ct);
                }
            }
        }
    }

    private static INotifierPipeServer CreatePipeServer()
    {
        if (PipeServerFactory is not null)
            return PipeServerFactory();

        return new NamedPipeServerStreamWrapper(new NamedPipeServerStream(
            PipeName, PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous));
    }

    internal interface INotifierPipeServer : IDisposable
    {
        Task WaitForConnectionAsync(CancellationToken ct);

        Stream Stream { get; }
    }

    private sealed class NamedPipeServerStreamWrapper : INotifierPipeServer
    {
        private readonly NamedPipeServerStream _inner;

        public NamedPipeServerStreamWrapper(NamedPipeServerStream inner) => _inner = inner;

        public Stream Stream => _inner;

        public Task WaitForConnectionAsync(CancellationToken ct) => _inner.WaitForConnectionAsync(ct);

        public void Dispose() => _inner.Dispose();
    }

    internal static async Task ProcessMessageStreamAsync(TextReader reader, CancellationTokenSource cts, Action<string, string> toastHandler)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false)) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                NotifierLog.Warn("Received empty pipe payload");
                continue;
            }

            try
            {
                var msg = JsonSerializer.Deserialize<NotificationMessage>(line);
                if (msg is null)
                {
                    NotifierLog.Warn("Deserialized notification message was null");
                    continue;
                }

                if (msg.Action == "ShowToast")
                {
                    NotifierLog.Info($"Toast: {msg.Title} - {msg.Body}");
                    toastHandler(msg.Title ?? "LocalTelemetry", msg.Body ?? string.Empty);
                }
                else if (msg.Action == "Shutdown")
                {
                    NotifierLog.Info("Shutdown requested");
                    cts.Cancel();
                    return;
                }
                else
                {
                    NotifierLog.Warn($"Unknown action received: '{msg.Action}'");
                }
            }
            catch (JsonException ex)
            {
                NotifierLog.Error(ex, "Invalid message JSON");
            }
        }
    }

    internal static void ShowToast(string title, string body)
    {
        try
        {
            var toast = ToastFactory(title, body);
            toast.Show();
        }
        catch (Exception ex)
        {
            NotifierLog.Error(ex, "ShowToast failed");
        }
    }

    // Message contract
    internal sealed record NotificationMessage
    {
        public string Action { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Body { get; set; }
    }

    // Hidden form (excluded from Alt+Tab via WS_EX_TOOLWINDOW)
    internal sealed class HiddenForm : Form
    {
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        public HiddenForm()
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            Opacity = 0;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                return cp;
            }
        }
    }
}
