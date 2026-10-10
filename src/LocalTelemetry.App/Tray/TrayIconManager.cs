using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LocalTelemetry.App.Win32;
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
    private readonly HiddenMenuOwnerForm _menuOwner;

    private ToolStripMenuItem? _trayOverlayToggleItem;
    private ToolStripMenuItem? _overlayOverlayToggleItem;
    private ContextMenuStrip? _overlayContextMenu;

    public event Action? OpenSettingsRequested;
    public event Action? QuitRequested;
    public event Action? ToggleOverlayRequested;
    public event Action? TaskManagerRequested;
    public event Action? TaskbarCreated;

    internal static bool SkipUiForTesting { get; set; }

    // Constructor
    public TrayIconManager()
    {
        try
        {
            _baseIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to extract application icon: {ex.Message}");
            _baseIcon = SystemIcons.Application;
        }

        // Dedicated top-level message window to own popup menus, handle outside-click dismissal, and catch TaskbarCreated broadcast
        _menuOwner = new HiddenMenuOwnerForm();
        _menuOwner.TaskbarCreated += () => TaskbarCreated?.Invoke();
        if (!SkipUiForTesting)
        {
            _menuOwner.Show();
        }

        _tray = new NotifyIcon
        {
            Text = "LocalTelemetry",
            Visible = !SkipUiForTesting,
            Icon = _baseIcon,
        };

        BuildTrayContextMenu();
        BuildOverlayContextMenu();

        _tray.DoubleClick += (_, _) => OpenSettingsRequested?.Invoke();
    }

    /// <summary>
    /// Shows the overlay dark context menu at the specified screen coordinates.
    /// Uses the top-level owner window and standard Windows focus patterns so clicking
    /// outside immediately dismisses the menu cleanly.
    /// </summary>
    public void ShowOverlayContextMenu(Point screenPos)
    {
        if (_overlayContextMenu is null)
        {
            Log.Error("Cannot display overlay context menu (_overlayContextMenu is null)");
            return;
        }

        try
        {
            Log.Debug($"Context menu opened at screen position ({screenPos.X}, {screenPos.Y})");
            if (!SkipUiForTesting)
            {
                NativeMethods.SetForegroundWindow(_menuOwner.Handle);
                _overlayContextMenu.Show(screenPos);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to display overlay context menu at ({screenPos.X}, {screenPos.Y})");
        }
    }

    /// <summary>Gets whether the overlay context menu is currently open and visible.</summary>
    public bool IsOverlayContextMenuOpen => _overlayContextMenu?.Visible == true;

    /// <summary>Gets the current screen bounds of the overlay context menu, or empty if not open.</summary>
    public Rectangle OverlayContextMenuBounds => _overlayContextMenu?.Visible == true ? _overlayContextMenu.Bounds : Rectangle.Empty;

    /// <summary>Closes the overlay context menu if it is currently open.</summary>
    public void CloseOverlayContextMenu()
    {
        if (_overlayContextMenu?.Visible == true)
        {
            _overlayContextMenu.Close();
        }
    }

    /// <summary>Updates the notification area icon visibility.</summary>
    public void SetTrayVisible(bool visible)
    {
        if (SkipUiForTesting)
        {
            _tray.Visible = false;
            return;
        }
        if (_tray.Visible != visible)
        {
            _tray.Visible = visible;
        }
    }

    /// <summary>Updates the checked state and text of the 'Show/Hide Overlay' menu item.</summary>
    public void SetOverlayVisible(bool visible)
    {
        string text = visible ? "Hide Overlay" : "Show Overlay";
        if (_trayOverlayToggleItem is not null)
        {
            _trayOverlayToggleItem.Checked = visible;
            _trayOverlayToggleItem.Text = text;
        }
        if (_overlayOverlayToggleItem is not null)
        {
            _overlayOverlayToggleItem.Checked = visible;
            _overlayOverlayToggleItem.Text = text;
        }
    }

    private void BuildTrayContextMenu()
    {
        var ctx = CreateBaseContextMenu();

        var statusHeader = new ToolStripMenuItem("LocalTelemetry")
        {
            Enabled = false,
            ForeColor = Color.FromArgb(142, 142, 147),
            Font = new Font("Calibri", 10f, FontStyle.Bold)
        };

        _trayOverlayToggleItem = new ToolStripMenuItem("Hide Overlay", null, (_, _) =>
        {
            ToggleOverlayRequested?.Invoke();
        })
        {
            Checked = true,
            CheckOnClick = false,
            ForeColor = Color.White
        };

        var settingsItem = new ToolStripMenuItem("Open Settings", null, (_, _) =>
        {
            OpenSettingsRequested?.Invoke();
        })
        {
            ForeColor = Color.White
        };

        var quitItem = new ToolStripMenuItem("Quit", null, (_, _) =>
        {
            Log.Info("Application exit requested (system tray)");
            QuitRequested?.Invoke();
        })
        {
            ForeColor = Color.FromArgb(255, 69, 58)
        };

        ctx.Items.Add(statusHeader);
        ctx.Items.Add(new ToolStripSeparator());
        ctx.Items.Add(_trayOverlayToggleItem);
        ctx.Items.Add(settingsItem);
        ctx.Items.Add(new ToolStripSeparator());
        ctx.Items.Add(quitItem);

        _tray.ContextMenuStrip = ctx;
    }

    private void BuildOverlayContextMenu()
    {
        var ctx = CreateBaseContextMenu();

        var statusHeader = new ToolStripMenuItem("LocalTelemetry")
        {
            Enabled = false,
            ForeColor = Color.FromArgb(142, 142, 147),
            Font = new Font("Calibri", 10f, FontStyle.Bold)
        };

        _overlayOverlayToggleItem = new ToolStripMenuItem("Hide Overlay", null, (_, _) =>
        {
            ToggleOverlayRequested?.Invoke();
        })
        {
            Checked = true,
            CheckOnClick = false,
            ForeColor = Color.White
        };

        var settingsItem = new ToolStripMenuItem("Open Settings", null, (_, _) =>
        {
            OpenSettingsRequested?.Invoke();
        })
        {
            ForeColor = Color.White
        };

        var taskmgrItem = new ToolStripMenuItem("Task Manager", null, (_, _) =>
        {
            TaskManagerRequested?.Invoke();
        })
        {
            ForeColor = Color.White
        };

        var quitItem = new ToolStripMenuItem("Quit", null, (_, _) =>
        {
            Log.Info("Application exit requested (context menu)");
            QuitRequested?.Invoke();
        })
        {
            ForeColor = Color.FromArgb(255, 69, 58)
        };

        ctx.Items.Add(statusHeader);
        ctx.Items.Add(new ToolStripSeparator());
        ctx.Items.Add(_overlayOverlayToggleItem);
        ctx.Items.Add(settingsItem);
        ctx.Items.Add(taskmgrItem);
        ctx.Items.Add(new ToolStripSeparator());
        ctx.Items.Add(quitItem);

        _overlayContextMenu = ctx;
    }

    private ContextMenuStrip CreateBaseContextMenu()
    {
        var ctx = new ContextMenuStrip
        {
            Renderer = new DarkTrayRenderer(),
            ShowImageMargin = false,
            AutoClose = true,
            Font = new Font("Calibri", 10.5f, FontStyle.Regular),
            BackColor = Color.FromArgb(28, 28, 30),
            ForeColor = Color.FromArgb(235, 235, 245),
            Padding = new Padding(6, 6, 6, 6)
        };

        ctx.Opened += (_, _) =>
        {
            try
            {
                int cornerPref = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(ctx.Handle, 33, ref cornerPref, sizeof(int));
                int darkMode = 1; // DWMWA_USE_IMMERSIVE_DARK_MODE
                DwmSetWindowAttribute(ctx.Handle, 20, ref darkMode, sizeof(int));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "DWM styling failed on context menu");
            }
        };

        ctx.Closed += (_, _) =>
        {
            try
            {
                NativeMethods.PostMessage(_menuOwner.Handle, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to post message to menu owner on context menu close");
            }
        };

        return ctx;
    }

    // Updates & Notifications
    /// <summary>Updates the notification area icon tooltip with current CPU, RAM, and GPU readings.</summary>
    public void Update(TelemetrySnapshot? s)
    {
        if (s is null || !_tray.Visible) return;
        try
        {
            _tray.Text = FormatTooltip(s);
        }
        catch (Exception ex)
        {
            Log.Error($"Notification tooltip update failed: {ex.Message}");
        }
    }

    private static string FormatTooltip(TelemetrySnapshot s)
    {
        string text = $"CPU {s.CpuUsagePct:F0}%  RAM {s.RamUsagePct:F0}%  GPU {s.GpuUsagePct:F0}%";
        return text.Length > 63 ? text[..63] : text;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // Disposal
    /// <summary>Releases the tray icon and its associated resources.</summary>
    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Icon = null;
        _baseIcon.Dispose();
        _tray.Dispose();
        _overlayContextMenu?.Dispose();
        _menuOwner.Dispose();
    }
}

