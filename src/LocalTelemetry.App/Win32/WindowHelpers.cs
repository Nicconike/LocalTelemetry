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

    /// <summary>
    /// Adds or removes the Windows Scheduled Task (LocalTelemetry Startup) with /RL HIGHEST.
    /// Running via Task Scheduler on logon with highest privileges allows the elevated app to boot silently
    /// without showing UAC prompts on system startup.
    /// </summary>
    public static void SetStartup(bool enable, bool startMinimized = false)
    {
        try
        {
            string exe = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrEmpty(exe)) return;

            if (enable)
            {
                string arg = startMinimized ? " --minimized" : "";
                string targetCmd = $"\"{exe}\"{arg}";

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Create /TN \"{TaskName}\" /TR \"{targetCmd}\" /SC ONLOGON /RL HIGHEST /F",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(3000);
            }
            else
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Delete /TN \"{TaskName}\" /F",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(3000);

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
        }
        catch (Exception ex)
        {
            Log.Error($"SetStartup failed: {ex.Message}");
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
