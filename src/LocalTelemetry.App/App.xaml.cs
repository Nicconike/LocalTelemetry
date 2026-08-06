using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Windows;
using LocalTelemetry.App.Overlay;
using LocalTelemetry.App.Services;
using LocalTelemetry.App.Settings;
using LocalTelemetry.App.Tray;
using LocalTelemetry.App.Win32;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Diagnostics;
using LocalTelemetry.Core.Hardware;
using LocalTelemetry.Core.Hardware.PawnIo;
using LocalTelemetry.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WpfApplication = System.Windows.Application;
using WpfStartupEventArgs = System.Windows.StartupEventArgs;
using WpfExitEventArgs = System.Windows.ExitEventArgs;

namespace LocalTelemetry.App;

/// <summary>
/// Application entry point for LocalTelemetry. Handles elevation, DI container setup,
/// hardware monitor lifecycle, overlay, tray icon and notification client wiring.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class App : WpfApplication
{
    private ServiceProvider? _services;
    private HardwareMonitor? _monitor;
    private NetUsageLogger? _netLogger;
    private TaskbarOverlay? _overlay;
    private TrayIconManager? _tray;
    private AlertService? _alerts;
    private SettingsShell? _settingsShell;
    private AppSettings _cfg = new AppSettings();
    private SynchronizationContext? _uiCtx;
    private NotificationClient? _notifClient;

    // Elevation
    private static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void ElevateAndExit(string[] args)
    {
        string exe = Environment.ProcessPath!;
        var sb = new System.Text.StringBuilder();
        foreach (var a in args)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (a.Contains(' ')) sb.Append('"').Append(a).Append('"');
            else sb.Append(a);
        }
        if (sb.Length > 0) sb.Append(' ');
        sb.Append("--elevated");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = sb.ToString(),
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"LocalTelemetry requires administrator privileges.\n\n{ex.Message}",
                "Elevation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        Environment.Exit(0);
    }

    // Startup
    protected override void OnStartup(WpfStartupEventArgs e)
    {
        base.OnStartup(e);

        if (!IsAdministrator() && !Array.Exists(e.Args, a => a == "--elevated"))
        {
            ElevateAndExit(e.Args);
            return;
        }

        Log.Info("running as administrator");

        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        DispatcherUnhandledException += HandleDispatcherUnhandledException;

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        _uiCtx = SynchronizationContext.Current;

        string exeDir = AppContext.BaseDirectory;
        AppSettings.InitPaths(exeDir);
        _cfg = AppSettings.Load();
        // Sync startup setting with actual Windows Registry state (installer choice & HKCU/HKLM keys)
        _cfg.RunAtStartup = WindowHelpers.IsStartupEnabled();
        Log.Init(AppSettings.SystemLogPath, AppSettings.MetricsLogPath, _cfg.EnableFileLogging);
        Log.Info($"config loaded from {AppSettings.ConfigPath}");

        _services = BuildContainer();
        _overlay = _services.GetRequiredService<TaskbarOverlay>();
        _overlay.OnDoubleClick = action =>
        {
            string normalized = (action ?? string.Empty).ToLowerInvariant().Trim();
            if (normalized is "taskmanager" or "taskmgr")
            {
                try { Process.Start("taskmgr"); }
                catch (Exception ex) { Log.Error($"failed to launch taskmgr: {ex.Message}"); }
            }
            else
            {
                OpenSettings();
            }
        };
        _tray = _services.GetRequiredService<TrayIconManager>();
        _tray.SetOverlayVisible(_cfg.Overlay.Visible);
        _alerts = _services.GetRequiredService<AlertService>();
        _monitor = _services.GetRequiredService<HardwareMonitor>();
        _notifClient = _services.GetRequiredService<NotificationClient>();
        Log.Info("Services Initialized (overlay, tray, alerts, monitor, notifier)");

        // Launch non-elevated notification helper (child process inherits user token, not admin)
        LaunchNotifier();

        // Create the single TrafficHistoryFile and import SRUM history BEFORE logger seeds today's data
        var historyFile = new TrafficHistoryFile(AppSettings.NetUsagePath);
        historyFile.Load();

        try
        {
            var srumTask = Task.Factory.StartNew(
                () => WindowsNetworkUsageProvider.ImportAndSaveHistoryAsync(historyFile),
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler.Default).Unwrap();
            srumTask.GetAwaiter().GetResult();
            Log.Info("SRUM: history import complete");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SRUM pre-import failed");
        }

        _netLogger = new NetUsageLogger(_cfg, historyFile);
        Log.Info("NetUsage: logger initialized");

        TrafficHistoryStore.Initialize(historyFile);
        Log.Info("TrafficHistoryStore: initialized");

        // Sync RunAtStartup setting with actual Windows Task Scheduler / Registry startup state
        bool isStartupActive = WindowHelpers.IsStartupEnabled();
        if (_cfg.RunAtStartup != isStartupActive)
        {
            _cfg.RunAtStartup = isStartupActive;
            _cfg.Save();
            Log.Info($"Synced RunAtStartup with Windows system state: {isStartupActive}");
        }

        WireEvents();

        bool minimized = Array.Exists(e.Args, a => a == "--minimized") || _cfg.StartMinimized;

        ApplyVendorColors(_cfg);

        if (_cfg.Overlay.Visible)
            _overlay.Show();

        _monitor.Start();

        Log.Info($"System: {Environment.MachineName}, {SystemInfo.GetOsDisplayVersion()}, {SystemInfo.GetSystemTypeLabel()}");
        Log.Info($"Motherboard: {SystemInfo.GetMotherboardManufacturer()} {SystemInfo.GetMotherboardProductName()} ({SystemInfo.GetMotherboardVersion()}, BIOS: {SystemInfo.GetBiosVersion()})");
        if (SystemInfo.HasBattery())
            Log.Info($"Battery: {SystemInfo.GetBatteryManufacturer()} {SystemInfo.GetBatteryDeviceName()}, design={SystemInfo.GetBatteryDesignCapacity()}, fullCharge={SystemInfo.GetBatteryFullChargedCapacity()}");

        if (!minimized)
            OpenSettings();

        WindowHelpers.SetStartup(_cfg.RunAtStartup, _cfg.StartMinimized);
        Log.Info($"startup complete (elevated=true, overlayVisible={_cfg.Overlay.Visible}, startMinimized={minimized})");
    }

    private bool _metricsWasEnabled;
    private void ToggleMetricsLogging(bool enable)
    {
        if (enable == _metricsWasEnabled) return;
        _metricsWasEnabled = enable;
        try
        {
            Log.EnableMetrics(enable);
            if (enable)
                Log.Info($"Metrics logging enabled: {AppSettings.MetricsLogPath}");
            else
                Log.Info("Metrics logging disabled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ToggleMetricsLogging failed");
        }
    }

    private void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Error(ex, "Unhandled AppDomain exception");
    }

    private void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error($"Unobserved Task exception: {e.Exception?.GetType().Name}: {e.Exception?.Message}");
        e.SetObserved();
    }

    private void HandleDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Dispatcher exception");
        e.Handled = true;
    }

    internal static void ApplyVendorColors(AppSettings cfg)
    {
        var vendorDefaults = BrandColorDefaults.BuildDefaultMetricColors();
        cfg.Overlay.DefaultMetricColors = vendorDefaults;

        foreach (var kvp in vendorDefaults)
        {
            if (cfg.Overlay.UserCustomizedMetricColors.Contains(kvp.Key))
                continue;

            cfg.Overlay.MetricColors[kvp.Key] = kvp.Value;
        }

        foreach (var kvp in BrandColorDefaults.BuildDefaultGroupColors(vendorDefaults))
        {
            if (cfg.Overlay.UserCustomizedGroupColors.Contains(kvp.Key))
                continue;

            cfg.Overlay.GroupColors[kvp.Key] = kvp.Value;
        }
    }

    // DI container
    private ServiceProvider BuildContainer()
    {
        var sc = new ServiceCollection();

        sc.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Error);
            b.AddSimpleConsole(o =>
            {
                o.TimestampFormat = "dd-MM-yyyy hh:mm:ss.fff tt ";
                o.SingleLine = true;
                o.IncludeScopes = false;
                o.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Disabled;
            });
