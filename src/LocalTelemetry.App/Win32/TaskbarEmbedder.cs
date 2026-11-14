using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Automation;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Diagnostics;
using LocalTelemetry.Core.Models;

namespace LocalTelemetry.App.Win32;

/// <summary>
/// Embeds the overlay window directly into the Windows taskbar as a native child window.
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

    /// <summary>
    /// Gets or sets the transparency color key for the layered overlay window (child mode only).
    /// Pixels matching this color become fully transparent. Default is <see cref="Color.Empty"/>,
    /// which disables color-key transparency.
    /// </summary>
    public Color KeyColor { get; set; } = Color.Empty;

    /// <summary>Gets the current deployment lifecycle state of the overlay.</summary>
    public OverlayDeploymentState State { get; private set; } = OverlayDeploymentState.Uninitialized;

    /// <summary>Gets whether the overlay is currently embedded in the taskbar.</summary>
    public bool IsEmbedded { get; private set; }

    /// <summary>Gets whether the available taskbar space is narrower than the overlay width.</summary>
    public bool IsTaskbarSpaceConstrained { get; private set; }

    /// <summary>Gets the measured width in pixels of the taskbar slot available for the overlay.</summary>
    public int AvailableSlotWidth { get; private set; }

    private int _trayLeftEdge = -1;
    private int _leftWidgetRightEdge = -1;
    private int _rightWidgetLeftEdge = -1;

    /// <summary>
    /// Attempts to embed the overlay window into the taskbar as a native child window.
    /// </summary>
    /// <param name="overlayHwnd">Handle to the overlay window.</param>
    /// <param name="overlaySize">Desired size of the overlay.</param>
    /// <returns><c>true</c> if embedded as a child window successfully; otherwise, <c>false</c>.</returns>
    public bool Embed(IntPtr overlayHwnd, Size overlaySize)
    {
        IsEmbedded = false;

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
                State = OverlayDeploymentState.Active;
                return true;
            }

            Log.Warn("PositionInBand failed after SetParent; detaching from parent");
            NativeMethods.SetParent(overlayHwnd, IntPtr.Zero);
        }

        Log.Warn("Taskbar overlay failed to embed into taskbar band; will retry on next layout tick");
        IsEmbedded = false;
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
        if (overlayHwnd != IntPtr.Zero && NativeMethods.IsWindow(overlayHwnd))
        {
            NativeMethods.SetWindowLongPtr(overlayHwnd, NativeMethods.GWLP_HWNDPARENT, IntPtr.Zero);
            NativeMethods.SetParent(overlayHwnd, IntPtr.Zero);
        }
        _leftWidgetRightEdge = -1;
        _rightWidgetLeftEdge = -1;
        IsTaskbarSpaceConstrained = false;
        AvailableSlotWidth = 0;
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
        ResetStaticEdgeCaches();
        _cachedAppListRightEdge = -1;
        _lastAppListQueryTicks = 0;
        _cachedAppListLeftEdge = -1;
        _lastAppListLeftQueryTicks = 0;
        IsTaskbarSpaceConstrained = false;
        AvailableSlotWidth = 0;
        Embed(overlayHwnd, overlaySize);
    }

    public bool IsChildMode => IsEmbedded;
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
    internal static Func<int>? AppListLeftEdgeOverride { get; set; }
    internal static Func<int>? AppListRightEdgeOverride { get; set; }
    internal static Func<bool>? IsTaskbarCenteredOverride { get; set; }
    internal static Func<bool>? IsWidgetsFeatureEnabledOverride { get; set; }
    internal static Func<int, int>? AppListLeftEdgeAutomationOverride { get; set; }
    internal static Func<int, int>? AppListRightEdgeAutomationOverride { get; set; }
    internal static Func<int, int>? TrayLeftEdgeAutomationOverride { get; set; }
    internal static bool ThrowInAttachChildForTesting { get; set; }
    internal static bool ThrowInDumpTaskbarHierarchyForTesting { get; set; }
    internal static bool ThrowInTrayLeftEdgeForTesting { get; set; }
    internal static bool ThrowInTrayLeftEdgeAutomationForTesting { get; set; }
    internal static bool ThrowInAppListLeftEdgeForTesting { get; set; }
    internal static bool ThrowInAppListRightEdgeForTesting { get; set; }
    internal static bool ThrowInIsWidgetsFeatureEnabledForTesting { get; set; }
    internal static bool ThrowInExplorerLookupForTesting { get; set; }
    internal static bool FailGetClientRectForTesting { get; set; }
    internal static bool ThrowInQueryWidgetsButtonEdgeForTesting { get; set; }
    internal static bool ThrowInQueryAppListLeftEdgeFromAutomationForTesting { get; set; }
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

    private static int FindNativeTrayLeft(IntPtr tray, NativeMethods.RECT trayRect)
    {
        int minLeft = int.MaxValue;
        IntPtr trayNotify = SafeFindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        if (trayNotify != IntPtr.Zero && NativeMethods.GetWindowRect(trayNotify, out var notifyRect) && notifyRect.Width > 0)
        {
            minLeft = Math.Min(minLeft, notifyRect.Left);
        }

        IntPtr clock = SafeFindWindowEx(tray, IntPtr.Zero, "TrayClockWClass", null);
        if (clock == IntPtr.Zero)
            clock = SafeFindWindowEx(tray, IntPtr.Zero, "ClockCompWidget", null);
        if (clock != IntPtr.Zero && NativeMethods.GetWindowRect(clock, out var clockRect) && clockRect.Width > 0)
        {
            minLeft = Math.Min(minLeft, clockRect.Left);
        }

        return Math.Min(minLeft, FindRightmostChildTrayLeft(tray, trayRect));
    }

    private static int GetTrayLeftEdge()
    {
        try
        {
            if (ThrowInTrayLeftEdgeForTesting)
                throw new InvalidOperationException("Simulated tray left edge error");

            IntPtr tray = FindExplorerTrayWnd();
            if (tray == IntPtr.Zero || !NativeMethods.GetWindowRect(tray, out var trayRect) || trayRect.Width <= 0)
            {
                LogTrayEdgeChange(-1, "no tray marker found, returning -1");
                return -1;
            }

            if (TrayLeftEdgeAutomationOverride is not null)
            {
                int automatedLeft = TrayLeftEdgeAutomationOverride(trayRect.Width);
                if (automatedLeft > 0)
                {
                    LogTrayEdgeChange(automatedLeft, $"UIAutomation tray left={automatedLeft}");
                    return automatedLeft;
                }
            }

            int minLeft = FindNativeTrayLeft(tray, trayRect);
            if (minLeft != int.MaxValue)
            {
                LogTrayEdgeChange(minLeft, $"resolved tray left={minLeft}");
                return minLeft;
            }

            int fallbackLeft = QueryTrayLeftEdgeFromAutomation(tray, trayRect.Width);
            if (fallbackLeft > 0)
            {
                LogTrayEdgeChange(fallbackLeft, $"UIAutomation tray left={fallbackLeft}");
                return fallbackLeft;
            }

            int estimatedTrayWidth = EstimateTrayWidth();
            int fallbackEdge = trayRect.Right - estimatedTrayWidth;
            LogTrayEdgeChange(fallbackEdge, $"fallback estimate from right={trayRect.Right}, width={estimatedTrayWidth}");
            return fallbackEdge;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to determine tray left edge");
            return -1;
        }
    }

    private static int FindChevronLeft(AutomationElement trayElem, int midX)
    {
        var chevronCond = new PropertyCondition(AutomationElement.AutomationIdProperty, "NotificationChevron");
        var chevron = trayElem.FindFirst(TreeScope.Descendants, chevronCond);
        if (chevron is not null)
        {
            var cr = chevron.Current.BoundingRectangle;
            if (!cr.IsEmpty && cr.Width > 0 && cr.Height > 0 && (int)cr.Left >= midX)
            {
                return (int)cr.Left;
            }
        }
        return -1;
    }

    private static List<System.Windows.Rect> CollectCandidateAutomationRects(AutomationElement trayElem, int midX)
    {
        var candidateRects = new List<System.Windows.Rect>();
        string[] targetIds =
        [
            "SystemPromotedNotificationArea",
            "NotificationArea",
            "SystemTrayFrameGrid",
            "SystemTrayIcons",
            "ControlCenterButton",
            "ClockButton"
        ];

        foreach (string id in targetIds)
        {
            var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, id);
            var elem = trayElem.FindFirst(TreeScope.Descendants, cond);
            if (elem is null) continue;

            var rect = elem.Current.BoundingRectangle;
            if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
            {
                candidateRects.Add(rect);
                if ((int)rect.Left >= midX) break;
            }
        }
        return candidateRects;
    }

    private static int QueryTrayLeftEdgeWorker(IntPtr tray, int taskbarWidth)
    {
        var trayElem = AutomationElement.FromHandle(tray);
        if (trayElem is null) return -1;

        int midX = taskbarWidth > 0 ? (taskbarWidth / 2) : 0;
        int chevronLeft = FindChevronLeft(trayElem, midX);
        if (chevronLeft > 0) return chevronLeft;

        var candidateRects = CollectCandidateAutomationRects(trayElem, midX);
        return CalculateTrayLeft(candidateRects, taskbarWidth);
    }

    private static int QueryTrayLeftEdgeFromAutomation(IntPtr tray, int taskbarWidth)
    {
        if (ThrowInTrayLeftEdgeAutomationForTesting)
            throw new InvalidOperationException("Simulated tray left edge automation error");

        long now = Stopwatch.GetTimestamp();
        if (_lastTrayAutomationQueryTicks > 0 && (now - _lastTrayAutomationQueryTicks) < (Stopwatch.Frequency * 30))
        {
            return _cachedTrayLeftAutomation;
        }

        try
        {
            var queryTask = Task.Run(() => QueryTrayLeftEdgeWorker(tray, taskbarWidth));

            if (queryTask.Wait(TimeSpan.FromMilliseconds(150)))
            {
                int calculatedLeft = queryTask.Result;
                if (calculatedLeft > 0)
                {
                    _cachedTrayLeftAutomation = calculatedLeft;
                    _lastTrayAutomationQueryTicks = now;
                    return calculatedLeft;
                }
            }
            else
            {
                Log.Debug("UIAutomation query for tray left edge timed out; avoiding explorer deadlock");
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "UIAutomation query for tray left edge failed");
        }

        _cachedTrayLeftAutomation = -1;
        _lastTrayAutomationQueryTicks = now;
        return -1;
    }

    internal static int CalculateTrayLeft(IEnumerable<System.Windows.Rect> rects, int taskbarWidth)
    {
        int midX = taskbarWidth > 0 ? (taskbarWidth / 2) : 0;
        int minLeft = int.MaxValue;
        foreach (var cr in rects)
        {
            if (!cr.IsEmpty && cr.Width > 0 && cr.Height > 0 && (int)cr.Left >= midX)
            {
                minLeft = Math.Min(minLeft, (int)cr.Left);
            }
        }
        return minLeft == int.MaxValue ? -1 : minLeft;
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
    private static long _lastWidgetNegativeQueryTicks = 0;
    private static int _cachedTrayLeftAutomation = -1;
    private static long _lastTrayAutomationQueryTicks = 0;

    private int _cachedAppListRightEdge = -1;
    private long _lastAppListQueryTicks = 0;
    private int _cachedAppListLeftEdge = -1;
    private long _lastAppListLeftQueryTicks = 0;

    internal static void InvalidateCaches()
    {
        ResetStaticEdgeCaches();
    }

    private static void ResetStaticEdgeCaches()
    {
        _lastLoggedTrayEdge = int.MinValue;
        _lastLoggedWidgetEdge = int.MinValue;
        _cachedLeftWidgetRightEdge = -1;
        _cachedRightWidgetLeftEdge = -1;
        _lastWidgetQueryTicks = 0;
        _lastWidgetNegativeQueryTicks = 0;
        _cachedTrayLeftAutomation = -1;
        _lastTrayAutomationQueryTicks = 0;
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
        if (_cachedAppListRightEdge >= 0 && (now - _lastAppListQueryTicks) < (Stopwatch.Frequency * 30))
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

        int fallback = !IsTaskbarCentered()
            ? _taskbarRect.Left + WindowHelpers.Scale(360, 1f)
            : _taskbarRect.Left + (_taskbarRect.Width / 2) + WindowHelpers.Scale(220, 1f);
        _cachedAppListRightEdge = fallback;
        _lastAppListQueryTicks = now;
        return fallback;
    }

    private static int TryResolveAppListRightEdge(AutomationElement listElem, int taskbarWidth)
    {
        int maxChildRight = FindMaxButtonRight(listElem);
        if (maxChildRight > 0) return maxChildRight;

        var rect = listElem.Current.BoundingRectangle;
        int maxAllowed = taskbarWidth > 0 ? (int)(taskbarWidth * 0.75) : 1400;
        if (!rect.IsEmpty && rect.Width > 0 && rect.Width < maxAllowed)
        {
            return (int)rect.Right;
        }

        return -1;
    }

    private static int SearchAppListRightEdge(AutomationElement trayElem, int taskbarWidth)
    {
        string[] targetIds = ["TaskList", "TaskbarItemsContainer"];
        foreach (string id in targetIds)
        {
            var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, id);
            var listElem = trayElem.FindFirst(TreeScope.Descendants, cond);
            if (listElem is null) continue;

            int edge = TryResolveAppListRightEdge(listElem, taskbarWidth);
            if (edge > 0) return edge;
        }
        return -1;
    }

    private static int QueryAppListRightEdgeFromAutomation(int taskbarWidth)
    {
        IntPtr tray = FindExplorerTrayWnd();
        if (tray == IntPtr.Zero) return -1;

        try
        {
            if (ThrowInQueryAppListRightEdgeFromAutomationForTesting)
                throw new InvalidOperationException("Simulated app list right edge automation error");

            var task = Task.Run(() =>
            {
                var trayElem = AutomationElement.FromHandle(tray);
                return trayElem is not null ? SearchAppListRightEdge(trayElem, taskbarWidth) : -1;
            });

            if (task.Wait(TimeSpan.FromMilliseconds(150)))
            {
                return task.Result;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "UIAutomation query for AppListRightEdge failed");
        }

        return -1;
    }

    private static int FindMaxButtonRight(AutomationElement listElem)
    {
        var children = listElem.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        if (children is null || children.Count == 0)
        {
            children = listElem.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        }
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

    private int GetAppListLeftEdge()
    {
        if (AppListLeftEdgeOverride is not null)
            return AppListLeftEdgeOverride();

        long now = Stopwatch.GetTimestamp();
        if (_cachedAppListLeftEdge >= 0 && (now - _lastAppListLeftQueryTicks) < (Stopwatch.Frequency * 30))
        {
            return _cachedAppListLeftEdge;
        }

        try
        {
            if (ThrowInAppListLeftEdgeForTesting)
                throw new InvalidOperationException("Simulated app list left edge error");

            if (_taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd)
                && NativeMethods.GetWindowRect(_taskBandHwnd, out var bandRect) && bandRect.Width > 0)
            {
                _cachedAppListLeftEdge = bandRect.Left;
                _lastAppListLeftQueryTicks = now;
                return bandRect.Left;
            }

            int automatedLeft = AppListLeftEdgeAutomationOverride is not null
                ? AppListLeftEdgeAutomationOverride(_taskbarRect.Width)
                : QueryAppListLeftEdgeFromAutomation();
            if (automatedLeft > 0)
            {
                _cachedAppListLeftEdge = automatedLeft;
                _lastAppListLeftQueryTicks = now;
                return automatedLeft;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to determine app list left edge via UIAutomation");
        }

        int fallback = !IsTaskbarCentered()
            ? _taskbarRect.Left + WindowHelpers.Scale(48, 1f)
            : _taskbarRect.Left + (_taskbarRect.Width / 2) - WindowHelpers.Scale(220, 1f);
        _cachedAppListLeftEdge = fallback;
        _lastAppListLeftQueryTicks = now;
        return fallback;
    }

    private static int FindStartButtonLeft(AutomationElement trayElem)
    {
        var startCond = new PropertyCondition(AutomationElement.AutomationIdProperty, "StartButton");
        var startBtn = trayElem.FindFirst(TreeScope.Descendants, startCond);
        if (startBtn is not null)
        {
            var sr = startBtn.Current.BoundingRectangle;
            if (!sr.IsEmpty && sr.Width > 0 && (int)sr.Left > 0)
            {
                return (int)sr.Left;
            }
        }
        return -1;
    }

    private static int FindContainerLeftEdge(AutomationElement trayElem)
    {
        string[] targetIds = ["TaskList", "TaskbarItemsContainer"];
        foreach (string id in targetIds)
        {
            var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, id);
            var listElem = trayElem.FindFirst(TreeScope.Descendants, cond);
            if (listElem is null) continue;

            int minChildLeft = FindMinButtonLeft(listElem);
            if (minChildLeft > 0) return minChildLeft;

            var rect = listElem.Current.BoundingRectangle;
            if (!rect.IsEmpty && rect.Width > 0 && (int)rect.Left > 0)
            {
                return (int)rect.Left;
            }
        }
        return -1;
    }

    private static int QueryAppListLeftEdgeWorker(IntPtr tray)
    {
        var trayElem = AutomationElement.FromHandle(tray);
        if (trayElem is null) return -1;

        int startLeft = FindStartButtonLeft(trayElem);
        if (startLeft > 0) return startLeft;

        return FindContainerLeftEdge(trayElem);
    }

    private static int QueryAppListLeftEdgeFromAutomation()
    {
        IntPtr tray = FindExplorerTrayWnd();
        if (tray == IntPtr.Zero) return -1;

        if (ThrowInQueryAppListLeftEdgeFromAutomationForTesting)
            throw new InvalidOperationException("Simulated app list left edge automation error");

        try
        {
            var task = Task.Run(() => QueryAppListLeftEdgeWorker(tray));

            if (task.Wait(TimeSpan.FromMilliseconds(150)))
            {
                return task.Result;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "UIAutomation query for AppListLeftEdge failed");
        }

        return -1;
    }

    private static int FindMinButtonLeft(AutomationElement listElem)
    {
        var children = listElem.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        if (children is null || children.Count == 0)
        {
            children = listElem.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        }
        if (children is null || children.Count == 0) return -1;
        var rects = new List<System.Windows.Rect>(children.Count);
        foreach (AutomationElement child in children)
            rects.Add(child.Current.BoundingRectangle);
        return CalculateMinButtonLeft(rects);
    }

    internal static int CalculateMinButtonLeft(IEnumerable<System.Windows.Rect> rects)
    {
        int minChildLeft = int.MaxValue;
        foreach (var cr in rects)
        {
            if (!cr.IsEmpty && cr.Width > 0 && (int)cr.Left >= 0)
            {
                minChildLeft = Math.Min(minChildLeft, (int)cr.Left);
            }
        }
        return minChildLeft == int.MaxValue ? -1 : minChildLeft;
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
        if (IsWidgetsFeatureEnabledOverride is not null)
            return IsWidgetsFeatureEnabledOverride();

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

    private static (int leftRightEdge, int rightLeftEdge) FindWidgetEdgesWorker(IntPtr tray, int taskbarWidth, long now)
    {
        var trayElem = AutomationElement.FromHandle(tray);
        if (trayElem is null) return (-1, -1);

        var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, "WidgetsButton");
        var widgetBtn = trayElem.FindFirst(TreeScope.Descendants, cond);
        if (widgetBtn is null) return (-1, -1);

        return ParseWidgetBoundingBox(widgetBtn, taskbarWidth, now);
    }

    private static (int leftRightEdge, int rightLeftEdge) QueryWidgetsButtonEdge(IntPtr tray, int taskbarWidth)
    {
        long now = Stopwatch.GetTimestamp();
        if ((_cachedLeftWidgetRightEdge >= 0 || _cachedRightWidgetLeftEdge >= 0) && (now - _lastWidgetQueryTicks) < (Stopwatch.Frequency * 30))
        {
            return (_cachedLeftWidgetRightEdge, _cachedRightWidgetLeftEdge);
        }

        if (_lastWidgetNegativeQueryTicks > 0 && (now - _lastWidgetNegativeQueryTicks) < (Stopwatch.Frequency * 30))
        {
            return (-1, -1);
        }

        try
        {
            if (ThrowInQueryWidgetsButtonEdgeForTesting)
                throw new InvalidOperationException("Simulated query widgets button error");

            var task = Task.Run(() => FindWidgetEdgesWorker(tray, taskbarWidth, now));

            if (task.Wait(TimeSpan.FromMilliseconds(150)))
            {
                var res = task.Result;
                if (res != (-1, -1))
                    return res;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "UIAutomation query for WidgetsButton failed");
        }

        _cachedLeftWidgetRightEdge = -1;
        _cachedRightWidgetLeftEdge = -1;
        _lastWidgetQueryTicks = now;
        _lastWidgetNegativeQueryTicks = now;
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

        _lastWidgetNegativeQueryTicks = 0;
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
            // AND an actual taskband window exists inside it is a legacy taskbar active
            IntPtr rebar = SafeFindWindowEx(tray, IntPtr.Zero, "ReBarWindow32", null);
            if (rebar != IntPtr.Zero)
            {
                IntPtr taskBand = SafeFindWindowEx(rebar, IntPtr.Zero, "MSTaskSwWClass", null);
                if (taskBand == IntPtr.Zero)
                    taskBand = SafeFindWindowEx(rebar, IntPtr.Zero, "MSTaskListWClass", null);

                if (taskBand != IntPtr.Zero && NativeMethods.IsWindow(taskBand))
                {
                    Log.Debug("ReBarWindow32 and taskband detected without DesktopWindowContentBridge (ExplorerPatcher / legacy taskbar replacement active)");
                    return false;
                }
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

            IntPtr prevParent = NativeMethods.SetParent(overlayHwnd, _parentHwnd);
            if (prevParent == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                Log.Debug($"SetParent to taskbar (0x{_parentHwnd:X}) failed with Win32 error {err}; falling back to owned window on taskbar");
                int s = NativeMethods.GetWindowLong(overlayHwnd, NativeMethods.GWL_STYLE);
                s &= ~NativeMethods.WS_CHILD;
                s |= NativeMethods.WS_POPUP;
                NativeMethods.SetWindowLong(overlayHwnd, NativeMethods.GWL_STYLE, s);
                NativeMethods.SetWindowLongPtr(overlayHwnd, NativeMethods.GWLP_HWNDPARENT, _parentHwnd);
            }

            RestoreOverlayLayeredStyle(overlayHwnd);
            ApplyLayeredAttributes(overlayHwnd);

            if (_taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd))
                ShrinkTaskBand(overlaySize);

            State = OverlayDeploymentState.Active;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to attach overlay window to taskbar");
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

    private bool ValidateBandWindows(IntPtr overlayHwnd)
    {
        if (overlayHwnd == IntPtr.Zero || !NativeMethods.IsWindow(overlayHwnd))
        {
            Log.Debug("Cannot position overlay in taskbar band because overlay window handle is invalid or destroyed.");
            State = OverlayDeploymentState.Failed;
            return false;
        }

        if (_parentHwnd == IntPtr.Zero)
        {
            Log.Debug("Cannot position overlay in taskbar band because parent taskbar window is not configured.");
            State = OverlayDeploymentState.Failed;
            return false;
        }

        if (!NativeMethods.IsWindow(_parentHwnd))
        {
            Log.Debug("Cannot position overlay in taskbar band because parent taskbar window is invalid or destroyed.");
            State = OverlayDeploymentState.Recovering;
            return false;
        }

        IntPtr rootHwnd = NativeMethods.GetAncestor(_parentHwnd, NativeMethods.GA_ROOT);
        if (rootHwnd == IntPtr.Zero || !NativeMethods.IsWindow(rootHwnd))
        {
            Log.Debug("Cannot position overlay in taskbar band because parent root window is invalid or destroyed.");
            State = OverlayDeploymentState.Recovering;
            return false;
        }

        return true;
    }

    private bool PositionInBand(IntPtr overlayHwnd, Size sz)
    {
        if (!ValidateBandWindows(overlayHwnd))
        {
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

        int targetX = x;
        int targetY = y;
        IntPtr actualParent = NativeMethods.GetParent(overlayHwnd);
        bool isActualChild = actualParent == _parentHwnd && _parentHwnd != IntPtr.Zero;
        if (!isActualChild && _parentHwnd != IntPtr.Zero)
        {
            var pt = new NativeMethods.POINT { X = x, Y = y };
            NativeMethods.ClientToScreen(_parentHwnd, ref pt);
            targetX = pt.X;
            targetY = pt.Y;
        }

        IntPtr zOrder = isActualChild ? NativeMethods.HWND_TOP : NativeMethods.HWND_TOPMOST;
        bool success = NativeMethods.SetWindowPos(
            overlayHwnd, zOrder,
            targetX, targetY, sz.Width, sz.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        if (!success)
        {
            int err = Marshal.GetLastWin32Error();
            if (err is 87 or 1400)
            {
                Log.Debug($"SetWindowPos returned transient error {err} during band positioning at ({targetX}, {targetY}) (taskbar transition in progress).");
                State = OverlayDeploymentState.Recovering;
            }
            else
            {
                Log.Error($"SetWindowPos failed with Win32 error {err} during band positioning at ({targetX}, {targetY}).");
                State = OverlayDeploymentState.Failed;
            }
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

    private int CalculateRightSlotWidth(NativeMethods.RECT parentRect, int offset)
    {
        int rightBoundary = GetRightSideBoundary();
        int clientRightBoundary = rightBoundary > 0 ? rightBoundary : (parentRect.Width - EstimateTrayWidth());
        int appRight = GetAppListRightEdge();
        var appPt = new NativeMethods.POINT { X = appRight, Y = 0 };
        NativeMethods.ScreenToClient(_parentHwnd, ref appPt);
        int clientAppRight = Math.Max(0, appPt.X);
        return Math.Max(0, clientRightBoundary - clientAppRight - (2 * offset));
    }

    private int CalculateLeftSlotWidth(int offset)
    {
        int leftEdge = _leftWidgetRightEdge > 0 ? _leftWidgetRightEdge : GetLeftWidgetRightEdge();
        int clientLeftEdge = 0;
        if (leftEdge > 0)
        {
            var pt = new NativeMethods.POINT { X = leftEdge, Y = 0 };
            NativeMethods.ScreenToClient(_parentHwnd, ref pt);
            clientLeftEdge = Math.Max(0, pt.X);
        }

        int appLeft = GetAppListLeftEdge();
        var appPt = new NativeMethods.POINT { X = appLeft, Y = 0 };
        NativeMethods.ScreenToClient(_parentHwnd, ref appPt);
        int clientAppLeft = Math.Max(clientLeftEdge, appPt.X);

        return Math.Max(0, clientAppLeft - clientLeftEdge - (2 * offset));
    }

    /// <summary>Computes the available width in pixels in the taskbar band for the current placement and taskbar geometry.</summary>
    public int CalculateAvailableSlotWidth()
    {
        if (_parentHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_parentHwnd) || !NativeMethods.GetClientRect(_parentHwnd, out var parentRect))
        {
            return AvailableSlotWidth > 0 ? AvailableSlotWidth : 0;
        }

        int offset = _cfg.Overlay.PlacementOffset;
        if (IsTaskbarCentered() && _cfg.Overlay.Placement == "left")
        {
            return CalculateLeftSlotWidth(offset);
        }

        return CalculateRightSlotWidth(parentRect, offset);
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

        int offset = _cfg.Overlay.PlacementOffset;
        int availableWidth = Math.Max(0, clientRightBoundary - clientAppRight - (2 * offset));
        AvailableSlotWidth = availableWidth;
        IsTaskbarSpaceConstrained = availableWidth > 0 && sz.Width > availableWidth;

        int maxRight = Math.Max(clientAppRight, clientRightBoundary - sz.Width);
        int x = _cfg.Overlay.Placement is "beside_apps" or "left"
            ? clientAppRight + offset
            : clientRightBoundary - sz.Width - offset;

        return Math.Clamp(x, clientAppRight, maxRight);
    }

    private int ComputeBandCenteredX(Size sz, int clientRightBoundary)
    {
        int offset = _cfg.Overlay.PlacementOffset;
        if (_cfg.Overlay.Placement == "left")
        {
            int leftEdge = _leftWidgetRightEdge > 0 ? _leftWidgetRightEdge : GetLeftWidgetRightEdge();
            int clientLeftEdge = 0;
            if (leftEdge > 0)
            {
                var pt = new NativeMethods.POINT { X = leftEdge, Y = 0 };
                NativeMethods.ScreenToClient(_parentHwnd, ref pt);
                clientLeftEdge = Math.Max(0, pt.X);
            }

            int appLeft = GetAppListLeftEdge();
            var appPt = new NativeMethods.POINT { X = appLeft, Y = 0 };
            NativeMethods.ScreenToClient(_parentHwnd, ref appPt);
            int clientAppLeft = Math.Max(clientLeftEdge, appPt.X);

            int availableWidth = Math.Max(0, clientAppLeft - clientLeftEdge - (2 * offset));
            AvailableSlotWidth = availableWidth;
            IsTaskbarSpaceConstrained = availableWidth > 0 && sz.Width > availableWidth;

            int preferredX = clientLeftEdge + offset;
            if (preferredX < clientAppLeft)
            {
                int maxX = Math.Max(clientLeftEdge, clientAppLeft - sz.Width - offset);
                return Math.Clamp(preferredX, clientLeftEdge, maxX);
            }

            return preferredX;
        }

        int appRight = GetAppListRightEdge();
        var rightAppPt = new NativeMethods.POINT { X = appRight, Y = 0 };
        NativeMethods.ScreenToClient(_parentHwnd, ref rightAppPt);
        int clientAppRight = Math.Max(0, rightAppPt.X);

        int availableRightWidth = Math.Max(0, clientRightBoundary - clientAppRight - (2 * offset));
        AvailableSlotWidth = availableRightWidth;
        IsTaskbarSpaceConstrained = availableRightWidth > 0 && sz.Width > availableRightWidth;

        int preferredRightX = clientRightBoundary - sz.Width - offset;
        int minRightX = clientAppRight + offset;
        int maxRightX = Math.Max(minRightX, clientRightBoundary - sz.Width);
        return Math.Clamp(preferredRightX, minRightX, maxRightX);
    }

    private static int EstimateTrayWidth()
    {
        int iconW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
        if (iconW <= 0) iconW = 16;
        int dpiScaledBase = WindowHelpers.Scale(200, 1f);
        return Math.Max(iconW * 6 + 90, dpiScaledBase);
    }
}
