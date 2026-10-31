using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using LocalTelemetry.App.Services;
using LocalTelemetry.App.Win32;
using LocalTelemetry.Core.Models;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Diagnostics;
using LocalTelemetry.Core.Hardware;
using Microsoft.Web.WebView2.Core;

namespace LocalTelemetry.App.Settings;

[SupportedOSPlatform("windows")]
public sealed partial class SettingsShell : Window
{
    /// <summary>Fires after settings are saved from the UI.</summary>
    public event Action? SettingsApplied;

    internal static Action<string>? PostJsonActionOverride { get; set; }
    internal static Action<string>? UrlLauncherAction { get; set; }
    internal static Action<ProcessStartInfo>? UrlProcessLauncher { get; set; }
    internal static Action<string>? FolderLauncherAction { get; set; }
    internal static Action<ProcessStartInfo>? FolderProcessLauncher { get; set; }
    internal static Action<string>? ScriptExecutorOverride { get; set; }
    internal static Func<string, Task>? ScriptExecutorAsyncOverride { get; set; }
    internal static bool SkipWebView2InitForTesting { get; set; }
    internal static bool SkipInitializeComponentForTesting { get; set; }
    internal static bool SkipShowForTesting { get; set; }
    internal static bool SkipDragMoveForTesting { get; set; }
    internal static bool ThrowInUpdateCheckForTesting { get; set; }
    internal static bool ThrowInBuildDateForTesting { get; set; }
    internal static bool ThrowInWebMessageForTesting { get; set; }
    internal static bool ThrowInImportWindowsUsageForTesting { get; set; }
    internal static bool ThrowInSetWindowIconForTesting { get; set; }
    internal static Action<string, string>? MessageBoxAction { get; set; }
    internal static Func<string, string, MessageBoxResult>? MessageBoxFunc { get; set; }
    internal Task? LastImportTask { get; private set; }

    public new void Show()
    {
        if (SkipShowForTesting) return;
        base.Show();
    }

    public new bool Activate()
    {
        if (SkipShowForTesting) return true;
        return base.Activate();
    }

    private const string SettingsHost = "localtelemetry.settings";
    private const string SettingsEntryPoint = "index.html";
    private const string PayloadProp = "payload";
    private const string NvidiaVendor = "NVIDIA";
    private const string EnabledStr = "enabled";
    private const string DisabledStr = "disabled";

    private readonly AppSettings _cfg;
    private readonly string _wwwRoot;
    private readonly UpdateCheckService _updateChecker;
    private bool _webViewReady;

    // NIC list caching and real-time updates
    private List<string>? _lastNics;
    private System.Threading.Timer? _nicDebounceTimer;

    // Traffic today push timer (5s interval)
    private System.Threading.Timer? _trafficTodayTimer;

    // App resource usage (CPU/RAM) on window title
    private System.Windows.Threading.DispatcherTimer? _appUsageTimer;
    private TimeSpan _lastProcessorTime;
    private DateTime _lastUsageCheckTime;
    private Process? _currentProcess;

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly JsonSerializerOptions JsonIn = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Construction
    /// <summary>Optional provider for taskbar slot constrained status and width.</summary>
    public Func<(bool constrained, int availableWidth)>? TaskbarSlotStatusProvider { get; set; }

    public SettingsShell(AppSettings cfg, NetUsageLogger? _ = null)
    {
        if (!SkipInitializeComponentForTesting)
        {
            InitializeComponent();
        }
        _cfg = cfg;
        _updateChecker = new UpdateCheckService(cfg);
        _wwwRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");

        SetWindowIcon();

        Loaded += OnLoaded;
        // Close WebView2 select dropdowns when the window is moved
        // (WebView2 native popups render at stale screen coordinates after move)
        LocationChanged += (_, _) => HandleWindowLocationChanged();
    }

    private void SetWindowIcon()
    {
        try
        {
            if (ThrowInSetWindowIconForTesting)
                throw new InvalidOperationException("Simulated window icon failure");

            string? exePath = Environment.ProcessPath;
            if (exePath is not null)
            {
                using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (sysIcon is not null)
                {
                    Icon = Imaging.CreateBitmapSourceFromHIcon(
                        sysIcon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
            }
        }
        catch (Exception ex) { Log.Error(ex, "Window icon extraction failed"); }
    }

    private void HandleWindowLocationChanged()
    {
        if (_webViewReady && (ScriptExecutorOverride is not null || ScriptExecutorAsyncOverride is not null || WebView?.CoreWebView2 is not null))
        {
            try
            {
                if (ScriptExecutorOverride is not null)
                {
                    ScriptExecutorOverride("try{document.activeElement?.blur();}catch(e){console.warn('WebView2 blur failed',e);}");
                }
                else if (ScriptExecutorAsyncOverride is not null)
                {
                    ScriptExecutorAsyncOverride("try{document.activeElement?.blur();}catch(e){console.warn('WebView2 blur failed',e);}");
                }
                else
                {
                    WebView!.CoreWebView2.ExecuteScriptAsync("try{document.activeElement?.blur();}catch(e){console.warn('WebView2 blur failed',e);}");
                }
            }
            catch (Exception ex) { Log.Error(ex, "WebView2 blur script failed after window move"); }
        }
    }

    // Use Loaded event so the WPF visual tree is ready.
    // Guard with _webViewReady so re-showing the window doesn't re-init.
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_appUsageTimer == null)
        {
            _currentProcess = Process.GetCurrentProcess();
            _lastProcessorTime = _currentProcess.TotalProcessorTime;
            _lastUsageCheckTime = DateTime.UtcNow;

            _appUsageTimer = new System.Windows.Threading.DispatcherTimer();
            _appUsageTimer.Interval = TimeSpan.FromSeconds(2);
            _appUsageTimer.Tick += (s, ev) =>
            {
                if (_currentProcess == null) return;
                try
                {
                    _currentProcess.Refresh();
                    var cpuTime = _currentProcess.TotalProcessorTime;
                    var now = DateTime.UtcNow;
                    var timeDelta = now - _lastUsageCheckTime;
                    var cpuDelta = cpuTime - _lastProcessorTime;

                    _lastProcessorTime = cpuTime;
                    _lastUsageCheckTime = now;

                    double cpuPct = 0;
                    if (timeDelta.TotalMilliseconds > 0)
                    {
                        cpuPct = cpuDelta.TotalMilliseconds / timeDelta.TotalMilliseconds * 100.0 / Environment.ProcessorCount;
                    }

                    WindowTitleText.Text = $"LocalTelemetry ({cpuPct:0.0}% CPU)";
                }
                catch (Exception ex) { Log.Warn(ex, "App usage title update failed"); }
            };
            _appUsageTimer.Start();
        }

        if (_webViewReady) return;
        if (SkipWebView2InitForTesting)
        {
            SetupTimersAndSubscriptions();
            return;
        }
        await InitWebViewAsync();
    }

