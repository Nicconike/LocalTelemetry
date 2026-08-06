using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LocalTelemetry.Core.Diagnostics;
using LocalTelemetry.Core.Models;

namespace LocalTelemetry.App.Tray;

/// <summary>
/// Manages the Windows notification area (system tray) icon, its context menu,
/// and tooltip updates driven by telemetry snapshots.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _tray;
    private readonly Icon _baseIcon;

    private ToolStripMenuItem? _overlayToggleItem;

    public event Action? OpenSettingsRequested;
    public event Action? QuitRequested;
    public event Action? ToggleOverlayRequested;

    // Constructor
    public TrayIconManager()
    {
        try
        {
            _baseIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        }
        catch (Exception ex)
        {
            Log.Error($"TrayIconManager: ExtractAssociatedIcon failed: {ex.Message}");
            _baseIcon = SystemIcons.Application;
        }

        _tray = new NotifyIcon
        {
            Text = "LocalTelemetry",
            Visible = true,
            Icon = _baseIcon,
        };

        BuildContextMenu();

        _tray.DoubleClick += (_, _) => OpenSettingsRequested?.Invoke();
    }

    /// <summary>Updates the checked state and text of the 'Show/Hide Overlay' menu item.</summary>
    public void SetOverlayVisible(bool visible)
    {
        if (_overlayToggleItem is not null)
        {
            _overlayToggleItem.Checked = visible;
            _overlayToggleItem.Text = visible ? "Hide Overlay" : "Show Overlay";
            Log.Info($"Tray: SetOverlayVisible({visible}) -> text='{_overlayToggleItem.Text}', checked={visible}");
        }
    }

    private void BuildContextMenu()
    {
        var ctx = new ContextMenuStrip
        {
            Renderer = new DarkTrayRenderer(),
            ShowImageMargin = false,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            BackColor = Color.FromArgb(28, 28, 30), // #1C1C1E sleek dark surface
            ForeColor = Color.FromArgb(235, 235, 245),
            Padding = new Padding(6, 6, 6, 6)
        };

        ctx.Opened += (_, _) =>
        {
            try
            {
                int cornerPref = 2; // DWMWCP_ROUND (Windows 11 Native Rounded Corners)
                DwmSetWindowAttribute(ctx.Handle, 33, ref cornerPref, sizeof(int));
                int darkMode = 1; // DWMWA_USE_IMMERSIVE_DARK_MODE
                DwmSetWindowAttribute(ctx.Handle, 20, ref darkMode, sizeof(int));
            }
            catch { }
        };

        var statusHeader = new ToolStripMenuItem("LocalTelemetry")
        {
            Enabled = false,
            ForeColor = Color.FromArgb(142, 142, 147), // Muted header label
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        _overlayToggleItem = new ToolStripMenuItem("Hide Overlay", null, (_, _) =>
        {
            Log.Info("Tray: toggle overlay item clicked");
            ToggleOverlayRequested?.Invoke();
        })
        {
            Checked = true,
            CheckOnClick = false,
            ForeColor = Color.White
        };

        var settingsItem = new ToolStripMenuItem("Open Settings", null, (_, _) => OpenSettingsRequested?.Invoke())
        {
            ForeColor = Color.White
        };

        var quitItem = new ToolStripMenuItem("Quit", null, (_, _) => QuitRequested?.Invoke())
        {
            ForeColor = Color.White
        };

        ctx.Items.Add(statusHeader);
        ctx.Items.Add(new ToolStripSeparator());
        ctx.Items.Add(_overlayToggleItem);
        ctx.Items.Add(settingsItem);
        ctx.Items.Add(new ToolStripSeparator());
        ctx.Items.Add(quitItem);

        _tray.ContextMenuStrip = ctx;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // Dark Menu Styling
    private sealed class DarkTrayRenderer : ToolStripProfessionalRenderer
    {
        public DarkTrayRenderer() : base(new TrayMenuTheme()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled) return;

            if (e.Item.Selected)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var brush = new SolidBrush(Color.FromArgb(44, 44, 46)); // #2C2C2E modern hover selection
                using var path = new System.Drawing.Drawing2D.GraphicsPath();
                int r = 6;
                var rect = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
                path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
                path.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90);
                path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
                path.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
                path.CloseFigure();
                e.Graphics.FillPath(brush, path);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(44, 44, 46), 1); // #2C2C2E divider line
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }
    }

    private sealed class TrayMenuTheme : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(28, 28, 30);
        public override Color MenuItemSelected => Color.FromArgb(44, 44, 46);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(44, 44, 46);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(44, 44, 46);
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuBorder => Color.FromArgb(56, 56, 58);
        public override Color SeparatorDark => Color.FromArgb(44, 44, 46);
        public override Color ImageMarginGradientBegin => Color.FromArgb(28, 28, 30);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(28, 28, 30);
        public override Color ImageMarginGradientEnd => Color.FromArgb(28, 28, 30);
    }

    // Updates & Notifications
    /// <summary>Updates the tray icon tooltip with CPU, GPU and RAM usage from the latest snapshot.</summary>
    /// <param name="snap">The current telemetry snapshot.</param>
    public void Update(TelemetrySnapshot snap)
    {
        _tray.Text = FormatTooltip(snap);
    }

    private static string FormatTooltip(TelemetrySnapshot s)
    {
        string text = $"CPU {s.CpuUsagePct:F0}%  RAM {s.RamUsagePct:F0}%  GPU {s.GpuUsagePct:F0}%";
        return text.Length > 63 ? text[..63] : text;
    }

    // Disposal
    /// <summary>Releases the tray icon and its associated resources.</summary>
    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Icon = null;
        _baseIcon.Dispose();
        _tray.Dispose();
    }
}
