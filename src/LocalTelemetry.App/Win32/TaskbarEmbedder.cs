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
public sealed class TaskbarEmbedder(AppSettings cfg, DisplayInfo? targetDisplay = null)
{
    private readonly AppSettings _cfg = cfg;

    /// <summary>Gets or sets the target display for this embedder instance.</summary>
    public DisplayInfo? TargetDisplay { get; set; } = targetDisplay;

    /// <summary>Returns the target taskbar HWND for this embedder.</summary>
    public IntPtr GetTargetTrayWnd()
    {
        if (TargetDisplay is not null && TargetDisplay.TaskbarHwnd != IntPtr.Zero)
            return TargetDisplay.TaskbarHwnd;

        return FindExplorerTrayWnd();
    }

    /// <summary>Gets whether this embedder targets a secondary display taskbar.</summary>
    public bool IsSecondaryTaskbar => TargetDisplay is not null
        ? !TargetDisplay.IsPrimary
        : IsSecondaryTrayWindow(GetTargetTrayWnd());

    private IntPtr _parentHwnd = IntPtr.Zero;
    private IntPtr _taskBandHwnd = IntPtr.Zero;
    private IntPtr _currentOverlayHwnd = IntPtr.Zero;
    private int _lastPositionedX = int.MinValue;
    private int _lastPositionedWidth = int.MinValue;
    private NativeMethods.Rect _taskBandOri;
    private bool _taskBandSaved;
    private bool _isWin11;

    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, byte> ActiveOverlayHwnds = new();

    private static bool IsAppOverlayWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        if (ActiveOverlayHwnds.ContainsKey(hwnd)) return true;

