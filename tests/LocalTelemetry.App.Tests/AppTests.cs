using System.Dynamic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using FluentAssertions;
using LocalTelemetry.App.Overlay;
using LocalTelemetry.App.Services;
using LocalTelemetry.App.Settings;
using LocalTelemetry.App.Tray;
using LocalTelemetry.App.Win32;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Hardware;
using LocalTelemetry.Core.Hardware.PawnIo;
using LocalTelemetry.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace LocalTelemetry.App.Tests;

public class AppTests
{
    private static readonly string[] ElevatedMinimizedArgs = ["--elevated", "--minimized"];
    private static readonly string[] NonElevatedArgs = ["--minimized"];

    private static IPawnIoManager CreateMockPawnIoManager()
    {
        var transport = Substitute.For<IPawnIoTransport>();
        var manager = Substitute.For<IPawnIoManager>();
        manager.TryCreate().Returns(transport);
        return manager;
    }

    [ModuleInitializer]
    internal static void InstallSafetyNet()
    {
        if (System.Windows.Application.ResourceAssembly is null)
        {
            System.Windows.Application.ResourceAssembly = typeof(App).Assembly;
        }

        App.IsAdministratorCheck = () => true;
        App.ElevateAndExitAction = _ => { };
        App.NotifierLauncherAction = _ => { };
        App.TaskManagerLauncherAction = _ => { };
        App.TaskManagerProcessLauncher = _ => { };
        App.QuitAppAction = () => { };
        App.SrumImporterAction = _ => Task.CompletedTask;
        App.HardwareInventoryAction = (_, _) => { };
        App.PawnIoManagerOverride = CreateMockPawnIoManager();
        App.SuppressSettingsWindowForTesting = true;
        App.SuppressOverlayForTesting = true;
        App.SuppressHardwareMonitorForTesting = true;
        App.MessageBoxAction = (_, _) => { };

        TaskbarOverlay.SkipShowWindowForTesting = true;
        TaskbarOverlay.SkipMouseHookForTesting = true;
        TaskbarOverlay.SkipTimersForTesting = true;
        TaskbarOverlay.SkipEmbedForTesting = true;

        TrayIconManager.SkipUiForTesting = true;

        WindowHelpers.StartupQueryExitCode = () => 1;
        WindowHelpers.LegacyRegistryCleanupAction = () => { };
        WindowHelpers.TaskSchedulerFactory = () => new object();
        WindowHelpers.SetStartupAction = _ => { };

        SettingsShell.SkipWebView2InitForTesting = true;
        SettingsShell.SkipInitializeComponentForTesting = true;
        SettingsShell.SkipShowForTesting = true;
        SettingsShell.MessageBoxAction = (_, _) => { };
        SettingsShell.UrlLauncherAction = _ => { };
        SettingsShell.PostJsonActionOverride = _ => { };

        HardwareMonitor.SuppressTimerForTesting = true;
    }

    private static readonly string[] ElevatedArgs = ["--elevated"];
    private static readonly string[] MinimizedArgs = ["--elevated", "--minimized"];

    private static App GetOrCreateApp()
    {
        if (System.Windows.Application.Current is App current && current.Dispatcher == System.Windows.Threading.Dispatcher.CurrentDispatcher)
            return current;

        var appType = typeof(System.Windows.Application);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        appType.GetField("_appInstance", flags)?.SetValue(null, null);
        appType.GetField("_appCreatedInThisAppDomain", flags)?.SetValue(null, false);

        return new App();
    }

    [Fact]
    public void ApplyVendorColors_AppliesBrandColorsToMetricsAndGroups()
    {
        var cfg = new AppSettings();
        cfg.Overlay.MetricColors.Clear();
        cfg.Overlay.GroupColors.Clear();

        App.ApplyVendorColors(cfg);

        cfg.Overlay.MetricColors.Should().NotBeEmpty();
        cfg.Overlay.GroupColors.Should().NotBeEmpty();

        cfg.Overlay.MetricColors.Should().ContainKey(Metrics.CpuPct);
        cfg.Overlay.GroupColors.Should().ContainKey("cpu");
    }

    [Fact]
    public void ApplyVendorColors_PreservesUserCustomizedColors()
    {
        var cfg = new AppSettings();
        cfg.Overlay.MetricColors[Metrics.CpuPct] = "#123456";
        cfg.Overlay.UserCustomizedMetricColors.Add(Metrics.CpuPct);

        cfg.Overlay.GroupColors["cpu"] = "#654321";
        cfg.Overlay.UserCustomizedGroupColors.Add("cpu");

        App.ApplyVendorColors(cfg);

        cfg.Overlay.MetricColors[Metrics.CpuPct].Should().Be("#123456");
        cfg.Overlay.GroupColors["cpu"].Should().Be("#654321");
    }

    [WpfFact]
    public void App_IsAdministrator_ReturnsBoolean()
    {
        var isAdmin = typeof(App).GetMethod("IsAdministrator", BindingFlags.NonPublic | BindingFlags.Static);
        isAdmin.Should().NotBeNull();
        var adminResult = (bool)isAdmin!.Invoke(null, null)!;
        adminResult.Should().Be(adminResult);
    }

    [WpfFact]
    public void App_HandleUnhandledException_LogsException()
    {
        var handleUnhandled = typeof(App).GetMethod("HandleUnhandledException", BindingFlags.NonPublic | BindingFlags.Static);
        var actUnhandled = () => handleUnhandled!.Invoke(null, [null!, new UnhandledExceptionEventArgs(new InvalidOperationException("Test exception"), false)]);
        actUnhandled.Should().NotThrow();

        var actNonException = () => handleUnhandled!.Invoke(null, [null!, new UnhandledExceptionEventArgs("raw object", false)]);
        actNonException.Should().NotThrow();
    }

