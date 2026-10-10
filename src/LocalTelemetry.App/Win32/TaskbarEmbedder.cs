using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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

    /// <summary>Minimum gap (physical px) kept between the overlay's right edge and the tray area.</summary>
    private const int TrayInset = 4;

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
        _lastLoggedTrayEdge = int.MinValue;
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
            IntPtr tray = FindExplorerTrayWnd();
            if (tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out var r) && r.Width > 0 && r.Height > 0)
            {
                _taskbarRect = new Rectangle(r.Left, r.Top, r.Width, r.Height);
                if (r.Top == 0 && r.Height < r.Width)
                    _taskbarEdge = NativeMethods.ABE_TOP;
                else if (r.Left == 0 && r.Width < r.Height)
                    _taskbarEdge = NativeMethods.ABE_LEFT;
                else if (r.Left > 0 && r.Width < r.Height)
                    _taskbarEdge = NativeMethods.ABE_RIGHT;
                else
                    _taskbarEdge = NativeMethods.ABE_BOTTOM;
            }
            else
            {
                var scr = System.Windows.Forms.Screen.PrimaryScreen;
                if (scr is not null && scr.Bounds.Width > 0 && scr.Bounds.Height > 0)
                {
                    if (scr.WorkingArea.Bottom < scr.Bounds.Bottom)
                    {
                        _taskbarRect = new Rectangle(scr.Bounds.Left, scr.WorkingArea.Bottom, scr.Bounds.Width, scr.Bounds.Bottom - scr.WorkingArea.Bottom);
                        _taskbarEdge = NativeMethods.ABE_BOTTOM;
                    }
                    else if (scr.WorkingArea.Top > scr.Bounds.Top)
                    {
                        _taskbarRect = new Rectangle(scr.Bounds.Left, scr.Bounds.Top, scr.Bounds.Width, scr.WorkingArea.Top - scr.Bounds.Top);
                        _taskbarEdge = NativeMethods.ABE_TOP;
                    }
                    else if (scr.WorkingArea.Right < scr.Bounds.Right)
                    {
                        _taskbarRect = new Rectangle(scr.WorkingArea.Right, scr.Bounds.Top, scr.Bounds.Right - scr.WorkingArea.Right, scr.Bounds.Height);
                        _taskbarEdge = NativeMethods.ABE_RIGHT;
                    }
                    else if (scr.WorkingArea.Left > scr.Bounds.Left)
                    {
                        _taskbarRect = new Rectangle(scr.Bounds.Left, scr.Bounds.Top, scr.WorkingArea.Left - scr.Bounds.Left, scr.Bounds.Height);
                        _taskbarEdge = NativeMethods.ABE_LEFT;
                    }
                    else
                    {
                        _taskbarRect = new Rectangle(scr.Bounds.Left, scr.Bounds.Bottom - 48, scr.Bounds.Width, 48);
                        _taskbarEdge = NativeMethods.ABE_BOTTOM;
                    }
                }
            }
        }
    }

    private static bool IsTaskbarCentered()
    {
        try
        {
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
            IntPtr tray = FindExplorerTrayWnd();
            if (tray == IntPtr.Zero) return -1;

            // TrayNotifyWnd exists on Win10 and most Win11 builds (order: icons | clock)
            IntPtr trayNotify = SafeFindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (trayNotify != IntPtr.Zero && NativeMethods.GetWindowRect(trayNotify, out var notifyRect))
            {
                LogTrayEdgeChange(notifyRect.Left, $"TrayNotifyWnd left={notifyRect.Left}");
                return notifyRect.Left;
            }

            // Win11: the clock is rendered as a separate top-level widget; its left edge
            // marks the start of the tray content area on taskbars without TrayNotifyWnd.
            IntPtr clock = SafeFindWindowEx(tray, IntPtr.Zero, "TrayClockWClass", null);
            if (clock == IntPtr.Zero)
                clock = SafeFindWindowEx(tray, IntPtr.Zero, "ClockCompWidget", null);
            if (clock != IntPtr.Zero && NativeMethods.GetWindowRect(clock, out var clockRect))
            {
                LogTrayEdgeChange(clockRect.Left, $"clock left={clockRect.Left}");
                return clockRect.Left;
            }

            // Win11: DesktopWindowContentBridge covers the full tray content area.
            // Estimate the tray left edge by subtracting a DPI-aware tray width.
            IntPtr bridge = SafeFindWindowEx(tray, IntPtr.Zero,
                "Windows.UI.Composition.DesktopWindowContentBridge", null);
            if (bridge != IntPtr.Zero && NativeMethods.GetWindowRect(bridge, out var bridgeRect))
            {
                int trayWidth = EstimateTrayWidth();
                int edge = bridgeRect.Right - trayWidth;
                LogTrayEdgeChange(edge, $"bridge estimate (right={bridgeRect.Right}, width={trayWidth}) => {edge}");
                return edge;
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

    // Reposition() runs on a 200ms timer; GetTrayLeftEdge() resolves the same value every
    // tick, so only emit a log line when the resolved edge actually changes significantly.
    private static int _lastLoggedTrayEdge = int.MinValue;

    private static void LogTrayEdgeChange(int edge, string message)
    {
        if (_lastLoggedTrayEdge != int.MinValue && Math.Abs(edge - _lastLoggedTrayEdge) < 40) return;
        _lastLoggedTrayEdge = edge;
        Log.Debug($"Detected tray edge via {message}");
    }

    /// <summary>
    /// Dumps the direct child windows of the Explorer taskbar (class name, visibility,
    /// rect) to the system log. Used to diagnose overlay placement issues.
    /// </summary>
    private static void LogTrayStructure()
    {
        try
        {
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

        if (!NativeMethods.GetClientRect(_parentHwnd, out var parentRect))
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
                return Math.Max(bandPt.X - sz.Width - 2, 0);
            }

            int bandEnd = bandPt.X + bandRect.Width;
            int rightBound = _trayLeftEdge > 0 ? _trayLeftEdge - 2 : parentRect.Width;
            return Math.Max(Math.Min(bandEnd + 2, rightBound - sz.Width), 0);
        }

        if (_cfg.Overlay.Placement == "left")
        {
            return _cfg.Overlay.PlacementOffset;
        }

        if (_isWin11 && IsTaskbarCentered())
        {
            int rightBound = _trayLeftEdge > 0 ? _trayLeftEdge : parentRect.Width;
            int computedX = rightBound - sz.Width - _cfg.Overlay.PlacementOffset;
            return _trayLeftEdge > 0
                ? Math.Min(computedX, _trayLeftEdge - sz.Width - TrayInset)
                : computedX;
        }

        return parentRect.Width - sz.Width - EstimateTrayWidth() - _cfg.Overlay.PlacementOffset;
    }

    private bool PositionFloat(IntPtr overlayHwnd, Size sz)
    {

        if (_cfg.Overlay.FloatX >= 0 && _cfg.Overlay.FloatY >= 0)
        {
            LastComputedFloatX = _cfg.Overlay.FloatX;
            LastComputedFloatY = _cfg.Overlay.FloatY;
            if (overlayHwnd != IntPtr.Zero && NativeMethods.IsWindow(overlayHwnd))
            {
                var targetRect = new Rectangle(_cfg.Overlay.FloatX, _cfg.Overlay.FloatY, sz.Width, sz.Height);
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
                    _cfg.Overlay.FloatX, _cfg.Overlay.FloatY, sz.Width, sz.Height,
                    flags);
                if (!success)
                {
                    int err = Marshal.GetLastWin32Error();
                    Log.Error($"SetWindowPos failed with Win32 error {err} during custom float positioning ({_cfg.Overlay.FloatX}, {_cfg.Overlay.FloatY}).");
                    State = OverlayDeploymentState.Failed;
                    return false;
                }
                _lastFloatRect = targetRect;
                return true;
            }
            return false;
        }

        if (_taskbarRect.Width <= 0 || _taskbarRect.Height <= 0)
        {
            Log.Error($"Taskbar bounds are invalid ({_taskbarRect.Width}x{_taskbarRect.Height}); skipping overlay repositioning until taskbar geometry is resolved");
            State = OverlayDeploymentState.Failed;
            return false;
        }

        bool horizontal = _taskbarEdge is NativeMethods.ABE_BOTTOM or NativeMethods.ABE_TOP;
        int x = horizontal
            ? _cfg.Overlay.Placement switch
            {
                "left" => _taskbarRect.Left + _cfg.Overlay.PlacementOffset,
                "center" => _taskbarRect.Left + (_taskbarRect.Width - sz.Width) / 2 + _cfg.Overlay.PlacementOffset,
                _ => ComputeRightFloatX(sz.Width),
            }
            : _taskbarRect.Left + 4;
        int y = _taskbarRect.Top + (_taskbarRect.Height - sz.Height) / 2;

        if (horizontal && (x < _taskbarRect.Left || x + sz.Width > _taskbarRect.Right + 100))
        {
            Log.Warn($"Calculated floating X coordinate ({x}) for width {sz.Width} is out of taskbar horizontal range [{_taskbarRect.Left}, {_taskbarRect.Right}]. Clamping to taskbar.");
            x = Math.Clamp(x, _taskbarRect.Left, Math.Max(_taskbarRect.Left, _taskbarRect.Right - sz.Width));
        }

        LastComputedFloatX = x;
        LastComputedFloatY = y;

        string floatLog = $"Positioned floating overlay ({sz.Width}x{sz.Height} at {x},{y}, offset {_cfg.Overlay.PlacementOffset}, placement '{_cfg.Overlay.Placement}')";
        if (floatLog != _lastPositionFloatLog)
        {
            _lastPositionFloatLog = floatLog;
            Log.Debug(floatLog);
        }

        var newFloatRect = new Rectangle(x, y, sz.Width, sz.Height);
        if (_lastFloatRect == newFloatRect)
        {
            return true;
        }

        uint floatFlags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
        if (_lastFloatRect.IsEmpty)
        {
            floatFlags |= NativeMethods.SWP_FRAMECHANGED;
        }

        ApplyLayeredAttributes(overlayHwnd);
        bool setOk = NativeMethods.SetWindowPos(
            overlayHwnd, NativeMethods.HWND_TOPMOST,
            x, y, sz.Width, sz.Height,
            floatFlags);
        if (!setOk)
        {
            int err = Marshal.GetLastWin32Error();
            Log.Error($"SetWindowPos failed with Win32 error {err} during floating positioning at ({x}, {y}).");
            State = OverlayDeploymentState.Failed;
            return false;
        }

        _lastFloatRect = newFloatRect;
        return true;
    }

    private int ComputeRightFloatX(int widgetWidth)
    {
        if (_trayLeftEdge > 0)
        {
            int x = _trayLeftEdge - widgetWidth - _cfg.Overlay.PlacementOffset;
            return Math.Max(x, _taskbarRect.Left + 4);
        }

        if (_isWin11 && IsTaskbarCentered())
        {
            int trayWidth = EstimateTrayWidth();
            return _taskbarRect.Right - trayWidth - widgetWidth - _cfg.Overlay.PlacementOffset;
        }

        return _taskbarRect.Right - widgetWidth - EstimateTrayWidth() - _cfg.Overlay.PlacementOffset;
    }

    private static int EstimateTrayWidth()
    {
        int iconW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
        return Math.Max(iconW * 9 + 90, 300);
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