    private void SetupTimersAndSubscriptions()
    {
        // Subscribe to network change events for real-time NIC dropdown updates.
        _nicDebounceTimer = new System.Threading.Timer(
            _ => Dispatcher.Invoke(CheckAndSendNics));
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

        // Start periodic push of today's traffic totals (every 5 seconds).
        _trafficTodayTimer = new System.Threading.Timer(
            _ => Dispatcher.Invoke(SendTrafficToday),
            null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        TrafficHistoryStore.HistoryUpdated += OnHistoryUpdated;

        _webViewReady = true;
        SendTrafficToday();
        SendTrafficMonths();
        SendAllTrafficHistory();
    }

    // WebView2 init
    private async Task InitWebViewAsync()
    {
        try
        {
            string exeDir = AppContext.BaseDirectory;
            string markerFile = Path.Combine(exeDir, "app.mode");
            bool standardMode = File.Exists(markerFile);

            string dataDir = standardMode
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalTelemetry", "webview2_data")
                : Path.Combine(exeDir, "webview2_data");

            var envOptions = new CoreWebView2EnvironmentOptions
            {
                Language = "en-GB",
                AdditionalBrowserArguments = "--disable-features=EyeDropper --force-dark-mode",
            };
            CoreWebView2Environment env =
                await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: dataDir,
                    options: envOptions);

            await WebView.EnsureCoreWebView2Async(env);
            WebView.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;

            CoreWebView2Settings wv = WebView.CoreWebView2.Settings;
            wv.IsWebMessageEnabled = true;
#if DEBUG
            wv.AreDevToolsEnabled = true;
#else
            wv.AreDevToolsEnabled = false;
#endif
            wv.AreDefaultContextMenusEnabled = false;
            wv.IsStatusBarEnabled = false;

            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                hostName: SettingsHost,
                folderPath: _wwwRoot,
                accessKind: CoreWebView2HostResourceAccessKind.DenyCors);

            // Check wwwroot exists before navigating
            string indexPath = Path.Combine(_wwwRoot, SettingsEntryPoint);
            if (!File.Exists(indexPath))
            {
                // Show inline error page instead of crashing
                WebView.CoreWebView2.NavigateToString(
                    "<html><body style='background:#1c1b19;color:#cdccca;font-family:Segoe UI;padding:2rem'>" +
                    "<h2>Settings UI not built</h2>" +
                    "<p>Run <code>bun run build</code> inside <code>Settings/wwwroot</code> first.</p>" +
                    "<p>Expected: <code>" + indexPath.Replace(@"\\", "/") + "</code></p>" +
                    "</body></html>");
            }
            else
            {
                WebView.Source = new Uri($"https://{SettingsHost}/{SettingsEntryPoint}");
            }

            SetupTimersAndSubscriptions();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Settings window failed to initialise WebView2");
            if (MessageBoxAction is not null)
            {
                MessageBoxAction("LocalTelemetry - Settings Error", ex.Message);
            }
            else if (MessageBoxFunc is not null)
            {
                MessageBoxFunc(ex.Message, "LocalTelemetry - Settings Error");
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"Settings window failed to initialise WebView2:\n\n{ex.Message}\n\n" +
                    "Install the WebView2 Runtime from:\nhttps://developer.microsoft.com/en-us/microsoft-edge/webview2/",
                    "LocalTelemetry - Settings Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            Hide();
        }
    }

    internal static bool IsValidMessageSource(string? source)
    {
        return !string.IsNullOrWhiteSpace(source) &&
               source.StartsWith($"https://{SettingsHost}", StringComparison.OrdinalIgnoreCase);
    }

    // Inbound message handler
    internal void ProcessWebMessage(string? source, string json)
    {
        if (!IsValidMessageSource(source))
        {
            Log.Warn($"Rejected web message from untrusted source '{source}'");
            return;
        }

        HandleWebMessageJson(json);
    }

    private void OnWebMessageReceived(
        object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        ProcessWebMessage(e.Source, e.WebMessageAsJson);
    }

    internal void HandleWebMessageJson(string json)
    {
        try
        {
            if (ThrowInWebMessageForTesting)
                throw new InvalidOperationException("Simulated web message error");

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string type = root.TryGetProperty("type", out JsonElement t)
                ? t.GetString() ?? string.Empty
                : string.Empty;

            if (type is not ("getSettings" or "getNics" or "getSystemInfo" or "getTrafficMonths" or "getTrafficHistoryAll" or "getHistoryMonths" or "getHistoryData"))
            {
                Log.Debug($"Received settings command '{type}'");
            }

            switch (type)
            {
                case "getSettings": SendSettings(); break;
                case "saveSettings":
                    if (root.TryGetProperty(PayloadProp, out JsonElement payload))
                        ApplySettings(payload);
                    break;
                case "getNics": SendNics(); break;
                case "getTrafficMonths": SendTrafficMonths(); break;
                case "getSystemInfo": SendSystemInfo(); break;
                case "openUrl": HandleOpenUrl(root); break;
                case "openFolder": HandleOpenFolder(root); break;
                case "getTrafficHistoryAll": SendAllTrafficHistory(); break;
                case "importDat": HandleImportJson(json); break;
                case "importWindowsUsage": LastImportTask = HandleImportWindowsUsage(root); break;
                case "checkForUpdates": HandleCheckForUpdatesMsg(root); break;
                case "log": HandleFrontendLog(root); break;
                default: break;
            }
        }
        catch (JsonException ex) { Log.Error(ex, "Failed to parse incoming web message JSON"); }
        catch (Exception ex) { Log.Error(ex, "An error occurred while handling incoming web message"); }
    }