        var titleBuf = new char[64];
        int len = NativeMethods.GetWindowText(hwnd, titleBuf, titleBuf.Length);
        if (len <= 0) return false;
        return new string(titleBuf, 0, len).Equals("LocalTelemetry_Overlay", StringComparison.Ordinal);
    }

    private Rectangle _taskbarRect;
    private uint _taskbarEdge;
    private int _lastShrinkWidth = -1;
    private int _lastShrinkOffset = -1;

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

    internal int NaturalOverlayWidth { get; set; }

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

        ActiveOverlayHwnds.TryAdd(overlayHwnd, 0);

        RefreshTaskbarInfo();
        _trayLeftEdge = ResolveTrayLeftEdge(GetTargetTrayWnd(), IsSecondaryTaskbar);
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
        _trayLeftEdge = ResolveTrayLeftEdge(GetTargetTrayWnd(), IsSecondaryTaskbar);
        UpdateWidgetsEdges();

        if (_taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd)
            && (overlaySize.Width != _lastShrinkWidth || _cfg.Overlay.PlacementOffset != _lastShrinkOffset))
        {
            ShrinkTaskBand(overlaySize);
            _lastShrinkWidth = overlaySize.Width;
            _lastShrinkOffset = _cfg.Overlay.PlacementOffset;
        }

        PositionInBand(overlayHwnd, overlaySize);
    }

    /// <summary>Detaches the overlay from the taskbar and restores the original taskband size.</summary>
    /// <param name="overlayHwnd">Handle to the overlay window.</param>
    public void Detach(IntPtr overlayHwnd)
    {
        ActiveOverlayHwnds.TryRemove(overlayHwnd, out _);
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
        _lastShrinkOffset = -1;
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
    internal static bool ThrowInScanTaskbarForTesting { get; set; }

    internal static IntPtr SafeFindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow)
    {
        if (FindWindowExOverride is not null)
            return FindWindowExOverride(hwndParent, hwndChildAfter, lpszClass, lpszWindow);
        return NativeMethods.FindWindowEx(hwndParent, hwndChildAfter, lpszClass, lpszWindow);
    }

    [ThreadStatic]
    internal static IntPtr? CurrentTargetTrayWnd;

    /// <summary>Locates Shell_TrayWnd owned by explorer.exe (avoids grabbing a
    /// third-party toolbar window that reused the same class name).</summary>
    public static IntPtr FindExplorerTrayWnd()
    {
        if (ExplorerTrayWndOverride is not null)
            return ExplorerTrayWndOverride();

        if (CurrentTargetTrayWnd.HasValue && CurrentTargetTrayWnd.Value != IntPtr.Zero)
            return CurrentTargetTrayWnd.Value;

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

    /// <summary>Locates all Shell_SecondaryTrayWnd windows owned by explorer.exe.</summary>
    public static List<IntPtr> FindExplorerSecondaryTrayWnds()
    {
        if (ExplorerSecondaryTrayWndsOverride is not null)
            return ExplorerSecondaryTrayWndsOverride();

        var list = new List<IntPtr>();
        IntPtr hwnd = IntPtr.Zero;
        while ((hwnd = SafeFindWindowEx(IntPtr.Zero, hwnd, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) continue;
            try
            {
                if (ThrowInExplorerLookupForTesting)
                    throw new InvalidOperationException("Simulated explorer lookup error");

                using var proc = Process.GetProcessById((int)pid);
                if (proc.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                    list.Add(hwnd);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Explorer process lookup race during secondary tray scan");
            }
        }
        return list;
    }

    internal static Func<List<IntPtr>>? ExplorerSecondaryTrayWndsOverride { get; set; }

    private void RefreshTaskbarInfo()
    {
        if (TargetDisplay is not null && !TargetDisplay.IsPrimary && TargetDisplay.TaskbarHwnd != IntPtr.Zero)
        {
            if (NativeMethods.GetWindowRect(TargetDisplay.TaskbarHwnd, out var r) && r.Width > 0 && r.Height > 0)
            {
                _taskbarRect = new Rectangle(r.Left, r.Top, r.Width, r.Height);
                _taskbarEdge = NativeMethods.ABE_BOTTOM;
                return;
            }

            var fallbackSecondary = CalculateFromWorkingArea(TargetDisplay.Bounds, TargetDisplay.WorkingArea);
            _taskbarRect = fallbackSecondary.Rect;
            _taskbarEdge = fallbackSecondary.Edge;
            return;
        }

        var data = new NativeMethods.AppBarData
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.AppBarData>(),
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

    private static int FindNativeTrayLeft(IntPtr tray, NativeMethods.Rect trayRect)
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

        IntPtr rebar = SafeFindWindowEx(tray, IntPtr.Zero, "ReBarWindow32", null);
        int clusterLeft = minLeft != int.MaxValue ? minLeft : trayRect.Right;
        minLeft = Math.Min(minLeft, FindAdjacentClusterLeft(tray, trayRect, clusterLeft, rebar, trayNotify, clock));

        return Math.Min(minLeft, FindRightmostChildTrayLeft(tray, trayRect));
    }

    private static bool IsAdjacentTrayChild(IntPtr child, NativeMethods.Rect trayRect, int clusterLeft, IntPtr rebar, IntPtr trayNotify, IntPtr clock, out int childLeft)
    {
        childLeft = 0;
        if (child == rebar || child == trayNotify || child == clock || !NativeMethods.IsWindowVisible(child))
            return false;

        if (IsAppOverlayWindow(child))
            return false;

        if (!NativeMethods.GetWindowRect(child, out var cr) || cr.Width <= 0 || cr.Height <= 0)
            return false;

        bool isHorizontallyAdjacent = cr.Right >= clusterLeft - 20 && cr.Left < clusterLeft;
        bool isReasonableBounds = cr.Left >= trayRect.Left + (trayRect.Width / 3) && cr.Width < trayRect.Width * 0.45;

        if (isHorizontallyAdjacent && isReasonableBounds)
        {
            childLeft = cr.Left;
            return true;
        }

        return false;
    }

    private static int FindAdjacentClusterLeft(IntPtr tray, NativeMethods.Rect trayRect, int initialClusterLeft, IntPtr rebar, IntPtr trayNotify, IntPtr clock)
    {
        int clusterLeft = initialClusterLeft;
        bool foundAdjacent;
        do
        {
            foundAdjacent = false;
            IntPtr child = IntPtr.Zero;
            while ((child = SafeFindWindowEx(tray, child, null, null)) != IntPtr.Zero)
            {
                if (IsAdjacentTrayChild(child, trayRect, clusterLeft, rebar, trayNotify, clock, out int childLeft))
                {
                    clusterLeft = childLeft;
                    foundAdjacent = true;
                }
            }
        } while (foundAdjacent);

        return clusterLeft;
    }

    private static int FindSecondaryClockLeft(IntPtr tray)
    {
        IntPtr clock = SafeFindWindowEx(tray, IntPtr.Zero, "TrayClockWClass", null);
        if (clock == IntPtr.Zero)
            clock = SafeFindWindowEx(tray, IntPtr.Zero, "ClockCompWidget", null);
        if (clock != IntPtr.Zero && !IsAppOverlayWindow(clock) && NativeMethods.GetWindowRect(clock, out var clockRect) && clockRect.Width > 0)
        {
            return clockRect.Left;
        }
        return -1;
    }

    internal static Func<IntPtr, bool>? IsSecondaryTrayWindowOverride { get; set; }

    private static bool IsSecondaryTrayWindow(IntPtr tray)
    {
        if (IsSecondaryTrayWindowOverride is not null)
            return IsSecondaryTrayWindowOverride(tray);
        if (tray == IntPtr.Zero) return false;
        var className = new char[256];
        int len = NativeMethods.GetClassName(tray, className, className.Length);
        if (len <= 0) return false;
        string cls = new string(className, 0, len);
        return cls.Equals("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase);
    }

    private static int FindSecondaryClockById(AutomationElement trayElem, int midX)
    {
        string[] clockIds = ["ClockButton", "TimeButton", "TimeWidget", "Clock", "NotificationCenterButton"];
        foreach (string id in clockIds)
        {
            var clockCond = new PropertyCondition(AutomationElement.AutomationIdProperty, id);
            var elem = trayElem.FindFirst(TreeScope.Descendants, clockCond);
            if (elem is not null)
            {
                var rect = elem.Current.BoundingRectangle;
                if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0 && (int)rect.Left >= midX)
                {
                    return (int)rect.Left;
                }
            }
        }
        return -1;
    }

    private static int FindSecondaryClockByPosition(AutomationElement trayElem, int midX)
    {
        var buttons = trayElem.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        int rightmostLeft = -1;
        double maxRight = double.MinValue;
        foreach (AutomationElement btn in buttons)
        {
            var r = btn.Current.BoundingRectangle;
            if (IsClockButtonCandidate(r, midX, maxRight))
            {
                maxRight = r.Right;
                rightmostLeft = (int)r.Left;
            }
        }
        return rightmostLeft;
    }

    private static bool IsClockButtonCandidate(System.Windows.Rect r, int midX, double maxRight) =>
        !r.IsEmpty && r.Width is > 20 and < 250 && r.Right > maxRight && (int)r.Left >= midX;

    private static int QuerySecondaryClockFromAutomation(IntPtr tray, NativeMethods.Rect trayRect)
    {
        long now = Stopwatch.GetTimestamp();
        if (_trayAutomationCache.TryGetValue(tray, out var cached) && (now - cached.Ticks) < (Stopwatch.Frequency * 30))
        {
            return cached.Edge;
        }

        try
        {
            var task = Task.Run(() =>
            {
                try
                {
                    var trayElem = AutomationElement.FromHandle(tray);
                    if (trayElem is null) return -1;

                    int midX = trayRect.Left + (trayRect.Width / 2);
                    int idClock = FindSecondaryClockById(trayElem, midX);
                    if (idClock != -1) return idClock;

                    return FindSecondaryClockByPosition(trayElem, midX);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "UIAutomation query for secondary clock worker failed");
                    return -1;
                }
            });
            task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

            if (task.Wait(TimeSpan.FromMilliseconds(250)))
            {
                int calculatedLeft = task.Result;
                if (calculatedLeft != -1)
                {
                    _trayAutomationCache[tray] = (calculatedLeft, now);
                    return calculatedLeft;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "UIAutomation query for secondary clock failed");
        }

        if (_trayAutomationCache.TryGetValue(tray, out var lastKnown) && lastKnown.Edge != -1)
        {
            return lastKnown.Edge;
        }

        return -1;
    }

    private static int ResolveSecondaryTrayLeftEdge(IntPtr tray, NativeMethods.Rect trayRect)
    {
        int secClock = FindSecondaryClockLeft(tray);
        if (secClock != -1)
        {
            LogTrayEdgeChange(secClock, $"resolved secondary tray clock left={secClock}");
            return secClock;
        }

        var scan = ScanTaskbar(tray, trayRect.Width, trayRect.Left);
        if (scan.TrayLeft != -1)
        {
            LogTrayEdgeChange(scan.TrayLeft, $"batched UIAutomation secondary tray left={scan.TrayLeft}");
            return scan.TrayLeft;
        }

        int secAutoClock = QuerySecondaryClockFromAutomation(tray, trayRect);
        if (secAutoClock != -1)
        {
            LogTrayEdgeChange(secAutoClock, $"UIAutomation secondary clock left={secAutoClock}");
            return secAutoClock;
        }

        // Dynamic taskbar edge detection: when a secondary taskbar does not contain a clock or tray,
        // its right boundary is the true right edge of the taskbar window.
        LogTrayEdgeChange(trayRect.Right, $"secondary taskbar has no right-docked elements; boundary is right={trayRect.Right}");
        return trayRect.Right;
    }

    internal static int ResolveTrayLeftEdge(IntPtr tray, bool isSecondary)
    {
        try
        {
            if (ThrowInTrayLeftEdgeForTesting)
                throw new InvalidOperationException("Simulated tray left edge error");

            if (tray == IntPtr.Zero || !NativeMethods.GetWindowRect(tray, out var trayRect) || trayRect.Width <= 0)
            {
                LogTrayEdgeChange(-1, "no tray marker found, returning -1");
                return -1;
            }

            return isSecondary
                ? ResolveSecondaryTrayLeftEdge(tray, trayRect)
                : ResolvePrimaryTrayLeftEdge(tray, trayRect, isSecondary);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to determine tray left edge");
            return -1;
        }
    }

    private static int ResolvePrimaryTrayLeftEdge(IntPtr tray, NativeMethods.Rect trayRect, bool isSecondary)
    {
        int nativeSecClock = FindSecondaryClockLeft(tray);
        if (nativeSecClock != -1 && FindNativeTrayLeft(tray, trayRect) == int.MaxValue)
        {
            LogTrayEdgeChange(nativeSecClock, $"resolved secondary tray clock left={nativeSecClock}");
            return nativeSecClock;
        }

        if (TrayLeftEdgeAutomationOverride is not null)
        {
            int automatedLeft = TrayLeftEdgeAutomationOverride(trayRect.Width);
            if (automatedLeft != -1)
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

        var scan = ScanTaskbar(tray, trayRect.Width, trayRect.Left);
        if (scan.TrayLeft != -1)
        {
            LogTrayEdgeChange(scan.TrayLeft, $"batched UIAutomation tray left={scan.TrayLeft}");
            return scan.TrayLeft;
        }

        int fallbackLeft = QueryTrayLeftEdgeFromAutomation(tray, trayRect.Width);
        if (fallbackLeft != -1)
        {
            LogTrayEdgeChange(fallbackLeft, $"UIAutomation tray left={fallbackLeft}");
            return fallbackLeft;
        }

        if (_trayAutomationCache.TryGetValue(tray, out var cached) && cached.Edge != -1)
        {
            return cached.Edge;
        }

        int estimatedTrayWidth = ResolveTrayWidth(tray, isSecondary);
        int fallbackEdge = trayRect.Right - estimatedTrayWidth;
        LogTrayEdgeChange(fallbackEdge, $"fallback estimate from right={trayRect.Right}, width={estimatedTrayWidth}");
        return fallbackEdge;
    }

    internal static int GetTrayLeftEdge()
    {
        IntPtr tray = CurrentTargetTrayWnd.HasValue && CurrentTargetTrayWnd.Value != IntPtr.Zero
            ? CurrentTargetTrayWnd.Value
            : FindExplorerTrayWnd();
        return ResolveTrayLeftEdge(tray, IsSecondaryTrayWindow(tray));
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
            "NotificationChevron",
            "SystemPromotedNotificationArea",
            "NotificationArea",
            "SystemTrayFrameGrid",
            "SystemTrayIcons",
            "ControlCenterButton",
            "ClockButton",
            "NotificationCenterButton",
            "TimeButton",
            "TimeWidget",
            "Clock"
        ];

        foreach (string id in targetIds)
        {
            var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, id);
            var elem = trayElem.FindFirst(TreeScope.Descendants, cond);
            if (elem is null) continue;

            var rect = elem.Current.BoundingRectangle;
            if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0 && (int)rect.Left >= midX)
            {
                candidateRects.Add(rect);
            }
        }
        return candidateRects;
    }

    private static int QueryTrayLeftEdgeWorker(IntPtr tray, int taskbarWidth)
    {
        try
        {
            var trayElem = AutomationElement.FromHandle(tray);
            if (trayElem is null) return -1;

            int taskbarLeft = 0;
            if (tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out var r))
            {
                taskbarLeft = r.Left;
            }

            int midX = taskbarLeft + (taskbarWidth > 0 ? (taskbarWidth / 2) : 0);
            int chevronLeft = FindChevronLeft(trayElem, midX);
            if (chevronLeft != -1) return chevronLeft;

            var candidateRects = CollectCandidateAutomationRects(trayElem, midX);
            return CalculateTrayLeft(candidateRects, taskbarWidth, taskbarLeft);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "UIAutomation query for tray left edge worker failed");
            return -1;
        }
    }

    private static int QueryTrayLeftEdgeFromAutomation(IntPtr tray, int taskbarWidth)
    {
        if (ThrowInTrayLeftEdgeAutomationForTesting)
            throw new InvalidOperationException("Simulated tray left edge automation error");

        long now = Stopwatch.GetTimestamp();
        if (_trayAutomationCache.TryGetValue(tray, out var cached) && (now - cached.Ticks) < (Stopwatch.Frequency * 30))
        {
            return cached.Edge;
        }

        try
        {
            var queryTask = Task.Run(() => QueryTrayLeftEdgeWorker(tray, taskbarWidth));
            queryTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

            if (queryTask.Wait(TimeSpan.FromMilliseconds(150)))
            {
                int calculatedLeft = queryTask.Result;
                if (calculatedLeft != -1)
                {
                    _trayAutomationCache[tray] = (calculatedLeft, now);
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

        if (_trayAutomationCache.TryGetValue(tray, out var lastKnown) && lastKnown.Edge != -1)
        {
            return lastKnown.Edge;
        }

        return -1;
    }

    internal static int CalculateTrayLeft(IEnumerable<System.Windows.Rect> rects, int taskbarWidth, int taskbarLeft = int.MinValue)
    {
        var list = rects as IList<System.Windows.Rect> ?? rects.ToList();
        if (list.Count == 0) return -1;

        int midX;
        if (taskbarLeft != int.MinValue)
        {
            midX = taskbarLeft + (taskbarWidth > 0 ? taskbarWidth / 2 : 0);
        }
        else
        {
            double minCoord = list.Min(r => r.Left);
            int halfWidth = taskbarWidth > 0 ? taskbarWidth / 2 : 0;
            midX = minCoord < 0 && taskbarWidth > 0
                ? (int)minCoord + halfWidth
                : halfWidth;
        }

        int minLeft = int.MaxValue;
        foreach (var cr in list)
        {
            if (!cr.IsEmpty && cr.Width > 0 && cr.Height > 0 && (int)cr.Left >= midX)
            {
                minLeft = Math.Min(minLeft, (int)cr.Left);
            }
        }
        return minLeft == int.MaxValue ? -1 : minLeft;
    }

    private static int FindRightmostChildTrayLeft(IntPtr tray, NativeMethods.Rect trayRect)
    {
        int minLeft = int.MaxValue;
        int midX = trayRect.Left + trayRect.Width / 2;
        IntPtr child = IntPtr.Zero;
        while ((child = SafeFindWindowEx(tray, child, null, null)) != IntPtr.Zero)
        {
            if (!NativeMethods.IsWindowVisible(child)) continue;
            if (IsAppOverlayWindow(child)) continue;
            if (!NativeMethods.GetWindowRect(child, out var cr) || cr.Width <= 0) continue;

            // If docked on the right side and doesn't span the majority of the taskbar
            if (cr.Right >= trayRect.Right - 50 && cr.Left >= midX && cr.Width < trayRect.Width * 0.45)
            {
                minLeft = Math.Min(minLeft, cr.Left);
            }
        }
        return minLeft;
    }

    internal static TaskbarScanResult ScanTaskbar(IntPtr tray, int taskbarWidth, int taskbarLeft)
    {
        long now = Stopwatch.GetTimestamp();
        if (_taskbarScanCache.TryGetValue(tray, out var cached) && (now - cached.Ticks) < (Stopwatch.Frequency * 30))
        {
            return cached;
        }

        if (ThrowInScanTaskbarForTesting)
            throw new InvalidOperationException("Simulated taskbar scan error");

        if (tray == IntPtr.Zero || !NativeMethods.GetWindowRect(tray, out var tr) || tr.Width <= 0)
        {
            return new TaskbarScanResult(-1, -1, -1, now);
        }

        try
        {
            var task = Task.Run(() => PerformTaskbarScanWorker(tray, tr, taskbarWidth, taskbarLeft));
            task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            if (task.Wait(TimeSpan.FromMilliseconds(500)))
            {
                var result = task.Result;
                _taskbarScanCache[tray] = result;
                return result;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Batched UIAutomation scan of taskbar failed");
        }

        if (_taskbarScanCache.TryGetValue(tray, out var lastKnown))
        {
            return lastKnown;
        }

        return new TaskbarScanResult(-1, -1, -1, now);
    }

    private static TaskbarScanResult PerformTaskbarScanWorker(IntPtr tray, NativeMethods.Rect tr, int taskbarWidth, int taskbarLeft)
    {
        long now = Stopwatch.GetTimestamp();
        try
        {
            var cacheReq = new CacheRequest
            {
                AutomationElementMode = AutomationElementMode.None,
                TreeScope = TreeScope.Descendants
            };
            cacheReq.Add(AutomationElement.ClassNameProperty);
            cacheReq.Add(AutomationElement.AutomationIdProperty);
            cacheReq.Add(AutomationElement.BoundingRectangleProperty);
            cacheReq.Add(AutomationElement.IsOffscreenProperty);
            cacheReq.Add(AutomationElement.ControlTypeProperty);

            NativeMethods.GetWindowThreadProcessId(tray, out uint explorerPid);
            var filter = explorerPid != 0
                ? (Condition)new PropertyCondition(AutomationElement.ProcessIdProperty, (int)explorerPid)
                : Condition.TrueCondition;

            AutomationElement? root;
            AutomationElementCollection? allElements = null;
            using (cacheReq.Activate())
            {
                root = AutomationElement.FromHandle(tray);
                if (root is not null)
                {
                    allElements = root.FindAll(TreeScope.Descendants, filter);
                }
            }

            int tbw = tr.Width;
            int midX = taskbarLeft + (taskbarWidth > 0 ? (taskbarWidth / 2) : 0);
            int appsR = int.MinValue;
            int appsL = int.MaxValue;
            int trayL = int.MaxValue;
            bool isSecondary = IsSecondaryTrayWindow(tray);

            int nativeTray = FindNativeTrayLeft(tray, tr);
            if (nativeTray != int.MaxValue)
            {
                trayL = nativeTray;
            }

            if (allElements is not null)
            {
                foreach (AutomationElement elem in allElements)
                {
                    ProcessCachedTaskbarElement(elem, tbw, midX, ref appsR, ref appsL, ref trayL);
                }
            }

            int finalAppsR = appsR != int.MinValue ? appsR : -1;
            int finalAppsL = appsL != int.MaxValue ? appsL : -1;
            int finalTrayL;
            if (trayL != int.MaxValue)
            {
                finalTrayL = trayL;
            }
            else
            {
                finalTrayL = isSecondary ? tr.Right : -1;
            }

            return new TaskbarScanResult(finalAppsR, finalAppsL, finalTrayL, now);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "UIAutomation perform taskbar scan worker failed");
            return new TaskbarScanResult(-1, -1, -1, now);
        }
    }

    private static void ProcessCachedTaskbarElement(AutomationElement elem, int tbw, int midX,
        ref int appsR, ref int appsL, ref int trayL)
    {
        try
        {
            if (elem.Cached.IsOffscreen) return;
            var r = elem.Cached.BoundingRectangle;
            if (r.IsEmpty || r.Width <= 0 || r.Height <= 0 || r.Width >= tbw * 0.8) return;

            string cls = elem.Cached.ClassName ?? string.Empty;
            string id = elem.Cached.AutomationId ?? string.Empty;
            var ctrlType = elem.Cached.ControlType;

            bool isRightDocked = (int)r.Left >= midX && (IsRightDockedId(id) || cls.Contains("Tray", StringComparison.OrdinalIgnoreCase) || cls.Contains("Clock", StringComparison.OrdinalIgnoreCase));
            if (isRightDocked)
            {
                trayL = Math.Min(trayL, (int)r.Left);
            }
            else
            {
                bool isApp = cls.StartsWith("Taskbar", StringComparison.OrdinalIgnoreCase)
                             || cls.StartsWith("Start", StringComparison.OrdinalIgnoreCase)
                             || cls.StartsWith("Search", StringComparison.OrdinalIgnoreCase)
                             || id.StartsWith("Task", StringComparison.OrdinalIgnoreCase)
                             || id.StartsWith("App", StringComparison.OrdinalIgnoreCase)
                             || (ctrlType == ControlType.Button && r.Width < (tbw * 0.4));

                if (isApp && (trayL == int.MaxValue || (int)r.Right <= trayL))
                {
                    appsR = Math.Max(appsR, (int)r.Right);
                    appsL = Math.Min(appsL, (int)r.Left);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to process cached taskbar automation element");
        }
    }

    internal static bool IsRightDockedId(string id)
    {
        return id is "NotificationChevron"
            or "SystemPromotedNotificationArea"
            or "NotificationArea"
            or "SystemTrayFrameGrid"
            or "SystemTrayIcons"
            or "ControlCenterButton"
            or "ClockButton"
            or "NotificationCenterButton"
            or "TimeButton"
            or "TimeWidget"
            or "Clock";
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
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, (int Edge, long Ticks)> _trayAutomationCache = new();
    internal sealed record TaskbarScanResult(int AppsRight, int AppsLeft, int TrayLeft, long Ticks);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, TaskbarScanResult> _taskbarScanCache = new();

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
        _trayAutomationCache.Clear();
        _taskbarScanCache.Clear();
        ActiveOverlayHwnds.Clear();
    }

    private static void LogWidgetEdgeChange(int edge, string message)
    {
        if (_lastLoggedWidgetEdge != int.MinValue && Math.Abs(edge - _lastLoggedWidgetEdge) < 40) return;
        _lastLoggedWidgetEdge = edge;
        Log.Debug($"Detected widget edge via {message}");
    }

    private int QueryLegacyTaskBandRightEdge()
    {
        if (_taskBandHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_taskBandHwnd))
            return -1;

        int buttonRight = QueryLegacyTaskBandButtonsRightEdge();
        if (buttonRight > 0)
        {
            return buttonRight;
        }

        if (NativeMethods.GetWindowRect(_taskBandHwnd, out var bandRect) && bandRect.Width > 0)
        {
            return bandRect.Right;
        }

        return -1;
    }

    private int QueryLegacyTaskBandButtonsRightEdge()
    {
        try
        {
            var bandElem = AutomationElement.FromHandle(_taskBandHwnd);
            if (bandElem is not null)
            {
                int maxBtn = FindMaxButtonRight(bandElem);
                if (maxBtn != -1 && maxBtn != int.MinValue)
                {
                    return maxBtn;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to query taskband button edge via UIAutomation");
        }

        return -1;
    }

    private int ResolveDynamicAppListRightEdge()
    {
        if (AppListRightEdgeAutomationOverride is not null)
        {
            return AppListRightEdgeAutomationOverride(_taskbarRect.Width);
        }

        var scan = ScanTaskbar(GetTargetTrayWnd(), _taskbarRect.Width, _taskbarRect.Left);
        if (scan.AppsRight != -1)
        {
            return scan.AppsRight;
        }

        return QueryAppListRightEdgeFromAutomationForTray(GetTargetTrayWnd(), _taskbarRect.Width);
    }

    private int GetAppListRightEdge()
    {
        if (AppListRightEdgeOverride is not null)
            return AppListRightEdgeOverride();

        long now = Stopwatch.GetTimestamp();
        if (_cachedAppListRightEdge != -1 && (now - _lastAppListQueryTicks) < (Stopwatch.Frequency * 30))
        {
            return _cachedAppListRightEdge;
        }

        try
        {
            if (ThrowInAppListRightEdgeForTesting)
                throw new InvalidOperationException("Simulated app list right edge error");

            int legacyRight = QueryLegacyTaskBandRightEdge();
            if (legacyRight > 0)
            {
                _cachedAppListRightEdge = legacyRight;
                _lastAppListQueryTicks = now;
                return legacyRight;
            }

            int automatedRight = ResolveDynamicAppListRightEdge();
            if (automatedRight != -1 && automatedRight != int.MinValue)
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

        if (_cachedAppListRightEdge > 0)
        {
            return _cachedAppListRightEdge;
        }

        int fallback = !IsTaskbarCentered()
            ? _taskbarRect.Left + (_taskbarRect.Width / 3)
            : _taskbarRect.Left + (_taskbarRect.Width / 2);
        _cachedAppListRightEdge = fallback;
        _lastAppListQueryTicks = now - (Stopwatch.Frequency * 28);
        return fallback;
    }

    private static int TryResolveAppListRightEdge(AutomationElement listElem, int taskbarWidth)
    {
        int maxChildRight = FindMaxButtonRight(listElem);
        if (maxChildRight != -1 && maxChildRight != int.MinValue) return maxChildRight;

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
            if (edge != -1 && edge != int.MinValue) return edge;
        }
        return -1;
    }

    internal static int QueryAppListRightEdgeFromAutomation(int taskbarWidth)
    {
        IntPtr tray = CurrentTargetTrayWnd.HasValue && CurrentTargetTrayWnd.Value != IntPtr.Zero
            ? CurrentTargetTrayWnd.Value
            : FindExplorerTrayWnd();
        return QueryAppListRightEdgeFromAutomationForTray(tray, taskbarWidth);
    }

    private static int QueryAppListRightEdgeFromAutomationForTray(IntPtr tray, int taskbarWidth)
    {
        if (tray == IntPtr.Zero) return -1;

        try
        {
            if (ThrowInQueryAppListRightEdgeFromAutomationForTesting)
                throw new InvalidOperationException("Simulated app list right edge automation error");

            var task = Task.Run(() =>
            {
                try
                {
                    var trayElem = AutomationElement.FromHandle(tray);
                    return trayElem is not null ? SearchAppListRightEdge(trayElem, taskbarWidth) : -1;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "UIAutomation query for AppListRightEdge worker failed");
                    return -1;
                }
            });
            task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

            if (task.Wait(TimeSpan.FromMilliseconds(1500)))
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
        int maxChildRight = int.MinValue;
        foreach (var cr in rects)
        {
            if (!cr.IsEmpty && cr.Width > 0)
            {
                maxChildRight = Math.Max(maxChildRight, (int)cr.Right);
            }
        }
        return maxChildRight == int.MinValue ? -1 : maxChildRight;
    }

    private int QueryLegacyTaskBandLeftEdge()
    {
        if (_taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd)
            && NativeMethods.GetWindowRect(_taskBandHwnd, out var bandRect) && bandRect.Width > 0)
        {
            return bandRect.Left;
        }
        return -1;
    }

    private int ResolveDynamicAppListLeftEdge()
    {
        if (AppListLeftEdgeAutomationOverride is not null)
        {
            return AppListLeftEdgeAutomationOverride(_taskbarRect.Width);
        }

        var scan = ScanTaskbar(GetTargetTrayWnd(), _taskbarRect.Width, _taskbarRect.Left);
        if (scan.AppsLeft != -1)
        {
            return scan.AppsLeft;
        }

        return QueryAppListLeftEdgeFromAutomationForTray(GetTargetTrayWnd());
    }

    private int GetAppListLeftEdge()
    {
        if (AppListLeftEdgeOverride is not null)
            return AppListLeftEdgeOverride();

        long now = Stopwatch.GetTimestamp();
        if (_cachedAppListLeftEdge != -1 && (now - _lastAppListLeftQueryTicks) < (Stopwatch.Frequency * 30))
        {
            return _cachedAppListLeftEdge;
        }

        try
        {
            if (ThrowInAppListLeftEdgeForTesting)
                throw new InvalidOperationException("Simulated app list left edge error");

            int legacyLeft = QueryLegacyTaskBandLeftEdge();
            if (legacyLeft >= 0)
            {
                _cachedAppListLeftEdge = legacyLeft;
                _lastAppListLeftQueryTicks = now;
                return legacyLeft;
            }

            int automatedLeft = ResolveDynamicAppListLeftEdge();
            if (automatedLeft != -1 && automatedLeft != int.MaxValue)
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

        if (_cachedAppListLeftEdge > 0)
        {
            return _cachedAppListLeftEdge;
        }

        int fallback = !IsTaskbarCentered()
            ? _taskbarRect.Left
            : _taskbarRect.Left + (_taskbarRect.Width / 4);
        _cachedAppListLeftEdge = fallback;
        _lastAppListLeftQueryTicks = now - (Stopwatch.Frequency * 28);
        return fallback;
    }

    private static int FindStartButtonLeft(AutomationElement trayElem)
    {
        var startCond = new PropertyCondition(AutomationElement.AutomationIdProperty, "StartButton");
        var startBtn = trayElem.FindFirst(TreeScope.Descendants, startCond);
        if (startBtn is not null)
        {
            var sr = startBtn.Current.BoundingRectangle;
            if (!sr.IsEmpty && sr.Width > 0)
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
            if (minChildLeft != -1 && minChildLeft != int.MaxValue) return minChildLeft;

            var rect = listElem.Current.BoundingRectangle;
            if (!rect.IsEmpty && rect.Width > 0)
            {
                return (int)rect.Left;
            }
        }
        return -1;
    }

    private static int QueryAppListLeftEdgeWorker(IntPtr tray)
    {
        try
        {
            var trayElem = AutomationElement.FromHandle(tray);
            if (trayElem is null) return -1;

            int startLeft = FindStartButtonLeft(trayElem);
            if (startLeft != -1) return startLeft;

            return FindContainerLeftEdge(trayElem);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "UIAutomation query for AppListLeftEdge worker failed");
            return -1;
        }
    }

    internal static int QueryAppListLeftEdgeFromAutomation()
    {
        IntPtr tray = CurrentTargetTrayWnd.HasValue && CurrentTargetTrayWnd.Value != IntPtr.Zero
            ? CurrentTargetTrayWnd.Value
            : FindExplorerTrayWnd();
        return QueryAppListLeftEdgeFromAutomationForTray(tray);
    }

    private static int QueryAppListLeftEdgeFromAutomationForTray(IntPtr tray)
    {
        if (tray == IntPtr.Zero) return -1;

        if (ThrowInQueryAppListLeftEdgeFromAutomationForTesting)
            throw new InvalidOperationException("Simulated app list left edge automation error");

        try
        {
            var task = Task.Run(() => QueryAppListLeftEdgeWorker(tray));
            task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

            if (task.Wait(TimeSpan.FromMilliseconds(1500)))
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
            if (!cr.IsEmpty && cr.Width > 0)
            {
                minChildLeft = Math.Min(minChildLeft, (int)cr.Left);
            }
        }
        return minChildLeft == int.MaxValue ? -1 : minChildLeft;
    }

    private void UpdateWidgetsEdges()
    {
        if (IsSecondaryTaskbar)
        {
            _leftWidgetRightEdge = -1;
            _rightWidgetLeftEdge = -1;
            return;
        }

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
        if (IsSecondaryTaskbar)
        {
            return _trayLeftEdge;
        }

        if (RightWidgetLeftEdgeOverride is not null)
        {
            int r = RightWidgetLeftEdgeOverride();
            if (r != -1 && _trayLeftEdge != -1) return Math.Min(_trayLeftEdge, r);
            if (r != -1) return r;
        }

        if (_rightWidgetLeftEdge != -1 && _trayLeftEdge != -1)
        {
            return Math.Min(_trayLeftEdge, _rightWidgetLeftEdge);
        }

        if (_rightWidgetLeftEdge != -1)
            return _rightWidgetLeftEdge;

        if (_trayLeftEdge != -1)
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
        try
        {
            var trayElem = AutomationElement.FromHandle(tray);
            if (trayElem is null) return (-1, -1);

            var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, "WidgetsButton");
            var widgetBtn = trayElem.FindFirst(TreeScope.Descendants, cond);
            if (widgetBtn is null) return (-1, -1);

            return ParseWidgetBoundingBox(widgetBtn, taskbarWidth, now);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "UIAutomation query for WidgetsButton worker failed");
            return (-1, -1);
        }
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
            task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

            if (task.Wait(TimeSpan.FromMilliseconds(250)))
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

            var tray = GetTargetTrayWnd();
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
        int overlayW = overlaySize.Width + gap + Math.Max(0, _cfg.Overlay.PlacementOffset);

        int clientOriLeft = 0;
        if (_parentHwnd != IntPtr.Zero && NativeMethods.GetWindowRect(_parentHwnd, out var parentRect))
        {
            clientOriLeft = Math.Max(0, _taskBandOri.Left - parentRect.Left);
        }

        int appsRight = ResolveClientAppsRight(clientOriLeft + 50);
        int minButtonsWidth = Math.Max(50, appsRight - clientOriLeft);
        int newBandW = Math.Max(cur.Width - overlayW, minButtonsWidth);

        if (_cfg.Overlay.Placement == "left")
        {
            NativeMethods.MoveWindow(_taskBandHwnd,
                clientOriLeft + overlayW, 0,
                newBandW, cur.Height, true);
        }
        else
        {
            NativeMethods.MoveWindow(_taskBandHwnd,
                clientOriLeft, 0,
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
        _currentOverlayHwnd = overlayHwnd;
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

        if (_lastPositionedX == int.MinValue || Math.Abs(x - _lastPositionedX) > 2 || sz.Width != _lastPositionedWidth)
        {
            _lastPositionedX = x;
            _lastPositionedWidth = sz.Width;
            Log.Debug($"Taskbar overlay positioned at x={x}, y={y}, width={sz.Width} (slot={AvailableSlotWidth}px, constrained={IsTaskbarSpaceConstrained})");
        }

        int targetX = x;
        int targetY = y;
        IntPtr actualParent = NativeMethods.GetParent(overlayHwnd);
        bool isActualChild = actualParent == _parentHwnd && _parentHwnd != IntPtr.Zero;
        if (!isActualChild && _parentHwnd != IntPtr.Zero)
        {
            var pt = new NativeMethods.Point { X = x, Y = y };
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

    private int CalculateRightSlotWidth(NativeMethods.Rect parentRect, int offset)
    {
        int rightBoundary = GetRightSideBoundary();
        int clientRightBoundary;
        if (rightBoundary != int.MinValue && rightBoundary != -1)
        {
            var pt = new NativeMethods.Point { X = rightBoundary, Y = 0 };
            NativeMethods.ScreenToClient(_parentHwnd, ref pt);
            clientRightBoundary = pt.X;
        }
        else
        {
            clientRightBoundary = parentRect.Width - ResolveTrayWidth(GetTargetTrayWnd(), IsSecondaryTaskbar);
        }
        int appRight = GetAppListRightEdge();
        var appPt = new NativeMethods.Point { X = appRight, Y = 0 };
        NativeMethods.ScreenToClient(_parentHwnd, ref appPt);
        int clientAppRight = Math.Max(0, appPt.X);
        return Math.Max(0, clientRightBoundary - clientAppRight - (2 * offset));
    }

    private int CalculateLeftSlotWidth(int offset)
    {
        int leftEdge = _leftWidgetRightEdge != -1 ? _leftWidgetRightEdge : GetLeftWidgetRightEdge();
        int clientLeftEdge = 0;
        if (leftEdge != -1)
        {
            var pt = new NativeMethods.Point { X = leftEdge, Y = 0 };
            NativeMethods.ScreenToClient(_parentHwnd, ref pt);
            clientLeftEdge = Math.Max(0, pt.X);
        }

        int appLeft = GetAppListLeftEdge();
        var appPt = new NativeMethods.Point { X = appLeft, Y = 0 };
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
        if (!_isWin11 && _taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd))
        {
            NativeMethods.GetWindowRect(_taskBandHwnd, out var bandRect);
            var bandPt = new NativeMethods.Point { X = bandRect.Left, Y = bandRect.Top };
            NativeMethods.ScreenToClient(_parentHwnd, ref bandPt);
            if (_cfg.Overlay.Placement == "left")
            {
                return Math.Max(0, bandPt.X - (2 * offset));
            }

            int rightBound = FindRebarDeskBandRightBound(bandPt.X, parentRect.Width);
            int appsRight = ResolveClientAppsRight(bandPt.X + bandRect.Width);
            return Math.Max(0, rightBound - appsRight - (2 * offset));
        }

        if (IsTaskbarCentered() && _cfg.Overlay.Placement == "left")
        {
            return CalculateLeftSlotWidth(offset);
        }

        return CalculateRightSlotWidth(parentRect, offset);
    }

    private int FindRebarDeskBandRightBound(int bandPtX, int parentWidth)
    {
        int rightBound = parentWidth;
        if (_trayLeftEdge != -1)
        {
            var trayPt = new NativeMethods.Point { X = _trayLeftEdge, Y = 0 };
            NativeMethods.ScreenToClient(_parentHwnd, ref trayPt);
            rightBound = Math.Min(rightBound, Math.Max(trayPt.X, 0));
        }

        IntPtr rebarChild = IntPtr.Zero;
        while ((rebarChild = SafeFindWindowEx(_parentHwnd, rebarChild, null, null)) != IntPtr.Zero)
        {
            if (rebarChild == _currentOverlayHwnd || rebarChild == _taskBandHwnd || !NativeMethods.IsWindowVisible(rebarChild)) continue;
            if (IsAppOverlayWindow(rebarChild)) continue;
            if (!NativeMethods.GetWindowRect(rebarChild, out var cr) || cr.Width <= 0 || cr.Height <= 0) continue;

            var pt = new NativeMethods.Point { X = cr.Left, Y = 0 };
            NativeMethods.ScreenToClient(_parentHwnd, ref pt);
            if (pt.X > bandPtX && pt.X < parentWidth)
            {
                rightBound = Math.Min(rightBound, pt.X);
            }
        }

        return rightBound;
    }

    private int ResolveClientAppsRight(int fallbackX)
    {
        int appRightScreen = GetAppListRightEdge();
        if (appRightScreen > 0 && _parentHwnd != IntPtr.Zero)
        {
            var appPt = new NativeMethods.Point { X = appRightScreen, Y = 0 };
            NativeMethods.ScreenToClient(_parentHwnd, ref appPt);
            return Math.Max(0, appPt.X);
        }
        return fallbackX;
    }

    private int ComputeBandWin10X(NativeMethods.Rect parentRect, Size sz)
    {
        NativeMethods.GetWindowRect(_taskBandHwnd, out var bandRect);
        var bandPt = new NativeMethods.Point { X = bandRect.Left, Y = bandRect.Top };
        NativeMethods.ScreenToClient(_parentHwnd, ref bandPt);

        int offset = _cfg.Overlay.PlacementOffset;
        int checkWidth = NaturalOverlayWidth > 0 ? NaturalOverlayWidth : sz.Width;

        if (_cfg.Overlay.Placement == "left")
        {
            int availableLeft = Math.Max(0, bandPt.X - (2 * offset));
            AvailableSlotWidth = availableLeft;
            IsTaskbarSpaceConstrained = bandPt.X > 0 && (availableLeft == 0 || checkWidth > availableLeft);

            int minLeftX = offset;
            int maxLeftX = Math.Max(minLeftX, bandPt.X - sz.Width - offset);
            return Math.Clamp(bandPt.X - sz.Width - offset, minLeftX, maxLeftX);
        }

        int rightBound = FindRebarDeskBandRightBound(bandPt.X, parentRect.Width);
        int appsRight = ResolveClientAppsRight(bandPt.X + bandRect.Width);
        int availableWidth = Math.Max(0, rightBound - appsRight - (2 * offset));
        AvailableSlotWidth = availableWidth;
        IsTaskbarSpaceConstrained = rightBound > 0 && (availableWidth == 0 || checkWidth > availableWidth);

        if (_cfg.Overlay.Placement == "beside_apps")
        {
            int minX = appsRight + offset;
            int maxX = Math.Max(minX, rightBound - sz.Width);
            return Math.Clamp(minX, minX, maxX);
        }

        // "right" (default on Windows 10)
        int minRightX = appsRight + offset;
        int maxRightX = Math.Max(minRightX, rightBound - sz.Width);
        int preferredRightX = rightBound - sz.Width - offset;
        return Math.Clamp(preferredRightX, minRightX, maxRightX);
    }

    private int ResolveClientRightBoundary(NativeMethods.Rect parentRect)
    {
        int rightBoundary = GetRightSideBoundary();
        if (rightBoundary != int.MinValue && rightBoundary != -1)
        {
            var pt = new NativeMethods.Point { X = rightBoundary, Y = 0 };
            NativeMethods.ScreenToClient(_parentHwnd, ref pt);
            return pt.X;
        }

        return parentRect.Width - ResolveTrayWidth(GetTargetTrayWnd(), IsSecondaryTaskbar);
    }

    internal int ComputeBandX(NativeMethods.Rect parentRect, Size sz)
    {
        if (!_isWin11 && _taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd))
        {
            return ComputeBandWin10X(parentRect, sz);
        }

        int clientRightBoundary = ResolveClientRightBoundary(parentRect);
        return !IsTaskbarCentered()
            ? ComputeBandLeftAlignedX(sz, clientRightBoundary)
            : ComputeBandCenteredX(sz, clientRightBoundary);
    }

    private int ComputeBandLeftAlignedX(Size sz, int clientRightBoundary)
    {
        int appRight = GetAppListRightEdge();
        var appPt = new NativeMethods.Point { X = appRight, Y = 0 };
        NativeMethods.ScreenToClient(_parentHwnd, ref appPt);
        int clientAppRight = Math.Max(0, appPt.X);

        int offset = _cfg.Overlay.PlacementOffset;
        int availableWidth = Math.Max(0, clientRightBoundary - clientAppRight - (2 * offset));
        AvailableSlotWidth = availableWidth;
        int checkWidth = NaturalOverlayWidth > 0 ? NaturalOverlayWidth : sz.Width;
        IsTaskbarSpaceConstrained = clientRightBoundary > 0 && (availableWidth == 0 || checkWidth > availableWidth);

        int minX = clientAppRight + offset;
        int maxX = Math.Max(minX, clientRightBoundary - sz.Width);
        int preferredX = _cfg.Overlay.Placement is "beside_apps" or "left"
            ? minX
            : clientRightBoundary - sz.Width - offset;

        return Math.Clamp(preferredX, minX, maxX);
    }

    private int ComputeBandCenteredX(Size sz, int clientRightBoundary)
    {
        int offset = _cfg.Overlay.PlacementOffset;
        int checkWidth = NaturalOverlayWidth > 0 ? NaturalOverlayWidth : sz.Width;
        if (_cfg.Overlay.Placement == "left")
        {
            int leftEdge = _leftWidgetRightEdge != -1 ? _leftWidgetRightEdge : GetLeftWidgetRightEdge();
            int clientLeftEdge = 0;
            if (leftEdge != -1)
            {
                var pt = new NativeMethods.Point { X = leftEdge, Y = 0 };
                NativeMethods.ScreenToClient(_parentHwnd, ref pt);
                clientLeftEdge = Math.Max(0, pt.X);
            }

            int appLeft = GetAppListLeftEdge();
            var appPt = new NativeMethods.Point { X = appLeft, Y = 0 };
            NativeMethods.ScreenToClient(_parentHwnd, ref appPt);
            int clientAppLeft = Math.Max(clientLeftEdge, appPt.X);

            int availableWidth = Math.Max(0, clientAppLeft - clientLeftEdge - (2 * offset));
            AvailableSlotWidth = availableWidth;
            IsTaskbarSpaceConstrained = clientAppLeft > 0 && (availableWidth == 0 || checkWidth > availableWidth);

            int preferredX = clientLeftEdge + offset;
            if (preferredX < clientAppLeft)
            {
                int maxX = Math.Max(clientLeftEdge, clientAppLeft - sz.Width - offset);
                return Math.Clamp(preferredX, clientLeftEdge, maxX);
            }

            return preferredX;
        }

        int appRight = GetAppListRightEdge();
        var rightAppPt = new NativeMethods.Point { X = appRight, Y = 0 };
        NativeMethods.ScreenToClient(_parentHwnd, ref rightAppPt);
        int clientAppRight = Math.Max(0, rightAppPt.X);

        int availableRightWidth = Math.Max(0, clientRightBoundary - clientAppRight - (2 * offset));
        AvailableSlotWidth = availableRightWidth;
        IsTaskbarSpaceConstrained = clientRightBoundary > 0 && (availableRightWidth == 0 || checkWidth > availableRightWidth);

        int preferredRightX = clientRightBoundary - sz.Width - offset;
        int minRightX = clientAppRight + offset;
        int maxRightX = Math.Max(minRightX, clientRightBoundary - sz.Width);
        return Math.Clamp(preferredRightX, minRightX, maxRightX);
    }

    internal static int ResolveTrayWidth(IntPtr currentTray, bool isSecondary)
    {
        if (isSecondary || IsSecondaryTrayWindow(currentTray))
        {
            uint dpi = currentTray != IntPtr.Zero ? NativeMethods.GetDpiForWindow(currentTray) : 96;
            float scale = dpi > 0 ? dpi / 96f : 1f;
            return WindowHelpers.Scale(80, scale);
        }

        int iconW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
        if (iconW <= 0) iconW = 16;
        int dpiScaledBase = WindowHelpers.Scale(200, 1f);
        return Math.Max(iconW * 6 + 90, dpiScaledBase);
    }

    internal static int EstimateTrayWidth()
    {
        IntPtr currentTray = CurrentTargetTrayWnd.HasValue && CurrentTargetTrayWnd.Value != IntPtr.Zero
            ? CurrentTargetTrayWnd.Value
            : FindExplorerTrayWnd();
        return ResolveTrayWidth(currentTray, IsSecondaryTrayWindow(currentTray));
    }
}
