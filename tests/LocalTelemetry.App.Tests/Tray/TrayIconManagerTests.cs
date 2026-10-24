using System.Reflection;
using FluentAssertions;
using LocalTelemetry.App.Tray;
using LocalTelemetry.Core.Models;
using Xunit;

namespace LocalTelemetry.App.Tests.Tray;

public class TrayIconManagerTests
{
    public TrayIconManagerTests()
    {
        TrayIconManager.SkipUiForTesting = true;
    }

    [WpfFact]
    public void TrayIconManager_EventsAndTooltipUpdate()
    {
        using var tray = new TrayIconManager();

        bool settingsFired = false;
        bool quitFired = false;
        bool toggleFired = false;
        bool taskmgrFired = false;

        tray.OpenSettingsRequested += () => settingsFired = true;
        tray.QuitRequested += () => quitFired = true;
        tray.ToggleOverlayRequested += () => toggleFired = true;
        tray.TaskManagerRequested += () => taskmgrFired = true;

        tray.SetTrayVisible(true);
        tray.SetTrayVisible(false);

        tray.SetOverlayVisible(true);
        tray.SetOverlayVisible(false);

        var snap = new TelemetrySnapshot
        {
            CpuUsagePct = 25.4f,
            RamUsagePct = 50.1f,
            GpuUsagePct = 75.8f
        };

        tray.SetTrayVisible(true);
        tray.Update(snap);
        tray.SetTrayVisible(false);
        tray.Update(snap);
        tray.Update(null);

        tray.ShowOverlayContextMenu(new Point(100, 100));

        settingsFired.Should().BeFalse();
        quitFired.Should().BeFalse();
        toggleFired.Should().BeFalse();
        taskmgrFired.Should().BeFalse();
    }

