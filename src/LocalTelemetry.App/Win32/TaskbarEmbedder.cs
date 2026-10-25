using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Automation;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Diagnostics;
using LocalTelemetry.Core.Models;

namespace LocalTelemetry.App.Win32;

/// <summary>
/// Embeds the overlay window into the Windows taskbar, either as a child of the
/// taskband (Approach A) or as a floating layered window (Approach B fallback).
/// Handles Win10 / Win11 taskbar structure differences and repositioning on resize.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TaskbarEmbedder(AppSettings cfg)
{
    private readonly AppSettings _cfg = cfg;


    private IntPtr _parentHwnd = IntPtr.Zero;
    private IntPtr _taskBandHwnd = IntPtr.Zero;
    private NativeMethods.RECT _taskBandOri;
    private bool _taskBandSaved;
    private bool _isWin11;

    private Rectangle _taskbarRect;
    private uint _taskbarEdge;
    private int _lastShrinkWidth = -1;
    private Rectangle _lastFloatRect = Rectangle.Empty;

    /// <summary>
    /// Gets or sets the transparency color key for the layered overlay window (child mode only).
    /// Pixels matching this color become fully transparent. Default is <see cref="Color.Empty"/>,
    /// which disables color-key transparency.
    /// </summary>
    public Color KeyColor { get; set; } = Color.Empty;

    /// <summary>Gets the current deployment lifecycle state of the overlay.</summary>
    public OverlayDeploymentState State { get; private set; } = OverlayDeploymentState.Uninitialized;

    /// <summary>Gets whether the overlay is currently embedded in the taskbar (either approach).</summary>
    public bool IsEmbedded { get; private set; }

    /// <summary>
    /// Gets whether the overlay is using the floating layered-window approach (Approach B)
    /// rather than the child-window approach (Approach A).
    /// </summary>
    public bool IsFallback { get; private set; }

    /// <summary>Gets the last calculated X coordinate for the floating overlay window.</summary>
    public int LastComputedFloatX { get; private set; }

    /// <summary>Gets the last calculated Y coordinate for the floating overlay window.</summary>
    public int LastComputedFloatY { get; private set; }

    private int _trayLeftEdge = -1;
    private int _leftWidgetRightEdge = -1;
    private int _rightWidgetLeftEdge = -1;

    // Reposition() runs on a 200ms timer; PositionInBand() computes the same position every
    // tick, so only emit a log line when the position message actually changes.
    private string? _lastPositionInBandLog;
    private string? _lastPositionFloatLog;

    /// <summary>
    /// Attempts to embed the overlay window into the taskbar. Tries the child-window
    /// approach first; falls back to a layered floating window on failure.
    /// </summary>
    /// <param name="overlayHwnd">Handle to the overlay window.</param>
    /// <param name="overlaySize">Desired size of the overlay.</param>
    /// <returns><c>true</c> if Approach A succeeded; <c>false</c> if fallback was used.</returns>
    public bool Embed(IntPtr overlayHwnd, Size overlaySize)
    {
        IsEmbedded = false;
        IsFallback = false;
        _lastFloatRect = Rectangle.Empty;

        if (overlayHwnd == IntPtr.Zero || !NativeMethods.IsWindow(overlayHwnd))
        {
            Log.Error("Overlay window handle is invalid or was destroyed. Embed aborted.");
            State = OverlayDeploymentState.Failed;
            return false;
        }

        RefreshTaskbarInfo();
        _trayLeftEdge = GetTrayLeftEdge();
        UpdateWidgetsEdges();
        LogTrayStructure();

        if (TrySetParent(overlayHwnd, overlaySize))
        {
            _lastShrinkWidth = overlaySize.Width;
            if (PositionInBand(overlayHwnd, overlaySize))
            {
                IsEmbedded = true;
                IsFallback = false;
                State = OverlayDeploymentState.Active;
                Log.Info($"Overlay embedded into taskbar (child mode, {overlaySize.Width}x{overlaySize.Height})");
                return true;
            }

            Log.Warn("PositionInBand failed after SetParent; detaching and falling back to floating mode");
            NativeMethods.SetParent(overlayHwnd, IntPtr.Zero);
        }

        RestoreLayeredStyles(overlayHwnd);
        if (PositionFloat(overlayHwnd, overlaySize))
        {
            IsEmbedded = true;
            IsFallback = true;
            State = OverlayDeploymentState.Active;
            Log.Info($"Taskbar overlay active (floating mode, {overlaySize.Width}x{overlaySize.Height})");
            return false;
        }

        Log.Error("Taskbar overlay failed to deploy into active state (both child and floating positioning failed).");
        IsEmbedded = false;
        IsFallback = false;
        State = OverlayDeploymentState.Failed;
        return false;
    }

    /// <summary>Repositions the overlay window on the taskbar after a resize or taskbar layout change.</summary>
    /// <param name="overlayHwnd">Handle to the overlay window.</param>
    /// <param name="overlaySize">New size of the overlay.</param>
    public void Reposition(IntPtr overlayHwnd, Size overlaySize)
    {
        RefreshTaskbarInfo();
        _trayLeftEdge = GetTrayLeftEdge();
        UpdateWidgetsEdges();
        if (IsFallback)
        {
            PositionFloat(overlayHwnd, overlaySize);
            return;
        }

        if (_taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd) && overlaySize.Width != _lastShrinkWidth)
        {
            ShrinkTaskBand(overlaySize);
            _lastShrinkWidth = overlaySize.Width;
        }

        PositionInBand(overlayHwnd, overlaySize);
    }

    /// <summary>Detaches the overlay from the taskbar and restores the original taskband size.</summary>
    /// <param name="overlayHwnd">Handle to the overlay window.</param>
    public void Detach(IntPtr overlayHwnd)
    {
        RestoreTaskBand();
        if (!IsFallback)
            NativeMethods.SetParent(overlayHwnd, IntPtr.Zero);
        else if (overlayHwnd != IntPtr.Zero && NativeMethods.IsWindow(overlayHwnd))
            NativeMethods.SetWindowLongPtr(overlayHwnd, NativeMethods.GWLP_HWNDPARENT, IntPtr.Zero);
        _lastFloatRect = Rectangle.Empty;
        _leftWidgetRightEdge = -1;
        _rightWidgetLeftEdge = -1;
        IsEmbedded = false;
        State = OverlayDeploymentState.Uninitialized;
    }

    /// <summary>
    /// Handles taskbar recreation (e.g. after Explorer restart) by restoring the
    /// original taskband and re-running the embed process.
    /// </summary>
    /// <param name="overlayHwnd">Handle to the overlay window.</param>
    /// <param name="overlaySize">Desired size of the overlay.</param>
    public void OnTaskbarRecreated(IntPtr overlayHwnd, Size overlaySize)
    {
        State = OverlayDeploymentState.Recovering;
        RestoreTaskBand();
        _taskBandSaved = false;
        _taskBandHwnd = IntPtr.Zero;
        _parentHwnd = IntPtr.Zero;
        _lastShrinkWidth = -1;
        _lastPositionInBandLog = null;
        _lastPositionFloatLog = null;
        ResetStaticEdgeCaches();
        _cachedAppListRightEdge = -1;
        _lastAppListQueryTicks = 0;
        _lastFloatRect = Rectangle.Empty;
        Embed(overlayHwnd, overlaySize);
    }

    public bool IsChildMode => IsEmbedded && !IsFallback;
    public IntPtr ParentHwnd => _parentHwnd;

    /// <summary>
    /// Re-applies the layered window attributes (alpha + color key) for an active overlay window.
    /// </summary>
    /// <param name="overlayHwnd">Handle to the overlay window.</param>
    public void UpdateLayeredSettings(IntPtr overlayHwnd)
    {
        if (overlayHwnd == IntPtr.Zero || !NativeMethods.IsWindow(overlayHwnd)) return;
        ApplyLayeredAttributes(overlayHwnd);
    }

    // Private
    internal static Func<IntPtr>? ExplorerTrayWndOverride { get; set; }
    internal static Func<IntPtr, IntPtr, string?, string?, IntPtr>? FindWindowExOverride { get; set; }
    internal static Func<int>? LeftWidgetRightEdgeOverride { get; set; }
    internal static Func<int>? RightWidgetLeftEdgeOverride { get; set; }
    internal static Func<int>? AppListRightEdgeOverride { get; set; }
    internal static Func<bool>? IsTaskbarCenteredOverride { get; set; }
    internal static Func<int, int>? AppListRightEdgeAutomationOverride { get; set; }
    internal static bool ThrowInAttachChildForTesting { get; set; }
    internal static bool ThrowInDumpTaskbarHierarchyForTesting { get; set; }
    internal static bool ThrowInTrayLeftEdgeForTesting { get; set; }
    internal static bool ThrowInAppListRightEdgeForTesting { get; set; }
    internal static bool ThrowInIsWidgetsFeatureEnabledForTesting { get; set; }
    internal static bool ThrowInExplorerLookupForTesting { get; set; }
    internal static bool FailGetClientRectForTesting { get; set; }
    internal static bool FailPositionFloatForTesting { get; set; }
    internal static bool ThrowInQueryWidgetsButtonEdgeForTesting { get; set; }
    internal static bool ThrowInQueryAppListRightEdgeFromAutomationForTesting { get; set; }
    internal static bool ThrowInIsTaskbarCenteredForTesting { get; set; }

    internal static IntPtr SafeFindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow)
    {
        if (FindWindowExOverride is not null)
            return FindWindowExOverride(hwndParent, hwndChildAfter, lpszClass, lpszWindow);
        return NativeMethods.FindWindowEx(hwndParent, hwndChildAfter, lpszClass, lpszWindow);
    }

    /// <summary>Locates Shell_TrayWnd owned by explorer.exe (avoids grabbing a
    /// third-party toolbar window that reused the same class name).</summary>
    public static IntPtr FindExplorerTrayWnd()
    {
        if (ExplorerTrayWndOverride is not null)
            return ExplorerTrayWndOverride();

        IntPtr hwnd = IntPtr.Zero;
        while ((hwnd = SafeFindWindowEx(IntPtr.Zero, hwnd, "Shell_TrayWnd", null)) != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) continue;
            try
            {
                if (ThrowInExplorerLookupForTesting)
                    throw new InvalidOperationException("Simulated explorer lookup error");

                using var proc = Process.GetProcessById((int)pid);
                if (proc.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                    return hwnd;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Explorer process lookup race - process exited between enumeration and query");
            }
        }
        return IntPtr.Zero;
    }

    private void RefreshTaskbarInfo()
    {
        var data = new NativeMethods.APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.APPBARDATA>(),
        };
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref data);
        _taskbarRect = new Rectangle(data.rc.Left, data.rc.Top, data.rc.Width, data.rc.Height);
        _taskbarEdge = data.uEdge;

        if (_taskbarRect.Width <= 0 || _taskbarRect.Height <= 0)
        {
            var fallback = ResolveFromTrayWindow() ?? ResolveFromScreenWorkingArea();
            if (fallback is not null)
            {
                _taskbarRect = fallback.Value.Rect;
                _taskbarEdge = fallback.Value.Edge;
            }
        }
    }

    private static (Rectangle Rect, uint Edge)? ResolveFromTrayWindow()
    {
        IntPtr tray = FindExplorerTrayWnd();
        if (tray == IntPtr.Zero || !NativeMethods.GetWindowRect(tray, out var r) || r.Width <= 0 || r.Height <= 0)
            return null;

        var rect = new Rectangle(r.Left, r.Top, r.Width, r.Height);
        uint edge;
        if (r.Top == 0 && r.Height < r.Width)
            edge = NativeMethods.ABE_TOP;
        else if (r.Left == 0 && r.Width < r.Height)
            edge = NativeMethods.ABE_LEFT;
        else if (r.Left > 0 && r.Width < r.Height)
            edge = NativeMethods.ABE_RIGHT;
        else
            edge = NativeMethods.ABE_BOTTOM;

        return (rect, edge);
    }

    private static (Rectangle Rect, uint Edge)? ResolveFromScreenWorkingArea()
    {
        var scr = Screen.PrimaryScreen;
        if (scr is null || scr.Bounds.Width <= 0 || scr.Bounds.Height <= 0)
            return null;

        return CalculateFromWorkingArea(scr.Bounds, scr.WorkingArea);
    }

    internal static (Rectangle Rect, uint Edge) CalculateFromWorkingArea(Rectangle bounds, Rectangle workingArea)
    {
        if (workingArea.Bottom < bounds.Bottom)
        {
            return (new Rectangle(bounds.Left, workingArea.Bottom, bounds.Width, bounds.Bottom - workingArea.Bottom), NativeMethods.ABE_BOTTOM);
        }
        if (workingArea.Top > bounds.Top)
        {
            return (new Rectangle(bounds.Left, bounds.Top, bounds.Width, workingArea.Top - bounds.Top), NativeMethods.ABE_TOP);
        }
        if (workingArea.Right < bounds.Right)
        {
            return (new Rectangle(workingArea.Right, bounds.Top, bounds.Right - workingArea.Right, bounds.Height), NativeMethods.ABE_RIGHT);
        }
        if (workingArea.Left > bounds.Left)
        {
            return (new Rectangle(bounds.Left, bounds.Top, workingArea.Left - bounds.Left, bounds.Height), NativeMethods.ABE_LEFT);
        }

        return (new Rectangle(bounds.Left, bounds.Bottom - 48, bounds.Width, 48), NativeMethods.ABE_BOTTOM);
    }

    /// <summary>Checks whether the Windows 11 taskbar icons are centered (1) or left-aligned (0).</summary>
    public static bool IsTaskbarCentered()
    {
        if (IsTaskbarCenteredOverride is not null)
            return IsTaskbarCenteredOverride();

        try
        {
            if (ThrowInIsTaskbarCenteredForTesting)
                throw new InvalidOperationException("Simulated TaskbarAl error");

            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            if (key is null) return true;
            int val = Convert.ToInt32(key.GetValue("TaskbarAl", 1));
            return val == 1;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to read TaskbarAl registry key; defaulting to centered");
            return true;
        }
    }

    private static int GetTrayLeftEdge()
    {
        try
        {
            if (ThrowInTrayLeftEdgeForTesting)
                throw new InvalidOperationException("Simulated tray left edge error");

            IntPtr tray = FindExplorerTrayWnd();
            if (tray == IntPtr.Zero) return -1;

            int minLeft = int.MaxValue;

            // 1. TrayNotifyWnd exists on Win10 and most Win11 builds (order: icons | clock)
            IntPtr trayNotify = SafeFindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (trayNotify != IntPtr.Zero && NativeMethods.GetWindowRect(trayNotify, out var notifyRect) && notifyRect.Width > 0)
            {
                minLeft = Math.Min(minLeft, notifyRect.Left);
            }

            // 2. Win11: clock widget
            IntPtr clock = SafeFindWindowEx(tray, IntPtr.Zero, "TrayClockWClass", null);
            if (clock == IntPtr.Zero)
                clock = SafeFindWindowEx(tray, IntPtr.Zero, "ClockCompWidget", null);
            if (clock != IntPtr.Zero && NativeMethods.GetWindowRect(clock, out var clockRect) && clockRect.Width > 0)
            {
                minLeft = Math.Min(minLeft, clockRect.Left);
            }

            // 3. Win11: inspect any visible child windows on the right half of the taskbar
            if (NativeMethods.GetWindowRect(tray, out var trayRect) && trayRect.Width > 0)
            {
                minLeft = Math.Min(minLeft, FindRightmostChildTrayLeft(tray, trayRect));

                if (minLeft != int.MaxValue)
                {
                    LogTrayEdgeChange(minLeft, $"resolved tray left={minLeft}");
                    return minLeft;
                }

                int estimatedTrayWidth = EstimateTrayWidth();
                int fallbackEdge = trayRect.Right - estimatedTrayWidth;
                LogTrayEdgeChange(fallbackEdge, $"fallback estimate from right={trayRect.Right}, width={estimatedTrayWidth}");
                return fallbackEdge;
            }

            LogTrayEdgeChange(-1, "no tray marker found, returning -1");
            return -1;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to determine tray left edge");
            return -1;
        }
    }

    private static int FindRightmostChildTrayLeft(IntPtr tray, NativeMethods.RECT trayRect)
    {
        int minLeft = int.MaxValue;
        int midX = trayRect.Left + trayRect.Width / 2;
        IntPtr child = IntPtr.Zero;
        while ((child = SafeFindWindowEx(tray, child, null, null)) != IntPtr.Zero)
        {
            if (!NativeMethods.IsWindowVisible(child)) continue;
            if (!NativeMethods.GetWindowRect(child, out var cr) || cr.Width <= 0) continue;

            // If docked on the right side and doesn't span the majority of the taskbar
            if (cr.Right >= trayRect.Right - 50 && cr.Left > midX && cr.Width < trayRect.Width * 0.45)
            {
                minLeft = Math.Min(minLeft, cr.Left);
            }
        }
        return minLeft;
    }

    // Reposition() runs on a 200ms timer; GetTrayLeftEdge() resolves the same value every
    // tick, so only emit a log line when the resolved edge actually changes significantly.
    private static int _lastLoggedTrayEdge = int.MinValue;

    private static void LogTrayEdgeChange(int edge, string message)
    {
        if (_lastLoggedTrayEdge != int.MinValue && Math.Abs(edge - _lastLoggedTrayEdge) < 40) return;
        _lastLoggedTrayEdge = edge;
        Log.Debug($"Detected tray edge via {message}");
    }

    private static int _lastLoggedWidgetEdge = int.MinValue;
    private static int _cachedLeftWidgetRightEdge = -1;
    private static int _cachedRightWidgetLeftEdge = -1;
    private static long _lastWidgetQueryTicks = 0;

    private int _cachedAppListRightEdge = -1;
    private long _lastAppListQueryTicks = 0;

    private static void ResetStaticEdgeCaches()
    {
        _lastLoggedTrayEdge = int.MinValue;
        _lastLoggedWidgetEdge = int.MinValue;
        _cachedLeftWidgetRightEdge = -1;
        _cachedRightWidgetLeftEdge = -1;
        _lastWidgetQueryTicks = 0;
    }

    private static void LogWidgetEdgeChange(int edge, string message)
    {
        if (_lastLoggedWidgetEdge != int.MinValue && Math.Abs(edge - _lastLoggedWidgetEdge) < 40) return;
        _lastLoggedWidgetEdge = edge;
        Log.Debug($"Detected widget edge via {message}");
    }

    private int GetAppListRightEdge()
    {
        if (AppListRightEdgeOverride is not null)
            return AppListRightEdgeOverride();

        long now = Stopwatch.GetTimestamp();
        if (_cachedAppListRightEdge >= 0 && (now - _lastAppListQueryTicks) < (Stopwatch.Frequency * 2))
        {
            return _cachedAppListRightEdge;
        }

        try
        {
            if (ThrowInAppListRightEdgeForTesting)
                throw new InvalidOperationException("Simulated app list right edge error");

            // If legacy taskband is present, use its actual bounding rectangle
            if (_taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd)
                && NativeMethods.GetWindowRect(_taskBandHwnd, out var bandRect) && bandRect.Width > 0)
            {
                _cachedAppListRightEdge = bandRect.Right;
                _lastAppListQueryTicks = now;
                return bandRect.Right;
            }

            int automatedRight = AppListRightEdgeAutomationOverride is not null
                ? AppListRightEdgeAutomationOverride(_taskbarRect.Width)
                : QueryAppListRightEdgeFromAutomation(_taskbarRect.Width);
            if (automatedRight > 0)
            {
                _cachedAppListRightEdge = automatedRight;
                _lastAppListQueryTicks = now;
                return automatedRight;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to determine app list right edge via UIAutomation");
        }

        int fallback = _taskbarRect.Left + WindowHelpers.Scale(360, 1f);
        _cachedAppListRightEdge = fallback;
        _lastAppListQueryTicks = now;
        return fallback;
    }

    private static int QueryAppListRightEdgeFromAutomation(int taskbarWidth)
    {
        IntPtr tray = FindExplorerTrayWnd();
        if (tray == IntPtr.Zero) return -1;

        if (ThrowInQueryAppListRightEdgeFromAutomationForTesting)
            throw new InvalidOperationException("Simulated app list right edge automation error");

        var trayElem = AutomationElement.FromHandle(tray);
        if (trayElem is null) return -1;

        string[] targetIds = ["TaskbarItemsContainer", "TaskList"];
        foreach (string id in targetIds)
        {
            var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, id);
            var listElem = trayElem.FindFirst(TreeScope.Descendants, cond);
            if (listElem is null) continue;

            int maxChildRight = FindMaxButtonRight(listElem);
            if (maxChildRight > 0) return maxChildRight;

            var rect = listElem.Current.BoundingRectangle;
            int maxAllowed = taskbarWidth > 0 ? (int)(taskbarWidth * 0.75) : 1400;
            if (!rect.IsEmpty && rect.Width > 0 && rect.Width < maxAllowed)
            {
                return (int)rect.Right;
            }
        }

        return -1;
    }

    private static int FindMaxButtonRight(AutomationElement listElem)
    {
        var children = listElem.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        if (children is null || children.Count == 0) return -1;
        var rects = new List<System.Windows.Rect>(children.Count);
        foreach (AutomationElement child in children)
            rects.Add(child.Current.BoundingRectangle);
        return CalculateMaxButtonRight(rects);
    }

    internal static int CalculateMaxButtonRight(IEnumerable<System.Windows.Rect> rects)
    {
        int maxChildRight = -1;
        foreach (var cr in rects)
        {
            if (!cr.IsEmpty && cr.Width > 0)
            {
                maxChildRight = Math.Max(maxChildRight, (int)cr.Right);
            }
        }
        return maxChildRight;
    }

    private void UpdateWidgetsEdges()
    {
        if (LeftWidgetRightEdgeOverride is not null)
        {
            _leftWidgetRightEdge = LeftWidgetRightEdgeOverride();
            _rightWidgetLeftEdge = RightWidgetLeftEdgeOverride is not null ? RightWidgetLeftEdgeOverride() : -1;
            return;
        }

        if (RightWidgetLeftEdgeOverride is not null)
        {
            _rightWidgetLeftEdge = RightWidgetLeftEdgeOverride();
            _leftWidgetRightEdge = LeftWidgetRightEdgeOverride is not null ? LeftWidgetRightEdgeOverride() : -1;
            return;
        }

        if (!IsWindows11Taskbar() || !IsWidgetsFeatureEnabled())
        {
            _leftWidgetRightEdge = -1;
            _rightWidgetLeftEdge = -1;
            return;
        }

        IntPtr tray = FindExplorerTrayWnd();
        if (tray == IntPtr.Zero)
        {
            _leftWidgetRightEdge = -1;
            _rightWidgetLeftEdge = -1;
            return;
        }

        var (leftRight, rightLeft) = QueryWidgetsButtonEdge(tray, _taskbarRect.Width);
        _leftWidgetRightEdge = leftRight;
        _rightWidgetLeftEdge = rightLeft;
    }

    private static int GetLeftWidgetRightEdge()
    {
        if (LeftWidgetRightEdgeOverride is not null)
            return LeftWidgetRightEdgeOverride();

        if (!IsWindows11Taskbar() || !IsWidgetsFeatureEnabled())
            return -1;

        IntPtr tray = FindExplorerTrayWnd();
        if (tray == IntPtr.Zero) return -1;

        return QueryWidgetsButtonRightEdge(tray);
    }

    private static int QueryWidgetsButtonRightEdge(IntPtr tray)
    {
        return QueryWidgetsButtonEdge(tray, 1920).leftRightEdge;
    }

    private int GetRightSideBoundary()
    {
        if (RightWidgetLeftEdgeOverride is not null)
        {
            int r = RightWidgetLeftEdgeOverride();
            if (r > 0 && _trayLeftEdge > 0) return Math.Min(_trayLeftEdge, r);
            if (r > 0) return r;
        }

        if (_rightWidgetLeftEdge > 0 && _trayLeftEdge > 0)
        {
            return Math.Min(_trayLeftEdge, _rightWidgetLeftEdge);
        }

        if (_rightWidgetLeftEdge > 0)
            return _rightWidgetLeftEdge;

        if (_trayLeftEdge > 0)
            return _trayLeftEdge;

        return -1;
    }

    private static bool IsWidgetsFeatureEnabled()
    {
        try
        {
            if (ThrowInIsWidgetsFeatureEnabledForTesting)
                throw new InvalidOperationException("Simulated widgets error");

            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            if (key is null) return true;
            object? val = key.GetValue("TaskbarDa");
            if (val is null) return true;
            return Convert.ToInt32(val) != 0;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to read TaskbarDa registry key; assuming enabled");
            return true;
        }
    }

    private static (int leftRightEdge, int rightLeftEdge) QueryWidgetsButtonEdge(IntPtr tray, int taskbarWidth)
    {
        long now = Stopwatch.GetTimestamp();
        if ((_cachedLeftWidgetRightEdge >= 0 || _cachedRightWidgetLeftEdge >= 0) && (now - _lastWidgetQueryTicks) < (Stopwatch.Frequency * 2))
        {
            return (_cachedLeftWidgetRightEdge, _cachedRightWidgetLeftEdge);
        }

        try
        {
            if (ThrowInQueryWidgetsButtonEdgeForTesting)
                throw new InvalidOperationException("Simulated query widgets button error");

            var trayElem = AutomationElement.FromHandle(tray);
            if (trayElem is not null)
            {
                var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, "WidgetsButton");
                var widgetBtn = trayElem.FindFirst(TreeScope.Descendants, cond);
                if (widgetBtn is not null)
                {
                    var result = ParseWidgetBoundingBox(widgetBtn, taskbarWidth, now);
                    if (result != (-1, -1))
                    {
                        return result;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "UIAutomation query for WidgetsButton failed");
        }

        _cachedLeftWidgetRightEdge = -1;
        _cachedRightWidgetLeftEdge = -1;
        _lastWidgetQueryTicks = now;
        return (-1, -1);
    }

    private static (int leftRightEdge, int rightLeftEdge) ParseWidgetBoundingBox(AutomationElement widgetBtn, int taskbarWidth, long now)
    {
        return CalculateWidgetEdges(widgetBtn.Current.BoundingRectangle, taskbarWidth, now);
    }

    internal static (int leftRightEdge, int rightLeftEdge) CalculateWidgetEdges(System.Windows.Rect rect, int taskbarWidth, long now)
    {
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
        {
            return (-1, -1);
        }

        int midX = taskbarWidth > 0 ? (taskbarWidth / 2) : 960;
        if (rect.Left < midX)
        {
            int right = (int)rect.Right;
            _cachedLeftWidgetRightEdge = right;
            _cachedRightWidgetLeftEdge = -1;
            _lastWidgetQueryTicks = now;
            LogWidgetEdgeChange(right, $"UIAutomation WidgetsButton on left, right={right}");
            return (right, -1);
        }

        int left = (int)rect.Left;
        _cachedLeftWidgetRightEdge = -1;
        _cachedRightWidgetLeftEdge = left;
        _lastWidgetQueryTicks = now;
        LogWidgetEdgeChange(left, $"UIAutomation WidgetsButton on right, left={left}");
        return (-1, left);
    }

    /// <summary>
    /// Dumps the direct child windows of the Explorer taskbar (class name, visibility,
    /// rect) to the system log. Used to diagnose overlay placement issues.
    /// </summary>
    private static void LogTrayStructure()
    {
        try
        {
            if (ThrowInDumpTaskbarHierarchyForTesting)
                throw new InvalidOperationException("Simulated dump hierarchy error");

            IntPtr tray = FindExplorerTrayWnd();
            if (tray == IntPtr.Zero)
            {
                Log.Debug("System taskbar window (Shell_TrayWnd) not found");
                return;
            }

            Log.Debug("Inspecting taskbar window hierarchy");
            var className = new char[256];
            IntPtr child = IntPtr.Zero;
            while ((child = NativeMethods.FindWindowEx(tray, child, null, null)) != IntPtr.Zero)
            {
                Array.Clear(className);
                NativeMethods.GetClassName(child, className, className.Length);
                string cls = new string(className).TrimEnd('\0');
                NativeMethods.GetWindowRect(child, out var r);
                Log.Debug($"Taskbar child window '{cls}', visible {NativeMethods.IsWindowVisible(child)}, bounds {r.Left},{r.Top} {r.Width}x{r.Height}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to inspect system taskbar window hierarchy");
        }
    }

    private static bool IsWindows11Taskbar()
    {
        // Primary: OS build number (Win11 starts at 22000)
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return false;

        var tray = FindExplorerTrayWnd();
        if (tray != IntPtr.Zero)
        {
            // If DesktopWindowContentBridge is present, native Win11 XAML taskbar is active.
            // DirectComposition swapchains render on top of Win32 child windows, so floating
            // mode (Approach B) must be used.
            if (SafeFindWindowEx(tray, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null) != IntPtr.Zero)
                return true;

            // Only if DesktopWindowContentBridge is absent AND ReBarWindow32 is present
            // is a legacy Win10 taskbar replacement (e.g. ExplorerPatcher) active.
            if (SafeFindWindowEx(tray, IntPtr.Zero, "ReBarWindow32", null) != IntPtr.Zero)
            {
                Log.Info("ReBarWindow32 detected without DesktopWindowContentBridge (ExplorerPatcher / legacy taskbar replacement active)");
                return false;
            }
        }

        return true;
    }

    private bool TrySetParent(IntPtr overlayHwnd, Size overlaySize)
    {
        try
        {
            if (ThrowInAttachChildForTesting)
                throw new InvalidOperationException("Simulated attach child error");

            var tray = FindExplorerTrayWnd();
            if (tray == IntPtr.Zero) return false;

            _isWin11 = IsWindows11Taskbar();
            if (!ResolveParentAndTaskBand(tray)) return false;

            SaveOriginalTaskBandRect();
            ConfigureOverlayWindowStyles(overlayHwnd);

            NativeMethods.SetParent(overlayHwnd, _parentHwnd);

            RestoreOverlayLayeredStyle(overlayHwnd);
            ApplyLayeredAttributes(overlayHwnd);

            if (_taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd))
                ShrinkTaskBand(overlaySize);

            State = OverlayDeploymentState.Active;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to attach overlay window to taskbar; switching to floating window fallback");
            return false;
        }
    }

    private bool ResolveParentAndTaskBand(IntPtr tray)
    {
        if (_isWin11)
        {
            _parentHwnd = tray;
            _taskBandHwnd = IntPtr.Zero;
            Log.Debug("Windows 11 taskbar detected; embedding overlay directly into Shell_TrayWnd");
            return true;
        }

        var rebar = SafeFindWindowEx(tray, IntPtr.Zero, "ReBarWindow32", null);
        if (rebar == IntPtr.Zero)
            rebar = SafeFindWindowEx(tray, IntPtr.Zero, "WorkerW", null);
        if (rebar == IntPtr.Zero || !NativeMethods.IsWindow(rebar))
        {
            Log.Debug("No taskbar parent band found (ReBarWindow32 / WorkerW absent); child embedding skipped");
            return false;
        }

        var taskBand = SafeFindWindowEx(rebar, IntPtr.Zero, "MSTaskSwWClass", null);
        if (taskBand == IntPtr.Zero)
            taskBand = SafeFindWindowEx(rebar, IntPtr.Zero, "MSTaskListWClass", null);

        if (taskBand == IntPtr.Zero || !NativeMethods.IsWindow(taskBand))
        {
            Log.Debug("No taskband window (MSTaskSwWClass / MSTaskListWClass) found inside parent; child embedding skipped");
            return false;
        }

        _parentHwnd = rebar;
        _taskBandHwnd = taskBand;
        Log.Debug("Taskband resolved successfully with parent and taskband window");
        return true;
    }

    private void SaveOriginalTaskBandRect()
    {
        if (_taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd) && !_taskBandSaved)
        {
            NativeMethods.GetWindowRect(_taskBandHwnd, out _taskBandOri);
            _taskBandSaved = true;
        }
    }

    private static void ConfigureOverlayWindowStyles(IntPtr overlayHwnd)
    {
        int ex = NativeMethods.GetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        ex &= ~NativeMethods.WS_EX_LAYERED;
        NativeMethods.SetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE, ex);

        int style = NativeMethods.GetWindowLong(overlayHwnd, NativeMethods.GWL_STYLE);
        style &= ~NativeMethods.WS_POPUP;
        style |= NativeMethods.WS_CHILD;
        NativeMethods.SetWindowLong(overlayHwnd, NativeMethods.GWL_STYLE, style);
    }

    private static void RestoreOverlayLayeredStyle(IntPtr overlayHwnd)
    {
        int ex = NativeMethods.GetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_LAYERED;
        NativeMethods.SetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE, ex);
    }

    private void ApplyLayeredAttributes(IntPtr overlayHwnd)
    {
        byte alpha = (byte)(_cfg.Overlay.Opacity * 255 / 100);
        uint flags = NativeMethods.LWA_ALPHA;
        int crKey = 0;
        if (!KeyColor.IsEmpty)
        {
            flags |= NativeMethods.LWA_COLORKEY;
            crKey = KeyColor.R | (KeyColor.G << 8) | (KeyColor.B << 16);
        }
        NativeMethods.SetLayeredWindowAttributes(overlayHwnd, crKey, alpha, flags);
    }

    private void ShrinkTaskBand(Size overlaySize)
    {
        if (_taskBandHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_taskBandHwnd)) return;

        bool horizontal = _taskbarEdge == NativeMethods.ABE_BOTTOM
                       || _taskbarEdge == NativeMethods.ABE_TOP;
        if (!horizontal) return;

        NativeMethods.GetWindowRect(_taskBandHwnd, out var cur);
        int gap = WindowHelpers.Scale(4, 1f);
        int overlayW = overlaySize.Width + gap;
        int newBandW = Math.Max(cur.Width - overlayW, 50);

        if (_cfg.Overlay.Placement == "left")
        {
            NativeMethods.MoveWindow(_taskBandHwnd,
                _taskBandOri.Left + overlayW, 0,
                newBandW, cur.Height, true);
        }
        else
        {
            NativeMethods.MoveWindow(_taskBandHwnd,
                _taskBandOri.Left, 0,
                newBandW, cur.Height, true);
        }
    }

    private void RestoreTaskBand()
    {
        if (_taskBandSaved && _taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd))
        {
            bool h = _taskbarEdge == NativeMethods.ABE_BOTTOM
                  || _taskbarEdge == NativeMethods.ABE_TOP;
            NativeMethods.MoveWindow(_taskBandHwnd,
                _taskBandOri.Left,
                h ? 0 : _taskBandOri.Top,
                _taskBandOri.Width, _taskBandOri.Height, true);
        }
        _taskBandSaved = false;
    }

    private bool PositionInBand(IntPtr overlayHwnd, Size sz)
    {
        if (_parentHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_parentHwnd))
        {
            Log.Error("Cannot position overlay in taskbar band because parent taskbar window is invalid or destroyed.");
            State = OverlayDeploymentState.Failed;
            return false;
        }

        if (!NativeMethods.GetClientRect(_parentHwnd, out var parentRect) || FailGetClientRectForTesting)
        {
            Log.Error("Cannot position overlay in taskbar band because retrieving parent taskbar client bounds failed.");
            State = OverlayDeploymentState.Failed;
            return false;
        }

        int x = ComputeBandX(parentRect, sz);
        x = Math.Max(x, 0);
        if (x + sz.Width > parentRect.Width)
            x = Math.Max(parentRect.Width - sz.Width, 0);

        int y = Math.Max(0, (parentRect.Height - sz.Height) / 2);

        string positionLog = $"Positioned overlay in taskbar band ({sz.Width}x{sz.Height} at {x},{y}, offset {_cfg.Overlay.PlacementOffset}, placement '{_cfg.Overlay.Placement}')";
        if (positionLog != _lastPositionInBandLog)
        {
            _lastPositionInBandLog = positionLog;
            Log.Debug(positionLog);
        }

        bool success = NativeMethods.SetWindowPos(
            overlayHwnd, NativeMethods.HWND_TOP,
            x, y, sz.Width, sz.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        if (!success)
        {
            int err = Marshal.GetLastWin32Error();
            Log.Error($"SetWindowPos failed with Win32 error {err} during band positioning at ({x}, {y}).");
            State = OverlayDeploymentState.Failed;
            return false;
        }

        byte alpha = (byte)(_cfg.Overlay.Opacity * 255 / 100);
        uint flags = NativeMethods.LWA_ALPHA;
        int crKey = 0;
        if (!KeyColor.IsEmpty)
        {
            flags |= NativeMethods.LWA_COLORKEY;
            crKey = KeyColor.R | (KeyColor.G << 8) | (KeyColor.B << 16);
        }
        NativeMethods.SetLayeredWindowAttributes(
            overlayHwnd, crKey, alpha, flags);

        return true;
    }

    private int ComputeBandX(NativeMethods.RECT parentRect, Size sz)
    {
        if (!_isWin11 && _taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd))
        {
            NativeMethods.GetWindowRect(_taskBandHwnd, out var bandRect);
            var bandPt = new NativeMethods.POINT { X = bandRect.Left, Y = bandRect.Top };
            NativeMethods.ScreenToClient(_parentHwnd, ref bandPt);

            if (_cfg.Overlay.Placement == "left")
            {
                return Math.Max(bandPt.X - sz.Width, 0);
            }

            int bandEnd = bandPt.X + bandRect.Width;
            int rightBound = _trayLeftEdge > 0 ? _trayLeftEdge : parentRect.Width;
            return Math.Max(Math.Min(bandEnd, rightBound - sz.Width), 0);
        }

        int rightBoundary = GetRightSideBoundary();
        int clientRightBoundary;
        if (rightBoundary > 0)
        {
            var pt = new NativeMethods.POINT { X = rightBoundary, Y = 0 };
            NativeMethods.ScreenToClient(_parentHwnd, ref pt);
            clientRightBoundary = pt.X;
        }
        else
        {
            clientRightBoundary = parentRect.Width - EstimateTrayWidth();
        }

        return !IsTaskbarCentered()
            ? ComputeBandLeftAlignedX(sz, clientRightBoundary)
            : ComputeBandCenteredX(sz, clientRightBoundary);
    }

    private int ComputeBandLeftAlignedX(Size sz, int clientRightBoundary)
    {
        int appRight = GetAppListRightEdge();
        var appPt = new NativeMethods.POINT { X = appRight, Y = 0 };
        NativeMethods.ScreenToClient(_parentHwnd, ref appPt);
        int clientAppRight = Math.Max(0, appPt.X);

        int maxRight = Math.Max(clientAppRight, clientRightBoundary - sz.Width);
        int x = _cfg.Overlay.Placement is "beside_apps" or "left"
            ? clientAppRight + _cfg.Overlay.PlacementOffset
            : clientRightBoundary - sz.Width - _cfg.Overlay.PlacementOffset;

        return Math.Clamp(x, clientAppRight, maxRight);
    }

    private int ComputeBandCenteredX(Size sz, int clientRightBoundary)
    {
        if (_cfg.Overlay.Placement == "left")
        {
            int leftEdge = _leftWidgetRightEdge > 0 ? _leftWidgetRightEdge : GetLeftWidgetRightEdge();
            if (leftEdge > 0)
            {
                var pt = new NativeMethods.POINT { X = leftEdge, Y = 0 };
                NativeMethods.ScreenToClient(_parentHwnd, ref pt);
                return Math.Max(0, pt.X + _cfg.Overlay.PlacementOffset);
            }

            return _cfg.Overlay.PlacementOffset;
        }

        int rightEdge = clientRightBoundary - sz.Width - _cfg.Overlay.PlacementOffset;
        return Math.Max(0, rightEdge);
    }

    private bool PositionFloat(IntPtr overlayHwnd, Size sz)
    {
        if (FailPositionFloatForTesting)
            return false;

        if (_cfg.Overlay.FloatX >= 0 && _cfg.Overlay.FloatY >= 0)
        {
            return PositionCustomFloat(overlayHwnd, sz);
        }

        if (_taskbarRect.Width <= 0 || _taskbarRect.Height <= 0)
        {
            Log.Error($"Taskbar bounds are invalid ({_taskbarRect.Width}x{_taskbarRect.Height}); skipping overlay repositioning until taskbar geometry is resolved");
            State = OverlayDeploymentState.Failed;
            return false;
        }

        return PositionTaskbarFloat(overlayHwnd, sz);
    }

    private bool PositionCustomFloat(IntPtr overlayHwnd, Size sz)
    {
        LastComputedFloatX = _cfg.Overlay.FloatX;
        LastComputedFloatY = _cfg.Overlay.FloatY;
        if (overlayHwnd == IntPtr.Zero || !NativeMethods.IsWindow(overlayHwnd))
        {
            return false;
        }

        var targetRect = new Rectangle(_cfg.Overlay.FloatX, _cfg.Overlay.FloatY, sz.Width, sz.Height);
        return ApplyFloatWindowPos(overlayHwnd, targetRect, $"custom float positioning ({_cfg.Overlay.FloatX}, {_cfg.Overlay.FloatY})");
    }

    private bool PositionTaskbarFloat(IntPtr overlayHwnd, Size sz)
    {
        bool horizontal = _taskbarEdge is NativeMethods.ABE_BOTTOM or NativeMethods.ABE_TOP;
        int x = horizontal ? ComputeHorizontalTaskbarFloatX(sz.Width) : _taskbarRect.Left + 4;
        int y = _taskbarRect.Top + (_taskbarRect.Height - sz.Height) / 2;

        LastComputedFloatX = x;
        LastComputedFloatY = y;

        string floatLog = $"Positioned floating overlay ({sz.Width}x{sz.Height} at {x},{y}, offset {_cfg.Overlay.PlacementOffset}, placement '{_cfg.Overlay.Placement}')";
        if (floatLog != _lastPositionFloatLog)
        {
            _lastPositionFloatLog = floatLog;
            Log.Debug(floatLog);
        }

        var newFloatRect = new Rectangle(x, y, sz.Width, sz.Height);
        return ApplyFloatWindowPos(overlayHwnd, newFloatRect, $"floating positioning at ({x}, {y})");
    }

    private int ComputeHorizontalTaskbarFloatX(int width)
    {
        int x;
        if (_isWin11 && !IsTaskbarCentered())
        {
            x = _cfg.Overlay.Placement is "beside_apps" or "left"
                ? ComputeLeftFloatX()
                : ComputeRightFloatX(width);
        }
        else
        {
            x = _cfg.Overlay.Placement switch
            {
                "left" => ComputeLeftFloatX(),
                "center" => _taskbarRect.Left + (_taskbarRect.Width - width) / 2 + _cfg.Overlay.PlacementOffset,
                _ => ComputeRightFloatX(width),
            };
        }

        int minX = (_isWin11 && !IsTaskbarCentered()) ? GetAppListRightEdge() : _taskbarRect.Left;
        int rightBoundary = GetRightSideBoundary();
        int maxX = Math.Max(minX, (rightBoundary > 0 ? rightBoundary : _taskbarRect.Right) - width);

        if (x < minX || x > maxX)
        {
            Log.Warn($"Calculated floating X coordinate ({x}) for width {width} is out of taskbar horizontal range [{minX}, {maxX}]. Clamping to taskbar.");
            x = Math.Clamp(x, minX, maxX);
        }

        return x;
    }

    private bool ApplyFloatWindowPos(IntPtr overlayHwnd, Rectangle targetRect, string errorContext)
    {
        if (_lastFloatRect == targetRect)
        {
            return true;
        }

        uint flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
        if (_lastFloatRect.IsEmpty)
        {
            flags |= NativeMethods.SWP_FRAMECHANGED;
        }

        ApplyLayeredAttributes(overlayHwnd);
        bool success = NativeMethods.SetWindowPos(
            overlayHwnd, NativeMethods.HWND_TOPMOST,
            targetRect.X, targetRect.Y, targetRect.Width, targetRect.Height,
            flags);
        if (!success)
        {
            int err = Marshal.GetLastWin32Error();
            Log.Error($"SetWindowPos failed with Win32 error {err} during {errorContext}.");
            State = OverlayDeploymentState.Failed;
            return false;
        }

        _lastFloatRect = targetRect;
        return true;
    }

    private int ComputeLeftFloatX()
    {
        if (_isWin11 && !IsTaskbarCentered())
        {
            int appRight = GetAppListRightEdge();
            int leftBase = appRight > _taskbarRect.Left
                ? appRight
                : _taskbarRect.Left + WindowHelpers.Scale(360, 1f);
            return leftBase + _cfg.Overlay.PlacementOffset;
        }

        int leftEdge = _leftWidgetRightEdge > 0 ? _leftWidgetRightEdge : GetLeftWidgetRightEdge();
        int defaultLeftBase = leftEdge > _taskbarRect.Left
            ? leftEdge
            : _taskbarRect.Left;

        return defaultLeftBase + _cfg.Overlay.PlacementOffset;
    }

    private int ComputeRightFloatX(int widgetWidth)
    {
        int rightBoundary = GetRightSideBoundary();
        if (rightBoundary > 0)
        {
            int x = rightBoundary - widgetWidth - _cfg.Overlay.PlacementOffset;
            return Math.Max(x, _taskbarRect.Left);
        }

        int trayWidth = EstimateTrayWidth();
        return _taskbarRect.Right - widgetWidth - trayWidth - _cfg.Overlay.PlacementOffset;
    }

    private static int EstimateTrayWidth()
    {
        int iconW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
        int dpiScaledBase = WindowHelpers.Scale(360, 1f);
        return Math.Max(iconW * 11 + 120, dpiScaledBase);
    }

    private static void RestoreLayeredStyles(IntPtr overlayHwnd)
    {
        NativeMethods.SetParent(overlayHwnd, IntPtr.Zero);

        int ex = NativeMethods.GetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_NOACTIVATE
             | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE, ex);

        int style = NativeMethods.GetWindowLong(overlayHwnd, NativeMethods.GWL_STYLE);
        style &= ~NativeMethods.WS_CHILD;
        style |= NativeMethods.WS_POPUP;
        NativeMethods.SetWindowLong(overlayHwnd, NativeMethods.GWL_STYLE, style);

        IntPtr tray = FindExplorerTrayWnd();
        if (tray != IntPtr.Zero)
        {
            NativeMethods.SetWindowLongPtr(overlayHwnd, NativeMethods.GWLP_HWNDPARENT, tray);
        }
    }
}
