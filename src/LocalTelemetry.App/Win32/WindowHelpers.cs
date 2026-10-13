using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LocalTelemetry.Core.Diagnostics;
using Microsoft.Win32;

namespace LocalTelemetry.App.Win32;

/// <summary>
/// Utility helpers for DPI scaling, Windows startup-registry management,
/// and color parsing.
///
/// The entire class is annotated <c>[SupportedOSPlatform("windows")]</c>.
/// This satisfies CA1416 at the call-site level without suppressing the
/// analyser globally: callers that are themselves annotated (or that are
/// transitively reachable only on Windows) inherit the guard automatically.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowHelpers
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "LocalTelemetry";

    // DPI
    /// <summary>Scales a logical pixel value to physical pixels.</summary>
    public static int Scale(float logical, float dpiScale)
        => (int)MathF.Round(logical * dpiScale);

    // Startup
    private const string TaskName = "LocalTelemetry Startup";
    private const string TaskDescription = "Starts LocalTelemetry at user logon (silent, highest privileges).";

    internal static Action<bool>? SetStartupAction { get; set; }
    internal static Func<string, bool>? RunKeyQuery { get; set; }

    /// <summary>
    /// Adds or removes the "LocalTelemetry Startup" scheduled task.
    /// Running via Task Scheduler on logon with highest privileges allows the elevated
    /// app to boot silently without showing UAC prompts on system startup.
    /// </summary>
    public static void SetStartup(bool enable)
    {
        if (SetStartupAction is not null)
        {
            SetStartupAction(enable);
            return;
        }

        try
        {
            if (enable)
                CreateStartupTask();
            else
                RemoveStartupTask();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to {(enable ? "enable" : "disable")} run at startup");
        }
    }

    internal static Func<dynamic>? TaskSchedulerFactory { get; set; }

    /// <summary>Test seam: replaces the <c>schtasks.exe /Query</c> exit code used by <see cref="IsStartupEnabled"/>.</summary>
    internal static Func<int>? StartupQueryExitCode { get; set; }

    /// <summary>Test seam: replaces the legacy Run-key registry delete in <see cref="RemoveStartupTask"/>.</summary>
    internal static Action? LegacyRegistryCleanupAction { get; set; }

    private static dynamic ConnectScheduler()
    {
        if (TaskSchedulerFactory is not null)
            return TaskSchedulerFactory();

        Type type = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Task Scheduler COM (Schedule.Service) is not registered");
        dynamic scheduler = Activator.CreateInstance(type)!;
        scheduler.Connect();
        return scheduler;
    }

    private static void CreateStartupTask()
    {
        string exe = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrEmpty(exe)) return;

        dynamic scheduler = ConnectScheduler();
        dynamic root = scheduler.GetFolder("\\");

        dynamic task = scheduler.NewTask(0);

        task.RegistrationInfo.Description = TaskDescription;
        task.Principal.LogonType = 3; // TASK_LOGON_INTERACTIVE_TOKEN
        task.Principal.RunLevel = 1;  // TASK_RUNLEVEL_HIGHEST

        // Ensure task runs on laptops/battery and doesn't terminate prematurely
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.ExecutionTimeLimit = "PT0S"; // Infinite execution time (no 72h auto-kill)
        task.Settings.StartWhenAvailable = true;
        task.Settings.Priority = 4; // Normal priority (interactive task; prevents logon task throttling)

        dynamic trigger = task.Triggers.Create(9); // TASK_TRIGGER_LOGON
        trigger.Enabled = true;

        dynamic action = task.Actions.Create(0);   // TASK_ACTION_EXEC
        action.Path = exe;
        action.Arguments = "--minimized";
        action.WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? string.Empty;

        root.RegisterTaskDefinition(TaskName, task, 6, string.Empty, string.Empty, 3, string.Empty);
        Log.Debug($"Created startup scheduled task '{TaskName}' -> {exe} --minimized");
    }

    private static void RemoveStartupTask()
    {
        try
        {
            dynamic scheduler = ConnectScheduler();
            dynamic root = scheduler.GetFolder("\\");

            bool exists;
            try
            {
                exists = root.GetTask(TaskName) is not null;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, $"Task '{TaskName}' does not exist or could not be queried");
                exists = false;
            }

            if (!exists) return;

            root.DeleteTask(TaskName, 0);
            Log.Debug($"Removed startup scheduled task '{TaskName}'");
        }
        catch (Exception ex) when (ex is COMException or System.IO.FileNotFoundException)
        {
            Log.Debug($"Startup scheduled task '{TaskName}' delete failed ({ex.Message.TrimEnd('\r', '\n')})");
        }

        try
        {
            if (LegacyRegistryCleanupAction is not null)
            {
                LegacyRegistryCleanupAction();
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                key?.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clean up legacy startup registry key");
        }
    }

    /// <summary>Returns <c>true</c> when the scheduled task or fallback Run key exists.</summary>
    public static bool IsStartupEnabled()
    {
        try
        {
            if (StartupQueryExitCode is not null)
            {
                if (StartupQueryExitCode() == 0) return true;
            }
            else
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"),
                    Arguments = $"/Query /TN \"{TaskName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(2000);
                if (p?.ExitCode == 0) return true;
            }

            if (RunKeyQuery is not null)
            {
                if (RunKeyQuery(AppName)) return true;
            }
            else
            {
                using RegistryKey? hkcuKey = Registry.CurrentUser.OpenSubKey(RunKey);
                if (hkcuKey?.GetValue(AppName) is not null) return true;

                using RegistryKey? hklmKey = Registry.LocalMachine.OpenSubKey(RunKey);
                if (hklmKey?.GetValue(AppName) is not null) return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check if run at startup is enabled");
            return false;
        }
    }

    // color helpers
    /// <summary>
    /// Parses a CSS hex color string (<c>#RRGGBB</c> or <c>RRGGBB</c>).
    /// Returns <paramref name="fallback"/> (default: <see cref="Color.White"/>)
    /// when the string is null, empty or invalid.
    /// </summary>
    public static Color ParseHex(string? hex, Color fallback = default)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback == default ? Color.White : fallback;

        try
        {
            return ColorTranslator.FromHtml(hex.StartsWith('#') ? hex : '#' + hex);
        }
        catch (ArgumentException ex)
        {
            Log.Debug(ex, $"Invalid hex color string '{hex}', using fallback");
            // Invalid hex string - use fallback color.
            return fallback == default ? Color.White : fallback;
        }
    }
}
