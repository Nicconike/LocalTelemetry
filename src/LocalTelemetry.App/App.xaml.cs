using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
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
    private TaskbarOverlayManager? _overlayManager;
    private TrayIconManager? _tray;
    private AlertService? _alerts;
    private SettingsShell? _settingsShell;
    private AppSettings _cfg = new AppSettings();
    private NotificationClient? _notifClient;
    private Process? _notifierProcess;
    private bool _notifierLaunched;
    private DateTime _startupUtc;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _singleInstanceEvent;
    private RegisteredWaitHandle? _singleInstanceWaitHandle;
    private bool _isAppSessionLocked;
    private bool _isAppPowerSuspended;
    internal static string SingleInstanceMutexName { get; set; } = @"Local\LocalTelemetry_App_SingleInstance";
    internal static string SingleInstanceEventName { get; set; } = @"Local\LocalTelemetry_App_ShowSettingsEvent";
    internal const string ShowSettingsMessageName = "LocalTelemetry_ShowSettingsWindow_Message";
    internal static bool EnforceSingleInstance { get; set; } = true;

    // Out-of-process Settings synchronization
    private bool _isSettingsProcess;
    private Mutex? _settingsMutex;
    private EventWaitHandle? _settingsShowEvent;
    private RegisteredWaitHandle? _settingsShowWaitHandle;
    private EventWaitHandle? _settingsCloseEvent;
    private RegisteredWaitHandle? _settingsCloseWaitHandle;
    private EventWaitHandle? _settingsReloadEvent;
    private RegisteredWaitHandle? _settingsReloadWaitHandle;
    private EventWaitHandle? _settingsTrafficUpdatedEvent;
    private RegisteredWaitHandle? _settingsTrafficUpdatedWaitHandle;
    private Process? _settingsProcess;

    internal static string SettingsMutexName { get; set; } = @"Local\LocalTelemetry_Settings_SingleInstance";
    internal static string SettingsShowEventName { get; set; } = @"Local\LocalTelemetry_Settings_ShowEvent";
    internal static string SettingsReloadEventName { get; set; } = @"Local\LocalTelemetry_Settings_ReloadEvent";
    internal static string SettingsCloseEventName { get; set; } = @"Local\LocalTelemetry_Settings_CloseEvent";
    internal static string SettingsTrafficUpdatedEventName { get; set; } = @"Local\LocalTelemetry_Settings_TrafficUpdatedEvent";
    internal static bool LaunchSettingsOutOfProcess { get; set; } = true;
    internal static Action<ProcessStartInfo>? SettingsProcessLauncher { get; set; }

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

    private static void SignalRunningMainInstance()
    {
        Log.Info("Another instance of LocalTelemetry is already running. Signaling it to open Settings.");
        try
        {
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "AllowSetForegroundWindow failed");
        }

        try
        {
            if (EventWaitHandle.TryOpenExisting(SingleInstanceEventName, out var showEvent))
            {
                using (showEvent)
                {
                    showEvent.Set();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to signal single-instance event");
        }
    }

    private void RegisterSingleInstanceWaitHandle()
    {
        try
        {
            _singleInstanceEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceEventName);
            _singleInstanceWaitHandle = ThreadPool.RegisterWaitForSingleObject(
                _singleInstanceEvent,
                (state, timedOut) =>
                {
                    if (!timedOut)
                    {
                        Dispatcher.InvokeAsync(OpenSettings);
                    }
                },
                null,
                -1,
                false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to register single-instance EventWaitHandle");
        }
    }

    private bool TryAcquireMainSingleInstance()
    {
        if (!EnforceSingleInstance) return true;

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            SignalRunningMainInstance();
            if (ExitHandler is not null)
                ExitHandler(0);
            else
                Environment.Exit(0);
            return false;
        }

        RegisterSingleInstanceWaitHandle();
        return true;
    }

    private const string VisibilityVisible = "visible";
    private const string VisibilityHidden = "hidden";

    // Startup
    protected override void OnStartup(WpfStartupEventArgs e)
    {
        base.OnStartup(e);

        bool isSettingsMode = Array.Exists(e.Args, a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase));
        if (isSettingsMode)
        {
            StartSettingsMode(e.Args);
            return;
        }

        if (!IsAdministrator() && !Array.Exists(e.Args, a => a == "--elevated"))
        {
            ElevateAndExit(e.Args);
            return;
        }

        if (!TryAcquireMainSingleInstance())
        {
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

    private static void SignalRunningSettingsInstance()
    {
        Log.Info("Another instance of Settings is already running. Signaling it to foreground.");
        try
        {
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "AllowSetForegroundWindow failed");
        }

        try
        {
            if (EventWaitHandle.TryOpenExisting(SettingsShowEventName, out var showEvent))
            {
                using (showEvent)
                {
                    showEvent.Set();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to signal settings show event");
        }
    }

    private bool TryAcquireSettingsSingleInstance()
    {
        if (!EnforceSingleInstance) return true;

        _settingsMutex = new Mutex(true, SettingsMutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            SignalRunningSettingsInstance();
            if (ExitHandler is not null)
                ExitHandler(0);
            else
                Environment.Exit(0);
            return false;
        }

        return true;
    }

    private void ActivateSettingsShellWindow()
    {
        if (_settingsShell is null) return;
        if (_settingsShell.WindowState == WindowState.Minimized)
        {
            _settingsShell.WindowState = WindowState.Normal;
        }
        _settingsShell.Show();
        _settingsShell.Activate();
        _settingsShell.Focus();
        var hwnd = new WindowInteropHelper(_settingsShell).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(hwnd);
        }
    }

    private void RegisterSettingsWaitHandles()
    {
        if (!EnforceSingleInstance) return;

        try
        {
            _settingsShowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SettingsShowEventName);
            _settingsShowWaitHandle = ThreadPool.RegisterWaitForSingleObject(
                _settingsShowEvent,
                (state, timedOut) =>
                {
                    if (!timedOut)
                    {
                        Dispatcher.InvokeAsync(ActivateSettingsShellWindow);
                    }
                },
                null,
                -1,
                false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to register settings show EventWaitHandle");
        }

        try
        {
            _settingsCloseEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SettingsCloseEventName);
            _settingsCloseWaitHandle = ThreadPool.RegisterWaitForSingleObject(
                _settingsCloseEvent,
                (state, timedOut) =>
                {
                    if (!timedOut)
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            _settingsShell?.Close();
                            Shutdown(0);
                        });
                    }
                },
                null,
                -1,
                false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to register settings close EventWaitHandle");
        }

        try
        {
            _settingsTrafficUpdatedEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SettingsTrafficUpdatedEventName);
            _settingsTrafficUpdatedWaitHandle = ThreadPool.RegisterWaitForSingleObject(
                _settingsTrafficUpdatedEvent,
                (state, timedOut) =>
                {
                    if (!timedOut)
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (_settingsShell is not null)
                            {
                                Log.Debug("Settings process received cross-process traffic update notification");
                                _settingsShell.RefreshTrafficHistory();
                            }
                        });
                    }
                },
                null,
                -1,
                false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to register settings traffic updated EventWaitHandle");
        }
    }

    private void SetupSettingsProcessMainWindow()
    {
        RegisterExceptionHandlers();
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        string exeDir = ExeDirOverride ?? AppContext.BaseDirectory;
        AppSettings.InitPaths(exeDir);
        UpdateConfig.DefaultIncludePrereleases = UpdateCheckService.IsPrereleaseVersion();
        _cfg = AppSettings.Load();
        ApplyVendorColors(_cfg);

        Log.Init(AppSettings.SystemLogPath, AppSettings.MetricsLogPath, _cfg.EnableFileLogging);
        Log.SystemLevel = Log.ParseLevel(_cfg.SystemLogLevel);

        var historyFile = new TrafficHistoryFile(AppSettings.NetUsagePath);
        historyFile.Load();
        TrafficHistoryStore.Initialize(historyFile);

        _settingsShell = new SettingsShell(_cfg);
        MainWindow = _settingsShell;

        _settingsShell.SettingsApplied += () =>
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(SettingsReloadEventName, out var reloadEvent))
                {
                    using (reloadEvent)
                    {
                        reloadEvent.Set();
                    }
                    Log.Debug("Signaled daemon to reload configuration");
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to signal settings reload event to daemon");
            }
        };

        _settingsShell.Closed += (_, _) =>
        {
            _settingsShell = null;
            Shutdown(0);
        };

        if (!SuppressSettingsWindowForTesting)
        {
            _settingsShell.Show();
            _settingsShell.Activate();
        }
    }

    private void StartSettingsMode(string[] args)
    {
        _isSettingsProcess = true;
        if (!IsAdministrator() && !Array.Exists(args, a => a == "--elevated"))
        {
            ElevateAndExit(args);
            return;
        }

        if (!TryAcquireSettingsSingleInstance())
        {
            return;
        }

        RegisterSettingsWaitHandles();
        SetupSettingsProcessMainWindow();
    }

    private void RegisterSettingsReloadListener()
    {
        try
        {
            _settingsReloadEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SettingsReloadEventName);
            _settingsReloadWaitHandle = ThreadPool.RegisterWaitForSingleObject(
                _settingsReloadEvent,
                (state, timedOut) =>
                {
                    if (!timedOut)
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            Log.Info("Configuration change detected from Settings process; reloading.");
                            var reloaded = AppSettings.Load();
                            _cfg.CopyFrom(reloaded);
                            ApplyVendorColors(_cfg);
                            HandleSettingsApplied();
                        });
                    }
                },
                null,
                -1,
                false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to register settings reload EventWaitHandle");
        }
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

        string exeDir = ExeDirOverride ?? AppContext.BaseDirectory;
        AppSettings.InitPaths(exeDir);
        UpdateConfig.DefaultIncludePrereleases = UpdateCheckService.IsPrereleaseVersion();
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
        _overlayManager = _services.GetRequiredService<TaskbarOverlayManager>();
        _overlay = _overlayManager.PrimaryOverlay;
        _tray = _services.GetRequiredService<TrayIconManager>();
        _tray.SetTrayVisible(_cfg.ShowSystemTrayIcon);
        _tray.SetOverlayVisible(_cfg.Overlay.Visible);
        _alerts = _services.GetRequiredService<AlertService>();
        _monitor = _services.GetRequiredService<HardwareMonitor>();
        _notifClient = _services.GetRequiredService<NotificationClient>();
        Log.Debug("Core services initialized");

        var historyFile = new TrafficHistoryFile(AppSettings.NetUsagePath);
        historyFile.Load();

        _netLogger = new NetUsageLogger(_cfg, historyFile);
        Log.Debug("Traffic history logger initialized");

        TrafficHistoryStore.Initialize(historyFile);
        TrafficHistoryStore.HistoryUpdated += SignalTrafficUpdated;
        Log.Debug("Traffic history store initialized");

        if (fileRunAtStartup != _cfg.RunAtStartup)
        {
            _cfg.Save();
            Log.Info($"Synchronized Windows startup state (startup {(_cfg.RunAtStartup ? "enabled" : "disabled")})");
        }

        WireEvents();
        RegisterSettingsReloadListener();
        return historyFile;
    }

    private void StartUserInterface(string[] args)
    {
        bool minimized = Array.Exists(args, a => a == "--minimized") || _cfg.StartMinimized;

        if (!SuppressOverlayForTesting && _cfg.Overlay.Visible)
        {
            if (_overlayManager is not null)
                _overlayManager.Show();
            else
                _overlay?.Show();
        }

        if (!SuppressHardwareMonitorForTesting)
            _monitor?.Start();

        if (!minimized)
            OpenSettings();

        Log.Info($"Application started (overlay {(_cfg.Overlay.Visible ? VisibilityVisible : VisibilityHidden)}, mode {(minimized ? "minimized" : "normal")})");
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

            try
            {
                ApplyVendorColors(_cfg);
                if (_cfg.Alerts.Enabled && _cfg.Alerts.ShowToastNotif)
                {
                    LaunchNotifier();
                }
                else
                {
                    EnsureNotifierStopped();
                }
                RunSrumPreImport(historyFile);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Background initialization encountered an error");
            }

            try
            {
                NativeMethods.EmptyWorkingSet(Process.GetCurrentProcess().Handle);
                Log.Debug("Post-startup working set trimmed successfully");
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to trim working set after background initialization");
            }
        }).ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
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
        Log.Error(e.Exception, "Unobserved Task exception");
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
                SignalTrafficUpdated();
                return;
            }

            bool hasRecords = historyFile.GetAllRecords().Any(r => r.DownBytes > 0 || r.UpBytes > 0);
            if (hasRecords)
            {
                Log.Debug("Historical traffic records already present; skipping initial SRUM import");
                return;
            }

            var srumTask = Task.Factory.StartNew(
                () => WindowsNetworkUsageProvider.ImportAndSaveHistoryAsync(historyFile),
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler.Default).Unwrap();
            srumTask.GetAwaiter().GetResult();
            Log.Debug("Historical network usage import completed");
            SignalTrafficUpdated();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import network usage history");
        }
    }

    private static void SignalTrafficUpdated()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(SettingsTrafficUpdatedEventName, out var trafficEvent))
            {
                using (trafficEvent)
                {
                    trafficEvent.Set();
                }
                Log.Debug("Signaled Settings process that traffic history was updated");
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to signal traffic history updated event");
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
        sc.AddSingleton<TaskbarOverlayManager>();
        sc.AddSingleton(sp => sp.GetRequiredService<TaskbarOverlayManager>().PrimaryOverlay);
        sc.AddSingleton<TrayIconManager>();
        sc.AddSingleton<NotificationClient>();
        sc.AddSingleton<AlertService>();

        return sc.BuildServiceProvider();
    }

    // Event wiring
    private void WireEvents()
    {
        if (_tray is null || _monitor is null || (_overlayManager is null && _overlay is null)) return;

        Action<System.Drawing.Point> showCtx = ShowTrayContextMenu;

        WireTrayEvents();
        if (_overlayManager is not null)
        {
            WireOverlayManagerEvents(showCtx);
        }
        else if (_overlay is not null)
        {
            WireSingleOverlay(showCtx);
        }

        _monitor.SnapshotReady += HandleSnapshotReady;
        WireLifecycleEvents();
    }

    private void WireLifecycleEvents()
    {
        var lifecycle = HardwareLifecycleManager.Instance;
        lifecycle.PowerSuspended += HandlePowerSuspended;
        lifecycle.PowerResumed += HandlePowerResumed;
        lifecycle.SessionLockStateChanged += HandleSessionLockStateChanged;
    }

    private void HandlePowerSuspended()
    {
        _isAppPowerSuspended = true;
        _overlayManager?.PauseAllTimers();
        _overlay?.PauseTimers();
    }

    private void HandlePowerResumed()
    {
        _isAppPowerSuspended = false;
        if (!_isAppSessionLocked)
        {
            _overlayManager?.ResumeAllTimers();
            _overlay?.ResumeTimers();
        }
    }

    private void HandleSessionLockStateChanged(bool isLocked)
    {
        _isAppSessionLocked = isLocked;
        if (isLocked)
        {
            _overlayManager?.PauseAllTimers();
            _overlay?.PauseTimers();
        }
        else if (!_isAppPowerSuspended)
        {
            _overlayManager?.ResumeAllTimers();
            _overlay?.ResumeTimers();
        }
    }

    private void WireSingleOverlay(Action<System.Drawing.Point> showCtx)
    {
        if (_overlay is null || _tray is null) return;
        _overlay.ShowContextMenuAction = showCtx;
        _overlay.IsContextMenuOpen = () => _tray.IsOverlayContextMenuOpen;
        _overlay.IsPointInsideContextMenu = pt => _tray.IsPointInsideOverlayContextMenu(pt);
        _overlay.GetContextMenuBounds = () => _tray.OverlayContextMenuBounds;
        _overlay.CloseContextMenuAction = () =>
        {
            _tray.CloseOverlayContextMenu();
            _overlay.StopMouseHook();
        };
        _overlay.OnOpenSettingsRequested = () => OpenSettings();
        _overlay.OnToggleOverlayRequested = () =>
        {
            _cfg.Overlay.Visible = !_cfg.Overlay.Visible;
            Log.Info($"User toggled overlay visibility via overlay menu to {(_cfg.Overlay.Visible ? VisibilityVisible : VisibilityHidden)}");
            _cfg.Save();
            HandleSettingsApplied();
        };
        _overlay.OnExitRequested = () => QuitApp();
        _overlay.OnDoubleClick += HandleOverlayDoubleClickAction;
    }

    private void ShowTrayContextMenu(System.Drawing.Point pt)
    {
        try
        {
            if (ThrowInTrayContextMenuForTesting)
                throw new InvalidOperationException("Simulated context menu failure");
            Log.Debug($"Forwarding right-click context menu display to TrayIconManager at ({pt.X}, {pt.Y})");
            _tray?.ShowOverlayContextMenu(pt);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to show context menu via tray manager at ({pt.X}, {pt.Y})");
        }
    }

    private void WireTrayEvents()
    {
        if (_tray is null) return;

        _tray.OpenSettingsRequested += () => OpenSettings();
        _tray.QuitRequested += () => QuitApp();
        _tray.ToggleOverlayRequested += () =>
        {
            _cfg.Overlay.Visible = !_cfg.Overlay.Visible;
            Log.Info($"User toggled overlay visibility via system tray to {(_cfg.Overlay.Visible ? VisibilityVisible : VisibilityHidden)}");
            _cfg.Save();
            HandleSettingsApplied();
        };
        _tray.TaskManagerRequested += () => LaunchTaskManager();
        _tray.TaskbarCreated += () =>
        {
            if (!_cfg.Overlay.Visible) return;
            if (_overlayManager is not null)
            {
                _overlayManager.RecoverTaskbars("TaskbarCreated notification from explorer");
            }
            else
            {
                _overlay?.RecoverTaskbar("TaskbarCreated notification from explorer");
            }
        };
    }

    private void WireOverlayManagerEvents(Action<System.Drawing.Point> showCtx)
    {
        if (_overlayManager is null || _tray is null) return;

        _overlayManager.ShowContextMenuAction = showCtx;
        _overlayManager.IsContextMenuOpen = () => _tray.IsOverlayContextMenuOpen;
        _overlayManager.IsPointInsideContextMenu = pt => _tray.IsPointInsideOverlayContextMenu(pt);
        _overlayManager.GetContextMenuBounds = () => _tray.OverlayContextMenuBounds;
        _overlayManager.CloseContextMenuAction = () =>
        {
            _tray.CloseOverlayContextMenu();
            _overlayManager.StopMouseHook();
        };
        _overlayManager.OnOpenSettingsRequested = () => OpenSettings();
        _overlayManager.OnToggleOverlayRequested = () =>
        {
            _cfg.Overlay.Visible = !_cfg.Overlay.Visible;
            Log.Info($"User toggled overlay visibility via overlay menu to {(_cfg.Overlay.Visible ? VisibilityVisible : VisibilityHidden)}");
            _cfg.Save();
            HandleSettingsApplied();
        };
        _overlayManager.OnExitRequested = () => QuitApp();
        _overlayManager.OnDoubleClick += HandleOverlayDoubleClickAction;
    }

    private void HandleOverlayDoubleClickAction(string action)
    {
        if (string.Equals(action, "taskmanager", StringComparison.OrdinalIgnoreCase))
        {
            LaunchTaskManager();
        }
        else if (string.Equals(action, "settings", StringComparison.OrdinalIgnoreCase))
        {
            OpenSettings();
        }
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

        if (_overlayManager is not null)
            _overlayManager.UpdateSnapshot(snap);
        else
            _overlay!.UpdateSnapshot(snap);

        if (_tray is not null)
            Dispatcher.InvokeAsync(() => _tray.Update(snap));
    }

    // Tray action handlers
    private void OpenSettings()
    {
        if (SuppressSettingsWindowForTesting) return;

        if (Dispatcher is not null && !Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(OpenSettings);
            return;
        }

        if (SettingsShellFactory is not null || !LaunchSettingsOutOfProcess)
        {
            RunInProcessSettings();
            return;
        }

        LaunchSettingsProcess();
    }

    private void LaunchSettingsProcess()
    {
        try
        {
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "AllowSetForegroundWindow failed");
        }

        bool settingsRunning = false;
        try
        {
            if (Mutex.TryOpenExisting(SettingsMutexName, out var mutex))
            {
                using (mutex)
                {
                    settingsRunning = true;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to check existing Settings mutex");
        }

        if (settingsRunning)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(SettingsShowEventName, out var showEvent))
                {
                    using (showEvent)
                    {
                        showEvent.Set();
                        Log.Info("Settings process already running; signaled show event");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to signal show event to existing Settings process");
            }
        }

        try
        {
            if (_settingsProcess is { HasExited: true })
            {
                _settingsProcess.Dispose();
                _settingsProcess = null;
            }

            string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "LocalTelemetry.exe");
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--settings",
                UseShellExecute = true,
            };

            if (SettingsProcessLauncher is not null)
            {
                SettingsProcessLauncher(psi);
            }
            else if (ProcessStarter is not null)
            {
                _settingsProcess = ProcessStarter(psi);
            }
            else
            {
                _settingsProcess = Process.Start(psi);
            }

            Log.Info("Launched Settings process");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch Settings process");
        }
    }

    private bool TryActivateExistingSettingsShell()
    {
        if (_settingsShell is null) return false;

        var handle = new WindowInteropHelper(_settingsShell).Handle;
        if (handle != IntPtr.Zero && NativeMethods.IsWindow(handle))
        {
            if (_settingsShell.WindowState == WindowState.Minimized)
            {
                _settingsShell.WindowState = WindowState.Normal;
            }
            _settingsShell.Show();
            _settingsShell.Activate();
            _settingsShell.Focus();
            NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(handle);
            return true;
        }

        _settingsShell = null;
        return false;
    }

    private void WireSettingsShellLifecycle(Action<bool, int> slotChangedHandler)
    {
        if (_overlayManager is not null)
        {
            _overlayManager.TaskbarSpaceConstraintChanged += slotChangedHandler;
        }
        else if (_overlay is not null)
        {
            _overlay.TaskbarSpaceConstraintChanged += slotChangedHandler;
        }

        Log.Debug("SettingsShell created");
        _settingsShell!.SettingsApplied += HandleSettingsApplied;
        _settingsShell.Closed += (_, _) =>
        {
            if (_overlayManager is not null)
            {
                _overlayManager.TaskbarSpaceConstraintChanged -= slotChangedHandler;
            }
            else if (_overlay is not null)
            {
                _overlay.TaskbarSpaceConstraintChanged -= slotChangedHandler;
            }
            Log.Debug("SettingsShell closed, clearing reference and trimming working set");
            _settingsShell = null;

            try
            {
                NativeMethods.EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to trim working set after SettingsShell closed");
            }
        };
    }

    private void RunInProcessSettings()
    {
        try
        {
            Log.Info("Opening Settings window in-process");

            if (TryActivateExistingSettingsShell())
            {
                return;
            }

            _settingsShell = SettingsShellFactory is not null
                ? SettingsShellFactory(_cfg, _netLogger!)
                : new SettingsShell(_cfg, _netLogger!);

            _settingsShell.TaskbarSlotStatusProvider = () => (_overlayManager?.IsTaskbarSpaceConstrained ?? _overlay?.IsTaskbarSpaceConstrained ?? false, _overlayManager?.AvailableSlotWidth ?? _overlay?.AvailableSlotWidth ?? 0);
            Action<bool, int> slotChangedHandler = (constrained, width) =>
            {
                _settingsShell?.NotifyTaskbarSlotChanged(constrained, width);
            };

            WireSettingsShellLifecycle(slotChangedHandler);

            _settingsShell.Show();
            _settingsShell.Activate();
            _settingsShell.Focus();
            var hwnd = new WindowInteropHelper(_settingsShell).Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(hwnd);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open in-process settings window");
        }
    }

    private int _lastPollIntervalMs;
    private bool? _lastRunAtStartup;

    private void HandleSettingsApplied()
    {
        if (_overlayManager is null && _overlay is null)
        {
            Log.Error("Cannot apply settings to taskbar overlay (overlay instance is null)");
            return;
        }

        Log.Debug($"Applied settings to overlay (visible: {_cfg.Overlay.Visible})");

        if (_cfg.RunAtStartup != _lastRunAtStartup)
        {
            _lastRunAtStartup = _cfg.RunAtStartup;
            WindowHelpers.SetStartup(_cfg.RunAtStartup);
        }

        _tray?.SetTrayVisible(_cfg.ShowSystemTrayIcon);
        _tray?.SetOverlayVisible(_cfg.Overlay.Visible);
        SyncOverlayVisibility();

        if (_settingsShell is not null && _settingsShell.IsLoaded)
        {
            _settingsShell.SendUpdatedSettings();
        }

        SyncMonitorInterval();

        // Toggle metrics logging (system log is always on)
        bool shouldLog = _cfg.EnableFileLogging;
        ToggleMetricsLogging(shouldLog);
        Log.SystemLevel = Log.ParseLevel(_cfg.SystemLogLevel);

        if (_cfg.Alerts.Enabled && _cfg.Alerts.ShowToastNotif)
            EnsureNotifierLaunched();
        else
            EnsureNotifierStopped();
    }

    private void SyncOverlayVisibility()
    {
        if (_cfg.Overlay.Visible)
        {
            if (_overlayManager is not null)
            {
                _overlayManager.Show();
                _overlayManager.RefreshOverlays();
            }
            else if (_overlay is not null)
            {
                _overlay.Show();
                _overlay.RefreshOverlay();
            }
        }
        else
        {
            if (_overlayManager is not null)
            {
                _overlayManager.Hide();
            }
            else if (_overlay is not null)
            {
                _overlay.Hide();
            }
        }
    }

    private void SyncMonitorInterval()
    {
        int ms = Math.Max(100, _cfg.Monitoring.PollIntervalMs);
        if (ms != _lastPollIntervalMs)
        {
            _lastPollIntervalMs = ms;
            _monitor?.Restart(ms);
        }
    }

    /// <summary>Full quit - stops monitor, saves settings, exits process.</summary>
    private void QuitApp()
    {
        Log.Info("Application quit requested by user");
        QuitAppAction?.Invoke();
        try
        {
            if (EventWaitHandle.TryOpenExisting(SettingsCloseEventName, out var closeEvent))
            {
                using (closeEvent)
                {
                    closeEvent.Set();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to signal settings close event during QuitApp");
        }
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
        if (!_notifierLaunched && _notifierProcess is null)
        {
            KillOrphanedNotifierProcesses();
            return;
        }

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
        finally
        {
            KillOrphanedNotifierProcesses();
        }
    }

    private static void KillOrphanedNotifierProcesses()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("LocalTelemetry.Notifier"))
            {
                try
                {
                    using (proc)
                    {
                        if (!proc.HasExited)
                        {
                            proc.Kill(entireProcessTree: true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to terminate orphaned notifier process");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to enumerate notifier processes for termination");
        }
    }

    private void ExitSettingsProcess(WpfExitEventArgs e)
    {
        Log.Debug("Settings process exiting cleanly");
        _settingsShowWaitHandle?.Unregister(null);
        _settingsShowWaitHandle = null;
        _settingsShowEvent?.Dispose();
        _settingsShowEvent = null;

        _settingsCloseWaitHandle?.Unregister(null);
        _settingsCloseWaitHandle = null;
        _settingsCloseEvent?.Dispose();
        _settingsCloseEvent = null;

        _settingsTrafficUpdatedWaitHandle?.Unregister(null);
        _settingsTrafficUpdatedWaitHandle = null;
        _settingsTrafficUpdatedEvent?.Dispose();
        _settingsTrafficUpdatedEvent = null;

        if (_settingsMutex is not null)
        {
            try
            {
                _settingsMutex.ReleaseMutex();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Exception releasing settings mutex");
            }
            _settingsMutex.Dispose();
            _settingsMutex = null;
        }

        base.OnExit(e);
    }

    private void SendGracefulShutdownToNotifier()
    {
        if (_notifClient is null) return;
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

    private void CloseExternalSettingsProcess()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(SettingsCloseEventName, out var closeEvent))
            {
                using (closeEvent)
                {
                    closeEvent.Set();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to signal settings close event");
        }

        if (_settingsProcess is { HasExited: false })
        {
            try
            {
                if (!_settingsProcess.WaitForExit(500))
                {
                    _settingsProcess.Kill();
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Exception terminating settings process");
            }
            finally
            {
                _settingsProcess.Dispose();
                _settingsProcess = null;
            }
        }
        else
        {
            _settingsProcess?.Dispose();
            _settingsProcess = null;
        }
    }

    private void CleanUpIpcAndInstanceHandles()
    {
        _settingsReloadWaitHandle?.Unregister(null);
        _settingsReloadWaitHandle = null;
        _settingsReloadEvent?.Dispose();
        _settingsReloadEvent = null;

        _settingsShowWaitHandle?.Unregister(null);
        _settingsShowWaitHandle = null;
        _settingsShowEvent?.Dispose();
        _settingsShowEvent = null;

        _settingsCloseWaitHandle?.Unregister(null);
        _settingsCloseWaitHandle = null;
        _settingsCloseEvent?.Dispose();
        _settingsCloseEvent = null;

        _settingsTrafficUpdatedWaitHandle?.Unregister(null);
        _settingsTrafficUpdatedWaitHandle = null;
        _settingsTrafficUpdatedEvent?.Dispose();
        _settingsTrafficUpdatedEvent = null;

        if (_settingsMutex is not null)
        {
            try
            {
                _settingsMutex.ReleaseMutex();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Exception releasing settings mutex");
            }
            _settingsMutex.Dispose();
            _settingsMutex = null;
        }

        _singleInstanceWaitHandle?.Unregister(null);
        _singleInstanceWaitHandle = null;
        _singleInstanceEvent?.Dispose();
        _singleInstanceEvent = null;

        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Exception releasing single-instance mutex");
            }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
    }

    // Shutdown
    protected override void OnExit(WpfExitEventArgs e)
    {
        if (_isSettingsProcess)
        {
            ExitSettingsProcess(e);
            return;
        }

        // Stop the monitor first so no new snapshots arrive during teardown.
        _monitor?.Stop();

        TimeSpan uptime = _startupUtc != default ? DateTime.UtcNow - _startupUtc : TimeSpan.Zero;
        Log.SessionEnd("Application Quit", uptime);

        // Dispose UI elements immediately so the overlay + tray icon vanish.
        _overlayManager?.Dispose();
        _overlay?.Dispose();
        _tray?.Dispose();

        _netLogger?.Dispose();
        _monitor?.Dispose();

        SendGracefulShutdownToNotifier();

        _services?.Dispose();
        _cfg.Save();
        Log.Shutdown();

        CloseExternalSettingsProcess();
        CleanUpIpcAndInstanceHandles();

        base.OnExit(e);
    }
}