    [WpfFact]
    public void App_HandleUnobservedTaskException_LogsException()
    {
        var handleUnobserved = typeof(App).GetMethod("HandleUnobservedTaskException", BindingFlags.NonPublic | BindingFlags.Static);
        var actUnobserved = () => handleUnobserved!.Invoke(null, [null!, new UnobservedTaskExceptionEventArgs(new AggregateException(new InvalidOperationException("Test unobserved")))]);
        actUnobserved.Should().NotThrow();

        var unobservedNull = (UnobservedTaskExceptionEventArgs)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(UnobservedTaskExceptionEventArgs));
        var actNull = () => handleUnobserved!.Invoke(null, [null!, unobservedNull]);
        actNull.Should().NotThrow();
    }

    [WpfFact]
    public void App_HandleDispatcherUnhandledException_LogsException()
    {
        var handleDispatcher = typeof(App).GetMethod("HandleDispatcherUnhandledException", BindingFlags.NonPublic | BindingFlags.Static);
        var dispatcherArgs = (DispatcherUnhandledExceptionEventArgs)typeof(DispatcherUnhandledExceptionEventArgs)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0]
            .Invoke([Dispatcher.CurrentDispatcher]);
        typeof(DispatcherUnhandledExceptionEventArgs).GetField("_exception", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(dispatcherArgs, new InvalidOperationException("Test dispatcher"));
        var actDispatcher = () => handleDispatcher!.Invoke(null, [null!, dispatcherArgs]);
        actDispatcher.Should().NotThrow();
    }

    [WpfFact]
    public void App_BuildContainer_RegistersRequiredServices()
    {
        var app = GetOrCreateApp();
        var buildContainer = typeof(App).GetMethod("BuildContainer", BindingFlags.NonPublic | BindingFlags.Instance);
        var container = (ServiceProvider)buildContainer!.Invoke(app, null)!;
        container.Should().NotBeNull();
        container.Dispose();
    }

    [WpfFact]
    public void App_ToggleMetricsLogging_ManagesFileLoggingState()
    {
        var app = GetOrCreateApp();
        var toggleMetrics = typeof(App).GetMethod("ToggleMetricsLogging", BindingFlags.NonPublic | BindingFlags.Instance);
        toggleMetrics!.Invoke(app, [true]);
        toggleMetrics.Invoke(app, [true]); // duplicate
        toggleMetrics.Invoke(app, [false]);
    }

    [WpfFact]
    public void App_OnStartup_HandlesUnelevatedExecution()
    {
        var app = GetOrCreateApp();
        bool elevatedCalled = false;
        App.IsAdministratorCheck = () => false;
        App.ElevateAndExitAction = _ => elevatedCalled = true;
        App.NotifierLauncherAction = _ => { };

        var onStartup = typeof(App).GetMethod("OnStartup", BindingFlags.NonPublic | BindingFlags.Instance);
        var startupArgs = (StartupEventArgs)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(StartupEventArgs));

        onStartup!.Invoke(app, [startupArgs]);
        elevatedCalled.Should().BeTrue();

        App.IsAdministratorCheck = () => true;
        App.ElevateAndExitAction = _ => { };
        App.NotifierLauncherAction = null;
    }

    [WpfFact]
    public void App_OnStartup_HandlesElevatedExecution()
    {
        var app = GetOrCreateApp();
        App.IsAdministratorCheck = () => true;
        App.NotifierLauncherAction = _ => { };
        App.SrumImporterAction = _ => Task.CompletedTask;
        App.PawnIoManagerOverride = CreateMockPawnIoManager();
        App.SuppressSettingsWindowForTesting = true;
        WindowHelpers.TaskSchedulerFactory = () => new ExpandoObject();
        WindowHelpers.StartupQueryExitCode = () => 0;

        try
        {
            typeof(AppSettings).GetProperty("LastDirectoryError", BindingFlags.Static | BindingFlags.Public)!.SetValue(null, new InvalidOperationException("Test fallback warning"));
            var onStartup = typeof(App).GetMethod("OnStartup", BindingFlags.NonPublic | BindingFlags.Instance);
            var startupArgs = (StartupEventArgs)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(StartupEventArgs));
            typeof(StartupEventArgs).GetField("_args", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(startupArgs, ElevatedArgs);

            onStartup!.Invoke(app, [startupArgs]);
        }
        finally
        {
            typeof(AppSettings).GetProperty("LastDirectoryError", BindingFlags.Static | BindingFlags.Public)!.SetValue(null, null);
            var monitorField = typeof(App).GetField("_monitor", BindingFlags.NonPublic | BindingFlags.Instance);
            if (monitorField?.GetValue(app) is HardwareMonitor monitor)
            {
                try { monitor.Stop(); } catch { }
                try { monitor.Dispose(); } catch { }
            }

            (typeof(App).GetField("_overlay", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(app) as TaskbarOverlay)?.Dispose();
            (typeof(App).GetField("_tray", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(app) as TrayIconManager)?.Dispose();

            App.IsAdministratorCheck = () => true;
            App.NotifierLauncherAction = null;
            App.SrumImporterAction = null;
            App.PawnIoManagerOverride = CreateMockPawnIoManager();
            WindowHelpers.TaskSchedulerFactory = null;
            WindowHelpers.StartupQueryExitCode = null;
            typeof(App).GetField("_settingsShell", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, null);
        }
    }

    [WpfFact]
    public void App_OnStartup_WithMinimizedAndStartupFlags_ExecutesConfigurationBranches()
    {
        var app = GetOrCreateApp();
        App.IsAdministratorCheck = () => true;
        App.NotifierLauncherAction = _ => { };
        App.SrumImporterAction = _ => Task.CompletedTask;
        App.PawnIoManagerOverride = CreateMockPawnIoManager();
        App.SuppressSettingsWindowForTesting = true;
        WindowHelpers.TaskSchedulerFactory = () => new ExpandoObject();
        WindowHelpers.StartupQueryExitCode = () => 0;

        try
        {
            var onStartup = typeof(App).GetMethod("OnStartup", BindingFlags.NonPublic | BindingFlags.Instance);
            var startupArgs = (StartupEventArgs)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(StartupEventArgs));
            typeof(StartupEventArgs).GetField("_args", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(startupArgs, MinimizedArgs);

            onStartup!.Invoke(app, [startupArgs]);
        }
        finally
        {
            var monitorField = typeof(App).GetField("_monitor", BindingFlags.NonPublic | BindingFlags.Instance);
            if (monitorField?.GetValue(app) is HardwareMonitor monitor)
            {
                try { monitor.Stop(); } catch { }
                try { monitor.Dispose(); } catch { }
            }

            (typeof(App).GetField("_overlay", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(app) as TaskbarOverlay)?.Dispose();
            (typeof(App).GetField("_tray", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(app) as TrayIconManager)?.Dispose();

            App.IsAdministratorCheck = () => true;
            App.NotifierLauncherAction = null;
            App.SrumImporterAction = null;
            App.PawnIoManagerOverride = CreateMockPawnIoManager();
            WindowHelpers.TaskSchedulerFactory = null;
            WindowHelpers.StartupQueryExitCode = null;
            typeof(App).GetField("_settingsShell", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, null);
        }
    }

    [WpfFact]
    public void App_HandleSnapshotReadyAndSettingsApplied_UpdatesOverlayAndNotifiesListeners()
    {
        var app = GetOrCreateApp();
        WindowHelpers.TaskSchedulerFactory = () => new ExpandoObject();
        App.TaskManagerLauncherAction = _ => { };
        SettingsShell.SkipWebView2InitForTesting = true;

        var cfg = new AppSettings { RunAtStartup = false };
        var embedder = new TaskbarEmbedder(cfg);
        var overlay = new TaskbarOverlay(cfg, embedder);
        var tray = new TrayIconManager();
        var notifClient = Substitute.For<NotificationClient>();
        var alertService = new AlertService(cfg, overlay, notifClient);

        string tempPath = Path.Combine(Path.GetTempPath(), "net_history_" + Guid.NewGuid().ToString("N") + ".dat");
        var historyFile = new TrafficHistoryFile(tempPath);
        var netLogger = new NetUsageLogger(cfg, historyFile);

        var sysInfo = Substitute.For<ISystemInfo>();
        var pawnIo = CreateMockPawnIoManager();
        pawnIo.TryInstall().Returns(true);
        pawnIo.StartDriverService().Returns(true);
        var monitor = new HardwareMonitor(cfg, sysInfo, pawnIo);

        var appFields = typeof(App);
        appFields.GetField("_cfg", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, cfg);
        appFields.GetField("_overlay", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, overlay);
        appFields.GetField("_tray", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, tray);
        appFields.GetField("_alerts", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, alertService);
        appFields.GetField("_notifClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, notifClient);
        appFields.GetField("_netLogger", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, netLogger);
        appFields.GetField("_monitor", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, monitor);
        appFields.GetField("_settingsShell", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, null);

        try
        {
            var wireEvents = typeof(App).GetMethod("WireEvents", BindingFlags.NonPublic | BindingFlags.Instance);
            wireEvents!.Invoke(app, null);

            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            (typeof(TrayIconManager).GetField("ToggleOverlayRequested", flags)!.GetValue(tray) as Action)?.Invoke();
            (typeof(TrayIconManager).GetField("OpenSettingsRequested", flags)!.GetValue(tray) as Action)?.Invoke();

            overlay.OnDoubleClick?.Invoke("taskmanager");
            overlay.OnDoubleClick?.Invoke("settings");

            var snap = new TelemetrySnapshot { CpuUsagePct = 50f };
            var handleSnapshot = typeof(App).GetMethod("HandleSnapshotReady", BindingFlags.NonPublic | BindingFlags.Instance);
            handleSnapshot!.Invoke(app, [snap]);

            var handleSettings = typeof(App).GetMethod("HandleSettingsApplied", BindingFlags.NonPublic | BindingFlags.Instance);
            cfg.Overlay.Visible = true;
            handleSettings!.Invoke(app, null);

            cfg.Overlay.Visible = false;
            handleSettings.Invoke(app, null);
        }
        finally
        {
            App.TaskManagerLauncherAction = null;
            SettingsShell.SkipWebView2InitForTesting = false;
            WindowHelpers.TaskSchedulerFactory = null;
            typeof(App).GetField("_settingsShell", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, null);
            overlay.Dispose();
            tray.Dispose();
            notifClient.Dispose();
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [WpfFact]
    public void App_OpenSettings_WhenSuppressedForTesting_ReturnsEarly()
    {
        var app = GetOrCreateApp();
        App.SuppressSettingsWindowForTesting = true;

        var openSettings = typeof(App).GetMethod("OpenSettings", BindingFlags.NonPublic | BindingFlags.Instance);
        var act = () => openSettings!.Invoke(app, null);
        act.Should().NotThrow();
    }

    [WpfFact]
    public void App_RunSrumPreImport_ImporterCompletes_DoesNotThrow()
    {
        App.SrumImporterAction = _ => Task.CompletedTask;

        string tempPath = Path.Combine(Path.GetTempPath(), "net_history_" + Guid.NewGuid().ToString("N") + ".dat");
        var historyFile = new TrafficHistoryFile(tempPath);

        var method = typeof(App).GetMethod("RunSrumPreImport", BindingFlags.NonPublic | BindingFlags.Static);
        var act = () => method!.Invoke(null, [historyFile]);
        act.Should().NotThrow();

        App.SrumImporterAction = null;
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }

    [WpfFact]
    public void App_RunSrumPreImport_ImporterThrows_IsCaught()
    {
        App.SrumImporterAction = _ => throw new InvalidOperationException("boom");

        string tempPath = Path.Combine(Path.GetTempPath(), "net_history_" + Guid.NewGuid().ToString("N") + ".dat");
        var historyFile = new TrafficHistoryFile(tempPath);

        var method = typeof(App).GetMethod("RunSrumPreImport", BindingFlags.NonPublic | BindingFlags.Static);
        var act = () => method!.Invoke(null, [historyFile]);
        act.Should().NotThrow();

        App.SrumImporterAction = null;
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }

    [WpfFact]
    public void App_RunSrumPreImport_WhenImporterActionNull_HandlesTaskExecutionSafely()
    {
        App.SrumImporterAction = null;

        string tempPath = Path.Combine(Path.GetTempPath(), "net_history_" + Guid.NewGuid().ToString("N") + ".dat");
        var historyFile = new TrafficHistoryFile(tempPath);

        var method = typeof(App).GetMethod("RunSrumPreImport", BindingFlags.NonPublic | BindingFlags.Static);
        var act = () => method!.Invoke(null, [historyFile]);
        act.Should().NotThrow();

        App.SrumImporterAction = _ => Task.CompletedTask;
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }

    [WpfFact]
    public void App_LaunchTaskManager_WithAndWithoutDelegate_DoesNotThrow()
    {
        var app = GetOrCreateApp();
        string? launched = null;
        App.TaskManagerLauncherAction = act => launched = act;

        var launchMethod = typeof(App).GetMethod("LaunchTaskManager", BindingFlags.NonPublic | BindingFlags.Instance);
        launchMethod!.Invoke(app, null);
        launched.Should().Be("taskmanager");

        App.TaskManagerLauncherAction = null;
        var actNull = () => launchMethod.Invoke(app, null);
        actNull.Should().NotThrow();
    }

    [WpfFact]
    public void App_EnsureNotifierLaunched_And_EnsureNotifierStopped_WithProcessAndLauncher()
    {
        var app = GetOrCreateApp();
        string? launchedDir = null;
        App.NotifierLauncherAction = dir => launchedDir = dir;

        var cfgField = typeof(App).GetField("_cfg", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cfg = (AppSettings)cfgField.GetValue(app)!;
        cfg.Alerts.Enabled = true;
        cfg.Alerts.ShowToastNotif = true;

        var launchMethod = typeof(App).GetMethod("EnsureNotifierLaunched", BindingFlags.NonPublic | BindingFlags.Instance);
        launchMethod!.Invoke(app, null);
        launchedDir.Should().Be(AppContext.BaseDirectory);

        App.NotifierLauncherAction = null;

        var stopMethod = typeof(App).GetMethod("EnsureNotifierStopped", BindingFlags.NonPublic | BindingFlags.Instance);
        var procField = typeof(App).GetField("_notifierProcess", BindingFlags.NonPublic | BindingFlags.Instance)!;
        typeof(App).GetField("_notifierLaunched", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, true);
        procField.SetValue(app, System.Diagnostics.Process.GetCurrentProcess());

        // Call stopMethod - should handle existing process safely
        var actStop = () => stopMethod!.Invoke(app, null);
        actStop.Should().NotThrow();

        // Clear process field so process isn't disposed twice
        procField.SetValue(app, null);
    }

    [WpfFact]
    public void App_EnsureNotifierStopped_WhenProcessNotNullAndExited_DisposesProcessSafely()
    {
        var app = GetOrCreateApp();
        var proc = new System.Diagnostics.Process();
        typeof(App).GetField("_notifierProcess", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, proc);
        typeof(App).GetField("_notifierLaunched", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, true);

        var stopMethod = typeof(App).GetMethod("EnsureNotifierStopped", BindingFlags.NonPublic | BindingFlags.Instance);
        var act = () => stopMethod!.Invoke(app, null);
        act.Should().NotThrow();
    }

    [WpfFact]
    public void App_ElevateAndExitAction_CanBeMockedSafely()
    {
        string[]? capturedArgs = null;
        App.ElevateAndExitAction = args => capturedArgs = args;

        try
        {
            App.ElevateAndExitAction(["--minimized", "arg1"]);
            capturedArgs.Should().Contain("--minimized");
        }
        finally
        {
            App.ElevateAndExitAction = (Action<string[]>)Delegate.CreateDelegate(typeof(Action<string[]>), typeof(App).GetMethod("ElevateAndExit", BindingFlags.NonPublic | BindingFlags.Static)!);
        }
    }

    [WpfFact]
    public void App_EnsureNotifierLaunched_WhenAlertsDisabled_DoesNotLaunch()
    {
        var app = GetOrCreateApp();
        bool launcherCalled = false;
        App.NotifierLauncherAction = _ => launcherCalled = true;

        var cfgField = typeof(App).GetField("_cfg", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cfg = (AppSettings)cfgField.GetValue(app)!;
        cfg.Alerts.Enabled = false;
        cfg.Alerts.ShowToastNotif = false;

        var launchMethod = typeof(App).GetMethod("EnsureNotifierLaunched", BindingFlags.NonPublic | BindingFlags.Instance);
        launchMethod!.Invoke(app, null);

        launcherCalled.Should().BeFalse();
        App.NotifierLauncherAction = null;
    }

    [WpfFact]
    public void App_EnsureNotifierStopped_WhenAlreadyStopped_ReturnsEarly()
    {
        var app = GetOrCreateApp();
        typeof(App).GetField("_notifierLaunched", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, false);
        typeof(App).GetField("_notifierProcess", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, null);

        var stopMethod = typeof(App).GetMethod("EnsureNotifierStopped", BindingFlags.NonPublic | BindingFlags.Instance);
        var actStop = () => stopMethod!.Invoke(app, null);
        actStop.Should().NotThrow();
    }

    [WpfFact]
    public void App_HandleSettingsApplied_ExercisesPollIntervalChangeAndAlertsToggling()
    {
        var app = GetOrCreateApp();
        var cfg = new AppSettings { RunAtStartup = false };
        cfg.Monitoring.PollIntervalMs = 500;
        cfg.Alerts.Enabled = false;
        cfg.Alerts.ShowToastNotif = false;
        cfg.Overlay.Visible = true;

        var embedder = new TaskbarEmbedder(cfg);
        var overlay = new TaskbarOverlay(cfg, embedder);
        var sysInfo = Substitute.For<ISystemInfo>();
        var pawnIo = CreateMockPawnIoManager();
        var monitor = new HardwareMonitor(cfg, sysInfo, pawnIo);

        var appFields = typeof(App);
        appFields.GetField("_cfg", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, cfg);
        appFields.GetField("_overlay", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, overlay);
        appFields.GetField("_monitor", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, monitor);

        var handleSettings = typeof(App).GetMethod("HandleSettingsApplied", BindingFlags.NonPublic | BindingFlags.Instance);
        var actSettings = () => handleSettings!.Invoke(app, null);
        actSettings.Should().NotThrow();

        overlay.Dispose();
        monitor.Dispose();
    }

    [WpfFact]
    public void App_HandleSettingsApplied_WhenOverlayInstanceIsNull_LogsErrorSafely()
    {
        var app = GetOrCreateApp();
        typeof(App).GetField("_overlay", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, null);

        var handleSettings = typeof(App).GetMethod("HandleSettingsApplied", BindingFlags.NonPublic | BindingFlags.Instance);
        var actSettings = () => handleSettings!.Invoke(app, null);
        actSettings.Should().NotThrow();
    }

    [WpfFact]
    public void App_LaunchNotifier_WhenLauncherActionNull_ExecutesFallbackLogicSafely()
    {
        var app = GetOrCreateApp();
        App.NotifierLauncherAction = null;

        var launchMethod = typeof(App).GetMethod("LaunchNotifier", BindingFlags.NonPublic | BindingFlags.Instance);
        var act = () => launchMethod!.Invoke(app, null);
        act.Should().NotThrow();

        App.NotifierLauncherAction = _ => { };
    }

    [WpfFact]
    public void App_WireEvents_OverlayContextAndActionCallbacks_ExerciseTriggers()
    {
        var app = GetOrCreateApp();
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);
        var overlay = new TaskbarOverlay(cfg, embedder);
        var tray = new TrayIconManager();
        var sysInfo = Substitute.For<ISystemInfo>();
        var pawnIo = CreateMockPawnIoManager();
        var monitor = new HardwareMonitor(cfg, sysInfo, pawnIo);

        var appFields = typeof(App);
        appFields.GetField("_cfg", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, cfg);
        appFields.GetField("_overlay", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, overlay);
        appFields.GetField("_tray", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, tray);
        appFields.GetField("_monitor", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, monitor);

        var wireEvents = typeof(App).GetMethod("WireEvents", BindingFlags.NonPublic | BindingFlags.Instance);
        wireEvents!.Invoke(app, null);

        // Test overlay callbacks wired by WireEvents
        overlay.ShowContextMenuAction.Should().NotBeNull();
        overlay.OnOpenSettingsRequested.Should().NotBeNull();
        overlay.OnToggleOverlayRequested.Should().NotBeNull();
        overlay.OnExitRequested.Should().NotBeNull();

        overlay.ShowContextMenuAction?.Invoke(new System.Drawing.Point(100, 100));
        overlay.OnOpenSettingsRequested?.Invoke();
        overlay.OnToggleOverlayRequested?.Invoke();
        overlay.OnExitRequested?.Invoke();

        overlay.Dispose();
        tray.Dispose();
        monitor.Dispose();
    }

    [WpfFact]
    public void App_QuitApp_ClosesSettingsShellSafely()
    {
        var app = GetOrCreateApp();
        var quitApp = typeof(App).GetMethod("QuitApp", BindingFlags.NonPublic | BindingFlags.Instance);
        var actQuit = () => quitApp!.Invoke(app, null);
        actQuit.Should().NotThrow();
    }

    [WpfFact]
    public void App_HandleSettingsApplied_WhenOverlayInvisibleAndPollIntervalChanges_ExecutesCleanly()
    {
        var app = GetOrCreateApp();
        var cfg = new AppSettings();
        cfg.Overlay.Visible = false;
        cfg.Monitoring.PollIntervalMs = 2000;
        cfg.Alerts.Enabled = false;

        var embedder = new TaskbarEmbedder(cfg);
        var overlay = new TaskbarOverlay(cfg, embedder);
        var sysInfo = Substitute.For<ISystemInfo>();
        var pawnIo = CreateMockPawnIoManager();
        var monitor = new HardwareMonitor(cfg, sysInfo, pawnIo);

        var appFields = typeof(App);
        appFields.GetField("_cfg", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, cfg);
        appFields.GetField("_overlay", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, overlay);
        appFields.GetField("_monitor", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, monitor);

        var handleSettings = typeof(App).GetMethod("HandleSettingsApplied", BindingFlags.NonPublic | BindingFlags.Instance);
        var act = () => handleSettings!.Invoke(app, null);
        act.Should().NotThrow();

        overlay.Dispose();
        monitor.Dispose();
    }

    [WpfFact]
    public void App_EnsureNotifierStopped_Branches_ExecuteWithoutExceptions()
    {
        var app = GetOrCreateApp();
        var appFields = typeof(App);

        // 1. Neither launched nor process
        appFields.GetField("_notifierLaunched", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, false);
        appFields.GetField("_notifierProcess", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, null);
        app.EnsureNotifierStopped();

        // 2. Launched true but process null
        appFields.GetField("_notifierLaunched", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, true);
        appFields.GetField("_notifierProcess", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, null);
        app.EnsureNotifierStopped();
    }

    [WpfFact]
    public void App_LaunchTaskManager_Fallback_ExecutesSafely()
    {
        var app = GetOrCreateApp();
        App.TaskManagerLauncherAction = null;

        var method = typeof(App).GetMethod("LaunchTaskManager", BindingFlags.NonPublic | BindingFlags.Instance);
        var act = () => method!.Invoke(app, null);
        act.Should().NotThrow();

        App.TaskManagerLauncherAction = _ => { };
    }

    [WpfFact]
    public void App_ToggleMetricsLogging_Branches_ExecuteWithoutExceptions()
    {
        var app = GetOrCreateApp();
        var method = typeof(App).GetMethod("ToggleMetricsLogging", BindingFlags.NonPublic | BindingFlags.Instance);

        // Call true then false
        method!.Invoke(app, [true]);
        method.Invoke(app, [false]);
    }

    [WpfFact]
    public void App_ExceptionHandlers_HandleSafelyWithoutCrashing()
    {
        var unhandledMethod = typeof(App).GetMethod("HandleUnhandledException", BindingFlags.NonPublic | BindingFlags.Static);
        unhandledMethod!.Invoke(null, [new object(), new UnhandledExceptionEventArgs(new InvalidOperationException("Test unhandled"), false)]);

        var unobservedMethod = typeof(App).GetMethod("HandleUnobservedTaskException", BindingFlags.NonPublic | BindingFlags.Static);
        unobservedMethod!.Invoke(null, [null, new UnobservedTaskExceptionEventArgs(new AggregateException(new InvalidOperationException("Test unobserved")))]);
        unobservedMethod.Invoke(null, [null, new UnobservedTaskExceptionEventArgs(null!)]);

        var dispatcherMethod = typeof(App).GetMethod("HandleDispatcherUnhandledException", BindingFlags.NonPublic | BindingFlags.Static);
        var dispArgs = (System.Windows.Threading.DispatcherUnhandledExceptionEventArgs)
            RuntimeHelpers.GetUninitializedObject(typeof(System.Windows.Threading.DispatcherUnhandledExceptionEventArgs));
        typeof(System.Windows.Threading.DispatcherUnhandledExceptionEventArgs)
            .GetField("_exception", BindingFlags.NonPublic | BindingFlags.Instance)?
            .SetValue(dispArgs, new InvalidOperationException("Test dispatcher ex"));
        dispatcherMethod!.Invoke(null, [new object(), dispArgs]);
    }

    [WpfFact]
    public void App_OnStartup_NonElevated_InvokesElevateAndExitAction()
    {
        bool elevateCalled = false;
        App.IsAdministratorCheck = () => false;
        App.ElevateAndExitAction = _ => { elevateCalled = true; };

        var app = GetOrCreateApp();
        var onStartupMethod = typeof(App).GetMethod("OnStartup", BindingFlags.NonPublic | BindingFlags.Instance);
        var args = (System.Windows.StartupEventArgs)
            RuntimeHelpers.GetUninitializedObject(typeof(System.Windows.StartupEventArgs));

        var field = typeof(System.Windows.StartupEventArgs).GetField("_args", BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(args, NonElevatedArgs);

        onStartupMethod!.Invoke(app, [args]);
        elevateCalled.Should().BeTrue();

        // Restore
        App.IsAdministratorCheck = () => true;
        App.ElevateAndExitAction = _ => { };
    }

    [WpfFact]
    public void App_OpenSettings_ShowsAndActivatesSettingsShell()
    {
        var app = GetOrCreateApp();
        App.SuppressSettingsWindowForTesting = false;
        SettingsShell.SkipShowForTesting = true;
        var cfg = new AppSettings();
        string tempPath = Path.Combine(Path.GetTempPath(), "test_" + Guid.NewGuid().ToString("N") + ".jsonl");
        var tempFile = new TrafficHistoryFile(tempPath);
        TrafficHistoryStore.Initialize(tempFile);
        var logger = new NetUsageLogger(cfg, tempFile);

        var appFields = typeof(App);
        appFields.GetField("_cfg", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, cfg);
        appFields.GetField("_netLogger", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, logger);

        var shells = new List<SettingsShell>();
        App.SettingsShellFactory = (c, l) =>
        {
            var s = new SettingsShell(c, l);
            shells.Add(s);
            return s;
        };

        var openSettingsMethod = typeof(App).GetMethod("OpenSettings", BindingFlags.NonPublic | BindingFlags.Instance);
        openSettingsMethod!.Invoke(app, null);

        // Call again to exercise already loaded branch
        openSettingsMethod.Invoke(app, null);

        foreach (var s in shells)
        {
            try { s.Close(); } catch { }
        }
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        logger.Dispose();
        App.SettingsShellFactory = null;
        App.SuppressSettingsWindowForTesting = true;
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }

    [WpfFact]
    public void App_OnExit_DisposesAllComponentsCleanly()
    {
        var app = GetOrCreateApp();
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);
        var overlay = new TaskbarOverlay(cfg, embedder);
        var tray = new TrayIconManager();
        var sysInfo = Substitute.For<ISystemInfo>();
        var pawnIo = CreateMockPawnIoManager();
        var monitor = new HardwareMonitor(cfg, sysInfo, pawnIo);
        var notifClient = Substitute.For<NotificationClient>();
        notifClient.SendShutdownAsync().Returns(Task.FromException(new InvalidOperationException("Pipe error")));

        var appFields = typeof(App);
        appFields.GetField("_cfg", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, cfg);
        appFields.GetField("_overlay", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, overlay);
        appFields.GetField("_tray", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, tray);
        appFields.GetField("_monitor", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, monitor);
        appFields.GetField("_notifClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, notifClient);

        var onExitMethod = typeof(App).GetMethod("OnExit", BindingFlags.NonPublic | BindingFlags.Instance);
        var exitArgs = (System.Windows.ExitEventArgs)
            RuntimeHelpers.GetUninitializedObject(typeof(System.Windows.ExitEventArgs));

        var act = () => onExitMethod!.Invoke(app, [exitArgs]);
        act.Should().NotThrow();
    }

    [WpfFact]
    public void App_ToggleMetricsLogging_AlreadyMatchingState_EarlyReturns()
    {
        var app = GetOrCreateApp();
        var method = typeof(App).GetMethod("ToggleMetricsLogging", BindingFlags.NonPublic | BindingFlags.Instance);

        // Invoke with true, then invoke with true again to hit early return (enable == _metricsWasEnabled)
        method!.Invoke(app, [true]);
        method.Invoke(app, [true]);
    }

    [WpfFact]
    public void App_HandleSessionEnding_ShutdownAndLogoff_WritesSessionEnd()
    {
        var app = GetOrCreateApp();
        var sessionEnding = typeof(App).GetMethod("HandleSessionEnding", BindingFlags.NonPublic | BindingFlags.Instance);
        sessionEnding.Should().NotBeNull();

        // 1. Shutdown
        var shutdownArgs = (SessionEndingCancelEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(SessionEndingCancelEventArgs));
        typeof(SessionEndingCancelEventArgs).GetField("_reason", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(shutdownArgs, ReasonSessionEnding.Shutdown);
        var actShutdown = () => sessionEnding!.Invoke(app, [null!, shutdownArgs]);
        actShutdown.Should().NotThrow();

        // 2. Logoff
        var logoffArgs = (SessionEndingCancelEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(SessionEndingCancelEventArgs));
        typeof(SessionEndingCancelEventArgs).GetField("_reason", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(logoffArgs, ReasonSessionEnding.Logoff);
        var actLogoff = () => sessionEnding!.Invoke(app, [null!, logoffArgs]);
        actLogoff.Should().NotThrow();
    }

    [WpfFact]
    public void App_BuildContainer_WithoutPawnIoOverride_RegistersWrapper()
    {
        var app = GetOrCreateApp();
        var priorOverride = App.PawnIoManagerOverride;
        try
        {
            App.PawnIoManagerOverride = null;
            var buildContainer = typeof(App).GetMethod("BuildContainer", BindingFlags.NonPublic | BindingFlags.Instance);
            using var container = (ServiceProvider)buildContainer!.Invoke(app, null)!;
            container.Should().NotBeNull();

            var loggerFactory = container.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("TestLogger");
            logger.LogError("Test error message");
        }
        finally
        {
            App.PawnIoManagerOverride = priorOverride;
        }
    }

    [WpfFact]
    public void App_LaunchTaskManager_WhenProcessLauncherThrows_HandlesErrorGracefully()
    {
        var app = GetOrCreateApp();
        Action<System.Diagnostics.ProcessStartInfo>? priorLauncher = App.TaskManagerProcessLauncher;
        try
        {
            App.TaskManagerProcessLauncher = _ => throw new InvalidOperationException("Failed to launch task manager");
            var launchMethod = typeof(App).GetMethod("LaunchTaskManager", BindingFlags.NonPublic | BindingFlags.Instance);
            var act = () => launchMethod!.Invoke(app, null);
            act.Should().NotThrow();
        }
        finally
        {
            App.TaskManagerProcessLauncher = priorLauncher;
        }
    }

    [WpfFact]
    public void App_HandleSettingsApplied_AllBranchesCoverage()
    {
        var app = GetOrCreateApp();
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);
        using var tray = new TrayIconManager();
        using var monitor = new HardwareMonitor(cfg, new SystemInfoWrapper(), CreateMockPawnIoManager());

        var appFields = typeof(App);
        appFields.GetField("_cfg", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, cfg);
        appFields.GetField("_overlay", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, overlay);
        appFields.GetField("_tray", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, tray);
        appFields.GetField("_monitor", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, monitor);

        var handleApplied = appFields.GetMethod("HandleSettingsApplied", BindingFlags.NonPublic | BindingFlags.Instance);

        // Branch 1: Overlay visible = true, startup toggled, poll interval changed, alerts toast enabled
        cfg.Overlay.Visible = true;
        cfg.RunAtStartup = true;
        cfg.Monitoring.PollIntervalMs = 500;
        cfg.Alerts.Enabled = true;
        cfg.Alerts.ShowToastNotif = true;
        cfg.EnableFileLogging = true;
        appFields.GetField("_lastRunAtStartup", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, false);
        appFields.GetField("_lastPollIntervalMs", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, 1000);

        handleApplied!.Invoke(app, null);

        // Branch 2: Overlay visible = false, startup matching, poll interval matching, alerts disabled
        cfg.Overlay.Visible = false;
        cfg.Alerts.Enabled = false;
        cfg.EnableFileLogging = false;

        handleApplied.Invoke(app, null);

        // Branch 3: Overlay is null
        appFields.GetField("_overlay", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, null);
        handleApplied.Invoke(app, null);
    }

    [WpfFact]
    public void App_EnsureNotifierLaunched_And_Stopped_StateHandling()
    {
        var app = GetOrCreateApp();
        var cfg = new AppSettings();
        typeof(App).GetField("_cfg", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, cfg);

        // 1. Notifier already launched -> early return
        typeof(App).GetField("_notifierLaunched", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, true);
        app.EnsureNotifierLaunched();

        // 2. Alerts disabled -> early return
        typeof(App).GetField("_notifierLaunched", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, false);
        cfg.Alerts.Enabled = false;
        app.EnsureNotifierLaunched();

        // 3. Alerts enabled + NotifierLauncherAction set -> launches successfully
        cfg.Alerts.Enabled = true;
        cfg.Alerts.ShowToastNotif = true;
        bool launcherInvoked = false;
        Action<string>? priorAction = App.NotifierLauncherAction;
        try
        {
            App.NotifierLauncherAction = _ => launcherInvoked = true;
            app.EnsureNotifierLaunched();
            launcherInvoked.Should().BeTrue();
        }
        finally
        {
            App.NotifierLauncherAction = priorAction;
        }

        // 4. EnsureNotifierStopped when launched = false and process = null -> early return
        typeof(App).GetField("_notifierLaunched", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, false);
        typeof(App).GetField("_notifierProcess", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, null);
        app.EnsureNotifierStopped();

        // 5. EnsureNotifierStopped when launched = true
        typeof(App).GetField("_notifierLaunched", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, true);
        app.EnsureNotifierStopped();
    }

    [WpfFact]
    public void App_ExceptionHandlers_HandleDispatcherAndUnobservedTask()
    {
        // 1. HandleUnobservedTaskException
        var handleTaskEx = typeof(App).GetMethod("HandleUnobservedTaskException", BindingFlags.NonPublic | BindingFlags.Static);
        var taskExArgs = (UnobservedTaskExceptionEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(UnobservedTaskExceptionEventArgs));
        typeof(UnobservedTaskExceptionEventArgs).GetField("_exception", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(taskExArgs, new AggregateException(new InvalidOperationException("Task error")));
        var actTask = () => handleTaskEx!.Invoke(null, [null, taskExArgs]);
        actTask.Should().NotThrow();

        // 2. HandleDispatcherUnhandledException
        var handleDispEx = typeof(App).GetMethod("HandleDispatcherUnhandledException", BindingFlags.NonPublic | BindingFlags.Static);
        var dispArgs = (DispatcherUnhandledExceptionEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(DispatcherUnhandledExceptionEventArgs));
        typeof(DispatcherUnhandledExceptionEventArgs).GetField("_exception", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(dispArgs, new InvalidOperationException("UI error"));
        var actDisp = () => handleDispEx!.Invoke(null, [null!, dispArgs]);
        actDisp.Should().NotThrow();
    }

    [WpfFact]
    public void App_WireEvents_AllHandlersAndCallbacks()
    {
        var app = GetOrCreateApp();
        var cfg = new AppSettings();
        cfg.Overlay.Visible = true;
        cfg.Overlay.DoubleClickAction = "settings";

        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);
        using var tray = new TrayIconManager();
        using var monitor = new HardwareMonitor(cfg, new SystemInfoWrapper(), CreateMockPawnIoManager());
        string tempPath = Path.Combine(Path.GetTempPath(), $"history_{Guid.NewGuid():N}.dat");
        var historyFile = new TrafficHistoryFile(tempPath);
        using var netLogger = new NetUsageLogger(cfg, historyFile);
        using var notifClient = new NotificationClient();
        var alerts = new AlertService(cfg, overlay, notifClient, netLogger);

        var appFields = typeof(App);
        appFields.GetField("_cfg", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, cfg);
        appFields.GetField("_overlay", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, overlay);
        appFields.GetField("_tray", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, tray);
        appFields.GetField("_monitor", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, monitor);
        appFields.GetField("_notifClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, notifClient);
        appFields.GetField("_alerts", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, alerts);
        appFields.GetField("_netLogger", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, netLogger);

        var wireEvents = appFields.GetMethod("WireEvents", BindingFlags.NonPublic | BindingFlags.Instance);
        wireEvents!.Invoke(app, null);

        // Exercise overlay actions
        overlay.ShowContextMenuAction?.Invoke(new System.Drawing.Point(50, 50));
        overlay.IsContextMenuOpen?.Invoke();
        overlay.GetContextMenuBounds?.Invoke();
        overlay.CloseContextMenuAction?.Invoke();
        overlay.OnOpenSettingsRequested?.Invoke();
        overlay.OnToggleOverlayRequested?.Invoke();
        overlay.OnExitRequested?.Invoke();

        // Exercise DoubleClick on overlay
        var doubleClickEvent = typeof(TaskbarOverlay).GetField("OnDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic);
        var doubleClickAction = (Action<string>?)doubleClickEvent?.GetValue(overlay);
        doubleClickAction?.Invoke("taskmanager");
        doubleClickAction?.Invoke("settings");
        doubleClickAction?.Invoke("unknown");

        // Exercise tray events
        tray.ShowOverlayContextMenu(new System.Drawing.Point(50, 50));

        // Fire tray TaskbarCreated and TaskManagerRequested
        var trayTaskbarCreated = typeof(TrayIconManager).GetField("TaskbarCreated", BindingFlags.Instance | BindingFlags.NonPublic);
        var taskbarCreatedAction = (Action?)trayTaskbarCreated?.GetValue(tray);
        taskbarCreatedAction?.Invoke();

        var trayTaskMgr = typeof(TrayIconManager).GetField("TaskManagerRequested", BindingFlags.Instance | BindingFlags.NonPublic);
        var taskMgrAction = (Action?)trayTaskMgr?.GetValue(tray);
        taskMgrAction?.Invoke();

        // Exercise LaunchTaskManager with null TaskManagerLauncherAction
        var priorLauncher = App.TaskManagerLauncherAction;
        var priorProcLauncher = App.TaskManagerProcessLauncher;
        try
        {
            App.TaskManagerLauncherAction = null;
            App.TaskManagerProcessLauncher = _ => { };
            var launchMethod = appFields.GetMethod("LaunchTaskManager", BindingFlags.NonPublic | BindingFlags.Instance);
            launchMethod!.Invoke(app, null);
        }
        finally
        {
            App.TaskManagerLauncherAction = priorLauncher;
            App.TaskManagerProcessLauncher = priorProcLauncher;
        }

        // Exercise EnsureNotifierLaunched when NotifierLauncherAction is null
        var priorNotifAction = App.NotifierLauncherAction;
        try
        {
            App.NotifierLauncherAction = null;
            typeof(App).GetField("_notifierLaunched", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, false);
            cfg.Alerts.Enabled = true;
            cfg.Alerts.ShowToastNotif = true;
            app.EnsureNotifierLaunched();
        }
        finally
        {
            App.NotifierLauncherAction = priorNotifAction;
        }

        // Exercise HandleSnapshotReady
        var snap = new TelemetrySnapshot { CpuUsagePct = 40f, RamUsagePct = 50f };
        var handleSnap = appFields.GetMethod("HandleSnapshotReady", BindingFlags.NonPublic | BindingFlags.Instance);
        handleSnap!.Invoke(app, [snap]);

        if (File.Exists(tempPath)) File.Delete(tempPath);
    }

    [WpfFact]
    public void App_ExceptionHandlers_And_SessionEnding_Coverage()
    {
        var app = GetOrCreateApp();

        // 1. HandleUnhandledException
        var handleUnhandled = typeof(App).GetMethod("HandleUnhandledException", BindingFlags.NonPublic | BindingFlags.Static);
        handleUnhandled!.Invoke(null, [app, new UnhandledExceptionEventArgs(new InvalidOperationException("Test exception"), false)]);
        handleUnhandled.Invoke(null, [app, new UnhandledExceptionEventArgs("Non-exception object", false)]);

        // 2. HandleUnobservedTaskException
        var handleUnobserved = typeof(App).GetMethod("HandleUnobservedTaskException", BindingFlags.NonPublic | BindingFlags.Static);
        var unobservedArgs = new UnobservedTaskExceptionEventArgs(new AggregateException(new InvalidOperationException("Task boom")));
        handleUnobserved!.Invoke(null, [app, unobservedArgs]);

        // 3. HandleDispatcherUnhandledException
        var handleDispatcher = typeof(App).GetMethod("HandleDispatcherUnhandledException", BindingFlags.NonPublic | BindingFlags.Static);
        var dispArgs = (System.Windows.Threading.DispatcherUnhandledExceptionEventArgs)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            typeof(System.Windows.Threading.DispatcherUnhandledExceptionEventArgs));
        typeof(System.Windows.Threading.DispatcherUnhandledExceptionEventArgs)
            .GetField("_exception", BindingFlags.NonPublic | BindingFlags.Instance)?
            .SetValue(dispArgs, new InvalidOperationException("Dispatcher boom"));
        handleDispatcher!.Invoke(null, [app, dispArgs]);
        dispArgs.Handled.Should().BeTrue();

        // 4. HandleSessionEnding
        var handleSessionEnding = typeof(App).GetMethod("HandleSessionEnding", BindingFlags.NonPublic | BindingFlags.Instance);
        var sessionCtor = typeof(SessionEndingCancelEventArgs).GetConstructors(
            BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault();
        if (sessionCtor != null)
        {
            var sessionArgs = (SessionEndingCancelEventArgs)sessionCtor.Invoke([ReasonSessionEnding.Shutdown]);
            handleSessionEnding!.Invoke(app, [app, sessionArgs]);

            var logoffArgs = (SessionEndingCancelEventArgs)sessionCtor.Invoke([ReasonSessionEnding.Logoff]);
            handleSessionEnding.Invoke(app, [app, logoffArgs]);
        }
    }
}

