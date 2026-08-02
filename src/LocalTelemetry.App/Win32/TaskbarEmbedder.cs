using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LocalTelemetry.App.Win32;

/// <summary>
/// Embeds the overlay window into the Windows taskbar, either as a child of the
/// taskband (Approach A) or as a floating layered window (Approach B fallback).
/// Handles Win10 / Win11 taskbar structure differences and repositioning on resize.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TaskbarEmbedder(AppSettings cfg, ILogger<TaskbarEmbedder> log)
{
    private readonly AppSettings _cfg = cfg;
    private readonly ILogger _log = log;

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

    /// <summary>Gets whether the overlay is currently embedded in the taskbar (either approach).</summary>
    public bool IsEmbedded { get; private set; }

    /// <summary>
    /// Gets whether the overlay is using the floating layered-window approach (Approach B)
    /// rather than the child-window approach (Approach A).
    /// </summary>
    public bool IsFallback { get; private set; }

    private int _trayLeftEdge = -1;

    /// <summary>
    /// Attempts to embed the overlay window into the taskbar. Tries the child-window
    /// approach first; falls back to a layered floating window on failure.
    /// </summary>
    /// <param name="overlayHwnd">Handle to the overlay window.</param>
    /// <param name="overlaySize">Desired size of the overlay.</param>
    /// <returns><c>true</c> if Approach A succeeded; <c>false</c> if fallback was used.</returns>
    public bool Embed(IntPtr overlayHwnd, Size overlaySize)
    {
        _trayLeftEdge = GetTrayLeftEdge();
        RefreshTaskbarInfo();
        IsEmbedded = false;
        IsFallback = false;

        if (TrySetParent(overlayHwnd, overlaySize))
        {
            _lastShrinkWidth = overlaySize.Width;
            IsEmbedded = true;
            IsFallback = false;
            PositionInBand(overlayHwnd, overlaySize);
            _log.LogInformation("Taskbar embed: Approach A (child window) active.");
            return true;
        }

        IsEmbedded = true;
        IsFallback = true;
        RestoreLayeredStyles(overlayHwnd);
        PositionFloat(overlayHwnd, overlaySize);
        _log.LogError("Taskbar embed: Approach A failed \u2013 Approach B (layered float) active.");
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

        if (_taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd))
        {
            if (overlaySize.Width != _lastShrinkWidth)
            {
                ShrinkTaskBand(overlaySize);
                _lastShrinkWidth = overlaySize.Width;
            }
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
        IsEmbedded = false;
    }

    /// <summary>
    /// Handles taskbar recreation (e.g. after Explorer restart) by restoring the
    /// original taskband and re-running the embed process.
    /// </summary>
    /// <param name="overlayHwnd">Handle to the overlay window.</param>
    /// <param name="overlaySize">Desired size of the overlay.</param>
    public void OnTaskbarRecreated(IntPtr overlayHwnd, Size overlaySize)
    {
        RestoreTaskBand();
        Embed(overlayHwnd, overlaySize);
    }

    public bool IsChildMode => IsEmbedded && !IsFallback;

    /// <summary>
    /// Re-applies the layered window attributes (alpha + color key) for an active
    /// child-mode overlay. No-op in fallback (float) mode, where per-pixel alpha is used.
    /// </summary>
    /// <param name="overlayHwnd">Handle to the overlay window.</param>
    public void UpdateLayeredSettings(IntPtr overlayHwnd)
    {
        if (!IsChildMode) return;
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

    // Private
    /// <summary>Locates Shell_TrayWnd owned by explorer.exe (avoids grabbing a
    /// third-party toolbar window that reused the same class name).</summary>
    public static IntPtr FindExplorerTrayWnd()
    {
        IntPtr hwnd = IntPtr.Zero;
        while ((hwnd = NativeMethods.FindWindowEx(IntPtr.Zero, hwnd, "Shell_TrayWnd", null)) != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) continue;
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                if (proc.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                    return hwnd;
            }
            catch (ArgumentException) { Log.Info("Explorer process lookup race - process exited between enumeration and query"); }
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
        catch (Exception) { return true; }
    }

    private static int GetTrayLeftEdge()
    {
        try
        {
            IntPtr tray = FindExplorerTrayWnd();
            if (tray == IntPtr.Zero) return -1;

            // TrayNotifyWnd exists on Win10 and most Win11 builds (order: icons | clock)
            IntPtr traywnd = NativeMethods.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (traywnd != IntPtr.Zero)
            {
                NativeMethods.GetWindowRect(traywnd, out var r);
                return r.Left;
            }

            // Win11: DesktopWindowContentBridge covers the full tray content area.
            // Estimate the tray left edge by subtracting a DPI-aware tray width.
            traywnd = NativeMethods.FindWindowEx(tray, IntPtr.Zero,
                "Windows.UI.Composition.DesktopWindowContentBridge", null);
            if (traywnd != IntPtr.Zero && NativeMethods.GetWindowRect(traywnd, out var bridgeRect))
            {
                int trayWidth = EstimateTrayWidth();
                return bridgeRect.Right - trayWidth;
            }

            return -1;
        }
        catch (Exception) { return -1; }
    }

    private static bool IsWindows11Taskbar()
    {
        // Primary: OS build number (Win11 starts at 22000)
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return false;

        // Confirm the actual taskbar has XAML bridge (Win11 taskbar structure)
        var tray = FindExplorerTrayWnd();
        if (tray == IntPtr.Zero) return false;
        return NativeMethods.FindWindowEx(tray, IntPtr.Zero,
            "Windows.UI.Composition.DesktopWindowContentBridge", null) != IntPtr.Zero;
    }

    private bool TrySetParent(IntPtr overlayHwnd, Size overlaySize)
    {
        try
        {
            var tray = FindExplorerTrayWnd();
            if (tray == IntPtr.Zero) return false;

            _isWin11 = IsWindows11Taskbar();

            if (!_isWin11)
            {
                var rebar = NativeMethods.FindWindowEx(tray, IntPtr.Zero, "ReBarWindow32", null);
                if (rebar == IntPtr.Zero)
                    rebar = NativeMethods.FindWindowEx(tray, IntPtr.Zero, "WorkerW", null);
                if (rebar == IntPtr.Zero || !NativeMethods.IsWindow(rebar)) return false;

                _parentHwnd = rebar;
                _taskBandHwnd = NativeMethods.FindWindowEx(rebar, IntPtr.Zero, "MSTaskSwWClass", null);
                if (_taskBandHwnd == IntPtr.Zero)
                    _taskBandHwnd = NativeMethods.FindWindowEx(rebar, IntPtr.Zero, "MSTaskListWClass", null);
            }
            else
            {
                _parentHwnd = tray;
                _taskBandHwnd = IntPtr.Zero;
            }

            if (_parentHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_parentHwnd))
            {
                _log.LogDebug("No valid parent window found; Approach A skipped.");
                return false;
            }

            // Save original taskband rect (Win10 only)
            if (_taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd) && !_taskBandSaved)
            {
                NativeMethods.GetWindowRect(_taskBandHwnd, out _taskBandOri);
                _taskBandSaved = true;
            }

            int ex = NativeMethods.GetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE);
            ex |= NativeMethods.WS_EX_NOACTIVATE
                | NativeMethods.WS_EX_TOOLWINDOW;
            ex &= ~NativeMethods.WS_EX_LAYERED;
            NativeMethods.SetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE, ex);

            int style = NativeMethods.GetWindowLong(overlayHwnd, NativeMethods.GWL_STYLE);
            style |= NativeMethods.WS_CHILD;
            NativeMethods.SetWindowLong(overlayHwnd, NativeMethods.GWL_STYLE, style);

            bool ok = NativeMethods.SetParent(overlayHwnd, _parentHwnd) != IntPtr.Zero;
            if (!ok) return false;

            ex = NativeMethods.GetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE);
            ex |= NativeMethods.WS_EX_LAYERED;
            NativeMethods.SetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE, ex);

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

            if (_taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd))
                ShrinkTaskBand(overlaySize);

            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SetParent threw; falling back to Approach B.");
            return false;
        }
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

    private void PositionInBand(IntPtr overlayHwnd, Size sz)
    {
        if (_parentHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_parentHwnd)) return;
        if (!NativeMethods.GetClientRect(_parentHwnd, out var parentRect)) return;

        int x;
        if (!_isWin11 && _taskBandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_taskBandHwnd))
        {
            NativeMethods.GetWindowRect(_taskBandHwnd, out var bandRect);
            var bandPt = new NativeMethods.POINT { X = bandRect.Left, Y = bandRect.Top };
            NativeMethods.ScreenToClient(_parentHwnd, ref bandPt);

            if (_cfg.Overlay.Placement == "left")
            {
                x = bandPt.X - sz.Width - 2;
            }
            else
            {
                int bandEnd = bandPt.X + bandRect.Width;
                int rightBound = _trayLeftEdge > 0
                    ? _trayLeftEdge - 2
                    : parentRect.Width;
                x = Math.Min(bandEnd + 2, rightBound - sz.Width);
            }
            x = Math.Max(x, 0);
        }
        else
        {
            bool centered = _isWin11 && IsTaskbarCentered();
            if (_cfg.Overlay.Placement == "left")
            {
                x = _cfg.Overlay.PlacementOffset;
            }
            else if (centered)
            {
                int rightBound = _trayLeftEdge > 0
                    ? _trayLeftEdge
                    : parentRect.Width;
                x = rightBound - sz.Width - _cfg.Overlay.PlacementOffset;
            }
            else
            {
                x = parentRect.Width - sz.Width - EstimateTrayWidth() - _cfg.Overlay.PlacementOffset;
            }
        }

        int y = Math.Max(0, (parentRect.Height - sz.Height) / 2);

        NativeMethods.SetWindowPos(
            overlayHwnd, NativeMethods.HWND_TOP,
            x, y, sz.Width, sz.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

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
    }

    private void PositionFloat(IntPtr overlayHwnd, Size sz)
    {
        if (_cfg.Overlay.FloatX >= 0 && _cfg.Overlay.FloatY >= 0)
        {
            NativeMethods.SetWindowPos(
                overlayHwnd, NativeMethods.HWND_TOPMOST,
                _cfg.Overlay.FloatX, _cfg.Overlay.FloatY, sz.Width, sz.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            return;
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

        _cfg.Overlay.FloatX = x;
        _cfg.Overlay.FloatY = y;

        NativeMethods.SetWindowPos(
            overlayHwnd, NativeMethods.HWND_TOPMOST,
            x, y, sz.Width, sz.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
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
        int ex = NativeMethods.GetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_NOACTIVATE
             | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE, ex);

        int style = NativeMethods.GetWindowLong(overlayHwnd, NativeMethods.GWL_STYLE);
        style &= ~NativeMethods.WS_CHILD;
        NativeMethods.SetWindowLong(overlayHwnd, NativeMethods.GWL_STYLE, style);
    }
}
