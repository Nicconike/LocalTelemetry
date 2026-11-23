using System.Runtime.Versioning;
using LocalTelemetry.Core.Diagnostics;

namespace LocalTelemetry.App.Win32;

/// <summary>
/// Represents a physical display monitor and its associated Windows taskbar window.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed record DisplayInfo
{
    /// <summary>Unique identifier used in settings (e.g. <c>"primary"</c>, <c>"display_2"</c>).</summary>
    public string Id { get; init; } = "primary";

    /// <summary>0-based display index (0 is always primary).</summary>
    public int Index { get; init; }

    /// <summary>System device name (e.g. <c>\\.\DISPLAY1</c>).</summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>User-friendly label (e.g. <c>Primary Display (2048 x 1280)</c>).</summary>
    public string FriendlyName { get; init; } = string.Empty;

    /// <summary>Whether this is the primary Windows display.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>Display monitor virtual desktop rectangle.</summary>
    public Rectangle Bounds { get; init; }

    /// <summary>Display monitor working area rectangle excluding the taskbar.</summary>
    public Rectangle WorkingArea { get; init; }

    /// <summary>Handle to the taskbar window for this display (<c>Shell_TrayWnd</c> or <c>Shell_SecondaryTrayWnd</c>).</summary>
    public IntPtr TaskbarHwnd { get; set; } = IntPtr.Zero;

    /// <summary>Whether a valid taskbar window was discovered for this display.</summary>
    public bool HasTaskbar
    {
        get
        {
            if (TaskbarHwnd == IntPtr.Zero) return false;
            if (SkipIsWindowCheckForTesting) return true;
            return NativeMethods.IsWindow(TaskbarHwnd);
        }
    }

    internal static bool SkipIsWindowCheckForTesting { get; set; }

    // Test seam
    internal static Func<List<DisplayInfo>>? EnumerateDisplaysOverride { get; set; }

    /// <summary>
    /// Enumerates all currently connected displays and resolves their taskbar windows.
    /// </summary>
    /// <returns>List of displays with Primary first, followed by secondary monitors.</returns>
    public static List<DisplayInfo> EnumerateDisplays()
    {
        if (EnumerateDisplaysOverride is not null)
            return EnumerateDisplaysOverride();

        var result = new List<DisplayInfo>();
        try
        {
            var screens = Screen.AllScreens;
            if (screens is null || screens.Length == 0)
            {
                return result;
            }

            IntPtr primaryTray = TaskbarEmbedder.FindExplorerTrayWnd();
            var secondaryTrays = TaskbarEmbedder.FindExplorerSecondaryTrayWnds();

            // Match secondary tray HWNDs to screen bounds
            var secondaryTrayMap = new Dictionary<Screen, IntPtr>();
            foreach (var secHwnd in secondaryTrays)
            {
                if (NativeMethods.GetWindowRect(secHwnd, out var rect))
                {
                    var r = new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
                    var matchedScreen = screens.FirstOrDefault(s => !s.Primary && s.Bounds.IntersectsWith(r));
                    if (matchedScreen is not null && !secondaryTrayMap.ContainsKey(matchedScreen))
                    {
                        secondaryTrayMap[matchedScreen] = secHwnd;
                    }
                }
            }

            // Primary screen is index 0
            var primaryScreen = screens.FirstOrDefault(s => s.Primary) ?? screens[0];
            result.Add(new DisplayInfo
            {
                Id = "primary",
                Index = 0,
                DeviceName = primaryScreen.DeviceName,
                FriendlyName = $"Display 1 (Primary - {primaryScreen.Bounds.Width} x {primaryScreen.Bounds.Height})",
                IsPrimary = true,
                Bounds = primaryScreen.Bounds,
                WorkingArea = primaryScreen.WorkingArea,
                TaskbarHwnd = primaryTray
            });

            // Secondary screens
            int secIdx = 1;
            foreach (var screen in screens.Where(s => s != primaryScreen))
            {
                int displayNum = secIdx + 1;
                secondaryTrayMap.TryGetValue(screen, out var trayHwnd);

                result.Add(new DisplayInfo
                {
                    Id = $"display_{displayNum}",
                    Index = secIdx,
                    DeviceName = screen.DeviceName,
                    FriendlyName = $"Display {displayNum} ({screen.Bounds.Width} x {screen.Bounds.Height})",
                    IsPrimary = false,
                    Bounds = screen.Bounds,
                    WorkingArea = screen.WorkingArea,
                    TaskbarHwnd = trayHwnd
                });
                secIdx++;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enumerate connected displays");
        }

        return result;
    }
}
