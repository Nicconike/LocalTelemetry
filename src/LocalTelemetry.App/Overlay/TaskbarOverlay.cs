using System.Runtime.Versioning;
using LocalTelemetry.App.Win32;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Diagnostics;
using LocalTelemetry.Core.Models;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<TaskbarOverlay> _log;
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

    private Bitmap? _renderBmp;
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

    /// <summary>Fired on double-click when <see cref="AppSettings.Overlay.DoubleClickAction"/> is set to a non-"none" action.</summary>
    public Action<string>? OnDoubleClick { get; set; }

    /// <summary>
    /// Initialises a new overlay instance. The window is not created until <see cref="Show"/> is called.
    /// </summary>
    /// <param name="cfg">Application settings used for layout, colors and behaviour.</param>
    /// <param name="embedder">Taskbar embedding strategy (child-window or layered float).</param>
    /// <param name="log">Logger for diagnostic output.</param>
    public TaskbarOverlay(AppSettings cfg, TaskbarEmbedder embedder, ILogger<TaskbarOverlay> log)
    {
        _cfg = cfg;
        _embedder = embedder;
        _log = log;
        _uiCtx = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        UpdateKeyColor();
    }

    /// <summary>Creates (or re-shows) the overlay window and embeds it in the taskbar.</summary>
    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Handle != IntPtr.Zero)
        {
            if (!Visible)
            {
                Log.Info("Show: re-showing hidden window");
                Visible = true;
                var resz = MeasureSize();
                _overlayWidth = resz.Width;
                _overlayHeight = resz.Height;
                _embedder.Embed(Handle, resz);
                NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
                RefreshOverlay();
            }
            return;
        }

        Log.Info("Show: creating new window");
        var cp = new CreateParams
        {
            Caption = string.Empty,
            ClassStyle = NativeMethods.CS_DBLCLKS,
            Style = NativeMethods.WS_POPUP,
            ExStyle = NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
            Width = _overlayWidth,
            Height = _overlayHeight,
            X = 0,
            Y = 0,
            Parent = IntPtr.Zero,
        };
        CreateHandle(cp);

        _dpi = GetDpi();
        Log.Info($"Show: FollowWindowsTheme={_cfg.Overlay.FollowWindowsTheme}");
        if (_cfg.Overlay.FollowWindowsTheme)
        {
            DetectSystemTheme();
            _themeTimer = new System.Threading.Timer(_ => ThemeTick(), null, 2_000, 2_000);
            Log.Info("Show: theme polling timer started (2s interval)");
        }
        UpdateKeyColor();
        var sz = MeasureSize();
        _overlayWidth = sz.Width;
        _overlayHeight = sz.Height;

        _embedder.Embed(Handle, sz);

        _lastTrayHwnd = TaskbarEmbedder.FindExplorerTrayWnd();
        _watchTimer = new System.Threading.Timer(_ => WatchTick(), null, 5_000, 5_000);
        _posTimer = new System.Threading.Timer(_ => PosTick(), null, 200, 200);

        Visible = true;
        Log.Info($"Show: done, sz={sz.Width}x{sz.Height}, " +
            $"IsChildMode={_embedder.IsChildMode}, IsFallback={_embedder.IsFallback}");
    }

    /// <summary>Hides the overlay window without destroying it.</summary>
    public void Hide()
    {
        Log.Info($"Hide: Handle={(long)Handle:X}");
        if (Handle == IntPtr.Zero) return;
        Visible = false;
        NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
        _embedder.Detach(Handle);
    }

    /// <summary>
    /// Updates the snapshot data and queues a UI-thread redraw when values have changed
    /// beyond display precision.
    /// </summary>
    /// <param name="snap">The latest telemetry snapshot.</param>
    public void UpdateSnapshot(TelemetrySnapshot snap)
    {
        if (!_cfg.Overlay.Visible || _disposed) return;
        _snap = snap;
        if (Handle == IntPtr.Zero) return;

        _uiCtx.Post(_ =>
        {
            try
            {
                if (_disposed || Handle == IntPtr.Zero) return;
                if (!ValuesChanged(snap, _lastRenderedSnap)) return;

                var sz = MeasureSize();
                if (sz.Width != _overlayWidth || sz.Height != _overlayHeight)
                {
                    _overlayWidth = sz.Width;
                    _overlayHeight = sz.Height;
                    _embedder.Reposition(Handle, sz);
                }

                if (_embedder.IsChildMode)
                    Invalidate();
                else
                    RenderFloat();

                _lastRenderedSnap = snap;
            }
            catch (Exception ex) { Log.Error($"UpdateSnapshot render error: {ex.Message}"); }
        }, null);
    }

    private static bool ValuesChanged(TelemetrySnapshot a, TelemetrySnapshot b)
    {
        // Compare at display precision to avoid redraw on sub-threshold noise
        if (MathF.Round(a.CpuUsagePct) != MathF.Round(b.CpuUsagePct)) return true;
        if (MathF.Round(a.CpuTempPackageC) != MathF.Round(b.CpuTempPackageC)) return true;
        if (MathF.Round(a.CpuFreqGhz, 2) != MathF.Round(b.CpuFreqGhz, 2)) return true;
        if (MathF.Round(a.CpuPackagePowerW) != MathF.Round(b.CpuPackagePowerW)) return true;
        if (MathF.Round(a.RamUsagePct) != MathF.Round(b.RamUsagePct)) return true;
        if (MathF.Round(a.RamUsedGb, 1) != MathF.Round(b.RamUsedGb, 1)) return true;
        if (MathF.Round(a.GpuUsagePct) != MathF.Round(b.GpuUsagePct)) return true;
        if (MathF.Round(a.GpuTempC) != MathF.Round(b.GpuTempC)) return true;
        if (MathF.Round(a.GpuVramUsedMb) != MathF.Round(b.GpuVramUsedMb)) return true;
        if (MathF.Round(a.GpuFreqMHz) != MathF.Round(b.GpuFreqMHz)) return true;
        if (MathF.Round(a.GpuPowerW) != MathF.Round(b.GpuPowerW)) return true;
        if (Math.Abs(a.NetDownBps - b.NetDownBps) > 100) return true;
        if (Math.Abs(a.NetUpBps - b.NetUpBps) > 100) return true;
        if (a.NetTotalBytes != b.NetTotalBytes) return true;
        if (MathF.Round(a.DiskReadMbps, 1) != MathF.Round(b.DiskReadMbps, 1)) return true;
        if (MathF.Round(a.DiskWriteMbps, 1) != MathF.Round(b.DiskWriteMbps, 1)) return true;
        if (a.NetInterfaceName != b.NetInterfaceName) return true;
        return false;
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
        if (!_refreshLogged) { _refreshLogged = true; Log.Info("RefreshOverlay"); }
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
            case NativeMethods.WM_PAINT:
                WmPaint();
                return;
            case NativeMethods.WM_ERASEBKGND:
                return;
            case NativeMethods.WM_DPICHANGED:
                HandleDpiChanged();
                return;
            case NativeMethods.WM_TABLET_QUERYSYSTEMGESTURESTATUS:
                m.Result = IntPtr.Zero;
                return;
            case NativeMethods.WM_NCHITTEST:
                m.Result = (IntPtr)NativeMethods.HTCLIENT;
                return;
            case NativeMethods.WM_LBUTTONDOWN:
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
                return;
            case NativeMethods.WM_LBUTTONDBLCLK:
                _lastClickTime = DateTime.MinValue;
                if (!string.IsNullOrEmpty(_cfg.Overlay.DoubleClickAction))
                    OnDoubleClick?.Invoke(_cfg.Overlay.DoubleClickAction);
                return;
        }
        base.WndProc(ref m);
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

    private bool DetectSystemTheme()
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
                Log.Info($"DetectSystemTheme: changed to {(isLight ? "light" : "dark")}");
            else
                _log.LogDebug("DetectSystemTheme: isLight={IsLight}", isLight);

            return changed;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "DetectSystemTheme failed");
            return false;
        }
    }

    private void ThemeTick()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var raw = key?.GetValue("SystemUsesLightTheme", 0);
            bool isLight = raw is int val && val == 1;
            if (isLight == _isLightTheme)
            {
                _log.LogDebug("ThemeTick: no change (isLight={IsLight})", isLight);
                return;
            }

            Log.Info($"ThemeTick: theme change detected (isLight={isLight}, was={_isLightTheme}), scheduling RefreshOverlay");
            _uiCtx.Post(_ =>
            {
                if (_disposed) return;
                RefreshOverlay();
            }, null);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "ThemeTick: registry read failed");
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
            _log.LogDebug(ex, "UpdateKeyColor parsing failed, using fallback");
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

        using var font = BuildFont();
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
        int total = oc.Row1.Count;

        // Phase 1: collect raw text parts
        var rawParts = new List<(int colIdx, bool isTop, string id, string rawPart)>();

        for (int i = 0; i < colCount; i++)
        {
            int topIdx = i * 2;
            int botIdx = i * 2 + 1;
            string topId = topIdx < total ? oc.Row1[topIdx] : "";
            string botId = botIdx < total ? oc.Row1[botIdx] : "";
            bool topOn = !string.IsNullOrEmpty(topId) && IsMetricEnabled(topId, mc) && IsMetricAvailable(topId, _snap);
            bool botOn = !string.IsNullOrEmpty(botId) && IsMetricEnabled(botId, mc) && IsMetricAvailable(botId, _snap);
            if (!topOn && !botOn) continue;

            if (topOn)
            {
                Metrics.AllMetricsById.TryGetValue(topId, out var desc);
                string part = desc is not null && desc.Group == "disk"
                    ? GetDiskLabel(topId)
                    : desc?.ShortLabel ?? "???";
                rawParts.Add((i, true, topId, part));
            }
            if (botOn)
            {
                Metrics.AllMetricsById.TryGetValue(botId, out var desc);
                string part = desc is not null && desc.Group == "disk"
                    ? GetDiskLabel(botId)
                    : desc?.ShortLabel ?? "???";
                rawParts.Add((i, false, botId, part));
            }
        }

        if (rawParts.Count == 0) return cols;

        // Phase 1.5: compute max char length per column for label padding
        int[] maxLenPerCol = new int[colCount];
        foreach (var rp in rawParts)
        {
            int len = rp.rawPart.Length;
            if (len > maxLenPerCol[rp.colIdx])
                maxLenPerCol[rp.colIdx] = len;
        }

        // Phase 2: build column layout - label and colon drawn separately
        var topEntryByCol = new (int colIdx, bool isTop, string id, string rawPart)[colCount];
        var botEntryByCol = new (int colIdx, bool isTop, string id, string rawPart)[colCount];
        for (int ri = 0; ri < rawParts.Count; ri++)
        {
            var rp = rawParts[ri];
            if (rp.isTop)
                topEntryByCol[rp.colIdx] = rp;
            else
                botEntryByCol[rp.colIdx] = rp;
        }
        int colonW = TextRenderer.MeasureText(g, ":", font, Size.Empty, flags).Width;
        for (int i = 0; i < colCount; i++)
        {
            var (colIdx, isTop, id, rawPart) = topEntryByCol[i];
            var botEntry = botEntryByCol[i];
            if (rawPart == null && botEntry.rawPart == null) continue;

            string topId = rawPart != null ? id : "";
            string botId = botEntry.rawPart != null ? botEntry.id : "";
            bool topOn = rawPart != null;
            bool botOn = botEntry.rawPart != null;

            string topLabel = string.Empty;
            string topVal = string.Empty;
            string botLabel = string.Empty;
            string botVal = string.Empty;
            int topValW = 0, botValW = 0, labelPartPx = 0;
            int maxLen = maxLenPerCol[i];

            if (topOn)
            {
                topLabel = rawPart!.PadRight(maxLen);
                labelPartPx = TextRenderer.MeasureText(g, topLabel, font, Size.Empty, flags).Width;
                topVal = Metrics.Format(topId, _snap, mc.UseNetBits, mc.UseFahrenheit);
                topValW = TextRenderer.MeasureText(g, " " + GetMaxValueString(topId), font, Size.Empty, flags).Width;
            }
            if (botOn)
            {
                botLabel = botEntry.rawPart!.PadRight(maxLen);
                int lblPx = TextRenderer.MeasureText(g, botLabel, font, Size.Empty, flags).Width;
                labelPartPx = topOn ? Math.Max(labelPartPx, lblPx) : lblPx;
                botVal = Metrics.Format(botId, _snap, mc.UseNetBits, mc.UseFahrenheit);
                botValW = TextRenderer.MeasureText(g, " " + GetMaxValueString(botId), font, Size.Empty, flags).Width;
            }

            int fullW = labelPartPx + colonW + Math.Max(
                topOn ? topValW : 0,
                botOn ? botValW : 0);

            cols.Add(new(topId, botId, topOn, botOn, topLabel, topVal, botLabel, botVal,
                topValW, botValW, fullW, labelPartPx, colonW));
        }
        return cols;
    }

    private string GetDiskLabel(string id)
    {
        int last = id.LastIndexOf('_');
        if (last > 5)
        {
            string diskId = id[5..last];
            string aspect = id[(last + 1)..];
            string suffix = aspect == "read" ? "R" : "W";
            int idx = diskId.StartsWith("disk") && int.TryParse(diskId[4..], out int di) ? di : -1;
            if (idx >= 0 && idx < _snap.Disks.Count && !string.IsNullOrEmpty(_snap.Disks[idx].BusType))
            {
                string busType = _snap.Disks[idx].BusType;
                int sameTypeCount = 0;
                for (int di2 = 0; di2 < _snap.Disks.Count; di2++)
                {
                    if (_snap.Disks[di2].BusType == busType) sameTypeCount++;
                }
                string num = sameTypeCount > 1 ? (idx + 1).ToString() : "";
                return $"{busType}{num}{suffix}";
            }
            return $"{diskId.ToUpperInvariant()}{suffix}";
        }
        return _snap.PrimaryDiskType;
    }

    private static string GetMaxValueString(string id)
    {
        if (id.StartsWith("disk_"))
        {
            if (id.EndsWith("_read") || id.EndsWith("_write"))
                return "9.99GB/s";
            if (id.EndsWith("_temp"))
                return "100°C";
            return "100%";
        }
        return id switch
        {
            Metrics.CpuPct or Metrics.RamPct or Metrics.GpuPct
                or Metrics.BatteryPct => "100%",
            Metrics.CpuTemp or Metrics.GpuTemp => "100°C",
            Metrics.CpuFreq => "6.00GHz",
            Metrics.CpuPower or Metrics.GpuPower => "999W",
            Metrics.RamUsed => "100.0GB",
            Metrics.GpuVram => "32768MB",
            Metrics.GpuFreq => "3999MHz",
            Metrics.NetDown or Metrics.NetUp => "100.0GB/s",
            Metrics.NetTotal => "1000.00MB",
            Metrics.BatteryRate => "+99.9W",
            _ => "9999",
        };
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
        if (_embedder.IsChildMode) { Invalidate(); return; }

        // Float mode: always use per-window alpha (SetLayeredWindowAttributes) via WM_PAINT
        // instead of UpdateLayeredWindow, because per-pixel alpha windows skip WM_NCHITTEST
        // for background pixels regardless of click-through state.
        if (_embedder.IsFallback)
        {
            Invalidate();
            return;
        }

        if (_renderBmp is null || _renderBmp.Width != _overlayWidth || _renderBmp.Height != _overlayHeight)
        {
            _renderBmp?.Dispose();
            _renderBmp = new Bitmap(_overlayWidth, _overlayHeight,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        }
        using (var g = Graphics.FromImage(_renderBmp))
        {
            g.Clear(Color.Transparent);
            DrawContent(g);
        }
        PushBitmap(_renderBmp);
    }

    private void PushBitmap(Bitmap bmp)
    {
        var screenDC = NativeMethods.GetDC(IntPtr.Zero);
        var memDC = NativeMethods.CreateCompatibleDC(screenDC);
        var hBmp = bmp.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
        var old = NativeMethods.SelectObject(memDC, hBmp);

        try
        {
            var bounds = Bounds;
            var pDst = new NativeMethods.POINT { X = bounds.X, Y = bounds.Y };
            var sz = new NativeMethods.SIZE { cx = _overlayWidth, cy = _overlayHeight };
            var pSrc = new NativeMethods.POINT { X = 0, Y = 0 };
            var bf = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = (byte)(_cfg.Overlay.Opacity * 255 / 100),
                AlphaFormat = NativeMethods.AC_SRC_ALPHA,
            };

            NativeMethods.UpdateLayeredWindow(
                Handle, screenDC, ref pDst, ref sz,
                memDC, ref pSrc, 0, ref bf, NativeMethods.ULW_ALPHA);
        }
        finally
        {
            NativeMethods.SelectObject(memDC, old);
            NativeMethods.DeleteObject(hBmp);
            NativeMethods.DeleteDC(memDC);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDC);
        }
    }

    private Size MeasureSize()
    {
        var oc = _cfg.Overlay;
        var mc = _cfg.Monitoring;

        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        using var font = BuildFont();

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
    }

    private float GetDpi()
    {
        if (Handle == IntPtr.Zero) return 1f;
        var dpi = NativeMethods.GetDpiForWindow(Handle);
        return dpi > 0 ? dpi / 96f : 1f;
    }

    private Rectangle Bounds
    {
        get
        {
            if (Handle == IntPtr.Zero) return Rectangle.Empty;
            NativeMethods.GetWindowRect(Handle, out var r);
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }
    }

    private void PosTick()
    {
        if (_disposed || Handle == IntPtr.Zero || !Visible) return;
        if (!_embedder.IsChildMode) return;

        _uiCtx.Post(_ =>
        {
            if (_disposed || Handle == IntPtr.Zero) return;
            var sz = MeasureSize();
            if (sz.Width != _overlayWidth || sz.Height != _overlayHeight)
            {
                _overlayWidth = sz.Width;
                _overlayHeight = sz.Height;
            }
            _embedder.Reposition(Handle, sz);
        }, null);
    }

    private void WatchTick()
    {
        var current = TaskbarEmbedder.FindExplorerTrayWnd();
        if (current == IntPtr.Zero || current == _lastTrayHwnd) return;
        _lastTrayHwnd = current;

        if (Handle == IntPtr.Zero) return;
        _uiCtx.Post(_ =>
        {
            if (_disposed || Handle == IntPtr.Zero) return;
            _embedder.OnTaskbarRecreated(Handle, new Size(_overlayWidth, _overlayHeight));
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
        StopTimers();
        _renderBmp?.Dispose();
        _cachedFont?.Dispose();
        foreach (var b in _brushCache.Values) b.Dispose();
        _brushCache.Clear();

        if (Handle != IntPtr.Zero)
        {
            _embedder.Detach(Handle);
            DestroyHandle();
        }
    }
}