    [WpfFact]
    public void FormatTooltip_TruncatesOver63Chars()
    {
        var method = typeof(TrayIconManager).GetMethod("FormatTooltip", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var snap = new TelemetrySnapshot
        {
            CpuUsagePct = 100.0f,
            RamUsagePct = 100.0f,
            GpuUsagePct = 100.0f
        };

        var tooltip = (string)method.Invoke(null, [snap])!;
        tooltip.Length.Should().BeLessThanOrEqualTo(63);
    }

    [WpfFact]
    public void ContextMenuItems_PerformClick_FiresEvents()
    {
        using var tray = new TrayIconManager();

        bool settingsFired = false;
        bool quitFired = false;
        bool toggleFired = false;
        bool taskmgrFired = false;

        tray.OpenSettingsRequested += () => settingsFired = true;
        tray.QuitRequested += () => quitFired = true;
        tray.ToggleOverlayRequested += () => toggleFired = true;
        tray.TaskManagerRequested += () => taskmgrFired = true;

        var field = typeof(TrayIconManager).GetField("_tray", BindingFlags.Instance | BindingFlags.NonPublic);
        var notifyIcon = (NotifyIcon)field!.GetValue(tray)!;
        var cms = notifyIcon.ContextMenuStrip!;

        cms.Should().NotBeNull();

        // Perform click on items in tray context menu
        foreach (ToolStripItem item in cms.Items)
        {
            if (item is ToolStripMenuItem menuItem)
            {
                menuItem.PerformClick();
            }
        }

        toggleFired.Should().BeTrue();
        settingsFired.Should().BeTrue();
        quitFired.Should().BeTrue();

        // Test overlay context menu items including Task Manager
        var overlayMenuField = typeof(TrayIconManager).GetField("_overlayContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        var overlayCms = (ContextMenuStrip?)overlayMenuField!.GetValue(tray);
        if (overlayCms is not null)
        {
            foreach (ToolStripItem item in overlayCms.Items)
            {
                if (item is ToolStripMenuItem menuItem)
                {
                    menuItem.PerformClick();
                }
            }
        }

        taskmgrFired.Should().BeTrue();

        // Exercise DoubleClick on NotifyIcon
        var doubleClickMethod = typeof(NotifyIcon).GetMethod("OnDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic);
        doubleClickMethod?.Invoke(notifyIcon, [EventArgs.Empty]);
        settingsFired.Should().BeTrue();

        // Exercise Opened and Closed event handlers on context menus
        var onOpened = typeof(ToolStripDropDown).GetMethod("OnOpened", BindingFlags.Instance | BindingFlags.NonPublic);
        onOpened?.Invoke(cms, [EventArgs.Empty]);

        var onClosed = typeof(ToolStripDropDown).GetMethod("OnClosed", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(ToolStripDropDownClosedEventArgs)], null);
        onClosed?.Invoke(cms, [new ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason.ItemClicked)]);

        // Exercise SetTrayVisible same-value branch
        tray.SetTrayVisible(true);
        tray.SetTrayVisible(true);

        // Exercise Update when tray is hidden
        tray.SetTrayVisible(false);
        tray.Update(new TelemetrySnapshot { CpuUsagePct = 50f });
    }

    [WpfFact]
    public void DarkTrayRenderer_And_Theme_RendersWithoutErrors()
    {
        var rendererType = typeof(TrayIconManager).Assembly.GetType("LocalTelemetry.App.Tray.DarkTrayRenderer");
        rendererType.Should().NotBeNull();

        var themeType = typeof(TrayIconManager).Assembly.GetType("LocalTelemetry.App.Tray.TrayMenuTheme");
        themeType.Should().NotBeNull();

        var theme = Activator.CreateInstance(themeType);
        theme.Should().NotBeNull();

        // Verify color theme properties
        var propNames = new[]
        {
            "ToolStripDropDownBackground", "MenuItemSelected", "MenuItemSelectedGradientBegin",
            "MenuItemSelectedGradientEnd", "MenuItemBorder", "MenuBorder", "SeparatorDark",
            "ImageMarginGradientBegin", "ImageMarginGradientMiddle", "ImageMarginGradientEnd"
        };

        foreach (var name in propNames)
        {
            var prop = themeType.GetProperty(name);
            var color = (Color)prop!.GetValue(theme)!;
            color.Should().NotBe(Color.Empty);
        }

        var renderer = Activator.CreateInstance(rendererType) as ToolStripProfessionalRenderer;
        renderer.Should().NotBeNull();

        using var bmp = new Bitmap(100, 30);
        using var g = Graphics.FromImage(bmp);

        using var item = new ToolStripMenuItem("Test Item") { Size = new Size(100, 30) };

        // Test render separator
        using var sep = new ToolStripSeparator { Size = new Size(100, 10) };
        var sepArgs = new ToolStripSeparatorRenderEventArgs(g, sep, false);
        var renderSepMethod = rendererType.GetMethod("OnRenderSeparator", BindingFlags.Instance | BindingFlags.NonPublic);
        renderSepMethod!.Invoke(renderer, [sepArgs]);

        // Test render item text
        var renderTextMethod = rendererType.GetMethod("OnRenderItemText", BindingFlags.Instance | BindingFlags.NonPublic);
        var textArgs = new ToolStripItemTextRenderEventArgs(g, item, "Test Text", new Rectangle(0, 0, 100, 30), Color.White, new Font("Calibri", 10f), TextFormatFlags.Left);
        renderTextMethod!.Invoke(renderer, [textArgs]);

        // Test render menu item background (unselected, selected, disabled)
        var renderBgMethod = rendererType.GetMethod("OnRenderMenuItemBackground", BindingFlags.Instance | BindingFlags.NonPublic);
        var bgArgsUnselected = new ToolStripItemRenderEventArgs(g, item);
        renderBgMethod!.Invoke(renderer, [bgArgsUnselected]);

        // Select item
        item.Select();
        var bgArgsSelected = new ToolStripItemRenderEventArgs(g, item);
        renderBgMethod.Invoke(renderer, [bgArgsSelected]);

        // Disabled item
        item.Enabled = false;
        var bgArgsDisabled = new ToolStripItemRenderEventArgs(g, item);
        renderBgMethod.Invoke(renderer, [bgArgsDisabled]);
    }

    [WpfFact]
    public void TrayIconManager_OverlayContextMenu_StateAndVisibilityLifecycle()
    {
        using var tray = new TrayIconManager();

        // 1. Initial closed state
        tray.IsOverlayContextMenuOpen.Should().BeFalse();
        tray.OverlayContextMenuBounds.Should().Be(Rectangle.Empty);
        tray.CloseOverlayContextMenu(); // Safe when closed

        // 2. SetOverlayVisible toggles text and checked state
        tray.SetOverlayVisible(true);
        var trayToggleField = typeof(TrayIconManager).GetField("_trayOverlayToggleItem", BindingFlags.Instance | BindingFlags.NonPublic);
        var overlayToggleField = typeof(TrayIconManager).GetField("_overlayOverlayToggleItem", BindingFlags.Instance | BindingFlags.NonPublic);

        var trayItem = (ToolStripMenuItem)trayToggleField!.GetValue(tray)!;
        var overlayItem = (ToolStripMenuItem)overlayToggleField!.GetValue(tray)!;

        trayItem.Checked.Should().BeTrue();
        trayItem.Text.Should().Be("Hide Overlay");
        overlayItem.Checked.Should().BeTrue();
        overlayItem.Text.Should().Be("Hide Overlay");

        tray.SetOverlayVisible(false);
        trayItem.Checked.Should().BeFalse();
        trayItem.Text.Should().Be("Show Overlay");
        overlayItem.Checked.Should().BeFalse();
        overlayItem.Text.Should().Be("Show Overlay");

        // 3. ShowOverlayContextMenu when context menu is null
        var menuField = typeof(TrayIconManager).GetField("_overlayContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        var originalMenu = menuField!.GetValue(tray);
        menuField.SetValue(tray, null);
        tray.ShowOverlayContextMenu(new Point(50, 50));
        menuField.SetValue(tray, originalMenu);

        // 4. ShowOverlayContextMenu with SkipUiForTesting = false
        bool priorSkipUi = TrayIconManager.SkipUiForTesting;
        try
        {
            TrayIconManager.SkipUiForTesting = false;
            tray.ShowOverlayContextMenu(new Point(100, 100));
            tray.CloseOverlayContextMenu();
        }
        finally
        {
            TrayIconManager.SkipUiForTesting = priorSkipUi;
        }
    }

    [WpfFact]
    public void TrayIconManager_HiddenMenuOwnerForm_And_TaskbarCreatedEvent()
    {
        using var tray = new TrayIconManager();
        bool taskbarCreatedFired = false;
        tray.TaskbarCreated += () => taskbarCreatedFired = true;

        var ownerField = typeof(TrayIconManager).GetField("_menuOwner", BindingFlags.Instance | BindingFlags.NonPublic);
        var menuOwner = (Form)ownerField!.GetValue(tray)!;

        // Verify CreateParams has WS_EX_TOOLWINDOW (0x80)
        var cpProp = menuOwner.GetType().GetProperty("CreateParams", BindingFlags.Instance | BindingFlags.NonPublic);
        var cp = (CreateParams)cpProp!.GetValue(menuOwner)!;
        (cp.ExStyle & 0x80).Should().Be(0x80);

        // Send WmTaskbarCreated message to WndProc
        var wmField = menuOwner.GetType().GetField("WmTaskbarCreated", BindingFlags.Static | BindingFlags.NonPublic);
        uint wmTaskbarCreated = (uint)wmField!.GetValue(null)!;

        var wndProc = menuOwner.GetType().GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic);
        var msg = Message.Create(menuOwner.Handle, (int)wmTaskbarCreated, IntPtr.Zero, IntPtr.Zero);
        wndProc!.Invoke(menuOwner, [msg]);

        taskbarCreatedFired.Should().BeTrue();

        // Also test WndProc with normal message (not taskbar created)
        var normalMsg = Message.Create(menuOwner.Handle, 0x0400 /* WM_USER */, IntPtr.Zero, IntPtr.Zero);
        wndProc.Invoke(menuOwner, [normalMsg]);
    }

    [WpfFact]
    public void TrayIconManager_Constructor_WithUiEnabled_ShowsMenuOwner()
    {
        bool priorSkipUi = TrayIconManager.SkipUiForTesting;
        try
        {
            TrayIconManager.SkipUiForTesting = false;
            using var tray = new TrayIconManager();
            var act = () =>
            {
                tray.SetTrayVisible(true);
                tray.SetTrayVisible(false);
            };
            act.Should().NotThrow();
        }
        finally
        {
            TrayIconManager.SkipUiForTesting = priorSkipUi;
        }
    }

    [WpfFact]
    public void TrayIconManager_Update_UpdatesTooltipText()
    {
        bool priorSkipUi = TrayIconManager.SkipUiForTesting;
        try
        {
            TrayIconManager.SkipUiForTesting = false;
            using var tray = new TrayIconManager();
            tray.SetTrayVisible(true);

            // 1. null snapshot -> no-op
            tray.Update(null);

            // 2. valid snapshot
            var snapshot = new TelemetrySnapshot
            {
                CpuUsagePct = 42.5f,
                RamUsagePct = 65.2f,
                GpuUsagePct = 80.0f
            };
            tray.Update(snapshot);

            var trayField = typeof(TrayIconManager).GetField("_tray", BindingFlags.Instance | BindingFlags.NonPublic);
            var notifyIcon = (NotifyIcon)trayField!.GetValue(tray)!;
            notifyIcon.Text.Should().Contain("CPU 42%").And.Contain("RAM 65%").And.Contain("GPU 80%");

            // 3. Tray not visible -> no-op
            tray.SetTrayVisible(false);
            tray.Update(snapshot);

            // 4. FormatTooltip directly
            var formatMethod = typeof(TrayIconManager).GetMethod("FormatTooltip", BindingFlags.Static | BindingFlags.NonPublic);
            var formatted = (string)formatMethod!.Invoke(null, [snapshot])!;
            formatted.Should().Be("CPU 42%  RAM 65%  GPU 80%");

            // 5. Test FormatTooltip length > 63 truncation
            var hugeSnapshot = new TelemetrySnapshot
            {
                CpuUsagePct = 12345678901234567890f,
                RamUsagePct = 12345678901234567890f,
                GpuUsagePct = 12345678901234567890f
            };
            var truncated = (string)formatMethod.Invoke(null, [hugeSnapshot])!;
            truncated.Length.Should().BeLessThanOrEqualTo(63);
        }
        finally
        {
            TrayIconManager.SkipUiForTesting = priorSkipUi;
        }
    }

    [WpfFact]
    public void TrayIconManager_ContextMenu_OpenedAndClosedEvents_Coverage()
    {
        using var tray = new TrayIconManager();
        var menuField = typeof(TrayIconManager).GetField("_overlayContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        var menu = (ContextMenuStrip)menuField!.GetValue(tray)!;

        // Trigger Opened event
        var onOpened = typeof(ToolStripDropDown).GetMethod("OnOpened", BindingFlags.Instance | BindingFlags.NonPublic);
        var actOpen = () => onOpened?.Invoke(menu, [EventArgs.Empty]);
        actOpen.Should().NotThrow();

        // Trigger Closed event
        var onClosed = typeof(ToolStripDropDown).GetMethod("OnClosed", BindingFlags.Instance | BindingFlags.NonPublic);
        var actClose = () => onClosed?.Invoke(menu, [new ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason.AppClicked)]);
        actClose.Should().NotThrow();
    }

    [WpfFact]
    public void TrayIconManager_ShowOverlayContextMenu_And_UpdateError_Coverage()
    {
        var priorSkipUi = TrayIconManager.SkipUiForTesting;
        try
        {
            TrayIconManager.SkipUiForTesting = false;
            using var tray = new TrayIconManager();

            // Null menu branch
            var menuField = typeof(TrayIconManager).GetField("_overlayContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var origMenu = menuField.GetValue(tray);
            menuField.SetValue(tray, null);
            tray.ShowOverlayContextMenu(new Point(50, 50));
            menuField.SetValue(tray, origMenu);

            // ShowOverlayContextMenu with real menu when SkipUiForTesting is false
            var actShow = () => tray.ShowOverlayContextMenu(new Point(100, 100));
            actShow.Should().NotThrow();

            tray.CloseOverlayContextMenu();
        }
        finally
        {
            TrayIconManager.SkipUiForTesting = priorSkipUi;
        }
    }
}

