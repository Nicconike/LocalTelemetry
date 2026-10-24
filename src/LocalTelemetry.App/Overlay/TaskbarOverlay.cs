using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LocalTelemetry.App.Win32;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Diagnostics;
using LocalTelemetry.Core.Hardware;
using LocalTelemetry.Core.Models;
using Microsoft.Win32;

namespace LocalTelemetry.App.Overlay;

/// <summary>
/// A transparent overlay window embedded in or floating above the Windows taskbar.
/// Renders hardware telemetry metrics using GDI+ and supports click-through,
/// DPI awareness and a flash alert effect.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TaskbarOverlay : NativeWindow, IDisposable
{
    private readonly AppSettings _cfg;
    private readonly TaskbarEmbedder _embedder;
    private readonly SynchronizationContext _uiCtx;

    private TelemetrySnapshot _snap = TelemetrySnapshot.Empty;
    private TelemetrySnapshot _lastRenderedSnap = TelemetrySnapshot.Empty;
    private float _dpi = 1f;
    private Color _keyColor;
    private bool _isLightTheme;
    private bool _flash;
    private DateTime _flashEnd = DateTime.MinValue;


    private System.Threading.Timer? _watchTimer;
    private System.Threading.Timer? _posTimer;
    private System.Threading.Timer? _themeTimer;
    private IntPtr _lastTrayHwnd = IntPtr.Zero;
    private Font? _cachedFont;
    private string _fontCacheKey = string.Empty;
    private readonly Dictionary<int, SolidBrush> _brushCache = [];
    private bool _disposed;
    private bool _refreshLogged;
    private int _overlayWidth = 200;
    private int _overlayHeight = 30;


    // Layout constants
    private const int ItemSpace = 3; // px gap between columns (DPI-scaled)
    private const int RowGap = 5; // px gap between top and bottom rows

    /// <summary>Gets whether the overlay window is currently shown.</summary>
    public bool Visible { get; private set; }

    /// <summary>Gets the current deployment lifecycle state of the overlay.</summary>
    public OverlayDeploymentState DeploymentState => _embedder.State;

    internal static bool SkipShowWindowForTesting { get; set; }
    internal static bool SkipMouseHookForTesting { get; set; }
    internal static bool SkipTimersForTesting { get; set; }
    internal static bool SkipEmbedForTesting { get; set; }

    /// <summary>Fired on double-click when <see cref="AppSettings.Overlay.DoubleClickAction"/> is set to a non-"none" action.</summary>
    public Action<string>? OnDoubleClick { get; set; }

    /// <summary>Fired when the user requests Settings from the overlay context menu.</summary>
    public Action? OnOpenSettingsRequested { get; set; }
    /// <summary>Fired when the user toggles overlay visibility from the overlay context menu.</summary>
    public Action? OnToggleOverlayRequested { get; set; }
    /// <summary>Fired when the user requests exiting the application from the overlay context menu.</summary>
    public Action? OnExitRequested { get; set; }

    /// <summary>
    /// Initialises a new overlay instance. The window is not created until <see cref="Show"/> is called.
    /// </summary>
    /// <param name="cfg">Application settings used for layout, colors and behaviour.</param>
    /// <param name="embedder">Taskbar embedding strategy (child-window or layered float).</param>
    public TaskbarOverlay(AppSettings cfg, TaskbarEmbedder embedder)
    {
        _cfg = cfg;
        _embedder = embedder;
        _uiCtx = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        UpdateKeyColor();
    }

    /// <summary>Creates (or re-shows) the overlay window and embeds it in the taskbar.</summary>
    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Handle != IntPtr.Zero && NativeMethods.IsWindow(Handle))
        {
            TryReshowExistingWindow();
            return;
        }

        EnsureWindowCreated();

        _dpi = GetDpi();
        InitializeThemePolling();
        UpdateKeyColor();
        var sz = MeasureSize();
        _overlayWidth = sz.Width;
        _overlayHeight = sz.Height;

        if (!SkipEmbedForTesting)
            _embedder.Embed(Handle, sz);

        StartWatchAndPositionTimers();

        if (!SkipEmbedForTesting && _embedder.State != OverlayDeploymentState.Active)
        {
            Log.Error($"Taskbar overlay failed to deploy into active state ({_embedder.State})");
            Visible = false;
            return;
        }

        ActivateOverlayWindow();
        Log.Info($"Taskbar overlay shown ({sz.Width}x{sz.Height}, {(_embedder.IsChildMode ? "child" : "floating")} mode)");
    }

    private void TryReshowExistingWindow()
    {
        if (Visible) return;

        Log.Debug("Re-showing existing taskbar overlay window");
        _dpi = GetDpi();
        _lastRenderedSnap = TelemetrySnapshot.Empty;
        var resz = MeasureSize();
        _overlayWidth = resz.Width;
        _overlayHeight = resz.Height;
        if (!SkipEmbedForTesting)
            _embedder.Embed(Handle, resz);

        if (!SkipEmbedForTesting && _embedder.State != OverlayDeploymentState.Active)
        {
            Log.Error($"Taskbar overlay failed to re-embed into active state ({_embedder.State})");
            Visible = false;
            return;
        }

        ActivateOverlayWindow();
    }

    private void ActivateOverlayWindow()
    {
        Visible = true;
        if (!SkipShowWindowForTesting)
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
        if (!SkipMouseHookForTesting)
            StartMouseHook();
        if (!SkipShowWindowForTesting)
            RefreshOverlay();
    }

    private void InitializeThemePolling()
    {
        if (!_cfg.Overlay.FollowWindowsTheme) return;

        DetectSystemTheme();
        if (!SkipTimersForTesting && _themeTimer is null)
        {
            _themeTimer = new System.Threading.Timer(_ => ThemeTick(), null, 2_000, 2_000);
            Log.Debug("Windows theme polling timer started (2s interval)");
        }
    }

    private void StartWatchAndPositionTimers()
    {
        if (SkipTimersForTesting || _watchTimer is not null) return;

        _lastTrayHwnd = TaskbarEmbedder.FindExplorerTrayWnd();
        _watchTimer = new System.Threading.Timer(_ => WatchTick(), null, 5_000, 5_000);
        _posTimer = new System.Threading.Timer(_ => PosTick(), null, 200, 200);
    }

    /// <summary>Hides the overlay window without destroying it.</summary>
    public void Hide()
    {
        Log.Info("Hiding taskbar overlay window");
        if (Handle == IntPtr.Zero || !NativeMethods.IsWindow(Handle))
        {
            Visible = false;
            return;
        }
        Visible = false;
        StopMouseHook();
        if (!SkipShowWindowForTesting)
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
        if (!SkipEmbedForTesting)
            _embedder.Detach(Handle);
    }

    /// <summary>
    /// Updates the snapshot data and queues a UI-thread redraw when values have changed
    /// beyond display precision.
    /// </summary>
    /// <param name="snap">The latest telemetry snapshot.</param>
    public void UpdateSnapshot(TelemetrySnapshot snap)
    {
        if (!_cfg.Overlay.Visible || _disposed || Handle == IntPtr.Zero) return;
        _snap = snap;

        _uiCtx.Post(_ =>
        {
            try
            {
                if (_disposed || Handle == IntPtr.Zero || !ValuesChanged(snap, _lastRenderedSnap))
                    return;

                ApplyOverlayDimensions(MeasureSize());

                if (_embedder.IsChildMode)
                    Invalidate();
                else
                    RenderFloat();

                _lastRenderedSnap = snap;
            }
            catch (Exception ex) { Log.Error(ex, "Failed to render telemetry snapshot to overlay"); }
        }, null);
    }

    private void ApplyOverlayDimensions(Size sz)
    {
        if (sz.Width != _overlayWidth || sz.Height != _overlayHeight)
        {
            _overlayWidth = sz.Width;
            _overlayHeight = sz.Height;
            _embedder.Reposition(Handle, sz);
        }
    }

    private static bool ValuesChanged(TelemetrySnapshot a, TelemetrySnapshot b)
    {
        return HasCpuValuesChanged(a, b)
            || HasRamValuesChanged(a, b)
            || HasGpuValuesChanged(a, b)
            || HasNetOrDiskValuesChanged(a, b);
    }

    private static bool HasCpuValuesChanged(TelemetrySnapshot a, TelemetrySnapshot b)
    {
        return MathF.Abs(a.CpuUsagePct - b.CpuUsagePct) >= 0.5f
            || MathF.Abs(a.CpuTempPackageC - b.CpuTempPackageC) >= 0.5f
            || MathF.Abs(a.CpuFreqGhz - b.CpuFreqGhz) >= 0.005f
            || MathF.Abs(a.CpuPackagePowerW - b.CpuPackagePowerW) >= 0.5f;
    }

    private static bool HasRamValuesChanged(TelemetrySnapshot a, TelemetrySnapshot b)
    {
        return MathF.Abs(a.RamUsagePct - b.RamUsagePct) >= 0.5f
            || MathF.Abs(a.RamUsedGb - b.RamUsedGb) >= 0.05f;
    }

    private static bool HasGpuValuesChanged(TelemetrySnapshot a, TelemetrySnapshot b)
    {
        return MathF.Abs(a.GpuUsagePct - b.GpuUsagePct) >= 0.5f
            || MathF.Abs(a.GpuTempC - b.GpuTempC) >= 0.5f
            || MathF.Abs(a.GpuVramUsedMb - b.GpuVramUsedMb) >= 0.5f
            || MathF.Abs(a.GpuFreqMHz - b.GpuFreqMHz) >= 0.5f
            || MathF.Abs(a.GpuPowerW - b.GpuPowerW) >= 0.5f;
    }

    private static bool HasNetOrDiskValuesChanged(TelemetrySnapshot a, TelemetrySnapshot b)
    {
        return Math.Abs(a.NetDownBps - b.NetDownBps) > 100
            || Math.Abs(a.NetUpBps - b.NetUpBps) > 100
            || a.NetTotalBytes != b.NetTotalBytes
            || MathF.Abs(a.DiskReadMbps - b.DiskReadMbps) >= 0.05f
            || MathF.Abs(a.DiskWriteMbps - b.DiskWriteMbps) >= 0.05f
            || a.NetInterfaceName != b.NetInterfaceName;
    }

    /// <summary>Triggers a visual flash (orange tint) on the overlay for the given duration.</summary>
    /// <param name="duration">How long the flash effect should last.</param>
    public void Flash(TimeSpan duration)
    {
        _flash = true;
        _flashEnd = DateTime.UtcNow + duration;
        if (Handle == IntPtr.Zero || _disposed) return;
        _uiCtx.Post(_ =>
        {
            if (_embedder.IsChildMode) Invalidate();
            else RenderFloat();
        }, null);
    }

    /// <summary>
    /// Forces a full overlay redraw (color, font, layout) on the UI thread.
    /// Called after settings changes.
    /// </summary>
    public void RefreshOverlay()
    {
        if (Handle == IntPtr.Zero || _disposed || !Visible || !_cfg.Overlay.Visible) return;
        if (!_refreshLogged) { _refreshLogged = true; Log.Debug("Refreshed overlay display"); }
        _uiCtx.Post(_ =>
        {
            if (_disposed || Handle == IntPtr.Zero) return;
            DetectSystemTheme();
            _cachedFont?.Dispose();
            _cachedFont = null;
            _fontCacheKey = string.Empty;
            foreach (var b in _brushCache.Values) b.Dispose();
            _brushCache.Clear();
            UpdateKeyColor();
            _dpi = GetDpi();
            var sz = MeasureSize();
            _overlayWidth = sz.Width;
            _overlayHeight = sz.Height;
            _embedder.Reposition(Handle, sz);
            if (_embedder.IsChildMode) Invalidate();
            else RenderFloat();
        }, null);
    }

    private DateTime _lastClickTime = DateTime.MinValue;

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WM_SETCURSOR:
                NativeMethods.SetCursor(NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_ARROW));
                m.Result = (IntPtr)1;
                return;
            case NativeMethods.WM_DESTROY:
            case NativeMethods.WM_NCDESTROY:
                if (Visible && !_disposed)
                {
                    Log.Debug("Taskbar overlay window received WM_DESTROY/WM_NCDESTROY (parent taskbar destroyed or closed)");
                }
                break;
            case NativeMethods.WM_PAINT:
                WmPaint();
                return;
            case NativeMethods.WM_ERASEBKGND:
                return;
            case NativeMethods.WM_DPICHANGED:
            case NativeMethods.WM_DPICHANGED_BEFOREPARENT:
            case NativeMethods.WM_DPICHANGED_AFTERPARENT:
                HandleDpiChanged();
                return;
            case NativeMethods.WM_DISPLAYCHANGE:
                HardwareLifecycleManager.Instance.NotifyDisplayChanged();
                break;
            case NativeMethods.WM_DEVICECHANGE:
                HandleDeviceChange(m.WParam);
                break;
            case NativeMethods.WM_TABLET_QUERYSYSTEMGESTURESTATUS:
                m.Result = IntPtr.Zero;
                return;
            case NativeMethods.WM_NCHITTEST:
                m.Result = (IntPtr)NativeMethods.HTCLIENT;
                return;
            case NativeMethods.WM_LBUTTONDOWN:
                HandleLButtonDown();
                return;
            case NativeMethods.WM_LBUTTONDBLCLK:
                _lastClickTime = DateTime.MinValue;
                if (!string.IsNullOrEmpty(_cfg.Overlay.DoubleClickAction))
                    OnDoubleClick?.Invoke(_cfg.Overlay.DoubleClickAction);
                return;
            case NativeMethods.WM_RBUTTONUP:
            case NativeMethods.WM_CONTEXTMENU:
                Log.Debug("Right-click message received in WndProc");
                ShowOverlayContextMenu();
                return;
        }
        base.WndProc(ref m);
    }

    private static void HandleDeviceChange(IntPtr wParam)
    {
        int dbtEvent = wParam.ToInt32();
        if (dbtEvent is NativeMethods.DBT_DEVICEARRIVAL or NativeMethods.DBT_DEVICEREMOVECOMPLETE or NativeMethods.DBT_DEVNODES_CHANGED)
        {
            HardwareLifecycleManager.Instance.NotifyDevicePnPChanged();
        }
    }

    private void HandleLButtonDown()
    {
        var now = DateTime.UtcNow;
        uint doubleClickMaxMs = NativeMethods.GetDoubleClickTime();
        if (doubleClickMaxMs == 0) doubleClickMaxMs = 500;

        if ((now - _lastClickTime).TotalMilliseconds <= doubleClickMaxMs)
        {
            _lastClickTime = DateTime.MinValue;
            if (!string.IsNullOrEmpty(_cfg.Overlay.DoubleClickAction))
                OnDoubleClick?.Invoke(_cfg.Overlay.DoubleClickAction);
        }
        else
        {
            _lastClickTime = now;
        }
    }

    /// <summary>Fired to display the overlay context menu at the specified point.</summary>
    public Action<Point>? ShowContextMenuAction { get; set; }

    /// <summary>Callback to query if the overlay context menu is currently open.</summary>
    public Func<bool>? IsContextMenuOpen { get; set; }

    /// <summary>Callback to query the screen bounds of the overlay context menu.</summary>
    public Func<Rectangle>? GetContextMenuBounds { get; set; }

    /// <summary>Callback to close the overlay context menu on click-outside.</summary>
    public Action? CloseContextMenuAction { get; set; }

    private void ShowOverlayContextMenu()
    {
        var pos = Cursor.Position;
        Log.Debug($"Right-click detected at screen coordinates ({pos.X}, {pos.Y})");
        if (ShowContextMenuAction is null)
        {
            Log.Error("Cannot display context menu (ShowContextMenuAction is not configured)");
            return;
        }

        try
        {
            ShowContextMenuAction.Invoke(pos);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Exception occurred while invoking ShowContextMenuAction at ({pos.X}, {pos.Y})");
        }
    }

    private void WmPaint()
    {
        var ps = new NativeMethods.PAINTSTRUCT();
        var hdc = NativeMethods.BeginPaint(Handle, ref ps);
        if (hdc == IntPtr.Zero) return;
        try
        {
            using var g = Graphics.FromHdc(hdc);
            using var buf = BufferedGraphicsManager.Current.Allocate(g, new Rectangle(0, 0, _overlayWidth, _overlayHeight));
            buf.Graphics.Clear(_keyColor);
            DrawContent(buf.Graphics);
            buf.Render(g);
        }
        finally { NativeMethods.EndPaint(Handle, ref ps); }
    }

    private void HandleDpiChanged()
    {
        _dpi = GetDpi();
        UpdateKeyColor();
        var sz = MeasureSize();
        _overlayWidth = sz.Width;
        _overlayHeight = sz.Height;
        _embedder.Reposition(Handle, sz);
        if (_embedder.IsChildMode) Invalidate();
        else RenderFloat();
    }

    private void DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var raw = key?.GetValue("SystemUsesLightTheme", 0);
            bool isLight = raw is int val && val == 1;
            bool changed = isLight != _isLightTheme;
            _isLightTheme = isLight;

            if (changed)
                Log.Debug($"System color theme detected as {(isLight ? "light" : "dark")}");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to detect system color theme");
        }
    }

    private void ThemeTick()
    {
        if (SkipTimersForTesting) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var raw = key?.GetValue("SystemUsesLightTheme", 0);
            bool isLight = raw is int val && val == 1;
            if (isLight == _isLightTheme) return;

            Log.Info($"Windows color theme changed to {(isLight ? "light" : "dark")}; applying matching theme");
            _uiCtx.Post(_ =>
            {
                if (_disposed) return;
                RefreshOverlay();
            }, null);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to read system theme from registry during periodic check");
        }
    }

    private void UpdateKeyColor()
    {
        try
        {
            string hex;
            if (_cfg.Overlay.FollowWindowsTheme && _isLightTheme)
            {
                hex = "f0f0f0";
            }
            else
            {
                hex = _cfg.Overlay.BgColor?.TrimStart('#') ?? "1c1b19";
            }

            var r = Convert.ToInt32(hex[..2], 16);
            var g = Convert.ToInt32(hex[2..4], 16);
            var b = Convert.ToInt32(hex[4..6], 16);
            _keyColor = Color.FromArgb(r, g, b);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to parse overlay transparency key color; using default fallback");
            _keyColor = Color.FromArgb(28, 27, 25);
        }
        _embedder.KeyColor = _keyColor;
        if (Handle != IntPtr.Zero)
            _embedder.UpdateLayeredSettings(Handle);
    }

    private void DrawContent(Graphics g)
    {
        g.Clear(_keyColor);

        if (_flash && DateTime.UtcNow < _flashEnd)
        {
            using var flashBrush = new SolidBrush(Color.FromArgb(60, 255, 80, 0));
            g.FillRectangle(flashBrush, 0, 0, _overlayWidth, _overlayHeight);
        }
        else
        {
            _flash = false;
        }
        DrawItems(g);
    }

    private void DrawItems(Graphics g)
    {
        var oc = _cfg.Overlay;
        var mc = _cfg.Monitoring;

        var font = BuildFont();
        Color labelColor = _isLightTheme ? Color.Black : Color.White;
        var drawFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        int rowH = TextRenderer.MeasureText(g, "Ay↓g", font, Size.Empty, drawFlags).Height + 6;
        int colCount = (oc.Row1.Count + 1) / 2;
        bool hasRow2 = oc.Row1.Count > 1;
        var cols = BuildColumns(g, font, drawFlags, oc, mc, colCount);
        if (cols.Count == 0) return;

        int vPad = (int)(8 * _dpi + 0.5f);
        int wndH = hasRow2 ? rowH * 2 + RowGap + vPad * 2 : rowH + vPad * 2;
        int gap = (int)(ItemSpace * _dpi + 0.5f);

        int yTop, yBot;
        if (hasRow2)
        {
            int topMargin = (int)(wndH * 0.15f + 0.5f);
            int botMargin = (int)(wndH * 0.15f + 0.5f);
            int availH = wndH - topMargin - botMargin;
            yTop = topMargin + (availH - rowH - rowH - RowGap) / 2;
            yBot = yTop + rowH + RowGap;
        }
        else
        {
            int topMargin = (int)(wndH * 0.15f + 0.5f);
            int availH = wndH - topMargin;
            yTop = topMargin + (availH - rowH) / 2;
            yBot = 0;
        }

        int x = gap;
        foreach (var col in cols)
        {
            int colW = col.FullW;
            int colonEndX = x + col.LabelPartPx + col.ColonW;

            if (col.TopOn)
            {
                TextRenderer.DrawText(g, col.TopLabel, font, new Rectangle(x, yTop, col.LabelPartPx, rowH), labelColor, drawFlags);
                TextRenderer.DrawText(g, ":", font, new Rectangle(x + col.LabelPartPx, yTop, col.ColonW, rowH), labelColor, drawFlags);
                var vb = MakeValueBrush(col.TopId, oc);
                TextRenderer.DrawText(g, " " + col.TopVal, font, new Rectangle(colonEndX, yTop, col.TopValW, rowH), vb.Color, drawFlags);
            }

            if (col.BotOn)
            {
                TextRenderer.DrawText(g, col.BotLabel, font, new Rectangle(x, yBot, col.LabelPartPx, rowH), labelColor, drawFlags);
                TextRenderer.DrawText(g, ":", font, new Rectangle(x + col.LabelPartPx, yBot, col.ColonW, rowH), labelColor, drawFlags);
                var vb = MakeValueBrush(col.BotId, oc);
                TextRenderer.DrawText(g, " " + col.BotVal, font, new Rectangle(colonEndX, yBot, col.BotValW, rowH), vb.Color, drawFlags);
            }

            x += colW + gap;
        }
    }

    private sealed record ColumnInfo(
        string TopId, string BotId, bool TopOn, bool BotOn,
        string TopLabel, string TopVal, string BotLabel, string BotVal,
        int TopValW, int BotValW,
        int FullW, int LabelPartPx, int ColonW);

    private List<ColumnInfo> BuildColumns(Graphics g, Font font, TextFormatFlags flags,
        OverlayConfig oc, MonitoringConfig mc, int colCount)
    {
        var cols = new List<ColumnInfo>();
        var rawParts = CollectRawParts(oc, mc, colCount);
        if (rawParts.Count == 0) return cols;

        int[] maxLenPerCol = new int[colCount];
        foreach (var rp in rawParts)
        {
            if (rp.rawPart.Length > maxLenPerCol[rp.colIdx])
                maxLenPerCol[rp.colIdx] = rp.rawPart.Length;
        }

        var topEntryByCol = new (int colIdx, bool isTop, string id, string rawPart)[colCount];
        var botEntryByCol = new (int colIdx, bool isTop, string id, string rawPart)[colCount];
        foreach (var rp in rawParts)
        {
            if (rp.isTop)
                topEntryByCol[rp.colIdx] = rp;
            else
                botEntryByCol[rp.colIdx] = rp;
        }

        int colonW = TextRenderer.MeasureText(g, ":", font, Size.Empty, flags).Width;
        var ctx = new RenderMeasureContext(g, font, flags, mc, colonW);
        for (int i = 0; i < colCount; i++)
        {
            var topEntry = topEntryByCol[i];
            var botEntry = botEntryByCol[i];
            if (topEntry.rawPart == null && botEntry.rawPart == null) continue;

            cols.Add(BuildSingleColumn(ctx, topEntry, botEntry, maxLenPerCol[i]));
        }
        return cols;
    }

    private readonly record struct RenderMeasureContext(Graphics G, Font Font, TextFormatFlags Flags, MonitoringConfig Mc, int ColonW);

    private List<(int colIdx, bool isTop, string id, string rawPart)> CollectRawParts(
        OverlayConfig oc, MonitoringConfig mc, int colCount)
    {
        var rawParts = new List<(int colIdx, bool isTop, string id, string rawPart)>();
        int total = oc.Row1.Count;

        for (int i = 0; i < colCount; i++)
        {
            int topIdx = i * 2;
            int botIdx = i * 2 + 1;
            string topId = topIdx < total ? oc.Row1[topIdx] : "";
            string botId = botIdx < total ? oc.Row1[botIdx] : "";

            if (!string.IsNullOrEmpty(topId) && IsMetricEnabled(topId, mc) && IsMetricAvailable(topId, _snap))
                rawParts.Add((i, true, topId, ResolveMetricLabel(topId)));

            if (!string.IsNullOrEmpty(botId) && IsMetricEnabled(botId, mc) && IsMetricAvailable(botId, _snap))
                rawParts.Add((i, false, botId, ResolveMetricLabel(botId)));
        }

        return rawParts;
    }

    private string ResolveMetricLabel(string metricId)
    {
        Metrics.AllMetricsById.TryGetValue(metricId, out var desc);
        return desc is not null && desc.Group == "disk"
            ? GetDiskLabel(metricId)
            : desc?.ShortLabel ?? "???";
    }

    private ColumnInfo BuildSingleColumn(
        in RenderMeasureContext ctx,
        (int colIdx, bool isTop, string id, string rawPart) topEntry,
        (int colIdx, bool isTop, string id, string rawPart) botEntry,
        int maxLen)
    {
        string topId = topEntry.rawPart != null ? topEntry.id : "";
        string botId = botEntry.rawPart != null ? botEntry.id : "";
        bool topOn = topEntry.rawPart != null;
        bool botOn = botEntry.rawPart != null;

        string topLabel = string.Empty, topVal = string.Empty, botLabel = string.Empty, botVal = string.Empty;
        int topValW = 0, botValW = 0, labelPartPx = 0;

        if (topOn)
        {
            topLabel = topEntry.rawPart!.PadRight(maxLen);
            labelPartPx = TextRenderer.MeasureText(ctx.G, topLabel, ctx.Font, Size.Empty, ctx.Flags).Width;
            topVal = Metrics.Format(topId, _snap, ctx.Mc.UseNetBits, ctx.Mc.UseFahrenheit);
            int topReservedW = TextRenderer.MeasureText(ctx.G, " " + Metrics.WidestValueString(topId, ctx.Mc.UseNetBits, ctx.Mc.UseFahrenheit), ctx.Font, Size.Empty, ctx.Flags).Width;
            int topActualW = TextRenderer.MeasureText(ctx.G, " " + topVal, ctx.Font, Size.Empty, ctx.Flags).Width;
            topValW = Math.Max(topReservedW, topActualW);
        }

        if (botOn)
        {
            botLabel = botEntry.rawPart!.PadRight(maxLen);
            int lblPx = TextRenderer.MeasureText(ctx.G, botLabel, ctx.Font, Size.Empty, ctx.Flags).Width;
            labelPartPx = topOn ? Math.Max(labelPartPx, lblPx) : lblPx;
            botVal = Metrics.Format(botId, _snap, ctx.Mc.UseNetBits, ctx.Mc.UseFahrenheit);
            int botReservedW = TextRenderer.MeasureText(ctx.G, " " + Metrics.WidestValueString(botId, ctx.Mc.UseNetBits, ctx.Mc.UseFahrenheit), ctx.Font, Size.Empty, ctx.Flags).Width;
            int botActualW = TextRenderer.MeasureText(ctx.G, " " + botVal, ctx.Font, Size.Empty, ctx.Flags).Width;
            botValW = Math.Max(botReservedW, botActualW);
        }

        int fullW = labelPartPx + ctx.ColonW + Math.Max(topOn ? topValW : 0, botOn ? botValW : 0);

        return new(topId, botId, topOn, botOn, topLabel, topVal, botLabel, botVal,
            topValW, botValW, fullW, labelPartPx, ctx.ColonW);
    }

    private string GetDiskLabel(string id)
    {
        int last = id.LastIndexOf('_');
        if (last <= 5) return _snap.PrimaryDiskType;

        string diskId = id[5..last];
        string aspect = id[(last + 1)..];
        string suffix = aspect == "read" ? "R" : "W";
        int idx = diskId.StartsWith("disk") && int.TryParse(diskId[4..], out int di) ? di : -1;

        if (idx >= 0 && idx < _snap.Disks.Count && !string.IsNullOrEmpty(_snap.Disks[idx].BusType))
        {
            string baseLabel = GetDiskBaseLabel(_snap.Disks[idx].BusType);
            var (sameTypeCount, typePos) = CountDisksOfSameType(baseLabel, idx);
            string num = sameTypeCount > 1 ? typePos.ToString() : "";
            return $"{baseLabel}{num}{suffix}";
        }

        return $"{diskId.ToUpperInvariant()}{suffix}";
    }

    private static string GetDiskBaseLabel(string rawType)
    {
        if (rawType.Contains("NVMe", StringComparison.OrdinalIgnoreCase)) return "NVMe";
        if (rawType.Contains("HDD", StringComparison.OrdinalIgnoreCase)) return "HDD";
        if (rawType.Contains("SATA", StringComparison.OrdinalIgnoreCase)) return "SATA";
        return rawType;
    }

    private (int sameTypeCount, int typePos) CountDisksOfSameType(string baseLabel, int targetIdx)
    {
        int sameTypeCount = 0;
        int typePos = 0;
        for (int di = 0; di < _snap.Disks.Count; di++)
        {
            string otherBase = GetDiskBaseLabel(_snap.Disks[di].BusType);
            if (otherBase.Equals(baseLabel, StringComparison.OrdinalIgnoreCase))
            {
                sameTypeCount++;
                if (di <= targetIdx) typePos++;
            }
        }
        return (sameTypeCount, typePos);
    }

    private static bool IsMetricEnabled(string id, MonitoringConfig mc) => id switch
    {
        Metrics.CpuPct or Metrics.CpuFreq or Metrics.CpuPower or Metrics.CpuTemp => mc.EnableCpu,
        Metrics.RamPct or Metrics.RamUsed => mc.EnableRam,
        Metrics.GpuPct or Metrics.GpuTemp or Metrics.GpuVram or Metrics.GpuFreq or Metrics.GpuPower => mc.EnableGpu,
        Metrics.NetDown or Metrics.NetUp or Metrics.NetTotal => mc.EnableNet,
        Metrics.BatteryPct or Metrics.BatteryRate => mc.EnableBattery,
        string s when s.StartsWith("disk_") => mc.EnableDisk,
        _ => true,
    };

    private static bool IsMetricAvailable(string id, TelemetrySnapshot snap) => id switch
    {
        Metrics.BatteryPct or Metrics.BatteryRate => snap.BatteryPct > 0,
        _ => true,
    };

    private static Color AdjustForTheme(Color c, bool isLightTheme)
    {
        float lum = 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

        if (isLightTheme)
        {
            if (lum > 160f)
            {
                float t = (lum - 160f) / (255f - 160f) * 0.45f;
                return Color.FromArgb(
                    (int)(c.R * (1f - t)),
                    (int)(c.G * (1f - t)),
                    (int)(c.B * (1f - t)));
            }
        }
        else
        {
            if (lum < 80f)
            {
                float t = (80f - lum) / 80f * 0.5f;
                return Color.FromArgb(
                    (int)(c.R * (1f - t) + 255f * t),
                    (int)(c.G * (1f - t) + 255f * t),
                    (int)(c.B * (1f - t) + 255f * t));
            }
        }

        return c;
    }

    private SolidBrush MakeValueBrush(string id, OverlayConfig oc)
    {
        string hex = oc.MetricColors.TryGetValue(id, out var userColor) ? userColor : oc.ValueColor;
        var c = AdjustForTheme(WindowHelpers.ParseHex(hex, Color.White), _isLightTheme);
        int argb = c.ToArgb();
        if (_brushCache.TryGetValue(argb, out var cached))
            return cached;
        var brush = new SolidBrush(c);
        _brushCache[argb] = brush;
        return brush;
    }

    private void RenderFloat()
    {
        if (Handle == IntPtr.Zero || !Visible || !_cfg.Overlay.Visible || _overlayWidth <= 0 || _overlayHeight <= 0) return;
        if (SkipShowWindowForTesting) return;
        Invalidate();
    }

    private Size MeasureSize()
    {
        var oc = _cfg.Overlay;
        var mc = _cfg.Monitoring;

        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        var font = BuildFont();

        int colCount = (oc.Row1.Count + 1) / 2;
        bool hasRow2 = oc.Row1.Count > 1;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var cols = BuildColumns(g, font, flags, oc, mc, colCount);
        int gap = (int)(ItemSpace * _dpi + 0.5f);
        int w = gap;
        foreach (var col in cols)
            w += col.FullW + gap;

        // Height = measured row height + vertical padding
        int rowH = TextRenderer.MeasureText(g, "Ay↓g", font, Size.Empty, flags).Height + 2;
        int vPad = (int)(8 * _dpi + 0.5f);
        int h = hasRow2
            ? rowH * 2 + RowGap + vPad * 2
            : rowH + vPad * 2;

        return new Size(Math.Max(w, 10), Math.Max(h, 12));
    }

    private Font BuildFont()
    {
        var oc = _cfg.Overlay;
        float physPx = oc.FontSizePx * _dpi * (oc.ScalePct / 100f);
        string key = $"Calibri|{physPx:F1}|{oc.FontBold}";
        if (key == _fontCacheKey && _cachedFont is not null)
            return _cachedFont;
        _cachedFont?.Dispose();
        var style = oc.FontBold ? FontStyle.Bold : FontStyle.Regular;
        _cachedFont = new Font("Calibri", physPx, style, GraphicsUnit.Pixel);
        _fontCacheKey = key;
        return _cachedFont;
    }

    private void Invalidate()
    {
        if (Handle == IntPtr.Zero) return;
        NativeMethods.InvalidateRect(Handle, IntPtr.Zero, false);
        NativeMethods.UpdateWindow(Handle);
    }

    private float GetDpi()
    {
        if (Handle == IntPtr.Zero) return 1f;
        var dpi = NativeMethods.GetDpiForWindow(Handle);
        return dpi > 0 ? dpi / 96f : 1f;
    }

    private void PosTick()
    {
        if (SkipTimersForTesting) return;
        if (_disposed || Handle == IntPtr.Zero || !Visible) return;
        if (!_embedder.IsEmbedded || _embedder.State == OverlayDeploymentState.Failed) return;

        _uiCtx.Post(_ =>
        {
            if (_disposed || Handle == IntPtr.Zero) return;
            float dpi = GetDpi();
            if (MathF.Abs(dpi - _dpi) > 0.001f)
            {
                HandleDpiChanged();
                return;
            }
            var sz = MeasureSize();
            if (sz.Width != _overlayWidth || sz.Height != _overlayHeight)
            {
                _overlayWidth = sz.Width;
                _overlayHeight = sz.Height;
            }
            _embedder.Reposition(Handle, sz);
        }, null);
    }

    private void EnsureWindowCreated()
    {
        if (Handle != IntPtr.Zero && NativeMethods.IsWindow(Handle))
        {
            return;
        }

        if (Handle != IntPtr.Zero)
        {
            try
            {
                DestroyHandle();
            }
            catch (Exception ex)
            {
                Log.Debug($"Resetting dead window handle: {ex.Message}");
            }
        }

        Log.Info("Creating taskbar overlay native window");
        var cp = new CreateParams
        {
            Caption = "LocalTelemetry_Overlay",
            ClassStyle = NativeMethods.CS_DBLCLKS,
            Style = NativeMethods.WS_POPUP,
            ExStyle = NativeMethods.WS_EX_TOOLWINDOW |
                NativeMethods.WS_EX_NOACTIVATE |
                NativeMethods.WS_EX_TOPMOST |
                NativeMethods.WS_EX_LAYERED,
            Width = _overlayWidth > 0 ? _overlayWidth : 200,
            Height = _overlayHeight > 0 ? _overlayHeight : 32,
            X = 0,
            Y = 0,
            Parent = IntPtr.Zero,
        };
        CreateHandle(cp);
        UpdateKeyColor();
    }

    /// <summary>
    /// Re-creates and re-embeds the overlay window if Explorer restarted, taskbar was recreated, or the native window was destroyed.
    /// </summary>
    /// <param name="reason">Diagnostic reason for the recovery attempt.</param>
    public void RecoverTaskbar(string reason)
    {
        if (_disposed || !_cfg.Overlay.Visible) return;

        Log.Info($"Re-embedding taskbar overlay due to {reason}");
        try
        {
            EnsureWindowCreated();

            var sz = MeasureSize();
            _overlayWidth = sz.Width;
            _overlayHeight = sz.Height;
            if (!SkipEmbedForTesting && Handle != IntPtr.Zero)
            {
                _embedder.OnTaskbarRecreated(Handle, sz);
            }
            if (!SkipShowWindowForTesting && Handle != IntPtr.Zero)
            {
                RefreshOverlay();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to recover taskbar overlay after explorer restart");
        }
    }

    private void WatchTick()
    {
        if (SkipTimersForTesting) return;
        if (!_cfg.Overlay.Visible || _disposed) return;

        var current = TaskbarEmbedder.FindExplorerTrayWnd();
        if (current == IntPtr.Zero) return;

        bool trayChanged = current != _lastTrayHwnd;
        bool windowDead = Handle == IntPtr.Zero || !NativeMethods.IsWindow(Handle);

        if (!trayChanged && !windowDead) return;
        _lastTrayHwnd = current;

        _uiCtx.Post(_ =>
        {
            if (_disposed) return;
            RecoverTaskbar(trayChanged ? "taskbar window change" : "taskbar window recovery");
        }, null);
    }

    private void StopTimers()
    {
        using var w = _watchTimer;
        using var p = _posTimer;
        using var t = _themeTimer;
        _watchTimer = null;
        _posTimer = null;
        _themeTimer = null;
    }

    /// <summary>
    /// Releases all resources used by the overlay, stops timers, detaches from the taskbar and destroys the native window.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Visible = false;
        StopMouseHook();
        StopTimers();
        _cachedFont?.Dispose();
        foreach (var b in _brushCache.Values) b.Dispose();
        _brushCache.Clear();

        if (Handle != IntPtr.Zero)
        {
            if (!SkipEmbedForTesting)
                _embedder.Detach(Handle);
            DestroyHandle();
        }
    }

    private void StartMouseHook()
    {
        // Global mouse hook is intentionally replaced by embedded window message handling.
    }

    private void StopMouseHook()
    {
        // Global mouse hook cleanup is intentionally a no-op when inactive.
    }

    internal Rectangle GetScreenBounds()
    {
        if (Handle == IntPtr.Zero || _overlayWidth <= 0 || _overlayHeight <= 0) return Rectangle.Empty;
        var pt = new NativeMethods.POINT { X = 0, Y = 0 };
        NativeMethods.ClientToScreen(Handle, ref pt);
        return new Rectangle(pt.X, pt.Y, _overlayWidth, _overlayHeight);
    }

    internal IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            HandleOutsideContextMenuClick(msg, lParam);

            if (TryHandleOverlayClick(msg, lParam, out var handledResult))
            {
                return handledResult;
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void HandleOutsideContextMenuClick(int msg, IntPtr lParam)
    {
        bool isButtonDown = msg is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN
            or NativeMethods.WM_NCLBUTTONDOWN or NativeMethods.WM_NCRBUTTONDOWN or NativeMethods.WM_NCMBUTTONDOWN;

        if (isButtonDown && IsContextMenuOpen?.Invoke() == true)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            var clickPt = new Point(hookStruct.pt.X, hookStruct.pt.Y);
            var bounds = GetContextMenuBounds?.Invoke() ?? Rectangle.Empty;
            if (!bounds.Contains(clickPt))
            {
                CloseContextMenuAction?.Invoke();
            }
        }
    }

    private bool TryHandleOverlayClick(int msg, IntPtr lParam, out IntPtr handledResult)
    {
        handledResult = IntPtr.Zero;
        if (!Visible || !_cfg.Overlay.Visible || Handle == IntPtr.Zero)
        {
            return false;
        }

        var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        var pt = new Point(hookStruct.pt.X, hookStruct.pt.Y);
        return GetScreenBounds().Contains(pt) && HandleOverlayMouseMessage(msg, out handledResult);
    }

    internal bool HandleOverlayMouseMessage(int msg, out IntPtr result)
    {
        result = IntPtr.Zero;

        if (msg is NativeMethods.WM_SETCURSOR or NativeMethods.WM_MOUSEMOVE)
        {
            NativeMethods.SetCursor(NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_ARROW));
            return false;
        }

        if (msg == NativeMethods.WM_RBUTTONDOWN)
        {
            result = (IntPtr)1;
            return true;
        }

        if (msg == NativeMethods.WM_RBUTTONUP)
        {
            Log.Debug("Mouse hook captured right-click release (WM_RBUTTONUP) on overlay");
            _uiCtx.Post(_ => ShowOverlayContextMenu(), null);
            result = (IntPtr)1;
            return true;
        }

        if (msg == NativeMethods.WM_LBUTTONDBLCLK)
        {
            _uiCtx.Post(_ =>
            {
                if (!string.IsNullOrEmpty(_cfg.Overlay.DoubleClickAction))
                    OnDoubleClick?.Invoke(_cfg.Overlay.DoubleClickAction);
            }, null);
            result = (IntPtr)1;
            return true;
        }

        return false;
    }
}
