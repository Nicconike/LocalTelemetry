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
    private Process? _notifierProcess;
    private bool _notifierLaunched;
    private DateTime _startupUtc;

    // Test seams
    internal static Func<bool>? IsAdministratorCheck { get; set; }
    internal static Action<string[]>? ElevateAndExitAction { get; set; }
    internal static Action<string>? NotifierLauncherAction { get; set; }
    internal static Action<string>? TaskManagerLauncherAction { get; set; }
    internal static Action<ProcessStartInfo>? TaskManagerProcessLauncher { get; set; }
    internal static Action? QuitAppAction { get; set; }
    internal static Func<string, Task>? SrumImporterAction { get; set; }
    internal static Action<ISystemInfo, string>? HardwareInventoryAction { get; set; }
    internal static string? ExeDirOverride { get; set; }
    internal static IPawnIoManager? PawnIoManagerOverride { get; set; }
    internal static bool SuppressSettingsWindowForTesting { get; set; }
    internal static bool SuppressOverlayForTesting { get; set; }
    internal static bool SuppressHardwareMonitorForTesting { get; set; }
    internal static Action<string, string>? MessageBoxAction { get; set; }
    internal static Func<AppSettings, NetUsageLogger, SettingsShell>? SettingsShellFactory { get; set; }
    internal static Func<ProcessStartInfo, Process?>? ProcessStarter { get; set; }
    internal static bool ThrowInToggleMetricsForTesting { get; set; }
    internal static bool ThrowInTrayContextMenuForTesting { get; set; }

    // Elevation
    private static bool IsAdministrator()
    {
        if (IsAdministratorCheck is not null) return IsAdministratorCheck();
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static Action<ProcessStartInfo>? ElevateProcessStarter { get; set; }
    internal static Action<int>? ExitHandler { get; set; }

    private static string BuildElevatedArguments(string[] args)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var a in args)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (a.Contains(' ')) sb.Append('"').Append(a).Append('"');
            else sb.Append(a);
        }
        if (sb.Length > 0) sb.Append(' ');
        sb.Append("--elevated");
        return sb.ToString();
    }

    private static void ShowElevationError(Exception ex)
    {
        if (MessageBoxAction is not null)
        {
            MessageBoxAction("Elevation failed", ex.Message);
            return;
        }

        System.Windows.MessageBox.Show(
            $"LocalTelemetry requires administrator privileges.\n\n{ex.Message}",
            "Elevation failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void ElevateAndExit(string[] args)
    {
        if (ElevateAndExitAction is not null)
        {
            ElevateAndExitAction(args);
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath!,
            Arguments = BuildElevatedArguments(args),
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            if (ElevateProcessStarter is not null)
                ElevateProcessStarter(psi);
            else
                Process.Start(psi);
        }
        catch (Exception ex)
        {
            ShowElevationError(ex);
        }

        if (ExitHandler is not null)
            ExitHandler(0);
        else
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

        Log.Debug("Running as administrator");
        RegisterExceptionHandlers();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        bool fileRunAtStartup = InitializeConfigurationAndLogging();
        var historyFile = InitializeCoreServicesAndHistory(fileRunAtStartup);

        StartUserInterface(e.Args);
        StartBackgroundInitialization(historyFile);
    }

    private void RegisterExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        SessionEnding += HandleSessionEnding;
    }

    private bool InitializeConfigurationAndLogging()
    {
        _startupUtc = DateTime.UtcNow;
        _uiCtx = SynchronizationContext.Current;

        string exeDir = ExeDirOverride ?? AppContext.BaseDirectory;
        AppSettings.InitPaths(exeDir);
        bool isFreshConfig = !File.Exists(AppSettings.ConfigPath);
        _cfg = AppSettings.Load();
        if (isFreshConfig && !TaskbarEmbedder.IsTaskbarCentered())
        {
            _cfg.Overlay.Placement = "right";
        }
        bool fileRunAtStartup = _cfg.RunAtStartup;
        _cfg.RunAtStartup = WindowHelpers.IsStartupEnabled();

        Log.Init(AppSettings.SystemLogPath, AppSettings.MetricsLogPath, _cfg.EnableFileLogging);
        Log.SystemLevel = Log.ParseLevel(_cfg.SystemLogLevel);
        if (AppSettings.LastDirectoryError is not null)
        {
            Log.Warn(AppSettings.LastDirectoryError, "Directory initialization fallback triggered");
        }

        _metricsWasEnabled = _cfg.EnableFileLogging;
        _lastRunAtStartup = _cfg.RunAtStartup;
        string appVer = UpdateCheckService.GetCurrentVersionString();
        string mode = AppSettings.IsStandardMode ? "Standard" : "Portable";
        string osName = SystemInfo.GetOsDisplayVersion();
        Log.SessionStart(appVer, IsAdministrator(), AppSettings.ConfigPath, mode, osName);

        return fileRunAtStartup;
    }

    private TrafficHistoryFile InitializeCoreServicesAndHistory(bool fileRunAtStartup)
    {
        _services = BuildContainer();
        _overlay = _services.GetRequiredService<TaskbarOverlay>();
        _tray = _services.GetRequiredService<TrayIconManager>();
        _tray.SetTrayVisible(_cfg.ShowSystemTrayIcon);
        _tray.SetOverlayVisible(_cfg.Overlay.Visible);
        _alerts = _services.GetRequiredService<AlertService>();
        _monitor = _services.GetRequiredService<HardwareMonitor>();
        _notifClient = _services.GetRequiredService<NotificationClient>();
        Log.Info("Core services initialized");

        var historyFile = new TrafficHistoryFile(AppSettings.NetUsagePath);
        historyFile.Load();

        _netLogger = new NetUsageLogger(_cfg, historyFile);
        Log.Debug("Traffic history logger initialized");

        TrafficHistoryStore.Initialize(historyFile);
        Log.Debug("Traffic history store initialized");

        if (fileRunAtStartup != _cfg.RunAtStartup)
        {
            _cfg.Save();
            Log.Info($"Synchronized Windows startup state (startup {(_cfg.RunAtStartup ? "enabled" : "disabled")})");
        }

        WireEvents();
        return historyFile;
    }

    private void StartUserInterface(string[] args)
    {
        bool minimized = Array.Exists(args, a => a == "--minimized") || _cfg.StartMinimized;

        if (!SuppressOverlayForTesting && _cfg.Overlay.Visible)
            _overlay?.Show();

        if (!SuppressHardwareMonitorForTesting)
            _monitor?.Start();

        if (!minimized)
            OpenSettings();

        Log.Info($"Application started (overlay {(_cfg.Overlay.Visible ? "visible" : "hidden")}, mode {(minimized ? "minimized" : "normal")})");
    }

    private void StartBackgroundInitialization(TrafficHistoryFile historyFile)
    {
        _ = Task.Run(() =>
        {
            try
            {
                if (HardwareInventoryAction is not null)
                    HardwareInventoryAction(new SystemInfoWrapper(), AppSettings.HardwareStatePath);
                else
                    HardwareInventory.LogHardwareConfiguration(new SystemInfoWrapper(), AppSettings.HardwareStatePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to log hardware configuration");
            }

            ApplyVendorColors(_cfg);
            LaunchNotifier();
            RunSrumPreImport(historyFile);

            if (_cfg.RunAtStartup)
                WindowHelpers.SetStartup(true);
        });
    }

    private bool _metricsWasEnabled;
    private void ToggleMetricsLogging(bool enable)
    {
        if (enable == _metricsWasEnabled) return;
        _metricsWasEnabled = enable;
        try
        {
            if (ThrowInToggleMetricsForTesting)
                throw new InvalidOperationException("Simulated metrics logging failure");
            Log.EnableMetrics(enable);
            Log.Info(enable ? "Metrics logging enabled" : "Metrics logging disabled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ToggleMetricsLogging failed");
        }
    }

    private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Error(ex, "Unhandled AppDomain exception");
    }

    private static void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error($"Unobserved Task exception: {e.Exception?.GetType().Name}: {e.Exception?.Message}");
        e.SetObserved();
    }

    private static void HandleDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Dispatcher exception");
        e.Handled = true;
    }

    private void HandleSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        string reason = e.ReasonSessionEnding == ReasonSessionEnding.Shutdown
            ? "Windows System Shutdown"
            : "Windows User Logoff";

        TimeSpan uptime = _startupUtc != default ? DateTime.UtcNow - _startupUtc : TimeSpan.Zero;
        Log.SessionEnd(reason, uptime);
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

    private static void RunSrumPreImport(TrafficHistoryFile historyFile)
    {
        try
        {
            if (SrumImporterAction is not null)
            {
                SrumImporterAction(historyFile.FilePath).GetAwaiter().GetResult();
                return;
            }

            var srumTask = Task.Factory.StartNew(
                () => WindowsNetworkUsageProvider.ImportAndSaveHistoryAsync(historyFile),
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler.Default).Unwrap();
            srumTask.GetAwaiter().GetResult();
            Log.Debug("Historical network usage import completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import network usage history");
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
        if (PawnIoManagerOverride is not null)
            sc.AddSingleton(PawnIoManagerOverride);
        else
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
        if (_tray is null || _overlay is null || _monitor is null) return;

        _tray.OpenSettingsRequested += () => OpenSettings();
        _tray.QuitRequested += () => QuitApp();
        _tray.ToggleOverlayRequested += () =>
        {
            _cfg.Overlay.Visible = !_cfg.Overlay.Visible;
            Log.Info($"User toggled overlay visibility via system tray to {(_cfg.Overlay.Visible ? "visible" : "hidden")}");
            _cfg.Save();
            HandleSettingsApplied();
        };
        _tray.TaskManagerRequested += () =>
        {
            LaunchTaskManager();
        };
        _tray.TaskbarCreated += () =>
        {
            if (_cfg.Overlay.Visible)
            {
                _overlay.RecoverTaskbar("TaskbarCreated notification from explorer");
            }
        };

        _overlay.ShowContextMenuAction = pt =>
        {
            try
            {
                if (ThrowInTrayContextMenuForTesting)
                    throw new InvalidOperationException("Simulated context menu failure");
                Log.Debug($"Forwarding right-click context menu display to TrayIconManager at ({pt.X}, {pt.Y})");
                _tray.ShowOverlayContextMenu(pt);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to show context menu via tray manager at ({pt.X}, {pt.Y})");
            }
        };

        _overlay.IsContextMenuOpen = () => _tray.IsOverlayContextMenuOpen;
        _overlay.GetContextMenuBounds = () => _tray.OverlayContextMenuBounds;
        _overlay.CloseContextMenuAction = () => _tray.CloseOverlayContextMenu();

        _overlay.OnOpenSettingsRequested = () => OpenSettings();
        _overlay.OnToggleOverlayRequested = () =>
        {
            _cfg.Overlay.Visible = !_cfg.Overlay.Visible;
            Log.Info($"User toggled overlay visibility via overlay menu to {(_cfg.Overlay.Visible ? "visible" : "hidden")}");
            _cfg.Save();
            HandleSettingsApplied();
        };
        _overlay.OnExitRequested = () => QuitApp();

        _overlay.OnDoubleClick += (action) =>
        {
            if (string.Equals(action, "taskmanager", StringComparison.OrdinalIgnoreCase))
            {
                LaunchTaskManager();
            }
            else if (string.Equals(action, "settings", StringComparison.OrdinalIgnoreCase))
            {
                Log.Info("Opened Settings (double-click)");
                OpenSettings();
            }
        };

        _monitor.SnapshotReady += HandleSnapshotReady;
    }

    private void LaunchTaskManager()
    {
        if (TaskManagerLauncherAction is not null)
        {
            TaskManagerLauncherAction("taskmanager");
            return;
        }

        try
        {
            var taskmgrPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "taskmgr.exe");
            var psi = new ProcessStartInfo(taskmgrPath) { UseShellExecute = true };
            if (TaskManagerProcessLauncher is not null)
            {
                TaskManagerProcessLauncher(psi);
            }
            else
            {
                using var proc = ProcessStarter is not null ? ProcessStarter(psi) : Process.Start(psi);
            }
            Log.Info("Launched Task Manager");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch taskmgr.exe");
        }
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
        if (SuppressSettingsWindowForTesting) return;

        if (_settingsShell is null || !_settingsShell.IsLoaded)
        {
            if (SettingsShellFactory is not null)
            {
                _settingsShell = SettingsShellFactory(_cfg, _netLogger!);
            }
            else
            {
                _settingsShell = new SettingsShell(_cfg, _netLogger!);
            }
            _settingsShell.TaskbarSlotStatusProvider = () => (_overlay?.IsTaskbarSpaceConstrained ?? false, _overlay?.AvailableSlotWidth ?? 0);
            Log.Debug("SettingsShell created");
            _settingsShell.SettingsApplied += HandleSettingsApplied;
            _settingsShell.Closed += (_, _) =>
            {
                Log.Debug("SettingsShell closed, clearing reference");
                _settingsShell = null;
            };
        }

        _settingsShell.Show();
        _settingsShell.Activate();
    }

    private int _lastPollIntervalMs;
    private bool? _lastRunAtStartup;

    private void HandleSettingsApplied()
    {
        if (_overlay is null) { Log.Error("Cannot apply settings to taskbar overlay (overlay instance is null)"); return; }

        Log.Debug($"Applied settings to overlay (visible: {_cfg.Overlay.Visible})");

        if (_cfg.RunAtStartup != _lastRunAtStartup)
        {
            _lastRunAtStartup = _cfg.RunAtStartup;
            WindowHelpers.SetStartup(_cfg.RunAtStartup);
        }

        _tray?.SetTrayVisible(_cfg.ShowSystemTrayIcon);
        _tray?.SetOverlayVisible(_cfg.Overlay.Visible);
        if (_cfg.Overlay.Visible)
        {
            _overlay.Show();
            _overlay.RefreshOverlay();
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
        Log.SystemLevel = Log.ParseLevel(_cfg.SystemLogLevel);

        if (_cfg.Alerts.Enabled && _cfg.Alerts.ShowToastNotif)
            EnsureNotifierLaunched();
        else
            EnsureNotifierStopped();
    }

    /// <summary>Full quit - stops monitor, saves settings, exits process.</summary>
    private void QuitApp()
    {
        QuitAppAction?.Invoke();
        _settingsShell?.Close();
        Shutdown();
    }

    // Notification helper (non-elevated child process)
    private void LaunchNotifier()
    {
        EnsureNotifierLaunched();
    }

    internal void EnsureNotifierLaunched()
    {
        if (_notifierLaunched) return;
        if (_cfg != null && (!_cfg.Alerts.Enabled || !_cfg.Alerts.ShowToastNotif)) return;
        try
        {
            string notifierPath = Path.Combine(
                AppContext.BaseDirectory, "LocalTelemetry.Notifier.exe");

            if (NotifierLauncherAction is not null)
            {
                NotifierLauncherAction(AppContext.BaseDirectory);
                _notifierLaunched = true;
                return;
            }

            if (!File.Exists(notifierPath))
            {
                Log.Error($"Notification helper executable not found at '{notifierPath}'");
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

            _notifierProcess = Process.Start(psi);
            _notifierLaunched = true;
            Log.Debug("Notification helper process launched");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch notification helper process");
        }
    }

    internal void EnsureNotifierStopped()
    {
        if (!_notifierLaunched && _notifierProcess is null) return;
        _notifierLaunched = false;
        try
        {
            if (_notifierProcess is { HasExited: false })
            {
                _notifClient?.SendShutdownAsync().ContinueWith(_ => { });
                try
                {
                    if (!_notifierProcess.WaitForExit(800))
                    {
                        _notifierProcess.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Exception while terminating notifier process");
                }
                finally
                {
                    _notifierProcess.Dispose();
                    _notifierProcess = null;
                }
            }
            else
            {
                _notifierProcess?.Dispose();
                _notifierProcess = null;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Exception while stopping notifier process");
            try
            {
                _notifierProcess?.Dispose();
            }
            catch (Exception disposeEx)
            {
                Log.Debug(disposeEx, "Failed to dispose notifier process during exception handling");
            }
            _notifierProcess = null;
        }
    }

    // Shutdown
    protected override void OnExit(WpfExitEventArgs e)
    {
        // Stop the monitor first so no new snapshots arrive during teardown.
        _monitor?.Stop();

        TimeSpan uptime = _startupUtc != default ? DateTime.UtcNow - _startupUtc : TimeSpan.Zero;
        Log.SessionEnd("Application Quit", uptime);

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
            catch (Exception ex)
            {
                Log.Error(ex, "Shutdown notification to notifier process failed");
            }
            _notifClient.Dispose();
        }

        _services?.Dispose();
        _cfg.Save();
        Log.Shutdown();
        base.OnExit(e);
    }
}
