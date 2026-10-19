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
    internal static string PipeName { get; set; } = "LocalTelemetryNotifier";

    internal static int ReconnectDelayMs { get; set; } = 1000;
    internal static Func<INotifierPipeServer>? PipeServerFactory { get; set; }
    internal static Func<string, string, ToastForm> ToastFactory { get; set; } = static (title, body) => new ToastForm(title, body);

    [STAThread]
    private static void Main(string[] args)
    {
        // Per-monitor V2 DPI awareness must be set before any HWND is created,
        // otherwise toast forms are bitmap-stretched after a display scale change.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                NotifierLog.Error(ex, "Unhandled AppDomain exception in Notifier process");
        };

        string logPath = ResolveLogPath();
        NotifierLog.Init(logPath);
        if (NotifierLog.LastError is not null)
        {
            // Log file initialization failed
            Debug.WriteLine($"NotifierLog.Init failed for '{logPath}': {NotifierLog.LastError}");
        }

        int parentPid = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 0;

        if (parentPid > 0)
            NotifierLog.Info($"Started (parent PID: {parentPid})");
        else
            NotifierLog.Warn("Started without parent PID - standalone mode");

        using var shutdownCts = new CancellationTokenSource();

        SetupParentMonitoring(parentPid, shutdownCts);

        // Hidden form to provide Windows message pump (required for WinForms/WPF)
        using var hiddenForm = new HiddenForm();
        hiddenForm.Load += (_, _) =>
        {
            _ = Task.Run(() => RunPipeServerAsync(shutdownCts, (title, body) =>
                hiddenForm.BeginInvoke(() => ShowToast(title, body))));
        };
        hiddenForm.FormClosing += (_, _) => NotifierLog.Info("Message pump exiting");

        SetupShutdownListener(hiddenForm, shutdownCts);

        Application.Run(hiddenForm);
        NotifierLog.Info("Exited");
        NotifierLog.Shutdown();
    }

    private static void SetupParentMonitoring(int parentPid, CancellationTokenSource shutdownCts)
    {
        if (parentPid <= 0) return;

        _ = Task.Run(() => MonitorParentProcessAsync(
            async () =>
            {
                try
                {
                    using var parent = Process.GetProcessById(parentPid);
                    await parent.WaitForExitAsync(shutdownCts.Token).ConfigureAwait(false);
                }
                catch (ArgumentException) { NotifierLog.Warn("Parent process not found (already exited)"); }
                catch (OperationCanceledException)
                {
                    NotifierLog.Debug("Parent process exit wait cancelled");
                }
            },
            shutdownCts), shutdownCts.Token);
    }

    private static void SetupShutdownListener(HiddenForm hiddenForm, CancellationTokenSource shutdownCts)
    {
        _ = Task.Run(() =>
        {
            shutdownCts.Token.WaitHandle.WaitOne();
            if (hiddenForm.IsHandleCreated)
                hiddenForm.BeginInvoke(() => hiddenForm.Close());
            else
                hiddenForm.Load += (_, _) => hiddenForm.Close();
        }, CancellationToken.None);
    }

    /// <summary>Resolves the notifier log path using the same data-dir rule as <c>AppSettings.InitPaths</c>.</summary>
    private static string ResolveLogPath()
    {
        string exeDir = AppContext.BaseDirectory;
        string markerFile = Path.Combine(exeDir, "app.mode");
        string dataDir = File.Exists(markerFile)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalTelemetry")
            : exeDir;
        return Path.Combine(dataDir, "lt_notifier.log");
    }

    internal static async Task MonitorParentProcessAsync(Func<Task> waitForParentExit, CancellationTokenSource cts)
    {
        try
        {
            await waitForParentExit();
        }
        catch (ArgumentException) { NotifierLog.Warn("Parent process not found (already exited)"); }
        NotifierLog.Info("Parent exited, shutting down");
        await cts.CancelAsync();
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
            PipeName, PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly));
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
                    NotifierLog.Info($"Displaying toast '{msg.Title}' - '{msg.Body}'");
                    toastHandler(msg.Title ?? "LocalTelemetry", msg.Body ?? string.Empty);
                }
                else if (msg.Action == "Shutdown")
                {
                    NotifierLog.Info("Shutdown requested");
                    await cts.CancelAsync().ConfigureAwait(false);
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
            NotifierLog.Error(ex, "Failed to display toast notification");
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
