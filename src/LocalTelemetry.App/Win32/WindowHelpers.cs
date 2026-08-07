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

    // Task Scheduler COM constants (taskschd):
    // TASK_LOGON_INTERACTIVE_TOKEN = 3, TASK_RUNLEVEL_HIGHEST = 1,
    // TASK_TRIGGER_LOGON = 9, TASK_ACTION_EXEC = 0, TASK_CREATE_OR_UPDATE = 6.
    // NOTE: schtasks.exe /Create /SC ONLOGON silently fails with 0x80004005 on some
    // Windows 11 builds, so creation uses the Task Scheduler COM API instead.

    /// <summary>
    /// Adds or removes the "LocalTelemetry Startup" scheduled task.
    /// Running via Task Scheduler on logon with highest privileges allows the elevated
    /// app to boot silently without showing UAC prompts on system startup.
    /// </summary>
    public static void SetStartup(bool enable)
    {
        try
        {
            if (enable)
                CreateStartupTask();
            else
                RemoveStartupTask();
        }
        catch (Exception ex)
        {
            Log.Error($"SetStartup({enable}) failed: {ex.Message}");
        }
    }

    private static dynamic ConnectScheduler()
    {
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

        dynamic trigger = task.Triggers.Create(9); // TASK_TRIGGER_LOGON
        trigger.Enabled = true;

        dynamic action = task.Actions.Create(0);   // TASK_ACTION_EXEC
        action.Path = exe;
        action.Arguments = "--minimized";

        root.RegisterTaskDefinition(TaskName, task, 6, string.Empty, string.Empty, 3, string.Empty);
        Log.Info($"SetStartup: created task '{TaskName}' -> {exe} --minimized");
    }

    private static void RemoveStartupTask()
    {
        try
        {
            dynamic scheduler = ConnectScheduler();
            dynamic root = scheduler.GetFolder("\\");
            root.DeleteTask(TaskName, 0);
        }
        catch (Exception ex) when (ex is COMException or System.IO.FileNotFoundException)
        {
            Log.Info($"SetStartup: task '{TaskName}' already absent ({ex.Message})");
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Error($"Cleanup legacy startup registry key failed: {ex.Message}");
        }
    }

    /// <summary>Returns <c>true</c> when the scheduled task or fallback Run key exists.</summary>
    public static bool IsStartupEnabled()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Query /TN \"{TaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(2000);
            if (p?.ExitCode == 0) return true;

            using RegistryKey? hkcuKey = Registry.CurrentUser.OpenSubKey(RunKey);
            if (hkcuKey?.GetValue(AppName) is not null) return true;

            using RegistryKey? hklmKey = Registry.LocalMachine.OpenSubKey(RunKey);
            return hklmKey?.GetValue(AppName) is not null;
        }
        catch (Exception ex)
        {
            Log.Error($"IsStartupEnabled check failed: {ex.Message}");
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
        catch (ArgumentException)
        {
            // Invalid hex string - use fallback color.
            return fallback == default ? Color.White : fallback;
        }
    }
}