// Dark Menu Styling
internal sealed class DarkTrayRenderer : ToolStripProfessionalRenderer
{
    public DarkTrayRenderer() : base(new TrayMenuTheme()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var rect = e.TextRectangle;
        rect.Offset(4, 0);
        TextRenderer.DrawText(
            e.Graphics,
            e.Text,
            e.TextFont,
            rect,
            e.TextColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

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

internal sealed class TrayMenuTheme : ProfessionalColorTable
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

internal sealed class HiddenMenuOwnerForm : Form
{
    private static readonly uint WmTaskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");

    public event Action? TaskbarCreated;

    public HiddenMenuOwnerForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        WindowState = FormWindowState.Normal;
        Size = new Size(1, 1);
        Location = new Point(-2000, -2000);
        Opacity = 0;

        if (WmTaskbarCreated != 0)
        {
            NativeMethods.ChangeWindowMessageFilter(WmTaskbarCreated, NativeMethods.MSGFLT_ADD);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (WmTaskbarCreated != 0 && Handle != IntPtr.Zero)
        {
            NativeMethods.ChangeWindowMessageFilterEx(Handle, WmTaskbarCreated, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_TOOLWINDOW (0x80) hides it from Alt-Tab
            cp.ExStyle |= 0x80;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (WmTaskbarCreated != 0 && m.Msg == WmTaskbarCreated)
        {
            Log.Info("Windows Explorer taskbar recreation detected (TaskbarCreated message received)");
            TaskbarCreated?.Invoke();
        }
        base.WndProc(ref m);
    }
}