#if DEBUG
            b.AddDebug();
#endif
        });
        sc.AddSingleton(_cfg);
        sc.AddSingleton<HardwareMonitor>();
        sc.AddSingleton<ISystemInfo, SystemInfoWrapper>();
        sc.AddSingleton<IPawnIoManager, PawnIoManagerWrapper>();
        sc.AddSingleton<TaskbarEmbedder>();
        sc.AddSingleton<TaskbarOverlay>();
        sc.AddSingleton<TrayIconManager>();
        sc.AddSingleton<NotificationClient>();
        sc.AddSingleton<AlertService>();

        return sc.BuildServiceProvider();
    }

    // Event wiring
    private void WireEvents()
    {
        _tray!.OpenSettingsRequested += () => OpenSettings();
        _tray!.QuitRequested += () => QuitApp();
        _tray!.ToggleOverlayRequested += () =>
        {
            _cfg.Overlay.Visible = !_cfg.Overlay.Visible;
            Log.Info($"Tray toggle overlay: visible={_cfg.Overlay.Visible}");
            _cfg.Save();
            HandleSettingsApplied();
        };

        _overlay!.OnDoubleClick += (action) =>
        {
            Log.Info($"Overlay double-click triggered: action='{action}'");
            if (string.Equals(action, "taskmanager", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
                    Log.Info("Overlay double-click: launched taskmgr.exe");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to launch taskmgr.exe via overlay double-click");
                }
            }
            else if (string.Equals(action, "settings", StringComparison.OrdinalIgnoreCase))
            {
                OpenSettings();
            }
        };

        _monitor!.SnapshotReady += HandleSnapshotReady;
    }

    private void HandleSnapshotReady(TelemetrySnapshot snap)
    {
        _netLogger!.Record(snap);
        _alerts!.Evaluate(snap);

        _overlay!.UpdateSnapshot(snap);

        if (_tray is not null)
            _uiCtx?.Post(_ => _tray.Update(snap), null);
    }

    // Tray action handlers
    private void OpenSettings()
    {
        if (_settingsShell is null || !_settingsShell.IsLoaded)
        {
            _settingsShell = new SettingsShell(_cfg, _netLogger!);
            Log.Info("SettingsShell created");
            _settingsShell.SettingsApplied += HandleSettingsApplied;
            _settingsShell.Closed += (_, _) =>
            {
                Log.Info("SettingsShell closed, clearing reference");
                _settingsShell = null;
            };
        }

        _settingsShell.Show();
        _settingsShell.Activate();
    }

    private int _lastPollIntervalMs;

    private void HandleSettingsApplied()
    {
        if (_overlay is null) { Log.Error("HandleSettingsApplied: _overlay is null!"); return; }
        Log.Info($"HandleSettingsApplied: Overlay.Visible={_cfg.Overlay.Visible}");
        _tray?.SetOverlayVisible(_cfg.Overlay.Visible);
        if (_cfg.Overlay.Visible)
        {
            _overlay.Show();
        }
        else
        {
            _overlay.Hide();
        }

        if (_settingsShell is not null && _settingsShell.IsLoaded)
        {
            _settingsShell.SendUpdatedSettings();
        }

        // Restart hardware monitor with new poll interval
        int ms = Math.Max(100, _cfg.Monitoring.PollIntervalMs);
        if (ms != _lastPollIntervalMs)
        {
            _lastPollIntervalMs = ms;
            _monitor?.Restart(ms);
        }

        // Toggle metrics logging (system log is always on)
        bool shouldLog = _cfg.EnableFileLogging;
        ToggleMetricsLogging(shouldLog);
    }

    /// <summary>Full quit - stops monitor, saves settings, exits process.</summary>
    private void QuitApp()
    {
        _settingsShell?.Close();
        Shutdown();
    }

    // Notification helper (non-elevated child process)
    private void LaunchNotifier()
    {
        try
        {
            string notifierPath = Path.Combine(
                AppContext.BaseDirectory, "LocalTelemetry.Notifier.exe");

            if (!File.Exists(notifierPath))
            {
                Log.Error($"Notifier not found: {notifierPath}");
                return;
            }

            int pid = Environment.ProcessId;

            // Launch the notifier directly
            var psi = new ProcessStartInfo
            {
                FileName = notifierPath,
                Arguments = pid.ToString(),
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            Process.Start(psi);
            Log.Info("Notifier launched");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Notifier launch failed");
        }
    }

    // Shutdown
    protected override void OnExit(WpfExitEventArgs e)
    {
        // Stop the monitor first so no new snapshots arrive during teardown.
        _monitor?.Stop();

        // Dispose UI elements immediately so the overlay + tray icon vanish.
        _overlay?.Dispose();
        _tray?.Dispose();

        _netLogger?.Dispose();
        _monitor?.Dispose();

        // Send graceful shutdown to the notifier via named pipe.
        // The notifier also monitors our PID and self-terminates if we crash.
        if (_notifClient is not null)
        {
            try
            {
                _notifClient.SendShutdownAsync()
                    .Wait(TimeSpan.FromSeconds(2));
            }
            catch { Log.Error("Shutdown notification to notifier process failed"); }
            _notifClient.Dispose();
        }

        _services?.Dispose();
        _cfg.Save();
        Log.Shutdown();
        base.OnExit(e);
    }
}
