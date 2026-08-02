using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using LocalTelemetry.App.Win32;
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

    private const string SettingsHost = "localtelemetry.settings";
    private const string SettingsEntryPoint = "index.html";

    private readonly AppSettings _cfg;
    private readonly string _wwwRoot;
    private readonly NetUsageLogger _netLogger;
    private bool _webViewReady;

    // NIC list caching and real-time updates
    private List<string>? _lastNics;
    private System.Threading.Timer? _nicDebounceTimer;

    // Traffic today push timer (5s interval)
    private System.Threading.Timer? _trafficTodayTimer;

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

    public SettingsShell(AppSettings cfg, NetUsageLogger netLogger)
    {
        InitializeComponent();
        _cfg = cfg;
        _netLogger = netLogger;
        _wwwRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");

        // Set taskbar/alt-tab icon from the embedded application icon
        try
        {
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

        Loaded += OnLoaded;
        // Close WebView2 select dropdowns when the window is moved
        // (WebView2 native popups render at stale screen coordinates after move)
        LocationChanged += (_, _) =>
        {
            if (_webViewReady && WebView.CoreWebView2 is not null)
            {
                try { WebView.CoreWebView2.ExecuteScriptAsync("try{document.activeElement?.blur()}catch(e){}"); }
                catch (Exception) { Log.Error("WebView2 blur script failed after window move"); }
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTLEFT = 10;
        const int HTRIGHT = 11;
        const int HTTOP = 12;
        const int HTTOPLEFT = 13;
        const int HTTOPRIGHT = 14;
        const int HTBOTTOM = 15;
        const int HTBOTTOMLEFT = 16;
        const int HTBOTTOMRIGHT = 17;

        if (msg == WM_NCHITTEST && WindowState == WindowState.Normal)
        {
            int x = (short)(lParam.ToInt32() & 0xFFFF);
            int y = (short)(lParam.ToInt32() >> 16);

            System.Windows.Point windowPos = PointFromScreen(new System.Windows.Point(x, y));
            const double border = 8.0;

            bool onLeft = windowPos.X <= border;
            bool onRight = windowPos.X >= ActualWidth - border;
            bool onTop = windowPos.Y <= border;
            bool onBottom = windowPos.Y >= ActualHeight - border;

            if (onTop && onLeft) { handled = true; return (IntPtr)HTTOPLEFT; }
            if (onTop && onRight) { handled = true; return (IntPtr)HTTOPRIGHT; }
            if (onBottom && onLeft) { handled = true; return (IntPtr)HTBOTTOMLEFT; }
            if (onBottom && onRight) { handled = true; return (IntPtr)HTBOTTOMRIGHT; }
            if (onLeft) { handled = true; return (IntPtr)HTLEFT; }
            if (onRight) { handled = true; return (IntPtr)HTRIGHT; }
            if (onTop) { handled = true; return (IntPtr)HTTOP; }
            if (onBottom) { handled = true; return (IntPtr)HTBOTTOM; }
        }
        return IntPtr.Zero;
    }

    // Use Loaded event so the WPF visual tree is ready.
    // Guard with _webViewReady so re-showing the window doesn't re-init.
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_webViewReady) return;
        await InitWebViewAsync();
    }

    // WebView2 init
    private async Task InitWebViewAsync()
    {
        try
        {
            string exeDir = AppContext.BaseDirectory;
            string markerFile = Path.Combine(exeDir, "app.mode");
            bool normalMode = File.Exists(markerFile);

            string dataDir = normalMode
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalTelemetry", "webview2_data")
                : Path.Combine(exeDir, "webview2_data");

            CoreWebView2Environment env =
                await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: dataDir);

            await WebView.EnsureCoreWebView2Async(env);

            CoreWebView2Settings wv = WebView.CoreWebView2.Settings;
            wv.IsWebMessageEnabled = true;
            wv.AreDevToolsEnabled = false;
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

            // Subscribe to network change events for real-time NIC dropdown updates.
            _nicDebounceTimer = new System.Threading.Timer(
                _ => Dispatcher.Invoke(CheckAndSendNics));
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            // Start periodic push of today's traffic totals (every 5 seconds).
            _trafficTodayTimer = new System.Threading.Timer(
                _ => Dispatcher.Invoke(SendTrafficToday),
                null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            _webViewReady = true;
        }
        catch (Exception ex)
        {
            // WebView2 Runtime not installed or failed to init - show dialog
            // instead of crashing the whole app
            System.Windows.MessageBox.Show(
                $"Settings window failed to initialise WebView2:\n\n{ex.Message}\n\n" +
                "Install the WebView2 Runtime from:\nhttps://developer.microsoft.com/en-us/microsoft-edge/webview2/",
                "LocalTelemetry - Settings Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Hide();
        }
    }

    // Inbound message handler
    private void OnWebMessageReceived(
        object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(e.WebMessageAsJson);
            JsonElement root = doc.RootElement;

            string type = root.TryGetProperty("type", out JsonElement t)
                ? t.GetString() ?? string.Empty
                : string.Empty;

            Log.Info($"OnWebMessageReceived: type='{type}'");

            switch (type)
            {
                case "getSettings": SendSettings(); break;
                case "saveSettings":
                    if (root.TryGetProperty("payload", out JsonElement payload))
                        ApplySettings(payload);
                    break;
                case "getNics": SendNics(); break;
                case "getTrafficMonths": SendTrafficMonths(); break;
                case "getSystemInfo": SendSystemInfo(); break;
                case "openUrl":
                    if (root.TryGetProperty("payload", out JsonElement urlPayload)
                        && urlPayload.TryGetProperty("url", out JsonElement urlEl))
                    {
                        string url = urlEl.GetString() ?? string.Empty;
                        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUri) &&
                           (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps))
                        {
                            Process.Start(new ProcessStartInfo { FileName = parsedUri.AbsoluteUri, UseShellExecute = true });
                        }
                        else
                        {
                            Log.Error($"Security Warning: Rejecting invalid or non-HTTP openUrl request: '{url}'");
                        }
                    }
                    break;
                case "getTrafficHistoryAll": SendAllTrafficHistory(); break;
                case "importDat": HandleImportDat(e); break;
                default: break;
            }
        }
        catch (JsonException ex) { Log.Error(ex, "OnWebMessageReceived JSON error"); }
        catch (Exception ex) { Log.Error(ex, "OnWebMessageReceived error"); }
    }

    // Outbound messages
    private void PostJson(string json)
    {
        if (!_webViewReady || WebView.CoreWebView2 is null) return;
        WebView.CoreWebView2.PostWebMessageAsJson(json);
    }

    public void SendUpdatedSettings()
    {
        SendSettings();
    }

    private void SendSettings()
    {
        var dto = SettingsDtoMapping.ToDto(_cfg);
        var json = JsonSerializer.Serialize(new { type = "settings", payload = dto }, JsonOut);
        // Log overlay visibility so we can confirm initial state
        Log.Info($"SendSettings: overlay.visible={dto.Overlay.Visible}, jsonLen={json.Length}");
        PostJson(json);
    }

    private void SendNics()
    {
        var nics = GetActiveNics();
        _lastNics = nics;
        var envelope = new { type = "nics", payload = nics };
        PostJson(JsonSerializer.Serialize(envelope, JsonOut));
        Log.Info($"SendNics: {nics.Count} adapter(s): {string.Join(", ", nics)}");
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
        catch (Exception) { return []; }
    }

    /// <summary>Debounced handler for <see cref="NetworkChange.NetworkAddressChanged"/>.</summary>
    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        _nicDebounceTimer?.Change(1000, System.Threading.Timeout.Infinite);
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
            Log.Info($"CheckAndSendNics: updated to {current.Count} adapter(s)");
        }
    }

    // Traffic history
    private void SendAllTrafficHistory()
    {
        var allRecords = TrafficHistoryStore.GetAll();
        var mapped = allRecords.Select(r => new
        {
            date = $"{r.Day:D2}/{r.Month:D2}/{r.Year:D4}",
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

    private void SendTrafficMonths()
    {
        var months = TrafficHistoryStore.GetAvailableMonths();
        months = [.. months.OrderByDescending(m => m)];
        var envelope = new { type = "trafficMonths", payload = months };
        PostJson(JsonSerializer.Serialize(envelope, JsonOut));
    }

    private void HandleImportDat(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            string content = root.TryGetProperty("payload", out var p)
                ? p.TryGetProperty("content", out var c)
                    ? c.GetString() ?? ""
                    : ""
                : "";

            if (string.IsNullOrEmpty(content))
            {
                PostImportError("Empty file content");
                return;
            }

            int days = DatImporter.Import(content);
            TrafficHistoryStore.Save();

            var envelope = new { type = "importDatResult", payload = new { daysImported = days } };
            PostJson(JsonSerializer.Serialize(envelope, JsonOut));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HandleImportDat failed");
            PostImportError(ex.Message);
        }
    }

    private void PostImportError(string message)
    {
        var envelope = new { type = "importDatResult", payload = new { daysImported = 0, error = message } };
        PostJson(JsonSerializer.Serialize(envelope, JsonOut));
    }

    // System info
    private void SendSystemInfo()
    {
        var asm = typeof(SettingsShell).Assembly;
        var infoVerAttr = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault();

        string version = infoVerAttr?.InformationalVersion ?? asm.GetName()?.Version?.ToString() ?? "0.0.0";
        // Strip git commit hash suffix if present (e.g. 1.0.0+9e26f47 -> 1.0.0)
        if (version.Contains('+')) version = version.Split('+')[0];

        string buildDate = string.Empty;
        try
        {
            string assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{asm.GetName().Name}.dll");
            if (File.Exists(assemblyPath))
                buildDate = File.GetLastWriteTime(assemblyPath).ToString("yyyy-MM-dd HH:mm");
        }
        catch (Exception ex) { Log.Error(ex, "Get build date failed"); }

        string exeDir = AppContext.BaseDirectory;
        bool normalMode = File.Exists(Path.Combine(exeDir, "app.mode"));
        string deploymentMode = normalMode ? "Normal" : "Portable";
        string targetRuntime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

        var gpus = SystemInfo.GetGpus();
        if (gpus.Any(g => g.Vendor == "NVIDIA") && !NvGpuMonitor.LatestData.HasValue)
        {
            using var nvml = new NvGpuMonitor();
            if (nvml.IsAvailable) nvml.Query();
        }

        using var amdMon = gpus.Any(g => g.Vendor == "AMD" && g.IsDedicated) ? new AmdGpuMonitor() : null;
        float? amdTdpW = amdMon?.IsAvailable == true ? amdMon.GetPowerLimitW() : null;

        var gpuList = gpus.Select(g =>
        {
            string? vramGb;
            if (g.Vendor == "NVIDIA" && NvGpuMonitor.LatestData.HasValue)
                vramGb = $"{NvGpuMonitor.LatestData.Value.VramTotalMb / 1024f:F1}";
            else if (g.VramBytes.HasValue)
                vramGb = $"{g.VramBytes.Value / (1024.0 * 1024.0 * 1024.0):F1}";
            else
                vramGb = null;

            string? driver = g.Vendor == "NVIDIA" ? NvGpuMonitor.LatestDriverVersion : g.DriverVersion;

            string? tdpW;
            if (g.Vendor == "NVIDIA" && NvGpuMonitor.LatestData.HasValue && NvGpuMonitor.LatestData.Value.TdpW > 0)
                tdpW = $"{NvGpuMonitor.LatestData.Value.TdpW:F0} W";
            else if (g.Vendor == "AMD" && amdTdpW.HasValue && amdTdpW.Value > 0)
                tdpW = $"{amdTdpW.Value:F0} W";
            else
                tdpW = null;

            return new { name = g.Name, vendor = g.Vendor, dedicated = g.IsDedicated, vramGb, driver, tdpW };
        }).ToList();

        var nics = SystemInfo.GetNics();
        string systemType = SystemInfo.GetSystemTypeLabel();
        bool isLaptop = systemType == "Laptop";

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
        };

        if (isLaptop)
        {
            string bm = SystemInfo.GetBatteryManufacturer();
            if (!string.IsNullOrEmpty(bm))
                payload["batteryManufacturer"] = bm;
            string bn = SystemInfo.GetBatteryDeviceName();
            if (!string.IsNullOrEmpty(bn))
                payload["batteryDeviceName"] = bn;
            string dc = SystemInfo.GetBatteryDesignCapacity();
            if (!string.IsNullOrEmpty(dc))
                payload["batteryDesignCapacity"] = dc;
            string fc = SystemInfo.GetBatteryFullChargedCapacity();
            if (!string.IsNullOrEmpty(fc))
                payload["batteryFullChargedCapacity"] = fc;
        }
        else
        {
            string psuName = SystemInfo.GetPsuName();
            if (!string.IsNullOrEmpty(psuName))
                payload["psu"] = psuName;
            string psuCap = SystemInfo.GetPsuMaxCapacity();
            if (!string.IsNullOrEmpty(psuCap))
                payload["psuCapacity"] = psuCap;
        }

        var envelope = new
        {
            type = "systemInfo",
            payload
        };
        PostJson(JsonSerializer.Serialize(envelope, JsonOut));
    }

    // Settings application
    private void ApplySettings(JsonElement payload)
    {
        var dto = JsonSerializer.Deserialize<SettingsDto>(payload.GetRawText(), JsonIn);
        if (dto is null)
        {
            Log.Error("ApplySettings: null DTO");
            PostJson("{\"type\":\"saved\",\"error\":\"invalid payload\"}");
            return;
        }
        try
        {
            string before = JsonSerializer.Serialize(_cfg, JsonOut);
            SettingsDtoMapping.ApplyTo(_cfg, dto);
            string after = JsonSerializer.Serialize(_cfg, JsonOut);

            if (before != after)
            {
                Log.Info($"ApplySettings: saved ({after.Length} chars)");
                WindowHelpers.SetStartup(_cfg.RunAtStartup, _cfg.StartMinimized);
                _cfg.Save();
            }

            PostJson("{\"type\":\"saved\"}");
            SettingsApplied?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error($"ApplySettings failed: {ex}");
            PostJson(JsonSerializer.Serialize(new { type = "saved", error = ex.Message }));
        }
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
        DragMove();
    }

    /// <summary>Close or minimize to tray based on setting.</summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cfg.MinimizeToTrayOnClose)
            Close();
        else
            System.Windows.Application.Current.Shutdown();
    }

    // Disposal
    protected override void OnClosed(EventArgs e)
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        _nicDebounceTimer?.Dispose();
        _trafficTodayTimer?.Dispose();

        // Properly dispose WebView2 to prevent phantom COM objects
        if (_webViewReady)
            WebView.Dispose();
        base.OnClosed(e);
    }
}