    private static void HandleFrontendLog(JsonElement root)
    {
        if (!root.TryGetProperty(PayloadProp, out JsonElement payload)) return;
        string level = payload.TryGetProperty("level", out JsonElement l) ? l.GetString() ?? "error" : "error";
        string message = payload.TryGetProperty("message", out JsonElement m) ? m.GetString() ?? string.Empty : string.Empty;
        string? detail = payload.TryGetProperty("detail", out JsonElement d) ? d.GetString() : null;

        string fullMsg = string.IsNullOrWhiteSpace(detail) ? message : $"{message} | {detail}";

        if (level.Equals("warn", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warn(fullMsg);
        }
        else
        {
            Log.Error(fullMsg);
        }
    }

    private void HandleOpenUrl(JsonElement root)
    {
        if (!root.TryGetProperty(PayloadProp, out JsonElement urlPayload)
            || !urlPayload.TryGetProperty("url", out JsonElement urlEl))
        {
            return;
        }

        string url = urlEl.GetString() ?? string.Empty;
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUri) &&
           (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps || parsedUri.Scheme == "ms-settings"))
        {
            Log.Info($"Opened external URI ({parsedUri.AbsoluteUri})");
            if (UrlLauncherAction is not null)
            {
                UrlLauncherAction(parsedUri.AbsoluteUri);
                return;
            }

            var psi = new ProcessStartInfo { FileName = parsedUri.AbsoluteUri, UseShellExecute = true };
            if (UrlProcessLauncher is not null)
                UrlProcessLauncher(psi);
            else if (!SkipShowForTesting && !SkipWebView2InitForTesting)
                Process.Start(psi);
        }
        else
        {
            Log.Error($"Rejecting invalid openUrl request '{url}'");
        }
    }

    private static void HandleOpenFolder(JsonElement root)
    {
        if (!root.TryGetProperty(PayloadProp, out JsonElement folderPayload)
            || !folderPayload.TryGetProperty("target", out JsonElement targetEl))
        {
            return;
        }

        string target = targetEl.GetString() ?? string.Empty;
        string dirToOpen;
        string folderLabel;

        if (target.Equals("app", StringComparison.OrdinalIgnoreCase))
        {
            dirToOpen = AppSettings.ExeDir;
            folderLabel = "application";
        }
        else if (target.Equals("logs", StringComparison.OrdinalIgnoreCase))
        {
            dirToOpen = AppSettings.LogsDir;
            folderLabel = "logs";
        }
        else
        {
            dirToOpen = AppSettings.DataDir;
            folderLabel = "data";
        }

        if (!string.IsNullOrEmpty(dirToOpen) && Directory.Exists(dirToOpen))
        {
            Log.Info($"Opened {folderLabel} folder in Windows Explorer ({dirToOpen})");
            if (FolderLauncherAction is not null)
            {
                FolderLauncherAction(dirToOpen);
                return;
            }

            string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            var psi = new ProcessStartInfo
            {
                FileName = explorerPath,
                Arguments = $"\"{dirToOpen}\"",
                UseShellExecute = true
            };
            if (FolderProcessLauncher is not null)
                FolderProcessLauncher(psi);
            else if (!SkipShowForTesting && !SkipWebView2InitForTesting)
                Process.Start(psi);
        }
    }

    private void HandleCheckForUpdatesMsg(JsonElement root)
    {
        bool force = root.TryGetProperty(PayloadProp, out JsonElement forcePayload)
            && forcePayload.TryGetProperty("force", out JsonElement forceEl)
            && forceEl.GetBoolean();
        if (force)
        {
            Log.Info("User requested manual software update check");
        }
        _ = HandleCheckForUpdatesAsync(force);
    }

    // Outbound messages
    private void PostJson(string json)
    {
        if (PostJsonActionOverride is not null)
        {
            PostJsonActionOverride(json);
            return;
        }
        var wv = WebView;
        if (!_webViewReady || wv is null || wv.CoreWebView2 is null) return;
        wv.CoreWebView2.PostWebMessageAsJson(json);
    }

    public void SendUpdatedSettings()
    {
        SendSettings();
    }

    /// <summary>
    /// Notifies the settings UI that the taskbar space constraint state or available slot width has changed.
    /// </summary>
    /// <param name="constrained">Whether the taskbar slot is narrower than the overlay width.</param>
    /// <param name="availableWidth">The measured width in pixels available for the overlay.</param>
    public void NotifyTaskbarSlotChanged(bool constrained, int availableWidth)
    {
        var envelope = new
        {
            type = "taskbarSlotChanged",
            payload = new
            {
                taskbarSpaceConstrained = constrained,
                availableSlotWidth = availableWidth
            }
        };
        PostJson(JsonSerializer.Serialize(envelope, JsonOut));
        Log.Debug($"Dispatched taskbar slot constraint update to settings UI (constrained: {constrained}, availableWidth: {availableWidth})");
    }

    private void SendSettings()
    {
        var (constrained, availableWidth) = TaskbarSlotStatusProvider?.Invoke() ?? (false, 0);
        var dto = SettingsDtoMapping.ToDto(_cfg, constrained, availableWidth);
        var json = JsonSerializer.Serialize(new { type = "settings", payload = dto }, JsonOut);
        Log.Debug($"Dispatched configuration to settings UI (overlay visible: {dto.Overlay.Visible}, constrained: {constrained})");
        PostJson(json);
    }

    private void SendNics()
    {
        var nics = GetActiveNics();
        _lastNics = nics;
        var envelope = new { type = "nics", payload = nics };
        PostJson(JsonSerializer.Serialize(envelope, JsonOut));
        Log.Debug($"Dispatched network adapters to settings UI ({nics.Count} found)");
    }

    /// <summary>
    /// Returns the list of active physical network adapters (up + default gateway).
    /// Excludes loopback, tunnel, PPP and slip interfaces.
    /// </summary>
    private static List<string> GetActiveNics()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                     && (n.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                      || n.NetworkInterfaceType == (NetworkInterfaceType)62
                      || n.NetworkInterfaceType == (NetworkInterfaceType)69
                      || n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                      || n.NetworkInterfaceType == (NetworkInterfaceType)117))
            .Select(n => (nic: n, gw: SafeGetGateways(n)))
            .Where(x => x.gw.Count > 0)
            .Select(x => $"{x.nic.Name} ({x.nic.Description})")
            .ToList();
    }

    /// <summary>
    /// Safely retrieves the default gateway addresses for a network interface,
    /// filtering out null and zero-address entries.
    /// </summary>
    private static List<System.Net.IPAddress> SafeGetGateways(NetworkInterface nic)
    {
        try
        {
            var gw = nic.GetIPProperties()?.GatewayAddresses;
            if (gw is null || gw.Count == 0) return [];
            return gw
                .Where(g => g.Address is not null && !g.Address.ToString().StartsWith("0."))
                .Select(g => g.Address)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to get gateway addresses for network interface");
            return [];
        }
    }

    /// <summary>Debounced handler for <see cref="NetworkChange.NetworkAddressChanged"/>.</summary>
    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        _nicDebounceTimer?.Change(1000, Timeout.Infinite);
    }

    /// <summary>Checks if the active NIC list changed and sends an update to the frontend.</summary>
    private void CheckAndSendNics()
    {
        if (!_webViewReady) return;

        var current = GetActiveNics();
        if (_lastNics is null || !_lastNics.SequenceEqual(current))
        {
            _lastNics = current;
            var envelope = new { type = "nics", payload = current };
            PostJson(JsonSerializer.Serialize(envelope, JsonOut));
            Log.Info($"Active network adapter list changed ({current.Count} adapter{(current.Count == 1 ? "" : "s")} available)");
        }
    }

    // Traffic history
    private void SendAllTrafficHistory()
    {
        var allRecords = TrafficHistoryStore.GetAll();
        var mapped = allRecords.Select(r => new
        {
            date = $"{r.Day:D2}-{r.Month:D2}-{r.Year:D4}",
            downBytes = r.DownBytes,
            upBytes = r.UpBytes,
            interfaceName = r.Interface,
            source = r.Source,
        }).ToList();

        var (td, tu) = TrafficHistoryStore.GetToday();
        var payload = new Dictionary<string, object?>
        {
            ["records"] = mapped,
            ["todayDown"] = td,
            ["todayUp"] = tu,
        };

        var envelope = new { type = "trafficHistoryAll", payload };
        PostJson(JsonSerializer.Serialize(envelope, JsonOut));
    }

    /// <summary>Sends a live update of today's network traffic to the frontend. Called every 5 seconds by the timer.</summary>
    private void SendTrafficToday()
    {
        if (!_webViewReady) return;
        var (td, tu) = TrafficHistoryStore.GetToday();
        var payload = new { downBytes = td, upBytes = tu };
        var envelope = new { type = "trafficToday", payload };
        PostJson(JsonSerializer.Serialize(envelope, JsonOut));
    }

    private void OnHistoryUpdated()
    {
        Dispatcher.Invoke(() =>
        {
            if (_webViewReady)
            {
                SendTrafficToday();
                SendTrafficMonths();
                SendAllTrafficHistory();
            }
        });
    }

    private void SendTrafficMonths()
    {
        var months = TrafficHistoryStore.GetAvailableMonths();
        months = [.. months.OrderByDescending(m => m)];
        var envelope = new { type = "trafficMonths", payload = months };
        PostJson(JsonSerializer.Serialize(envelope, JsonOut));
    }

    internal const int MaxImportContentLength = 10 * 1024 * 1024; // 10 MB

    internal void HandleImportJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string content = "";
            if (root.TryGetProperty("payload", out var p) && p.TryGetProperty("content", out var c))
            {
                content = c.GetString() ?? "";
            }

            if (string.IsNullOrEmpty(content))
            {
                PostImportError("Empty file content");
                return;
            }

            if (content.Length > MaxImportContentLength)
            {
                PostImportError("File exceeds maximum allowed size (10 MB)");
                return;
            }

            int days;
            string trimmed = content.TrimStart();
            if (trimmed.StartsWith('{'))
            {
                days = JsonlTrafficImporter.Import(content);
            }
            else
            {
                days = DatImporter.Import(content);
            }

            TrafficHistoryStore.Save();
            Log.Info($"User imported network usage database ({days} day{(days == 1 ? "" : "s")} recorded)");

            var envelope = new { type = "importDatResult", payload = new { daysImported = days } };
            PostJson(JsonSerializer.Serialize(envelope, JsonOut));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import network usage database content");
            PostImportError(ex.Message);
        }
    }

    private void PostImportError(string message)
    {
        var envelope = new { type = "importDatResult", payload = new { daysImported = 0, error = message } };
        PostJson(JsonSerializer.Serialize(envelope, JsonOut));
    }

    private async Task HandleImportWindowsUsage(JsonElement root)
    {
        try
        {
            if (ThrowInImportWindowsUsageForTesting)
                throw new InvalidOperationException("Simulated import windows usage error");

            if (!root.TryGetProperty(PayloadProp, out JsonElement payload)
                || !payload.TryGetProperty("startDate", out JsonElement startEl)
                || !payload.TryGetProperty("endDate", out JsonElement endEl))
            {
                PostWindowsImportResult(false, 0, 0, 0, "Missing start or end date.");
                return;
            }

            string startStr = startEl.GetString() ?? string.Empty;
            string endStr = endEl.GetString() ?? string.Empty;

            string[] dateFormats = ["dd-MM-yyyy", "yyyy-MM-dd", "d-M-yyyy", "dd/MM/yyyy"];
            if (!DateTime.TryParseExact(startStr, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime start) &&
                !DateTime.TryParse(startStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
            {
                PostWindowsImportResult(false, 0, 0, 0, "Invalid start date format (expected DD-MM-YYYY).");
                return;
            }

            if (!DateTime.TryParseExact(endStr, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime end) &&
                !DateTime.TryParse(endStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
            {
                PostWindowsImportResult(false, 0, 0, 0, "Invalid end date format (expected DD-MM-YYYY).");
                return;
            }

            TrafficHistoryFile historyFile;
            try
            {
                historyFile = TrafficHistoryStore.HistoryFile;
            }
            catch (Exception ex)
            {
                PostWindowsImportResult(false, 0, 0, 0, $"Traffic history not initialized: {ex.Message}");
                return;
            }

            var result = await WindowsNetworkUsageProvider.ImportDateRangeAsync(historyFile, start, end);
            PostWindowsImportResult(result.Success, result.DaysImported, result.DownBytes, result.UpBytes, result.Message ?? result.ErrorMessage);

            if (result.Success && result.DaysImported > 0)
            {
                SendAllTrafficHistory();
                SendTrafficMonths();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to handle importWindowsUsage message");
            PostWindowsImportResult(false, 0, 0, 0, ex.Message);
        }
    }

    private void PostWindowsImportResult(bool success, int daysImported, long downBytes, long upBytes, string? message)
    {
        var envelope = new
        {
            type = "windowsUsageImportResult",
            payload = new
            {
                success,
                daysImported,
                downBytes,
                upBytes,
                message = message ?? string.Empty,
            }
        };
        PostJson(JsonSerializer.Serialize(envelope, JsonOut));
    }

    private async Task HandleCheckForUpdatesAsync(bool force)
    {
        try
        {
            if (ThrowInUpdateCheckForTesting)
                throw new InvalidOperationException("Simulated update check error");
            Log.Debug($"Dispatching software update check (Forced: {force})");
            var result = await _updateChecker.CheckForUpdatesAsync(force).ConfigureAwait(false);
            var resJson = JsonSerializer.Serialize(new
            {
                type = "updateCheckResult",
                payload = result
            }, JsonOut);
            await Dispatcher.InvokeAsync(() => PostJson(resJson));
            Log.Debug($"Software update check result sent to frontend (Update Available: {result.IsUpdateAvailable})");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to dispatch software update check to frontend");
            var errResult = new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                CurrentVersion = UpdateCheckService.GetCurrentVersionString(),
                ErrorMessage = ex.Message
            };
            var resJson = JsonSerializer.Serialize(new { type = "updateCheckResult", payload = errResult }, JsonOut);
            await Dispatcher.InvokeAsync(() => PostJson(resJson));
        }
    }

    // System info
    // System info
    private void SendSystemInfo()
    {
        var (version, buildDate) = GetAppVersionInfo();
        string exeDir = AppContext.BaseDirectory;
        bool standardMode = File.Exists(Path.Combine(exeDir, "app.mode"));
        string deploymentMode = standardMode ? "Standard" : "Portable";
        string targetRuntime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

        var gpuList = BuildGpuList();
        var nics = SystemInfo.GetNics();
        string systemType = SystemInfo.GetSystemTypeLabel();

        long totalRamBytes = SystemInfo.GetTotalRamBytes();
        string? ramGbStr = totalRamBytes > 0
            ? $"{totalRamBytes / (1024.0 * 1024.0 * 1024.0):F1}" : null;
        var allDisks = SystemInfo.GetAllDisks();

        var payload = new Dictionary<string, object?>
        {
            ["version"] = version,
            ["buildDate"] = buildDate,
            ["deploymentMode"] = deploymentMode,
            ["targetRuntime"] = targetRuntime,
            ["deviceName"] = Environment.MachineName,
            ["os"] = SystemInfo.GetOsDisplayVersion(),
            ["cpu"] = SystemInfo.GetCpuName(),
            ["cpuVendor"] = SystemInfo.GetCpuVendor(),
            ["cpuCores"] = SystemInfo.GetCpuCoreCount(),
            ["cpuThreads"] = SystemInfo.GetCpuThreadCount(),
            ["cpuBaseSpeedMhz"] = SystemInfo.GetCpuBaseSpeedMhz(),
            ["cpuMaxSpeedMhz"] = SystemInfo.GetCpuMaxSpeedMhz(),
            ["cpuSocket"] = SystemInfo.GetCpuSocket(),
            ["cpuTdpWatts"] = HardwareMonitor.CachedTdpWatts,
            ["gpus"] = gpuList,
            ["installedRamGb"] = SystemInfo.GetInstalledRamGb(),
            ["ramGb"] = ramGbStr,
            ["ramMfr"] = SystemInfo.GetRamManufacturer(),
            ["ramSpeed"] = SystemInfo.GetRamSpeed(),
            ["ramSlots"] = SystemInfo.GetRamModuleCount(),
            ["ramModules"] = SystemInfo.GetRamModules(),
            ["disk"] = SystemInfo.GetDiskModel(),
            ["disks"] = allDisks.Select(d => new
            {
                model = d.Model,
                vendor = d.Vendor,
                busType = d.BusType,
                mediaType = d.MediaType,
                displayType = d.DisplayType,
                formattedSize = d.FormattedSize,
                sizeGb = d.SizeBytes.HasValue ? $"{d.SizeBytes.Value / (1024.0 * 1024.0 * 1024.0):F1}" : null,
                diskIndex = d.DiskIndex,
                boot = d.IsBootDrive
            }).ToList(),
            ["motherboardMfr"] = SystemInfo.GetMotherboardManufacturer(),
            ["motherboardModel"] = SystemInfo.GetMotherboardProductName(),
            ["motherboardVersion"] = SystemInfo.GetMotherboardVersion(),
            ["motherboardSerial"] = SystemInfo.GetMotherboardSerial(),
            ["bios"] = SystemInfo.GetBiosVersion(),
            ["biosUefi"] = SystemInfo.GetBiosIsUefi(),
            ["systemModel"] = SystemInfo.GetSystemModel(),
            ["ramType"] = SystemInfo.GetRamType(),
            ["nics"] = nics.Select(n => n.Name).ToList(),
            ["systemType"] = systemType,
            ["exeDir"] = AppSettings.ExeDir,
            ["dataDir"] = AppSettings.DataDir,
            ["logsDir"] = AppSettings.LogsDir,
            ["isStandardMode"] = AppSettings.IsStandardMode,
        };

        AppendPowerInfo(payload, systemType == "Laptop");

        var envelope = new
        {
            type = "systemInfo",
            payload,
        };
        PostJson(JsonSerializer.Serialize(envelope, JsonOut));
    }

    private static (string version, string buildDate) GetAppVersionInfo()
    {
        var asm = typeof(SettingsShell).Assembly;
        var infoVerAttr = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault();

        string version = infoVerAttr?.InformationalVersion ?? asm.GetName()?.Version?.ToString() ?? "0.0.0";
        if (version.Contains('+')) version = version.Split('+')[0];

        string buildDate = string.Empty;
        try
        {
            if (ThrowInBuildDateForTesting)
                throw new InvalidOperationException("Simulated build date failure");
            string assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{asm.GetName().Name}.dll");
            if (!File.Exists(assemblyPath))
                assemblyPath = Environment.ProcessPath ?? assemblyPath;
            if (File.Exists(assemblyPath))
                buildDate = File.GetLastWriteTime(assemblyPath).ToString("dd-MM-yyyy, hh:mm tt", CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { Log.Error(ex, "Failed to determine application build timestamp"); }

        return (version, buildDate);
    }

    private static void AppendPowerInfo(Dictionary<string, object?> payload, bool isLaptop)
    {
        if (isLaptop)
        {
            AppendBatteryInfo(payload);
        }
        else
        {
            AppendDesktopPsuInfo(payload);
        }
    }

    private static void AppendBatteryInfo(Dictionary<string, object?> payload)
    {
        string bm = SystemInfo.GetBatteryManufacturer();
        if (!string.IsNullOrEmpty(bm)) payload["batteryManufacturer"] = bm;
        string bn = SystemInfo.GetBatteryDeviceName();
        if (!string.IsNullOrEmpty(bn)) payload["batteryDeviceName"] = bn;
        string dc = SystemInfo.GetBatteryDesignCapacity();
        if (!string.IsNullOrEmpty(dc)) payload["batteryDesignCapacity"] = dc;
        string fc = SystemInfo.GetBatteryFullChargedCapacity();
        if (!string.IsNullOrEmpty(fc)) payload["batteryFullChargedCapacity"] = fc;

        double health = SystemInfo.GetBatteryHealthPct();
        if (health > 0) payload["batteryHealthPct"] = health;
        double wear = SystemInfo.GetBatteryWearLevelPct();
        if (health > 0) payload["batteryWearLevelPct"] = wear;
        int cycles = SystemInfo.GetBatteryCycleCount();
        if (cycles > 0) payload["batteryCycleCount"] = cycles;
    }

    private static void AppendDesktopPsuInfo(Dictionary<string, object?> payload)
    {
        string psuName = SystemInfo.GetPsuName();
        if (!string.IsNullOrEmpty(psuName)) payload["psu"] = psuName;
        string psuCap = SystemInfo.GetPsuMaxCapacity();
        if (!string.IsNullOrEmpty(psuCap)) payload["psuCapacity"] = psuCap;
    }

    private sealed record GpuInfoDto(string Name, string Vendor, bool Dedicated, string? VramGb, string? Driver, string? TdpW);

    private static List<GpuInfoDto> BuildGpuList()
    {
        var gpus = SystemInfo.GetGpus();
        if (gpus.Any(g => g.Vendor == NvidiaVendor) && !NvGpuMonitor.LatestData.HasValue)
        {
            using var nvml = new NvGpuMonitor();
            if (nvml.IsAvailable) nvml.Query();
        }

        using var amdMon = gpus.Any(g => g.Vendor == "AMD" && g.IsDedicated) ? new AmdGpuMonitor() : null;
        float? amdTdpW = amdMon?.IsAvailable == true ? amdMon.GetPowerLimitW() : null;

        return gpus.Select(g =>
        {
            string? vramGb = FormatGpuVramGb(g);
            string? driver = g.Vendor == NvidiaVendor ? NvGpuMonitor.LatestDriverVersion : g.DriverVersion;
            string? tdpW = FormatGpuTdpW(g, amdTdpW);

            return new GpuInfoDto(g.Name, g.Vendor, g.IsDedicated, vramGb, driver, tdpW);
        }).ToList();
    }

    private static string? FormatGpuVramGb((string Name, string Vendor, bool IsDedicated, long? VramBytes, string? DriverVersion) g)
    {
        if (g.Vendor == NvidiaVendor && NvGpuMonitor.LatestData.HasValue)
            return $"{NvGpuMonitor.LatestData.Value.VramTotalMb / 1024f:F1}";
        if (g.VramBytes.HasValue)
            return $"{g.VramBytes.Value / (1024.0 * 1024.0 * 1024.0):F1}";
        return null;
    }

    private static string? FormatGpuTdpW((string Name, string Vendor, bool IsDedicated, long? VramBytes, string? DriverVersion) g, float? amdTdpW)
    {
        if (g.Vendor == NvidiaVendor && NvGpuMonitor.LatestData.HasValue && NvGpuMonitor.LatestData.Value.TdpW > 0)
            return $"{NvGpuMonitor.LatestData.Value.TdpW:F0} W";
        if (g.Vendor == "AMD" && amdTdpW.HasValue && amdTdpW.Value > 0)
            return $"{amdTdpW.Value:F0} W";
        return null;
    }

    // Settings application
    private void ApplySettings(JsonElement payload)
    {
        var dto = JsonSerializer.Deserialize<SettingsDto>(payload.GetRawText(), JsonIn);
        if (dto is null)
        {
            Log.Error("Failed to apply settings due to invalid payload data");
            PostJson("{\"type\":\"saved\",\"error\":\"invalid payload\"}");
            return;
        }
        try
        {
            bool beforeStartup = _cfg.RunAtStartup;
            string before = JsonSerializer.Serialize(_cfg, JsonOut);
            SettingsDtoMapping.ApplyTo(_cfg, dto);
            string after = JsonSerializer.Serialize(_cfg, JsonOut);

            if (before != after)
            {
                var diffs = ComputeSettingsDiff(before, after);
                foreach (var diff in diffs.Where(d => !string.IsNullOrWhiteSpace(d)))
                {
                    Log.Info(diff);
                }
                if (beforeStartup != _cfg.RunAtStartup)
                {
                    WindowHelpers.SetStartup(_cfg.RunAtStartup);
                }
                _cfg.Save();
            }

            PostJson("{\"type\":\"saved\"}");
            SettingsApplied?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply configuration settings");
            PostJson(JsonSerializer.Serialize(new { type = "saved", error = ex.Message }));
        }
    }

    private static List<string> ComputeSettingsDiff(string beforeJson, string afterJson)
    {
        var rawChanges = new List<(string Path, string OldVal, string NewVal)>();
        try
        {
            using var docBefore = JsonDocument.Parse(beforeJson);
            using var docAfter = JsonDocument.Parse(afterJson);
            CompareElements(docBefore.RootElement, docAfter.RootElement, "", rawChanges);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to compute settings diff");
            return [];
        }

        const string groupColorsPrefix = "overlay.groupcolors.";
        const string metricColorsPrefix = "overlay.metriccolors.";

        var alertChanges = rawChanges.Where(c => c.Path.StartsWith("alerts.", StringComparison.OrdinalIgnoreCase)).ToList();
        var colorChanges = rawChanges.Where(c => c.Path.StartsWith(groupColorsPrefix, StringComparison.OrdinalIgnoreCase)
                                              || c.Path.StartsWith(metricColorsPrefix, StringComparison.OrdinalIgnoreCase)).ToList();

        var result = rawChanges
            .Where(c => !c.Path.StartsWith("alerts.", StringComparison.OrdinalIgnoreCase)
                     && !c.Path.StartsWith(groupColorsPrefix, StringComparison.OrdinalIgnoreCase)
                     && !c.Path.StartsWith(metricColorsPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(c => FormatHumanReadableChange(c.Path, c.OldVal, c.NewVal))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (alertChanges.Count > 0)
        {
            result.Add(ConsolidateAlertsDiff(alertChanges));
        }

        if (colorChanges.Count > 0)
        {
            result.AddRange(ConsolidateColorDiffs(colorChanges));
        }

        return result;
    }

    private static readonly (string ToggleKey, string ThreshKey, string Label, string Unit, bool IsLow)[] AlertMetricPairs =
    [
        ("alerts.alertcputemp", "alerts.cputempmaxc", "CPU temp", "°C", false),
        ("alerts.alertgputemp", "alerts.gputempmaxc", "GPU temp", "°C", false),
        ("alerts.alertramusage", "alerts.ramusagemaxpct", "RAM usage", "%", false),
        ("alerts.alertcpuusage", "alerts.cpuusagemaxpct", "CPU usage", "%", false),
        ("alerts.alertgpuusage", "alerts.gpuusagemaxpct", "GPU usage", "%", false),
        ("alerts.alertgpuvram", "alerts.gpuvrammaxmb", "GPU VRAM", " MB", false),
        ("alerts.alertbatterylow", "alerts.batterylowpct", "Low battery", "%", true),
        ("alerts.alertcpufreq", "alerts.cpufreqminmhz", "CPU throttle", " MHz", true),
        ("alerts.alertcpupower", "alerts.cpupowermaxw", "CPU power", " W", false),
        ("alerts.alertgpufreq", "alerts.gpufreqminmhz", "GPU throttle", " MHz", true),
        ("alerts.alertgpupower", "alerts.gpupowermaxw", "GPU power", " W", false),
        ("alerts.alertnetdaily", "alerts.netdailymaxgb", "Daily network limit", " GB", false),
        ("alerts.alertnetmonthly", "alerts.netmonthlymaxgb", "Monthly network limit", " GB", false),
    ];

    private static string FormatMasterAlertToggle(
        Dictionary<string, (string OldVal, string NewVal)> map,
        (string OldVal, string NewVal) masterToggle)
    {
        if (masterToggle.NewVal.Equals("false", StringComparison.OrdinalIgnoreCase))
            return "Alerts disabled";

        var enabledSummaries = new List<string>();
        foreach (var (toggleKey, threshKey, label, unit, isLow) in AlertMetricPairs)
        {
            if (!map.TryGetValue(toggleKey, out var toggle) ||
                !toggle.NewVal.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string prefix = isLow ? "≤ " : "";
            if (map.TryGetValue(threshKey, out var thresh))
                enabledSummaries.Add($"{label} ({prefix}{thresh.NewVal}{unit})");
            else
                enabledSummaries.Add($"{label} enabled");
        }

        return enabledSummaries.Count > 0
            ? $"Alerts enabled: {string.Join(", ", enabledSummaries)}"
            : "Alerts enabled";
    }

    private static string? FormatMetricPairAlert(
        Dictionary<string, (string OldVal, string NewVal)> map,
        (string ToggleKey, string ThreshKey, string Label, string Unit, bool IsLow) pair,
        HashSet<string> handledKeys)
    {
        var (toggleKey, threshKey, label, unit, isLow) = pair;
        bool hasToggle = map.TryGetValue(toggleKey, out var toggle);
        bool hasThresh = map.TryGetValue(threshKey, out var thresh);

        if (!hasToggle && !hasThresh) return null;

        string prefix = isLow ? "≤ " : "";
        if (hasToggle && hasThresh)
        {
            handledKeys.Add(toggleKey);
            handledKeys.Add(threshKey);
            return toggle.NewVal.Equals("true", StringComparison.OrdinalIgnoreCase)
                ? $"{label} alert enabled ({prefix}{thresh.NewVal}{unit})"
                : $"{label} alert disabled";
        }

        if (hasToggle)
        {
            handledKeys.Add(toggleKey);
            return $"{label} alert {ToggleStr(toggle.NewVal)}";
        }

        handledKeys.Add(threshKey);
        return $"{label} threshold set to {prefix}{thresh.NewVal}{unit}";
    }

    private static string ConsolidateAlertsDiff(List<(string Path, string OldVal, string NewVal)> alertChanges)
    {
        var map = alertChanges.ToDictionary(
            c => c.Path.ToLowerInvariant(),
            c => (OldVal: c.OldVal.Trim('"').Trim(), NewVal: c.NewVal.Trim('"').Trim()));

        if (map.TryGetValue("alerts.enabled", out var masterToggle))
            return FormatMasterAlertToggle(map, masterToggle);

        var formattedItems = new List<string>();
        var handledKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in AlertMetricPairs)
        {
            string? item = FormatMetricPairAlert(map, pair, handledKeys);
            if (item is not null)
                formattedItems.Add(item);
        }

        foreach (var change in alertChanges)
        {
            string key = change.Path.ToLowerInvariant();
            if (handledKeys.Contains(key)) continue;

            string? formatted = FormatAppearanceAlertsOrUpdatesChange(key, change.NewVal.Trim('"').Trim());
            if (!string.IsNullOrWhiteSpace(formatted))
                formattedItems.Add(formatted);
        }

        return formattedItems.Count switch
        {
            0 => "Alerts configuration updated",
            1 => formattedItems[0],
            _ => $"Alerts updated: {string.Join(", ", formattedItems)}"
        };
    }

    private static readonly Dictionary<string, string> MetricGroupMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cpu_pct"] = "cpu",
        ["cpu_temp"] = "cpu",
        ["cpu_freq"] = "cpu",
        ["cpu_power"] = "cpu",
        ["ram_pct"] = "ram",
        ["ram_used"] = "ram",
        ["gpu_pct"] = "gpu",
        ["gpu_temp"] = "gpu",
        ["gpu_vram"] = "gpu",
        ["gpu_freq"] = "gpu",
        ["gpu_power"] = "gpu",
        ["net_down"] = "net",
        ["net_up"] = "net",
        ["net_total"] = "net",
        ["battery_pct"] = "battery",
        ["battery_rate"] = "battery",
    };

    private static List<string> ConsolidateColorDiffs(List<(string Path, string OldVal, string NewVal)> colorChanges)
    {
        var result = new List<string>();
        var groupMap = new Dictionary<string, (string OldVal, string NewVal)>(StringComparer.OrdinalIgnoreCase);
        var metricChanges = new List<(string MetricId, string OldVal, string NewVal)>();

        foreach (var (path, oldVal, newVal) in colorChanges)
        {
            string cleanOld = oldVal.Trim('"').Trim();
            string cleanNew = newVal.Trim('"').Trim();
            if (path.StartsWith("overlay.groupcolors.", StringComparison.OrdinalIgnoreCase))
            {
                string group = path[20..];
                groupMap[group] = (cleanOld, cleanNew);
            }
            else if (path.StartsWith("overlay.metriccolors.", StringComparison.OrdinalIgnoreCase))
            {
                string metricId = path[21..];
                metricChanges.Add((metricId, cleanOld, cleanNew));
            }
        }

        var handledMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (group, (oldVal, newVal)) in groupMap)
        {
            string groupName = CapitalizeGroupName(group);
            var matchingMetrics = metricChanges
                .Where(m => (MetricGroupMap.TryGetValue(m.MetricId, out var g) && g.Equals(group, StringComparison.OrdinalIgnoreCase))
                         || (group.Equals("disk", StringComparison.OrdinalIgnoreCase) && m.MetricId.StartsWith("disk_", StringComparison.OrdinalIgnoreCase)))
                .Where(m => string.Equals(m.NewVal, newVal, StringComparison.OrdinalIgnoreCase))
                .Select(m => GetMetricName(m.MetricId))
                .ToList();

            if (matchingMetrics.Count > 0)
            {
                result.Add($"{groupName} group color changed to {newVal} (applied to {string.Join(", ", matchingMetrics)})");
                foreach (var m in metricChanges.Where(m => (MetricGroupMap.TryGetValue(m.MetricId, out var g) && g.Equals(group, StringComparison.OrdinalIgnoreCase))
                         || (group.Equals("disk", StringComparison.OrdinalIgnoreCase) && m.MetricId.StartsWith("disk_", StringComparison.OrdinalIgnoreCase)))
                         .Where(m => string.Equals(m.NewVal, newVal, StringComparison.OrdinalIgnoreCase)))
                {
                    handledMetrics.Add(m.MetricId);
                }
            }
            else
            {
                result.Add($"{groupName} group color changed from {oldVal} to {newVal}");
            }
        }

        foreach (var (metricId, oldVal, newVal) in metricChanges)
        {
            if (handledMetrics.Contains(metricId)) continue;
            result.Add($"Metric '{GetMetricName(metricId)}' color changed from {oldVal} to {newVal}");
        }

        return result;
    }

    private static string CapitalizeGroupName(string group) => group.ToLowerInvariant() switch
    {
        "cpu" => "CPU",
        "gpu" => "GPU",
        "ram" => "RAM",
        "net" => "Network",
        "disk" => "Disk",
        "battery" => "Battery",
        _ => char.ToUpperInvariant(group[0]) + group[1..]
    };

    private static void CompareElements(JsonElement e1, JsonElement e2, string path, List<(string Path, string OldVal, string NewVal)> changes)
    {
        if (e1.ValueKind != e2.ValueKind)
        {
            changes.Add((path, e1.GetRawText(), e2.GetRawText()));
            return;
        }

        if (e1.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in e2.EnumerateObject())
            {
                string propPath = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";
                if (e1.TryGetProperty(prop.Name, out var v1))
                {
                    CompareElements(v1, prop.Value, propPath, changes);
                }
                else
                {
                    changes.Add((propPath, "", prop.Value.GetRawText()));
                }
            }
        }
        else
        {
            if (e1.GetRawText() != e2.GetRawText())
            {
                changes.Add((path, e1.GetRawText(), e2.GetRawText()));
            }
        }
    }

    private static string FormatHumanReadableChange(string path, string oldVal, string newVal)
    {
        static string Clean(string val) => val.Trim('"').Trim();

        oldVal = Clean(oldVal);
        newVal = Clean(newVal);

        string normPath = path.ToLowerInvariant();

        string? categoryMsg = FormatGeneralOrLoggingChange(normPath, newVal)
            ?? FormatMonitoringChange(normPath, newVal)
            ?? FormatNetworkChange(normPath, newVal)
            ?? FormatOverlayChange(normPath, path, oldVal, newVal)
            ?? FormatAppearanceAlertsOrUpdatesChange(normPath, newVal);

        return categoryMsg ?? $"{path} changed from {oldVal} to {newVal}";
    }

    private static string? FormatGeneralOrLoggingChange(string normPath, string newVal) => normPath switch
    {
        "showsystemtrayicon" => newVal.Equals("true", StringComparison.OrdinalIgnoreCase)
            ? "System tray icon shown" : "System tray icon hidden",
        "runatstartup" => $"Run at startup {ToggleStr(newVal)}",
        "startminimized" => $"Start minimized {ToggleStr(newVal)}",
        "minimizetotrayonclose" => $"Minimize to tray on close {ToggleStr(newVal)}",
        "datadirectory" => $"Data directory set to '{newVal}'",
        "enablefilelogging" => $"Metrics logging {ToggleStr(newVal)}",
        "systemloglevel" => $"System log level set to {newVal}",
        _ => null
    };

    private static string? FormatMonitoringChange(string normPath, string newVal) => normPath switch
    {
        "monitoring.pollintervalms" => $"Polling interval changed to {newVal} ms",
        "monitoring.enablecpu" => $"CPU tracking {ToggleStr(newVal)}",
        "monitoring.enablegpu" => $"GPU tracking {ToggleStr(newVal)}",
        "monitoring.enableram" => $"RAM tracking {ToggleStr(newVal)}",
        "monitoring.enabledisk" => $"Disk tracking {ToggleStr(newVal)}",
        "monitoring.enablenet" => $"Network tracking {ToggleStr(newVal)}",
        "monitoring.enablebattery" => $"Battery tracking {ToggleStr(newVal)}",
        "monitoring.cpuusagemode" => $"CPU usage source changed to {newVal.ToUpperInvariant()}",
        "monitoring.usefahrenheit" => $"Temperature unit changed to {(newVal.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Fahrenheit" : "Celsius")}",
        "monitoring.usenetbits" => $"Network speed unit changed to {(newVal.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Bits/s" : "Bytes/s")}",
        "monitoring.cpufreqmode" => $"CPU clock mode changed to {newVal.ToUpperInvariant()}",
        "monitoring.gpuusagesource" => $"GPU usage source changed to {newVal.ToUpperInvariant()}",
        "monitoring.diskactivitysource" => $"Disk activity source changed to {newVal}",
        "monitoring.logcpumode" => $"CPU logging level set to {FormatLogMode(newVal)}",
        "monitoring.loggpumode" => $"GPU logging level set to {FormatLogMode(newVal)}",
        "monitoring.logrammode" => $"RAM logging level set to {FormatLogMode(newVal)}",
        "monitoring.lognetmode" => $"Network logging level set to {FormatLogMode(newVal)}",
        "monitoring.logbatterymode" => $"Battery logging level set to {FormatLogMode(newVal)}",
        "monitoring.logdiskmode" => $"Disk logging level set to {FormatLogMode(newVal)}",
        "monitoring.preferrednic" => string.Equals(newVal, "auto", StringComparison.OrdinalIgnoreCase)
            ? "Preferred network interface set to Auto"
            : $"Preferred network interface set to '{newVal}'",
        _ => null
    };

    private static string FormatLogMode(string modeVal) => modeVal switch
    {
        "0" => "Off",
        "1" => "Errors Only",
        "2" => "Full (All Metrics)",
        _ => modeVal
    };

    private static string? FormatNetworkChange(string normPath, string newVal) => normPath switch
    {
        "network.selectedinterface" => string.IsNullOrEmpty(newVal) ? "Network adapter set to All Adapters" : $"Network adapter set to '{newVal}'",
        "network.unitformat" => $"Network unit format set to {newVal}",
        "network.speedunit" => $"Network speed unit set to {newVal}",
        "network.autoresetmonthly" => $"Monthly network auto-reset {ToggleStr(newVal)}",
        _ => null
    };

    private static string? FormatOverlayChange(string normPath, string path, string oldVal, string newVal) => normPath switch
    {
        "overlay.visible" => newVal.Equals("true", StringComparison.OrdinalIgnoreCase)
            ? "Taskbar overlay turned on" : "Taskbar overlay turned off",
        "overlay.placement" => $"Position changed to {newVal}",
        "overlay.placementoffset" => $"Offset changed to {newVal} px",
        "overlay.opacity" => $"Opacity changed to {newVal}%",
        "overlay.scalepct" => $"Scale changed to {newVal}%",
        "overlay.fontsizepx" => $"Font size changed to {newVal} px",
        "overlay.fontbold" => $"Bold font {ToggleStr(newVal)}",
        "overlay.labelcolor" => $"Label color changed to {newVal}",
        "overlay.valuecolor" => $"Value color changed to {newVal}",
        "overlay.bgcolor" => $"Background color changed to {newVal}",
        "overlay.showtoastnotif" => $"Toast notifications {ToggleStr(newVal)}",
        "overlay.flashoverlay" => $"Flashing alerts {ToggleStr(newVal)}",
        "overlay.showlabels" => newVal.Equals("true", StringComparison.OrdinalIgnoreCase)
            ? "Labels shown" : "Labels hidden",
        "overlay.doubleclickaction" => newVal.Equals("taskmanager", StringComparison.OrdinalIgnoreCase)
            ? "Double-click action set to Task Manager" : "Double-click action set to Open Settings",
        "overlay.followwindowstheme" => $"Follow Windows theme {ToggleStr(newVal)}",
        "overlay.row1" or "overlay.metrics" => FormatOverlayLayoutChange(oldVal, newVal),
        _ when normPath.StartsWith("overlay.metriccolors.", StringComparison.Ordinal) =>
            $"Metric '{GetMetricName(path[21..])}' color changed from {oldVal} to {newVal}",
        _ when normPath.StartsWith("overlay.groupcolors.", StringComparison.Ordinal) =>
            $"{char.ToUpperInvariant(path[20]) + path[21..]} group color changed from {oldVal} to {newVal}",
        _ when normPath.StartsWith("overlay.usercustomized", StringComparison.Ordinal) =>
            "", // Internal tracking array; avoid redundant logging
        _ => null
    };

    private static string? FormatAppearanceAlertsOrUpdatesChange(string normPath, string newVal) => normPath switch
    {
        "windowtheme" => $"Window theme changed to {newVal}",
        "alerts.enabled" => $"Alerts {ToggleStr(newVal)}",
        "alerts.alertcputemp" => $"CPU temperature alert {ToggleStr(newVal)}",
        "alerts.alertgputemp" => $"GPU temperature alert {ToggleStr(newVal)}",
        "alerts.alertcpuusage" => $"CPU usage alert {ToggleStr(newVal)}",
        "alerts.alertramusage" => $"RAM usage alert {ToggleStr(newVal)}",
        "alerts.alertgpuusage" => $"GPU usage alert {ToggleStr(newVal)}",
        "alerts.alertgpuvram" => $"GPU VRAM alert {ToggleStr(newVal)}",
        "alerts.alertbatterylow" => $"Battery low alert {ToggleStr(newVal)}",
        "alerts.alertcpufreq" => $"CPU frequency alert {ToggleStr(newVal)}",
        "alerts.alertcpupower" => $"CPU power alert {ToggleStr(newVal)}",
        "alerts.alertgpufreq" => $"GPU frequency alert {ToggleStr(newVal)}",
        "alerts.alertgpupower" => $"GPU power alert {ToggleStr(newVal)}",
        "alerts.alertnetdaily" => $"Daily network limit alert {ToggleStr(newVal)}",
        "alerts.alertnetmonthly" => $"Monthly network limit alert {ToggleStr(newVal)}",
        "alerts.fireoncepersession" => $"Fire alerts only once per session {ToggleStr(newVal)}",
        "alerts.cputempmaxc" => $"CPU temp threshold set to {newVal}°C",
        "alerts.gputempmaxc" => $"GPU temp threshold set to {newVal}°C",
        "alerts.cpuusagemaxpct" => $"CPU usage threshold set to {newVal}%",
        "alerts.ramusagemaxpct" => $"RAM usage threshold set to {newVal}%",
        "alerts.gpuusagemaxpct" => $"GPU usage threshold set to {newVal}%",
        "alerts.gpuvrammaxmb" => $"GPU VRAM threshold set to {newVal} MB",
        "alerts.batterylowpct" => $"Low battery threshold set to {newVal}%",
        "alerts.cpufreqminmhz" => $"CPU frequency threshold set to {newVal} MHz",
        "alerts.cpupowermaxw" => $"CPU power threshold set to {newVal} W",
        "alerts.gpufreqminmhz" => $"GPU frequency threshold set to {newVal} MHz",
        "alerts.gpupowermaxw" => $"GPU power threshold set to {newVal} W",
        "alerts.netdailymaxgb" => $"Daily network limit set to {newVal} GB",
        "alerts.netmonthlymaxgb" => $"Monthly network limit set to {newVal} GB",
        "alerts.cooldownsecs" => $"Alert cooldown set to {newVal}s",
        "alerts.showtoastnotif" => $"Alert toast notifications {ToggleStr(newVal)}",
        "alerts.flashoverlay" => $"Alert overlay flashing {ToggleStr(newVal)}",
        "netusage.enabled" => $"Network usage tracking {ToggleStr(newVal)}",
        "updates.includeprereleases" => $"Include pre-release builds {ToggleStr(newVal)}",
        "updates.checkfrequency" => newVal.Length > 0 ? $"Update check frequency set to {char.ToUpperInvariant(newVal[0]) + newVal[1..]}" : "Update check frequency changed",
        _ => null
    };

    private static string ToggleStr(string newVal) => newVal.Equals("true", StringComparison.OrdinalIgnoreCase) ? EnabledStr : DisabledStr;

    private static string GetMetricName(string metricId) => metricId.ToLowerInvariant() switch
    {
        "cpu_pct" => "CPU Usage",
        "cpu_temp" => "CPU Temperature",
        "cpu_freq" => "CPU Frequency",
        "cpu_power" => "CPU Power",
        "ram_pct" => "RAM Usage %",
        "ram_used" => "RAM Used",
        "gpu_pct" => "GPU Usage",
        "gpu_temp" => "GPU Temperature",
        "gpu_vram" => "GPU VRAM",
        "gpu_freq" => "GPU Frequency",
        "gpu_power" => "GPU Power",
        "net_down" => "Download Speed",
        "net_up" => "Upload Speed",
        "net_total" => "Total Network Usage",
        "battery_pct" => "Battery %",
        "battery_rate" => "Battery Rate",
        _ when metricId.StartsWith("disk_", StringComparison.Ordinal) => $"Disk {metricId[5..].ToUpperInvariant()}",
        _ => metricId
    };

    private static List<string> ParseMetricIds(string val)
    {
        var metricIds = new List<string>();
        if (string.IsNullOrWhiteSpace(val)) return metricIds;
        string trimmed = val.Trim();

        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        string? id = elem.GetString();
                        if (!string.IsNullOrWhiteSpace(id))
                            metricIds.Add(id.Trim());
                    }
                }
                return metricIds;
            }
            catch (JsonException ex)
            {
                Log.Debug(ex, "Failed to parse metrics layout as JSON; falling back to delimiter split");
                var parts = trimmed.Trim('[', ']').Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                metricIds.AddRange(parts.Select(p => p.Trim('"', '\'')).Where(p => !string.IsNullOrWhiteSpace(p)));
                return metricIds;
            }
        }

        var fallbackParts = trimmed.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        metricIds.AddRange(fallbackParts.Select(p => p.Trim('"', '\'')).Where(p => !string.IsNullOrWhiteSpace(p)));
        return metricIds;
    }

    private static string FormatOverlayLayoutChange(string oldVal, string newVal)
    {
        var newIds = ParseMetricIds(newVal);
        var oldIds = ParseMetricIds(oldVal);

        if (newIds.Count == 0)
            return "Metrics layout cleared (0 metrics)";

        // If oldVal was empty or unparseable, format as configured/updated count
        if (oldIds.Count == 0)
        {
            var names = newIds.Select(GetMetricName);
            string metricPlural = newIds.Count == 1 ? "metric" : "metrics";
            return $"Metrics layout updated ({newIds.Count} {metricPlural} - {string.Join(", ", names)})";
        }

        var added = newIds.Except(oldIds).Select(GetMetricName).ToList();
        var removed = oldIds.Except(newIds).Select(GetMetricName).ToList();

        if (added.Count > 0 && removed.Count == 0)
        {
            string metricPlural = added.Count == 1 ? "metric" : "metrics";
            return $"Added {metricPlural} to overlay ({string.Join(", ", added)})";
        }

        if (removed.Count > 0 && added.Count == 0)
        {
            string metricPlural = removed.Count == 1 ? "metric" : "metrics";
            return $"Removed {metricPlural} from overlay ({string.Join(", ", removed)})";
        }

        if (added.Count > 0)
        {
            return $"Metrics layout updated (Added {string.Join(", ", added)}, Removed {string.Join(", ", removed)})";
        }

        if (!oldIds.SequenceEqual(newIds))
        {
            return "Metrics layout reordered";
        }

        return string.Empty;
    }

    // Title-bar & caption buttons
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        if (SkipDragMoveForTesting) return;
        DragMove();
    }

    /// <summary>Closes the settings configuration window.</summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // Disposal
    protected override void OnClosed(EventArgs e)
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        _nicDebounceTimer?.Dispose();
        _trafficTodayTimer?.Dispose();

        // Properly dispose WebView2 to prevent phantom COM objects
        if (_webViewReady)
            WebView?.Dispose();
        base.OnClosed(e);
    }
}





