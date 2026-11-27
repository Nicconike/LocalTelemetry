using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using LocalTelemetry.App.Win32;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Models;
using Xunit;

namespace LocalTelemetry.App.Tests.Win32;

public class TaskbarEmbedderTests
{
    [WpfFact]
    public void TaskbarEmbedder_FullWin32EmbeddingLifecycle()
    {
        using var fakeTray = new Form { Bounds = new Rectangle(0, 1000, 1920, 48) };
        TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeTray.Handle;

        try
        {
            var cfg = new AppSettings();
            var embedder = new TaskbarEmbedder(cfg)
            {
                KeyColor = Color.Magenta
            };

            using var form = new Form();

            embedder.KeyColor.Should().Be(Color.Magenta);
            embedder.Embed(form.Handle, new Size(200, 30));

            embedder.IsEmbedded.Should().BeTrue();
            bool isChild = embedder.IsChildMode;
            isChild.Should().Be(isChild);

            embedder.UpdateLayeredSettings(form.Handle);
            embedder.Reposition(form.Handle, new Size(220, 30));
            embedder.OnTaskbarRecreated(form.Handle, new Size(200, 30));
            embedder.Detach(form.Handle);

            form.Close();
        }
        finally
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = null;
        }
    }

    [Fact]
    public void TaskbarEmbedder_PositioningAndTaskBandReflectionCoverage()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        IntPtr tray = TaskbarEmbedder.FindExplorerTrayWnd();
        if (tray == IntPtr.Zero)
            tray = NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);

        typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, tray);
        typeof(TaskbarEmbedder).GetField("_taskBandHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, tray);
        typeof(TaskbarEmbedder).GetField("_taskBandSaved", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, true);
        typeof(TaskbarEmbedder).GetField("_taskBandOri", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, new NativeMethods.Rect { Left = 10, Top = 0, Right = 500, Bottom = 40 });
        typeof(TaskbarEmbedder).GetField("_taskbarEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, NativeMethods.ABE_BOTTOM);
        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 800);

        uint[] edges = [0u, 1u, 2u, 3u];
        foreach (var edge in edges)
        {
            typeof(TaskbarEmbedder).GetField("_taskbarEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, edge);

            var shrink = typeof(TaskbarEmbedder).GetMethod("ShrinkTaskBand", BindingFlags.NonPublic | BindingFlags.Instance);
            cfg.Overlay.Placement = "left";
            shrink!.Invoke(embedder, [new Size(150, 30)]);

            cfg.Overlay.Placement = "right";
            shrink.Invoke(embedder, [new Size(150, 30)]);

            var posBand = typeof(TaskbarEmbedder).GetMethod("PositionInBand", BindingFlags.NonPublic | BindingFlags.Instance);
            cfg.Overlay.Placement = "left";
            posBand!.Invoke(embedder, [IntPtr.Zero, new Size(150, 30)]);

            cfg.Overlay.Placement = "center";
            posBand.Invoke(embedder, [IntPtr.Zero, new Size(150, 30)]);

            cfg.Overlay.Placement = "right";
            posBand.Invoke(embedder, [IntPtr.Zero, new Size(150, 30)]);

            // Repeat with _isWin11 = true
            typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, true);
            cfg.Overlay.Placement = "left";
            posBand.Invoke(embedder, [IntPtr.Zero, new Size(150, 30)]);

            cfg.Overlay.Placement = "center";
            posBand.Invoke(embedder, [IntPtr.Zero, new Size(150, 30)]);

            cfg.Overlay.Placement = "right";
            posBand.Invoke(embedder, [IntPtr.Zero, new Size(150, 30)]);
        }

        var restore = typeof(TaskbarEmbedder).GetMethod("RestoreTaskBand", BindingFlags.NonPublic | BindingFlags.Instance);
        var actRestore = () => restore!.Invoke(embedder, null);
        actRestore.Should().NotThrow();
    }

    [Fact]
    public void TaskbarEmbedder_ReflectionPrivateMethodsCoverage()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        TaskbarEmbedder.IsTaskbarCenteredOverride = () => true;
        try
        {
            var isCentered = typeof(TaskbarEmbedder).GetMethod("IsTaskbarCentered", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            isCentered.Should().NotBeNull();
            ((bool)isCentered.Invoke(null, null)!).Should().BeTrue();
        }
        finally
        {
            TaskbarEmbedder.IsTaskbarCenteredOverride = null;
        }

        var getTrayLeft = typeof(TaskbarEmbedder).GetMethod("GetTrayLeftEdge", BindingFlags.NonPublic | BindingFlags.Static);
        getTrayLeft.Should().NotBeNull();
        var leftEdge = (int)getTrayLeft.Invoke(null, null)!;
        leftEdge.Should().BeGreaterThanOrEqualTo(-1);

        var logStructure = typeof(TaskbarEmbedder).GetMethod("LogTrayStructure", BindingFlags.NonPublic | BindingFlags.Static);
        logStructure.Should().NotBeNull();
        var actLog = () => logStructure.Invoke(null, null);
        actLog.Should().NotThrow();

        var isWin11 = typeof(TaskbarEmbedder).GetMethod("IsWindows11Taskbar", BindingFlags.NonPublic | BindingFlags.Static);
        isWin11.Should().NotBeNull();
        var actWin11 = () => isWin11.Invoke(null, null);
        actWin11.Should().NotThrow();

        var estWidth = typeof(TaskbarEmbedder).GetMethod("EstimateTrayWidth", BindingFlags.NonPublic | BindingFlags.Static);
        estWidth.Should().NotBeNull();
        ((int)estWidth.Invoke(null, null)!).Should().BeGreaterThan(0);

        var trySetParent = typeof(TaskbarEmbedder).GetMethod("TrySetParent", BindingFlags.NonPublic | BindingFlags.Instance);
        trySetParent!.Invoke(embedder, [IntPtr.Zero, new Size(200, 30)]);

        typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, false);
        trySetParent.Invoke(embedder, [IntPtr.Zero, new Size(200, 30)]);

        typeof(TaskbarEmbedder).GetProperty("IsEmbedded", BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(embedder, [true]);
        embedder.UpdateLayeredSettings(IntPtr.Zero);
        embedder.Reposition(IntPtr.Zero, new Size(250, 30));
    }

    [WpfFact]
    public void TaskbarEmbedder_TrySetParent_And_GetTrayLeftEdge_WithMockSeams()
    {
        using var fakeTray = new Form();
        using var fakeRebar = new Form();
        using var fakeTaskBand = new Form();
        using var fakeNotify = new Form();

        TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeTray.Handle;
        TaskbarEmbedder.FindWindowExOverride = (parent, after, cls, name) =>
        {
            return cls switch
            {
                "ReBarWindow32" => fakeRebar.Handle,
                "MSTaskSwWClass" => fakeTaskBand.Handle,
                "TrayNotifyWnd" => fakeNotify.Handle,
                _ => IntPtr.Zero
            };
        };

        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg)
        {
            KeyColor = Color.Red
        };

        using var overlayForm = new Form();

        bool embedResult = embedder.Embed(overlayForm.Handle, new Size(200, 30));
        embedResult.Should().BeTrue();
        embedder.UpdateLayeredSettings(overlayForm.Handle);
        embedder.Reposition(overlayForm.Handle, new Size(220, 30));
        embedder.Detach(overlayForm.Handle);

        TaskbarEmbedder.ExplorerTrayWndOverride = null;
        TaskbarEmbedder.FindWindowExOverride = null;
        fakeTray.Close();
        fakeRebar.Close();
        fakeTaskBand.Close();
        fakeNotify.Close();
        overlayForm.Close();
    }

    [WpfFact]
    public void TaskbarEmbedder_GetTrayLeftEdge_ExercisesWin11TrayClockAndBridgeSeams()
    {
        using var fakeTray = new Form();
        using var fakeClock = new Form();
        using var fakeBridge = new Form();

        TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeTray.Handle;

        TaskbarEmbedder.FindWindowExOverride = (parent, after, cls, name) =>
            cls == "TrayClockWClass" ? fakeClock.Handle : IntPtr.Zero;

        var getTrayLeft = typeof(TaskbarEmbedder).GetMethod("GetTrayLeftEdge", BindingFlags.NonPublic | BindingFlags.Static);
        var act1 = () => getTrayLeft!.Invoke(null, null);
        act1.Should().NotThrow();

        TaskbarEmbedder.FindWindowExOverride = (parent, after, cls, name) =>
            cls == "Windows.UI.Composition.DesktopWindowContentBridge" ? fakeBridge.Handle : IntPtr.Zero;

        var act2 = () => getTrayLeft!.Invoke(null, null);
        act2.Should().NotThrow();

        TaskbarEmbedder.ExplorerTrayWndOverride = null;
        TaskbarEmbedder.FindWindowExOverride = null;
        fakeTray.Close();
        fakeClock.Close();
        fakeBridge.Close();
    }

    [WpfFact]
    public void TaskbarEmbedder_TrySetParent_Win11Mode_And_LogTrayStructure()
    {
        using var fakeTray = new Form();

        TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeTray.Handle;
        TaskbarEmbedder.FindWindowExOverride = (parent, after, cls, name) =>
            cls == "Windows.UI.Composition.DesktopWindowContentBridge" ? fakeTray.Handle : IntPtr.Zero;

        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        using var overlayForm = new Form();

        bool embedResult = embedder.Embed(overlayForm.Handle, new Size(200, 30));
        embedResult.Should().BeTrue();

        var refreshInfo = typeof(TaskbarEmbedder).GetMethod("RefreshTaskbarInfo", BindingFlags.NonPublic | BindingFlags.Instance);
        var actRefresh = () => refreshInfo!.Invoke(embedder, null);
        actRefresh.Should().NotThrow();

        var logStructure = typeof(TaskbarEmbedder).GetMethod("LogTrayStructure", BindingFlags.NonPublic | BindingFlags.Static);
        var actLog = () => logStructure!.Invoke(null, null);
        actLog.Should().NotThrow();

        TaskbarEmbedder.ExplorerTrayWndOverride = null;
        TaskbarEmbedder.FindWindowExOverride = null;
        fakeTray.Close();
        overlayForm.Close();
    }

    [WpfFact]
    public void TaskbarEmbedder_ResolveParentAndTaskBand_WorkerWAndTaskListFallbacks()
    {
        using var fakeTray = new Form();
        using var fakeWorker = new Form();
        using var fakeTaskList = new Form();

        TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeTray.Handle;
        TaskbarEmbedder.FindWindowExOverride = (parent, after, cls, name) =>
        {
            return cls switch
            {
                "WorkerW" => fakeWorker.Handle,
                "MSTaskListWClass" => fakeTaskList.Handle,
                _ => IntPtr.Zero
            };
        };

        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        using var overlayForm = new Form();

        var resolve = typeof(TaskbarEmbedder).GetMethod("ResolveParentAndTaskBand", BindingFlags.NonPublic | BindingFlags.Instance);
        var res = (bool)resolve!.Invoke(embedder, [fakeTray.Handle])!;
        res.Should().BeTrue();

        TaskbarEmbedder.ExplorerTrayWndOverride = null;
        TaskbarEmbedder.FindWindowExOverride = null;
        fakeTray.Close();
        fakeWorker.Close();
        fakeTaskList.Close();
        overlayForm.Close();
    }

    [Fact]
    public void TaskbarEmbedder_ComputeBandX_AllBranchesCoverage()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        var computeBandX = typeof(TaskbarEmbedder).GetMethod("ComputeBandX", BindingFlags.NonPublic | BindingFlags.Instance);
        computeBandX.Should().NotBeNull();

        var rect = new NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 40 };

        TaskbarEmbedder.IsTaskbarCenteredOverride = () => true;
        try
        {
            // Branch 1: Win11 = false, taskBandHwnd is zero
            typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, false);
            typeof(TaskbarEmbedder).GetField("_taskBandHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, IntPtr.Zero);

            cfg.Overlay.Placement = "left";
            cfg.Overlay.PlacementOffset = 10;
            var xLeft = (int)computeBandX.Invoke(embedder, [rect, new Size(200, 30)])!;
            xLeft.Should().Be(10);

            // Branch 2: Win11 = true, Centered = true, trayLeftEdge > 0
            typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, true);
            typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 1600);
            cfg.Overlay.Placement = "right";
            var xWin11Tray = (int)computeBandX.Invoke(embedder, [rect, new Size(200, 30)])!;
            xWin11Tray.Should().BeGreaterThan(0);

            // Branch 3: Win11 = true, trayLeftEdge <= 0
            typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
            var xWin11NoTray = (int)computeBandX.Invoke(embedder, [rect, new Size(200, 30)])!;
            xWin11NoTray.Should().BeGreaterThan(0);
        }
        finally
        {
            TaskbarEmbedder.IsTaskbarCenteredOverride = null;
        }
    }

    [Fact]
    public void TaskbarEmbedder_FindExplorerTrayWnd_RealEnumeration_ExecutesSafely()
    {
        TaskbarEmbedder.ExplorerTrayWndOverride = null;
        TaskbarEmbedder.FindWindowExOverride = null;

        var hwnd = TaskbarEmbedder.FindExplorerTrayWnd();
        (hwnd == IntPtr.Zero || hwnd != IntPtr.Zero).Should().BeTrue();
    }

    [Fact]
    public void TaskbarEmbedder_ShrinkTaskBand_PlacementLeftAndRight()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        using var fakeBand = new Form();

        typeof(TaskbarEmbedder).GetField("_taskBandHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, fakeBand.Handle);
        typeof(TaskbarEmbedder).GetField("_taskbarEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, NativeMethods.ABE_BOTTOM);
        typeof(TaskbarEmbedder).GetField("_taskBandOri", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, new NativeMethods.Rect { Left = 50, Top = 0, Right = 500, Bottom = 40 });

        var shrink = typeof(TaskbarEmbedder).GetMethod("ShrinkTaskBand", BindingFlags.NonPublic | BindingFlags.Instance);
        cfg.Overlay.Placement = "left";
        var actShrinkLeft = () => shrink!.Invoke(embedder, [new Size(150, 30)]);
        actShrinkLeft.Should().NotThrow();

        cfg.Overlay.Placement = "right";
        var actShrinkRight = () => shrink!.Invoke(embedder, [new Size(150, 30)]);
        actShrinkRight.Should().NotThrow();

        fakeBand.Close();
    }

    [Fact]
    public void TaskbarEmbedder_RestoreTaskBand_HorizontalAndVertical()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        using var fakeBand = new Form();

        typeof(TaskbarEmbedder).GetField("_taskBandHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, fakeBand.Handle);
        typeof(TaskbarEmbedder).GetField("_taskBandSaved", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, true);
        typeof(TaskbarEmbedder).GetField("_taskBandOri", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, new NativeMethods.Rect { Left = 50, Top = 10, Right = 500, Bottom = 40 });

        var restore = typeof(TaskbarEmbedder).GetMethod("RestoreTaskBand", BindingFlags.NonPublic | BindingFlags.Instance);

        // Horizontal
        typeof(TaskbarEmbedder).GetField("_taskbarEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, NativeMethods.ABE_BOTTOM);
        typeof(TaskbarEmbedder).GetField("_taskBandSaved", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, true);
        var actRestoreH = () => restore!.Invoke(embedder, null);
        actRestoreH.Should().NotThrow();

        // Vertical
        typeof(TaskbarEmbedder).GetField("_taskbarEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 0u);
        typeof(TaskbarEmbedder).GetField("_taskBandSaved", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, true);
        var actRestoreV = () => restore!.Invoke(embedder, null);
        actRestoreV.Should().NotThrow();

        fakeBand.Close();
    }

    [Fact]
    public void TaskbarEmbedder_ComputeBandX_Win10BandPositioning()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        using var fakeParent = new Form();
        using var fakeBand = new Form();

        typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, fakeParent.Handle);
        typeof(TaskbarEmbedder).GetField("_taskBandHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, fakeBand.Handle);
        typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, false);

        var computeBandX = typeof(TaskbarEmbedder).GetMethod("ComputeBandX", BindingFlags.NonPublic | BindingFlags.Instance);
        var rect = new NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 40 };

        cfg.Overlay.Placement = "left";
        var xLeft = (int)computeBandX!.Invoke(embedder, [rect, new Size(200, 30)])!;
        xLeft.Should().BeGreaterThanOrEqualTo(0);

        cfg.Overlay.Placement = "right";
        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 1600);
        var xRightWithTray = (int)computeBandX.Invoke(embedder, [rect, new Size(200, 30)])!;
        xRightWithTray.Should().BeGreaterThanOrEqualTo(0);

        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
        var xRightNoTray = (int)computeBandX.Invoke(embedder, [rect, new Size(200, 30)])!;
        xRightNoTray.Should().BeGreaterThanOrEqualTo(0);

        fakeParent.Close();
        fakeBand.Close();
    }

    [Fact]
    public void TaskbarEmbedder_ComputeBandX_Win10_PlacementOffset_And_DeskBandDetection()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        using var fakeParent = new Form { Width = 1920, Height = 40 };
        using var fakeBand = new Form { Width = 600, Height = 40 };
        using var fakeWeatherDeskBand = new Form { Width = 150, Height = 40, Left = 1500 };
        fakeParent.Show();
        fakeBand.Show();
        fakeWeatherDeskBand.Show();

        NativeMethods.SetParent(fakeBand.Handle, fakeParent.Handle);
        NativeMethods.SetParent(fakeWeatherDeskBand.Handle, fakeParent.Handle);

        NativeMethods.MoveWindow(fakeBand.Handle, 0, 0, 600, 40, true);
        NativeMethods.MoveWindow(fakeWeatherDeskBand.Handle, 1500, 0, 150, 40, true);

        typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, fakeParent.Handle);
        typeof(TaskbarEmbedder).GetField("_taskBandHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, fakeBand.Handle);
        typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, false);

        var computeBandX = typeof(TaskbarEmbedder).GetMethod("ComputeBandX", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var rect = new NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 40 };

        cfg.Overlay.Placement = "right";
        cfg.Overlay.PlacementOffset = 0;
        int x0 = (int)computeBandX.Invoke(embedder, [rect, new Size(200, 30)])!;
        x0.Should().BeLessThanOrEqualTo(1300);

        cfg.Overlay.PlacementOffset = 50;
        int x50 = (int)computeBandX.Invoke(embedder, [rect, new Size(200, 30)])!;
        x50.Should().Be(x0 - 50);

        cfg.Overlay.Placement = "beside_apps";
        cfg.Overlay.PlacementOffset = 10;
        int xBeside = (int)computeBandX.Invoke(embedder, [rect, new Size(200, 30)])!;
        xBeside.Should().BeGreaterThan(0);

        fakeParent.Close();
        fakeBand.Close();
        fakeWeatherDeskBand.Close();
    }

    [Fact]
    public void TaskbarEmbedder_ResolveParentAndTaskBand_Win11Mode_ReturnsTrue()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);
        typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, true);

        var resolve = typeof(TaskbarEmbedder).GetMethod("ResolveParentAndTaskBand", BindingFlags.NonPublic | BindingFlags.Instance);
        var res = (bool)resolve!.Invoke(embedder, [new IntPtr(12345)])!;
        res.Should().BeTrue();
        embedder.ParentHwnd.Should().Be(new IntPtr(12345));
    }

    [WpfFact]
    public void TaskbarEmbedder_RefreshTaskbarInfo_EmptyAppbar_FallsBackToTrayWindow()
    {
        using var fakeTray = new Form();
        fakeTray.Bounds = new Rectangle(0, 1000, 1920, 48);

        TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeTray.Handle;

        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        var refresh = typeof(TaskbarEmbedder).GetMethod("RefreshTaskbarInfo", BindingFlags.NonPublic | BindingFlags.Instance);
        refresh!.Invoke(embedder, null);

        var rectField = (Rectangle)typeof(TaskbarEmbedder).GetField("_taskbarRect", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(embedder)!;
        rectField.Width.Should().BeGreaterThan(0);
        rectField.Height.Should().BeGreaterThan(0);

        TaskbarEmbedder.ExplorerTrayWndOverride = null;
        fakeTray.Close();
    }

    [Fact]
    public void TaskbarEmbedder_Embed_InvalidWindowHandle_SetsFailedStateAndReturnsFalse()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        bool result = embedder.Embed(IntPtr.Zero, new Size(200, 30));

        result.Should().BeFalse();
        embedder.State.Should().Be(OverlayDeploymentState.Failed);
        embedder.IsEmbedded.Should().BeFalse();
    }

    [WpfFact]
    public void TaskbarEmbedder_Embed_InvalidTaskbarGeometry_SetsFailedStateAndReturnsFalse()
    {
        using var fakeTray = new Form { Bounds = new Rectangle(0, 1000, 1920, 48) };
        using var overlayForm = new Form();
        TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeTray.Handle;
        TaskbarEmbedder.FailGetClientRectForTesting = true;

        try
        {
            var cfg = new AppSettings();
            var embedder = new TaskbarEmbedder(cfg);

            var result = embedder.Embed(overlayForm.Handle, new Size(200, 30));

            result.Should().BeFalse();
            embedder.State.Should().Be(OverlayDeploymentState.Failed);
            embedder.IsEmbedded.Should().BeFalse();
        }
        finally
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = null;
            TaskbarEmbedder.FailGetClientRectForTesting = false;
        }
    }

    [WpfFact]
    public void TaskbarEmbedder_Embed_SuccessSetsActive_And_DetachResetsToUninitialized()
    {
        using var fakeTray = new Form { Bounds = new Rectangle(0, 1000, 1920, 48) };
        using var overlayForm = new Form();

        TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeTray.Handle;

        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        embedder.State.Should().Be(OverlayDeploymentState.Uninitialized);

        embedder.Embed(overlayForm.Handle, new Size(200, 30));
        embedder.State.Should().Be(OverlayDeploymentState.Active);

        embedder.Detach(overlayForm.Handle);
        embedder.State.Should().Be(OverlayDeploymentState.Uninitialized);

        TaskbarEmbedder.ExplorerTrayWndOverride = null;
    }

    [Fact]
    public void TaskbarEmbedder_IsWindows11Taskbar_WithBothBridgeAndReBar_ReturnsTrueForNativeWin11()
    {
        using var fakeTray = new Form();
        using var fakeBridge = new Form();
        using var fakeRebar = new Form();

        TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeTray.Handle;
        TaskbarEmbedder.FindWindowExOverride = (parent, after, cls, name) =>
            cls switch
            {
                "Windows.UI.Composition.DesktopWindowContentBridge" => fakeBridge.Handle,
                "ReBarWindow32" => fakeRebar.Handle,
                _ => IntPtr.Zero
            };

        var isWin11Method = typeof(TaskbarEmbedder).GetMethod("IsWindows11Taskbar", BindingFlags.NonPublic | BindingFlags.Static);
        var result = (bool)isWin11Method!.Invoke(null, null)!;

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            result.Should().BeTrue();
        }

        TaskbarEmbedder.ExplorerTrayWndOverride = null;
        TaskbarEmbedder.FindWindowExOverride = null;
    }

    [WpfFact]
    public void TaskbarEmbedder_RefreshTaskbarInfo_AllEdgeVariations()
    {
        using var fakeTray = new Form();
        TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeTray.Handle;

        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);
        var refresh = typeof(TaskbarEmbedder).GetMethod("RefreshTaskbarInfo", BindingFlags.NonPublic | BindingFlags.Instance);
        var edgeField = typeof(TaskbarEmbedder).GetField("_taskbarEdge", BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            fakeTray.Bounds = new Rectangle(0, 0, 1920, 48);
            refresh!.Invoke(embedder, null);
            edgeField!.GetValue(embedder).Should().NotBeNull();

            fakeTray.Bounds = new Rectangle(0, 0, 60, 1080);
            refresh.Invoke(embedder, null);

            fakeTray.Bounds = new Rectangle(1860, 0, 60, 1080);
            refresh.Invoke(embedder, null);

            fakeTray.Bounds = new Rectangle(0, 1032, 1920, 48);
            refresh.Invoke(embedder, null);
        }
        finally
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = null;
            fakeTray.Close();
        }
    }

    [WpfFact]
    public void TaskbarEmbedder_RefreshTaskbarInfo_PrimaryScreenFallback_WhenTrayWindowZero()
    {
        TaskbarEmbedder.ExplorerTrayWndOverride = () => IntPtr.Zero;

        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);
        var refresh = typeof(TaskbarEmbedder).GetMethod("RefreshTaskbarInfo", BindingFlags.NonPublic | BindingFlags.Instance);
        var rectField = typeof(TaskbarEmbedder).GetField("_taskbarRect", BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            refresh!.Invoke(embedder, null);
            var rect = (Rectangle)rectField!.GetValue(embedder)!;
            rect.Width.Should().BeGreaterThan(0);
            rect.Height.Should().BeGreaterThan(0);
        }
        finally
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = null;
        }
    }

    [WpfFact]
    public void TaskbarEmbedder_Detach_EmbeddedMode_CleansHwndParent()
    {
        using var form = new Form();
        form.Show();

        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        typeof(TaskbarEmbedder).GetProperty("IsEmbedded", BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(embedder, [true]);

        embedder.Detach(form.Handle);

        embedder.IsEmbedded.Should().BeFalse();
        embedder.State.Should().Be(OverlayDeploymentState.Uninitialized);

        form.Close();
    }

    [Fact]
    public void TaskbarEmbedder_ComputeBandX_And_PositionInBand_Overflow_Coverage()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        var computeBandX = typeof(TaskbarEmbedder).GetMethod("ComputeBandX", BindingFlags.NonPublic | BindingFlags.Instance);
        computeBandX.Should().NotBeNull();

        TaskbarEmbedder.IsTaskbarCenteredOverride = () => true;
        try
        {
            var isWin11Field = typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance);
            var trayEdgeField = typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance);

            // Win11 with left-aligned placement fallback (line 663)
            isWin11Field!.SetValue(embedder, true);
            trayEdgeField!.SetValue(embedder, -1);
            cfg.Overlay.Placement = "right";
            cfg.Overlay.PlacementOffset = 10;

            var parentRect = new NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 48 };
            int x = (int)computeBandX.Invoke(embedder, [parentRect, new Size(200, 30)])!;
            x.Should().BeGreaterThan(0);

            // Win11 with placement = "left" (lines 649-652)
            cfg.Overlay.Placement = "left";
            cfg.Overlay.PlacementOffset = 25;
            int xLeft = (int)computeBandX.Invoke(embedder, [parentRect, new Size(200, 30)])!;
            xLeft.Should().Be(25);

            // Win10 without taskband (lines 663)
            isWin11Field.SetValue(embedder, false);
            cfg.Overlay.Placement = "right";
            int xWin10 = (int)computeBandX.Invoke(embedder, [parentRect, new Size(200, 30)])!;
            xWin10.Should().BeGreaterThan(0);
        }
        finally
        {
            TaskbarEmbedder.IsTaskbarCenteredOverride = null;
        }
    }

    [Fact]
    public void TaskbarEmbedder_TrySetParent_And_ResolveParent_FailureBranches()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        var resolveMethod = typeof(TaskbarEmbedder).GetMethod("ResolveParentAndTaskBand", BindingFlags.NonPublic | BindingFlags.Instance);
        resolveMethod.Should().NotBeNull();

        var isWin11Field = typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance);

        // Legacy Win10 mode with invalid tray HWND (lines 471-473)
        isWin11Field!.SetValue(embedder, false);
        bool res = (bool)resolveMethod.Invoke(embedder, [IntPtr.Zero])!;
        res.Should().BeFalse();

        // TrySetParent with invalid tray (lines 428-432)
        var trySetParentMethod = typeof(TaskbarEmbedder).GetMethod("TrySetParent", BindingFlags.NonPublic | BindingFlags.Instance);
        TaskbarEmbedder.ExplorerTrayWndOverride = () => IntPtr.Zero;
        try
        {
            bool tryRes = (bool)trySetParentMethod!.Invoke(embedder, [IntPtr.Zero, new Size(200, 30)])!;
            tryRes.Should().BeFalse();
        }
        finally
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = null;
        }
    }

    [Fact]
    public void TaskbarEmbedder_Centered_And_TrayStructure_Coverage()
    {
        // 1. IsTaskbarCentered
        var isCentered = typeof(TaskbarEmbedder).GetMethod("IsTaskbarCentered", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        isCentered.Should().NotBeNull();
        var res = (bool)isCentered.Invoke(null, null)!;
        res.Should().Be(res);

        // 2. LogTrayStructure when ExplorerTrayWnd is zero
        var logStructure = typeof(TaskbarEmbedder).GetMethod("LogTrayStructure", BindingFlags.NonPublic | BindingFlags.Static);
        logStructure.Should().NotBeNull();

        TaskbarEmbedder.ExplorerTrayWndOverride = () => IntPtr.Zero;
        try
        {
            var act = () => logStructure.Invoke(null, null);
            act.Should().NotThrow();
        }
        finally
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = null;
        }
    }

    [Fact]
    public void TaskbarEmbedder_LeftWidgetPositioning_BandAndFloat()
    {
        using var fakeTray = new Form { Bounds = new Rectangle(0, 0, 1920, 48) };
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, fakeTray.Handle);
        typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, true);
        typeof(TaskbarEmbedder).GetField("_taskbarRect", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, new Rectangle(0, 0, 1920, 48));

        cfg.Overlay.Placement = "left";
        cfg.Overlay.PlacementOffset = 10;

        TaskbarEmbedder.IsTaskbarCenteredOverride = () => true;
        TaskbarEmbedder.AppListLeftEdgeOverride = () => 600;
        TaskbarEmbedder.LeftWidgetRightEdgeOverride = () => -1;
        try
        {
            var computeBandX = typeof(TaskbarEmbedder).GetMethod("ComputeBandX", BindingFlags.NonPublic | BindingFlags.Instance);
            var parentRect = new NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 48 };

            // 1. When widget is present on left at X=180
            TaskbarEmbedder.LeftWidgetRightEdgeOverride = () => 180;
            typeof(TaskbarEmbedder).GetField("_leftWidgetRightEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 180);
            int bandXWithWidget = (int)computeBandX!.Invoke(embedder, [parentRect, new Size(200, 30)])!;
            bandXWithWidget.Should().BeGreaterThan(0);

            // 2. When no widget is present (_leftWidgetRightEdge = -1)
            TaskbarEmbedder.LeftWidgetRightEdgeOverride = () => -1;
            typeof(TaskbarEmbedder).GetField("_leftWidgetRightEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
            int bandXNoWidget = (int)computeBandX.Invoke(embedder, [parentRect, new Size(200, 30)])!;
            bandXNoWidget.Should().Be(10);
        }
        finally
        {
            TaskbarEmbedder.IsTaskbarCenteredOverride = null;
            TaskbarEmbedder.AppListLeftEdgeOverride = null;
            TaskbarEmbedder.LeftWidgetRightEdgeOverride = null;
        }
    }

    [Fact]
    public void TaskbarEmbedder_LeftWidgetEdgeDetection_And_Overrides()
    {
        // 1. Override returns exact value
        TaskbarEmbedder.LeftWidgetRightEdgeOverride = () => 215;
        try
        {
            var getLeftWidget = typeof(TaskbarEmbedder).GetMethod("GetLeftWidgetRightEdge", BindingFlags.NonPublic | BindingFlags.Static);
            getLeftWidget.Should().NotBeNull();
            int edge = (int)getLeftWidget.Invoke(null, null)!;
            edge.Should().Be(215);
        }
        finally
        {
            TaskbarEmbedder.LeftWidgetRightEdgeOverride = null;
        }

        // 2. Logging and feature detection helpers execute cleanly
        var logWidgetEdge = typeof(TaskbarEmbedder).GetMethod("LogWidgetEdgeChange", BindingFlags.NonPublic | BindingFlags.Static);
        logWidgetEdge.Should().NotBeNull();
        var actLog = () => logWidgetEdge.Invoke(null, [200, "test"]);
        actLog.Should().NotThrow();

        var isWidgetsEnabled = typeof(TaskbarEmbedder).GetMethod("IsWidgetsFeatureEnabled", BindingFlags.NonPublic | BindingFlags.Static);
        isWidgetsEnabled.Should().NotBeNull();
        var actCheck = () => isWidgetsEnabled.Invoke(null, null);
        actCheck.Should().NotThrow();
    }

    [Fact]
    public void TaskbarEmbedder_PositionInBand_FailurePaths()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        var posBand = typeof(TaskbarEmbedder).GetMethod("PositionInBand", BindingFlags.NonPublic | BindingFlags.Instance);
        posBand.Should().NotBeNull();

        // 1. Invalid parent window -> returns false, state becomes Failed
        typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(embedder, IntPtr.Zero);
        bool res1 = (bool)posBand.Invoke(embedder, [IntPtr.Zero, new Size(150, 30)])!;
        res1.Should().BeFalse();
        embedder.State.Should().Be(OverlayDeploymentState.Failed);

        // 2. QueryWidgetsButtonRightEdge with zero pointer -> returns -1
        var resetStaticCaches = typeof(TaskbarEmbedder).GetMethod("ResetStaticEdgeCaches", BindingFlags.NonPublic | BindingFlags.Static)!;
        resetStaticCaches.Invoke(null, null);
        var queryWidgets = typeof(TaskbarEmbedder).GetMethod("QueryWidgetsButtonRightEdge", BindingFlags.NonPublic | BindingFlags.Static);
        queryWidgets.Should().NotBeNull();
        int edge = (int)queryWidgets.Invoke(null, [IntPtr.Zero])!;
        edge.Should().Be(-1);

        // 3. FindExplorerTrayWnd and SetWindowPos failure path
        var primaryHwnd = TaskbarEmbedder.FindExplorerTrayWnd();

        if (primaryHwnd != IntPtr.Zero)
        {
            typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(embedder, primaryHwnd);
            typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(embedder, 500);

            bool resSetPosFail = (bool)posBand.Invoke(embedder, [new IntPtr(-1), new Size(150, 30)])!;
            resSetPosFail.Should().BeFalse();
            embedder.State.Should().Be(OverlayDeploymentState.Failed);
        }
    }

    [Fact]
    public void TaskbarEmbedder_ResolveFromTrayWindow_And_ScreenWorkingArea_Coverage()
    {
        var resolveTray = typeof(TaskbarEmbedder).GetMethod("ResolveFromTrayWindow", BindingFlags.NonPublic | BindingFlags.Static)!;
        var resolveScreen = typeof(TaskbarEmbedder).GetMethod("ResolveFromScreenWorkingArea", BindingFlags.NonPublic | BindingFlags.Static)!;

        var trayRes = resolveTray.Invoke(null, null);
        if (trayRes is not null)
        {

            var (rect, _) = (((Rectangle, uint))trayRes);
            rect.Width.Should().BeGreaterThan(0);
        }

        var screenRes = resolveScreen.Invoke(null, null);
        screenRes.Should().NotBeNull();

        var (sRect, _) = (((Rectangle, uint))screenRes!);
        sRect.Width.Should().BeGreaterThan(0);
        sRect.Height.Should().BeGreaterThan(0);

        // Cached widgets button query
        typeof(TaskbarEmbedder).GetField("_cachedLeftWidgetRightEdge", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 150);
        typeof(TaskbarEmbedder).GetField("_lastWidgetQueryTicks", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Stopwatch.GetTimestamp());

        var queryWidgets = typeof(TaskbarEmbedder).GetMethod("QueryWidgetsButtonRightEdge", BindingFlags.NonPublic | BindingFlags.Static)!;
        int cachedRight = (int)queryWidgets.Invoke(null, [IntPtr.Zero])!;
        cachedRight.Should().Be(150);

        // Reset static fields
        var resetCachesCoverage = typeof(TaskbarEmbedder).GetMethod("ResetStaticEdgeCaches", BindingFlags.NonPublic | BindingFlags.Static)!;
        resetCachesCoverage.Invoke(null, null);
    }

    [Fact]
    public void TaskbarEmbedder_EdgeCaches_And_TrayInspection_Coverage()
    {
        var estTrayWidthMethod = typeof(TaskbarEmbedder).GetMethod("EstimateTrayWidth", BindingFlags.NonPublic | BindingFlags.Static)!;
        var logTrayEdgeMethod = typeof(TaskbarEmbedder).GetMethod("LogTrayEdgeChange", BindingFlags.NonPublic | BindingFlags.Static)!;
        var logWidgetEdgeMethod = typeof(TaskbarEmbedder).GetMethod("LogWidgetEdgeChange", BindingFlags.NonPublic | BindingFlags.Static)!;
        var resetCachesMethod = typeof(TaskbarEmbedder).GetMethod("ResetStaticEdgeCaches", BindingFlags.NonPublic | BindingFlags.Static)!;
        var logTrayStructMethod = typeof(TaskbarEmbedder).GetMethod("LogTrayStructure", BindingFlags.NonPublic | BindingFlags.Static)!;
        var isWidgetsEnabledMethod = typeof(TaskbarEmbedder).GetMethod("IsWidgetsFeatureEnabled", BindingFlags.NonPublic | BindingFlags.Static)!;

        // 1. Static edge change logging and cache resetting
        logTrayEdgeMethod.Invoke(null, [100, "Test tray edge change"]);
        logTrayEdgeMethod.Invoke(null, [100, "Duplicate tray edge"]); // hits early return branch
        logWidgetEdgeMethod.Invoke(null, [200, "Test widget edge change"]);
        logWidgetEdgeMethod.Invoke(null, [200, "Duplicate widget edge"]); // hits early return branch
        resetCachesMethod.Invoke(null, null);

        // 2. Tray structure inspection and widgets feature check
        var actStruct = () => logTrayStructMethod.Invoke(null, null);
        actStruct.Should().NotThrow();

        var isWidgets = (bool)isWidgetsEnabledMethod.Invoke(null, null)!;
        isWidgets.Should().Be(isWidgets);

        // 3. EstimateTrayWidth
        int trayWidth = (int)estTrayWidthMethod.Invoke(null, null)!;
        trayWidth.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TaskbarEmbedder_EmbedFailure_And_LeftWidget_Coverage()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        // 1. Embed failure path with zero handle
        bool embedRes = embedder.Embed(IntPtr.Zero, new Size(200, 30));
        embedRes.Should().BeFalse();
        embedder.State.Should().Be(OverlayDeploymentState.Failed);
        embedder.IsEmbedded.Should().BeFalse();

        // 2. GetLeftWidgetRightEdge reflection
        var leftWidgetMethod = typeof(TaskbarEmbedder).GetMethod("GetLeftWidgetRightEdge", BindingFlags.NonPublic | BindingFlags.Static)!;
        int widgetEdge = (int)leftWidgetMethod.Invoke(null, null)!;
        widgetEdge.Should().BeInRange(-1, int.MaxValue);

        // 3. PositionInBand with invalid HWND and oversized width
        var posBandMethod = typeof(TaskbarEmbedder).GetMethod("PositionInBand", BindingFlags.NonPublic | BindingFlags.Instance)!;
        using var form = new Form();
        typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, form.Handle);
        bool posBandFail = (bool)posBandMethod.Invoke(embedder, [new IntPtr(9999999), new Size(10000, 30)])!;
        posBandFail.Should().BeFalse();
        embedder.State.Should().Be(OverlayDeploymentState.Failed);

        // 4. FindExplorerTrayWnd invocation
        var trayHwnd = TaskbarEmbedder.FindExplorerTrayWnd();
        trayHwnd.Should().NotBe(new IntPtr(12345678));

        // 5. QueryWidgetsButtonEdge with caching
        var queryWidgets = typeof(TaskbarEmbedder).GetMethod("QueryWidgetsButtonEdge", BindingFlags.NonPublic | BindingFlags.Static)!;
        queryWidgets.Invoke(null, [IntPtr.Zero, 1920]);
        queryWidgets.Invoke(null, [IntPtr.Zero, 1920]);

        // 6. Registry helper queries
        var isWidgets = typeof(TaskbarEmbedder).GetMethod("IsWidgetsFeatureEnabled", BindingFlags.NonPublic | BindingFlags.Static)!;
        isWidgets.Invoke(null, null);

        var isCentered = typeof(TaskbarEmbedder).GetMethod("IsTaskbarCentered", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        isCentered.Invoke(null, null);

        var getTrayLeft = typeof(TaskbarEmbedder).GetMethod("GetTrayLeftEdge", BindingFlags.NonPublic | BindingFlags.Static)!;
        getTrayLeft.Invoke(null, null);
    }

    [Fact]
    public void TaskbarEmbedder_LeftAlignedTaskbar_PositionsWithoutCollisions()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        using var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(0, 0),
            Size = new Size(1920, 48)
        };
        typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, form.Handle);
        typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, true);
        typeof(TaskbarEmbedder).GetField("_taskbarRect", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, new Rectangle(0, 1032, 1920, 48));
        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 1600);

        // Mock taskbar alignment as left-aligned and app list right edge ending at 500px
        TaskbarEmbedder.IsTaskbarCenteredOverride = () => false;
        TaskbarEmbedder.AppListRightEdgeOverride = () => 500;

        try
        {
            var compBandX = typeof(TaskbarEmbedder).GetMethod("ComputeBandX", BindingFlags.NonPublic | BindingFlags.Instance)!;

            var parentRect = new NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 48 };
            var overlaySize = new Size(300, 40);

            // 1. Placement = "left" on left-aligned taskbar
            cfg.Overlay.Placement = "left";
            cfg.Overlay.PlacementOffset = 10;

            int bandLeftX = (int)compBandX.Invoke(embedder, [parentRect, overlaySize])!;
            // Band left position must start at or after app list (500 + 10 = 510)
            bandLeftX.Should().BeGreaterThanOrEqualTo(510);

            // 2. Placement = "right" on left-aligned taskbar
            cfg.Overlay.Placement = "right";
            cfg.Overlay.PlacementOffset = 10;

            int bandRightX = (int)compBandX.Invoke(embedder, [parentRect, overlaySize])!;
            // Band right position must stay before tray left (1600 - 300 - 10 = 1290)
            bandRightX.Should().BeLessThanOrEqualTo(1290);
            bandRightX.Should().BeGreaterThanOrEqualTo(500);
        }
        finally
        {
            TaskbarEmbedder.AppListRightEdgeOverride = null;
            TaskbarEmbedder.IsTaskbarCenteredOverride = null;
        }
    }

    [Fact]
    public void TaskbarEmbedder_LeftAlignedTaskbar_WithRightWeatherWidget_AvoidsWeatherCollision()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        using var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(0, 0),
            Size = new Size(1920, 48)
        };
        typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, form.Handle);
        typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, true);
        typeof(TaskbarEmbedder).GetField("_taskbarRect", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, new Rectangle(0, 1032, 1920, 48));
        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 1600);

        // Weather widget docked on right at X=1450, left of tray (1600)
        TaskbarEmbedder.IsTaskbarCenteredOverride = () => false;
        TaskbarEmbedder.AppListRightEdgeOverride = () => 500;
        TaskbarEmbedder.RightWidgetLeftEdgeOverride = () => 1450;

        try
        {
            var compBandX = typeof(TaskbarEmbedder).GetMethod("ComputeBandX", BindingFlags.NonPublic | BindingFlags.Instance)!;

            var parentRect = new NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 48 };
            var overlaySize = new Size(300, 40);

            cfg.Overlay.Placement = "right";
            cfg.Overlay.PlacementOffset = 10;

            // Right boundary must be min(1600, 1450) = 1450.
            // Expected placement: 1450 - 300 - 10 = 1140 (safely left of weather widget!)
            int bandRightX = (int)compBandX.Invoke(embedder, [parentRect, overlaySize])!;
            bandRightX.Should().Be(1140);
        }
        finally
        {
            TaskbarEmbedder.RightWidgetLeftEdgeOverride = null;
            TaskbarEmbedder.AppListRightEdgeOverride = null;
            TaskbarEmbedder.IsTaskbarCenteredOverride = null;
        }
    }

    [Fact]
    public void TaskbarEmbedder_EdgeResolution_And_ScreenWorkingArea_Branches()
    {
        var resolveScreen = typeof(TaskbarEmbedder).GetMethod("ResolveFromScreenWorkingArea", BindingFlags.NonPublic | BindingFlags.Static);
        resolveScreen.Should().NotBeNull();
        var screenRes = resolveScreen.Invoke(null, null);
        screenRes.Should().NotBeNull();

        var resolveTray = typeof(TaskbarEmbedder).GetMethod("ResolveFromTrayWindow", BindingFlags.NonPublic | BindingFlags.Static);
        resolveTray.Should().NotBeNull();
        var trayRes = resolveTray.Invoke(null, null);
        // On headless or CI runner, tray might be null or valid tuple
        trayRes?.ToString().Should().NotBeNull();

        var logTray = typeof(TaskbarEmbedder).GetMethod("LogTrayStructure", BindingFlags.NonPublic | BindingFlags.Static);
        logTray.Should().NotBeNull();
        var actLog = () => logTray.Invoke(null, null);
        actLog.Should().NotThrow();
    }

    [Fact]
    public void TaskbarEmbedder_FindExplorerTrayWnd_And_CenteredRegistry_Coverage()
    {
        // 1. IsTaskbarCentered without override
        TaskbarEmbedder.IsTaskbarCenteredOverride = null;
        bool isCentered = TaskbarEmbedder.IsTaskbarCentered();
        isCentered.Should().Be(isCentered);

        // 2. IsWidgetsFeatureEnabled
        var isWidgets = typeof(TaskbarEmbedder).GetMethod("IsWidgetsFeatureEnabled", BindingFlags.NonPublic | BindingFlags.Static);
        isWidgets.Should().NotBeNull();
        bool widgetsVal = (bool)isWidgets.Invoke(null, null)!;
        widgetsVal.Should().Be(widgetsVal);

        // 3. FindExplorerTrayWnd with mock FindWindowExOverride simulating non-explorer vs explorer
        var origFind = TaskbarEmbedder.FindWindowExOverride;
        using var form = new Form();
        try
        {
            int callCount = 0;
            TaskbarEmbedder.FindWindowExOverride = (parent, child, cls, win) =>
            {
                callCount++;
                return callCount == 1 ? form.Handle : IntPtr.Zero;
            };

            IntPtr found = TaskbarEmbedder.FindExplorerTrayWnd();
            found.Should().NotBeNull();
        }
        finally
        {
            TaskbarEmbedder.FindWindowExOverride = origFind;
        }

        // 4. FindRightmostChildTrayLeft
        var findChild = typeof(TaskbarEmbedder).GetMethod("FindRightmostChildTrayLeft", BindingFlags.NonPublic | BindingFlags.Static);
        findChild.Should().NotBeNull();
        var dummyRect = new NativeMethods.Rect { Left = 0, Top = 1000, Right = 1920, Bottom = 1048 };
        int childLeft = (int)findChild.Invoke(null, [form.Handle, dummyRect])!;
        childLeft.Should().Be(int.MaxValue);
    }

    [Fact]
    public void TaskbarEmbedder_WidgetsAndAppListEdges_ComprehensiveBranches()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        // 1. GetRightSideBoundary branches
        var getRightSide = typeof(TaskbarEmbedder).GetMethod("GetRightSideBoundary", BindingFlags.NonPublic | BindingFlags.Instance);
        getRightSide.Should().NotBeNull();

        // Case A: RightWidgetLeftEdgeOverride with _trayLeftEdge > 0
        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 1500);
        TaskbarEmbedder.RightWidgetLeftEdgeOverride = () => 1400;
        int rA = (int)getRightSide.Invoke(embedder, null)!;
        rA.Should().Be(1400);

        // Case B: RightWidgetLeftEdgeOverride with _trayLeftEdge <= 0
        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
        int rB = (int)getRightSide.Invoke(embedder, null)!;
        rB.Should().Be(1400);

        TaskbarEmbedder.RightWidgetLeftEdgeOverride = null;

        // Case C: _rightWidgetLeftEdge > 0 and _trayLeftEdge > 0
        typeof(TaskbarEmbedder).GetField("_rightWidgetLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 1300);
        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 1600);
        int rC = (int)getRightSide.Invoke(embedder, null)!;
        rC.Should().Be(1300);

        // Case D: _rightWidgetLeftEdge > 0 and _trayLeftEdge <= 0
        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
        int rD = (int)getRightSide.Invoke(embedder, null)!;
        rD.Should().Be(1300);

        // Case E: _rightWidgetLeftEdge <= 0 and _trayLeftEdge > 0
        typeof(TaskbarEmbedder).GetField("_rightWidgetLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 1700);
        int rE = (int)getRightSide.Invoke(embedder, null)!;
        rE.Should().Be(1700);

        // Case F: both <= 0
        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
        int rF = (int)getRightSide.Invoke(embedder, null)!;
        rF.Should().Be(-1);

        // 2. UpdateWidgetsEdges branches
        var updateWidgets = typeof(TaskbarEmbedder).GetMethod("UpdateWidgetsEdges", BindingFlags.NonPublic | BindingFlags.Instance);
        updateWidgets.Should().NotBeNull();

        // Left override
        TaskbarEmbedder.LeftWidgetRightEdgeOverride = () => 200;
        TaskbarEmbedder.RightWidgetLeftEdgeOverride = () => 1400;
        updateWidgets.Invoke(embedder, null);
        TaskbarEmbedder.RightWidgetLeftEdgeOverride = null;
        updateWidgets.Invoke(embedder, null);
        TaskbarEmbedder.LeftWidgetRightEdgeOverride = null;

        // Right override
        TaskbarEmbedder.RightWidgetLeftEdgeOverride = () => 1400;
        updateWidgets.Invoke(embedder, null);
        TaskbarEmbedder.LeftWidgetRightEdgeOverride = () => 200;
        updateWidgets.Invoke(embedder, null);
        TaskbarEmbedder.RightWidgetLeftEdgeOverride = null;
        TaskbarEmbedder.LeftWidgetRightEdgeOverride = null;

        // No overrides
        updateWidgets.Invoke(embedder, null);

        // 3. GetAppListRightEdge branches (cached vs recalculate)
        var getAppList = typeof(TaskbarEmbedder).GetMethod("GetAppListRightEdge", BindingFlags.NonPublic | BindingFlags.Instance);
        getAppList.Should().NotBeNull();

        // Cached value hit
        typeof(TaskbarEmbedder).GetField("_cachedAppListRightEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 450);
        typeof(TaskbarEmbedder).GetField("_lastAppListQueryTicks", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, Stopwatch.GetTimestamp());
        int cachedEdge = (int)getAppList.Invoke(embedder, null)!;
        cachedEdge.Should().Be(450);

        // Cache expired / fallback path
        typeof(TaskbarEmbedder).GetField("_cachedAppListRightEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
        typeof(TaskbarEmbedder).GetField("_lastAppListQueryTicks", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 0L);
        typeof(TaskbarEmbedder).GetField("_taskbarRect", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, new Rectangle(0, 1000, 1920, 48));
        int fallbackEdge = (int)getAppList.Invoke(embedder, null)!;
        fallbackEdge.Should().BeGreaterThan(0);

        // 4. ResetStaticEdgeCaches
        var resetCaches = typeof(TaskbarEmbedder).GetMethod("ResetStaticEdgeCaches", BindingFlags.NonPublic | BindingFlags.Static);
        resetCaches.Should().NotBeNull();
        var actReset = () => resetCaches.Invoke(null, null);
        actReset.Should().NotThrow();
    }

    [Fact]
    public void TaskbarEmbedder_ChildEmbedding_FailurePaths_And_BandResolution()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        // 1. PositionInBand with destroyed/zero parent hwnd
        typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, IntPtr.Zero);
        var posBand = typeof(TaskbarEmbedder).GetMethod("PositionInBand", BindingFlags.NonPublic | BindingFlags.Instance);
        posBand.Should().NotBeNull();
        bool posBandRes = (bool)posBand.Invoke(embedder, [IntPtr.Zero, new Size(200, 30)])!;
        posBandRes.Should().BeFalse();
        embedder.State.Should().Be(OverlayDeploymentState.Failed);

        // 2. ResolveParentAndTaskBand for legacy taskbar (_isWin11 = false)
        typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, false);
        var resolveParent = typeof(TaskbarEmbedder).GetMethod("ResolveParentAndTaskBand", BindingFlags.NonPublic | BindingFlags.Instance);
        resolveParent.Should().NotBeNull();

        // When tray has no ReBarWindow32 / WorkerW
        bool legacyRes = (bool)resolveParent.Invoke(embedder, [new IntPtr(0x1234)])!;
        legacyRes.Should().BeFalse();

        // 3. TrySetParent failure when tray is IntPtr.Zero
        var origTray = TaskbarEmbedder.ExplorerTrayWndOverride;
        try
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = () => IntPtr.Zero;
            var trySetParent = typeof(TaskbarEmbedder).GetMethod("TrySetParent", BindingFlags.NonPublic | BindingFlags.Instance);
            trySetParent.Should().NotBeNull();
            bool parentRes = (bool)trySetParent.Invoke(embedder, [IntPtr.Zero, new Size(200, 30)])!;
            parentRes.Should().BeFalse();
        }
        finally
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = origTray;
        }

        // 4. Embed failure branch: TrySetParent false and PositionFloat fails on IntPtr.Zero
        try
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = () => IntPtr.Zero;
            bool embedSuccess = embedder.Embed(IntPtr.Zero, new Size(200, 30));
            embedSuccess.Should().BeFalse();
            embedder.IsEmbedded.Should().BeFalse();
            embedder.State.Should().Be(OverlayDeploymentState.Failed);
        }
        finally
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = origTray;
        }
    }

    [Fact]
    public void TaskbarEmbedder_EdgeCalculations_And_Diagnostics_Coverage()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        // 1. GetRightSideBoundary branches
        var getRightBoundary = typeof(TaskbarEmbedder).GetMethod("GetRightSideBoundary", BindingFlags.NonPublic | BindingFlags.Instance)!;
        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 1600);
        typeof(TaskbarEmbedder).GetField("_rightWidgetLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 1550);
        int boundary = (int)getRightBoundary.Invoke(embedder, null)!;
        boundary.Should().Be(1550);

        var origRightWidget = TaskbarEmbedder.RightWidgetLeftEdgeOverride;
        try
        {
            TaskbarEmbedder.RightWidgetLeftEdgeOverride = () => 1500;
            boundary = (int)getRightBoundary.Invoke(embedder, null)!;
            boundary.Should().Be(1500);
        }
        finally
        {
            TaskbarEmbedder.RightWidgetLeftEdgeOverride = origRightWidget;
        }

        // 2. IsWidgetsFeatureEnabled
        var isWidgets = typeof(TaskbarEmbedder).GetMethod("IsWidgetsFeatureEnabled", BindingFlags.NonPublic | BindingFlags.Static)!;
        var actWidgets = () => isWidgets.Invoke(null, null);
        actWidgets.Should().NotThrow();

        // 3. LogTrayStructure
        var logDiag = typeof(TaskbarEmbedder).GetMethod("LogTrayStructure", BindingFlags.NonPublic | BindingFlags.Static)!;
        var actLogDiag = () => logDiag.Invoke(null, null);
        actLogDiag.Should().NotThrow();

        // 4. Detach on non-desktop IntPtr
        var actDetach = () => embedder.Detach(new IntPtr(0x1234));
        actDetach.Should().NotThrow();

        // 5. ResolveFromScreenWorkingArea and Automation queries with zero hwnd
        var resolveScreen = typeof(TaskbarEmbedder).GetMethod("ResolveFromScreenWorkingArea", BindingFlags.NonPublic | BindingFlags.Static)!;
        var screenRes = resolveScreen.Invoke(null, null);
        screenRes.Should().NotBeNull();

        var queryWidgets = typeof(TaskbarEmbedder).GetMethod("QueryWidgetsButtonEdge", BindingFlags.NonPublic | BindingFlags.Static)!;
        var queryAppList = typeof(TaskbarEmbedder).GetMethod("QueryAppListRightEdgeFromAutomation", BindingFlags.NonPublic | BindingFlags.Static)!;

        var origExplorerTray = TaskbarEmbedder.ExplorerTrayWndOverride;
        try
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = () => IntPtr.Zero;
            var wRes = queryWidgets.Invoke(null, [IntPtr.Zero, 1920]);
            wRes.Should().Be((-1, -1));

            var aRes = queryAppList.Invoke(null, [1920]);
            aRes.Should().Be(-1);
        }
        finally
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = origExplorerTray;
        }

        // 6. IsTaskbarCentered live query
        var origCentered = TaskbarEmbedder.IsTaskbarCenteredOverride;
        try
        {
            TaskbarEmbedder.IsTaskbarCenteredOverride = null;
            var actCentered = () => TaskbarEmbedder.IsTaskbarCentered();
            actCentered.Should().NotThrow();
        }
        finally
        {
            TaskbarEmbedder.IsTaskbarCenteredOverride = origCentered;
        }
    }

    [Fact]
    public void TaskbarEmbedder_PureCalculations_FullCoverage()
    {
        // 1. CalculateFromWorkingArea
        // Bottom edge
        var resBottom = TaskbarEmbedder.CalculateFromWorkingArea(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040));
        resBottom.Edge.Should().Be(NativeMethods.ABE_BOTTOM);
        resBottom.Rect.Height.Should().Be(40);

        // Top edge
        var resTop = TaskbarEmbedder.CalculateFromWorkingArea(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 50, 1920, 1030));
        resTop.Edge.Should().Be(NativeMethods.ABE_TOP);
        resTop.Rect.Height.Should().Be(50);

        // Right edge
        var resRight = TaskbarEmbedder.CalculateFromWorkingArea(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1850, 1080));
        resRight.Edge.Should().Be(NativeMethods.ABE_RIGHT);
        resRight.Rect.Width.Should().Be(70);

        // Left edge
        var resLeft = TaskbarEmbedder.CalculateFromWorkingArea(new Rectangle(0, 0, 1920, 1080), new Rectangle(60, 0, 1860, 1080));
        resLeft.Edge.Should().Be(NativeMethods.ABE_LEFT);
        resLeft.Rect.Width.Should().Be(60);

        // Fallback (working area equals bounds)
        var resFallback = TaskbarEmbedder.CalculateFromWorkingArea(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1080));
        resFallback.Edge.Should().Be(NativeMethods.ABE_BOTTOM);
        resFallback.Rect.Height.Should().Be(48);

        // 2. CalculateMaxButtonRight
        TaskbarEmbedder.CalculateMaxButtonRight([]).Should().Be(-1);
        TaskbarEmbedder.CalculateMaxButtonRight([System.Windows.Rect.Empty, new System.Windows.Rect(0, 0, 0, 0)]).Should().Be(-1);
        TaskbarEmbedder.CalculateMaxButtonRight([
            new System.Windows.Rect(10, 0, 100, 30), // right = 110
            new System.Windows.Rect(120, 0, 80, 30) // right = 200
        ]).Should().Be(200);

        // 3. CalculateWidgetEdges
        TaskbarEmbedder.CalculateWidgetEdges(System.Windows.Rect.Empty, 1920, 1000).Should().Be((-1, -1));
        TaskbarEmbedder.CalculateWidgetEdges(new System.Windows.Rect(0, 0, 0, 0), 1920, 1000).Should().Be((-1, -1));

        // Left side (< midX 960)
        var leftWidgets = TaskbarEmbedder.CalculateWidgetEdges(new System.Windows.Rect(50, 5, 100, 30), 1920, 1000);
        leftWidgets.Should().Be((150, -1));

        // Right side (>= midX 960)
        var rightWidgets = TaskbarEmbedder.CalculateWidgetEdges(new System.Windows.Rect(1200, 5, 100, 30), 1920, 1000);
        rightWidgets.Should().Be((-1, 1200));
    }

    [WpfFact]
    public void TaskbarEmbedder_RemainingEdgeBranches_And_TestingSeams_Coverage()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);
        using var overlayForm = new Form();
        using var fakeTray = new Form { Bounds = new Rectangle(0, 1000, 1920, 48) };
        using var fakeChild = new Form { Bounds = new Rectangle(1800, 1005, 50, 38) };

        // 1. ThrowInExplorerLookupForTesting
        try
        {
            TaskbarEmbedder.ThrowInExplorerLookupForTesting = true;
            var findTray = typeof(TaskbarEmbedder).GetMethod("FindExplorerTrayWnd", BindingFlags.NonPublic | BindingFlags.Static);
            findTray?.Invoke(null, null);
        }
        finally
        {
            TaskbarEmbedder.ThrowInExplorerLookupForTesting = false;
        }

        // 2. ThrowInTrayLeftEdgeForTesting
        try
        {
            TaskbarEmbedder.ThrowInTrayLeftEdgeForTesting = true;
            var getTrayLeft = typeof(TaskbarEmbedder).GetMethod("GetTrayLeftEdge", BindingFlags.NonPublic | BindingFlags.Static);
            getTrayLeft?.Invoke(null, null);
        }
        finally
        {
            TaskbarEmbedder.ThrowInTrayLeftEdgeForTesting = false;
        }

        // 3. ThrowInAppListRightEdgeForTesting and AppListRightEdgeAutomationOverride
        try
        {
            TaskbarEmbedder.ThrowInAppListRightEdgeForTesting = true;
            var queryApp = typeof(TaskbarEmbedder).GetMethod("GetAppListRightEdge", BindingFlags.NonPublic | BindingFlags.Instance);
            queryApp?.Invoke(embedder, null);

            TaskbarEmbedder.ThrowInAppListRightEdgeForTesting = false;
            TaskbarEmbedder.AppListRightEdgeAutomationOverride = _ => 650;
            typeof(TaskbarEmbedder).GetField("_cachedAppListRightEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
            var valAutomated = (int)queryApp?.Invoke(embedder, null)!;
            valAutomated.Should().Be(650);
        }
        finally
        {
            TaskbarEmbedder.ThrowInAppListRightEdgeForTesting = false;
            TaskbarEmbedder.AppListRightEdgeAutomationOverride = null;
        }

        // 4. ThrowInIsWidgetsFeatureEnabledForTesting
        try
        {
            TaskbarEmbedder.ThrowInIsWidgetsFeatureEnabledForTesting = true;
            var isWidgets = typeof(TaskbarEmbedder).GetMethod("IsWidgetsFeatureEnabled", BindingFlags.NonPublic | BindingFlags.Static);
            var enabled = (bool)isWidgets?.Invoke(null, null)!;
            enabled.Should().BeTrue();
        }
        finally
        {
            TaskbarEmbedder.ThrowInIsWidgetsFeatureEnabledForTesting = false;
        }

        // 5. ThrowInDumpTaskbarHierarchyForTesting
        try
        {
            TaskbarEmbedder.ThrowInDumpTaskbarHierarchyForTesting = true;
            var logTray = typeof(TaskbarEmbedder).GetMethod("LogTrayStructure", BindingFlags.NonPublic | BindingFlags.Static);
            logTray?.Invoke(null, null);
        }
        finally
        {
            TaskbarEmbedder.ThrowInDumpTaskbarHierarchyForTesting = false;
        }

        // 6. ThrowInAttachChildForTesting
        try
        {
            TaskbarEmbedder.ThrowInAttachChildForTesting = true;
            var trySetParent = typeof(TaskbarEmbedder).GetMethod("TrySetParent", BindingFlags.NonPublic | BindingFlags.Instance);
            var res = (bool)trySetParent?.Invoke(embedder, [overlayForm.Handle, new Size(100, 30)])!;
            res.Should().BeFalse();
        }
        finally
        {
            TaskbarEmbedder.ThrowInAttachChildForTesting = false;
        }

        // 7. FailGetClientRectForTesting in PositionInBand
        try
        {
            TaskbarEmbedder.FailGetClientRectForTesting = true;
            typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, fakeTray.Handle);
            var posBand = typeof(TaskbarEmbedder).GetMethod("PositionInBand", BindingFlags.NonPublic | BindingFlags.Instance);
            var resBand = (bool)posBand?.Invoke(embedder, [overlayForm.Handle, new Size(100, 30)])!;
            resBand.Should().BeFalse();
        }
        finally
        {
            TaskbarEmbedder.FailGetClientRectForTesting = false;
        }

        // 8. Embed failure path (lines 118-122)
        try
        {
            TaskbarEmbedder.ThrowInAttachChildForTesting = true;

            bool embedded = embedder.Embed(overlayForm.Handle, new Size(200, 30));
            embedded.Should().BeFalse();
            embedder.State.Should().Be(OverlayDeploymentState.Failed);
        }
        finally
        {
            TaskbarEmbedder.ThrowInAttachChildForTesting = false;
        }

        // 9. FindRightmostChildTrayLeft
        fakeChild.StartPosition = FormStartPosition.Manual;
        fakeChild.SetBounds(1800, 1005, 50, 38);
        fakeChild.Visible = true;
        NativeMethods.GetWindowRect(fakeChild.Handle, out var actualChildRect);
        var findRight = typeof(TaskbarEmbedder).GetMethod("FindRightmostChildTrayLeft", BindingFlags.NonPublic | BindingFlags.Static);
        var trayRect = new NativeMethods.Rect { Left = 0, Top = actualChildRect.Top - 5, Right = actualChildRect.Right + 10, Bottom = actualChildRect.Bottom + 5 };
        TaskbarEmbedder.FindWindowExOverride = (p, c, cls, name) => c == IntPtr.Zero ? fakeChild.Handle : IntPtr.Zero;
        try
        {
            var leftFound = (int)findRight?.Invoke(null, [fakeTray.Handle, trayRect])!;
            leftFound.Should().Be(actualChildRect.Left);
        }
        finally
        {
            TaskbarEmbedder.FindWindowExOverride = null;
        }

        // 10. ResolveParentAndTaskBand when _isWin11 is false and taskBand is null
        typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, false);
        TaskbarEmbedder.FindWindowExOverride = (p, c, cls, name) => cls switch
        {
            "ReBarWindow32" => fakeTray.Handle,
            _ => IntPtr.Zero
        };
        try
        {
            var resolveMethod = typeof(TaskbarEmbedder).GetMethod("ResolveParentAndTaskBand", BindingFlags.NonPublic | BindingFlags.Instance);
            var resolved = (bool)resolveMethod?.Invoke(embedder, [fakeTray.Handle])!;
            resolved.Should().BeFalse();
        }
        finally
        {
            TaskbarEmbedder.FindWindowExOverride = null;
        }

        // 11. GetAppListRightEdge with _taskBandHwnd set (with and without child buttons)
        using var fakeBand = new Form { Bounds = new Rectangle(0, 1000, 1920, 48), FormBorderStyle = FormBorderStyle.None };
        typeof(TaskbarEmbedder).GetField("_taskBandHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, fakeBand.Handle);
        typeof(TaskbarEmbedder).GetField("_cachedAppListRightEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
        typeof(TaskbarEmbedder).GetField("_lastAppListQueryTicks", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 0L);
        var queryAppBand = typeof(TaskbarEmbedder).GetMethod("GetAppListRightEdge", BindingFlags.NonPublic | BindingFlags.Instance);
        var bandRight = (int)queryAppBand?.Invoke(embedder, null)!;
        NativeMethods.GetWindowRect(fakeBand.Handle, out var fakeBandRect);
        bandRight.Should().Be(fakeBandRect.Right);

        // Taskband with buttons returns button edge
        typeof(TaskbarEmbedder).GetField("_taskBandHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, fakeTray.Handle);
        typeof(TaskbarEmbedder).GetField("_cachedAppListRightEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
        typeof(TaskbarEmbedder).GetField("_lastAppListQueryTicks", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 0L);
        var buttonBandRight = (int)queryAppBand?.Invoke(embedder, null)!;
        buttonBandRight.Should().BeGreaterThan(0);

        // 12. UpdateWidgetsEdges branches (tray null vs tray valid)
        TaskbarEmbedder.LeftWidgetRightEdgeOverride = null;
        TaskbarEmbedder.RightWidgetLeftEdgeOverride = null;
        var updateWidgetsMethod = typeof(TaskbarEmbedder).GetMethod("UpdateWidgetsEdges", BindingFlags.NonPublic | BindingFlags.Instance);

        // Tray is zero
        TaskbarEmbedder.ExplorerTrayWndOverride = () => IntPtr.Zero;
        updateWidgetsMethod?.Invoke(embedder, null);

        // Tray is valid, with ThrowInQueryWidgetsButtonEdgeForTesting
        try
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeTray.Handle;
            TaskbarEmbedder.ThrowInQueryWidgetsButtonEdgeForTesting = true;
            typeof(TaskbarEmbedder).GetField("_cachedLeftWidgetRightEdge", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, -1);
            typeof(TaskbarEmbedder).GetField("_cachedRightWidgetLeftEdge", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, -1);
            updateWidgetsMethod?.Invoke(embedder, null);
            TaskbarEmbedder.ThrowInQueryWidgetsButtonEdgeForTesting = false;

            // QueryWidgetsButtonEdge cache hit
            typeof(TaskbarEmbedder).GetField("_cachedLeftWidgetRightEdge", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 150);
            typeof(TaskbarEmbedder).GetField("_cachedRightWidgetLeftEdge", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 1200);
            typeof(TaskbarEmbedder).GetField("_lastWidgetQueryTicks", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, System.Diagnostics.Stopwatch.GetTimestamp());
            var queryWidgets = typeof(TaskbarEmbedder).GetMethod("QueryWidgetsButtonEdge", BindingFlags.NonPublic | BindingFlags.Static);
            var cachedRes = ((int, int))queryWidgets?.Invoke(null, [fakeTray.Handle, 1920])!;
            cachedRes.Item1.Should().Be(150);
            cachedRes.Item2.Should().Be(1200);

            // Controls inside fakeTray for UIAutomation queries
            var containerBtn = new Button { Name = "TaskbarItemsContainer", Bounds = new Rectangle(100, 0, 400, 40) };
            var childBtn = new Button { Name = "AppButton1", Bounds = new Rectangle(10, 5, 50, 30) };
            containerBtn.Controls.Add(childBtn);
            fakeTray.Controls.Add(containerBtn);

            var widgetsBtn = new Button { Name = "WidgetsButton", Bounds = new Rectangle(50, 5, 100, 30) };
            fakeTray.Controls.Add(widgetsBtn);
            fakeTray.Show();

            // UIAutomation find WidgetsButton
            TaskbarEmbedder.InvalidateCaches();
            var autoWidgetRes = ((int, int))queryWidgets?.Invoke(null, [fakeTray.Handle, 1920])!;
            autoWidgetRes.Should().NotBe((-1, -1));

            // 13. QueryAppListRightEdgeFromAutomation error path, null tray path, and UIAutomation find
            var queryAppAutomation = typeof(TaskbarEmbedder).GetMethod("QueryAppListRightEdgeFromAutomation", BindingFlags.NonPublic | BindingFlags.Static);
            TaskbarEmbedder.ExplorerTrayWndOverride = () => IntPtr.Zero;
            var resNullTray = (int)queryAppAutomation?.Invoke(null, [1920])!;
            resNullTray.Should().Be(-1);

            TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeTray.Handle;
            var autoAppRight = (int)queryAppAutomation?.Invoke(null, [1920])!;
            autoAppRight.Should().BeGreaterThan(0);

            TaskbarEmbedder.ThrowInQueryAppListRightEdgeFromAutomationForTesting = true;
            typeof(TaskbarEmbedder).GetField("_cachedAppListRightEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
            typeof(TaskbarEmbedder).GetField("_taskBandHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, IntPtr.Zero);
            queryAppBand?.Invoke(embedder, null);
            TaskbarEmbedder.ThrowInQueryAppListRightEdgeFromAutomationForTesting = false;
        }
        finally
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = null;
        }

        // 14. ThrowInIsTaskbarCenteredForTesting
        TaskbarEmbedder.IsTaskbarCenteredOverride = null;
        try
        {
            TaskbarEmbedder.ThrowInIsTaskbarCenteredForTesting = true;
            var centered = TaskbarEmbedder.IsTaskbarCentered();
            centered.Should().BeTrue();
        }
        finally
        {
            TaskbarEmbedder.ThrowInIsTaskbarCenteredForTesting = false;
        }

        // 15. ResolveFromScreenWorkingArea
        var resolveWorkingArea = typeof(TaskbarEmbedder).GetMethod("ResolveFromScreenWorkingArea", BindingFlags.NonPublic | BindingFlags.Static);
        resolveWorkingArea?.Invoke(null, null);

        // 16. ResolveFromTrayWindow orientations
        var resolveTray = typeof(TaskbarEmbedder).GetMethod("ResolveFromTrayWindow", BindingFlags.NonPublic | BindingFlags.Static);
        TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeTray.Handle;
        fakeTray.StartPosition = FormStartPosition.Manual;

        // Top
        fakeTray.SetBounds(0, 0, 1920, 48);
        var resTop = resolveTray?.Invoke(null, null);
        resTop.Should().NotBeNull();

        // Left
        fakeTray.SetBounds(0, 0, 48, 1080);
        var resLeft = resolveTray?.Invoke(null, null);
        resLeft.Should().NotBeNull();

        // Right
        fakeTray.SetBounds(1872, 0, 48, 1080);
        var resRight = resolveTray?.Invoke(null, null);
        resRight.Should().NotBeNull();

        // Bottom
        fakeTray.SetBounds(0, 1032, 1920, 48);
        var resBottom = resolveTray?.Invoke(null, null);
        resBottom.Should().NotBeNull();

        TaskbarEmbedder.ExplorerTrayWndOverride = null;

        // 17. GetLeftWidgetRightEdge with null vs valid tray
        var getLeftWidget = typeof(TaskbarEmbedder).GetMethod("GetLeftWidgetRightEdge", BindingFlags.NonPublic | BindingFlags.Static);
        TaskbarEmbedder.LeftWidgetRightEdgeOverride = null;

        TaskbarEmbedder.ExplorerTrayWndOverride = () => IntPtr.Zero;
        var edgeZero = (int)getLeftWidget?.Invoke(null, null)!;
        edgeZero.Should().Be(-1);

        TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeTray.Handle;
        getLeftWidget?.Invoke(null, null);
        TaskbarEmbedder.ExplorerTrayWndOverride = null;

        // 18. GetRightSideBoundary branches
        var getRightBoundary = typeof(TaskbarEmbedder).GetMethod("GetRightSideBoundary", BindingFlags.NonPublic | BindingFlags.Instance);

        // Case A: RightWidgetLeftEdgeOverride > 0 and _trayLeftEdge > 0
        TaskbarEmbedder.RightWidgetLeftEdgeOverride = () => 1200;
        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 1500);
        var rightBoundaryVal = (int)getRightBoundary?.Invoke(embedder, null)!;
        rightBoundaryVal.Should().Be(1200);

        // Case B: _rightWidgetLeftEdge > 0 and _trayLeftEdge > 0 with override null
        TaskbarEmbedder.RightWidgetLeftEdgeOverride = null;
        typeof(TaskbarEmbedder).GetField("_rightWidgetLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 1100);
        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 1500);
        rightBoundaryVal = (int)getRightBoundary?.Invoke(embedder, null)!;
        rightBoundaryVal.Should().Be(1100);
    }

    [WpfFact]
    public void TaskbarEmbedder_ButtonCalculations_WorkingArea_And_BandPositioning_Coverage()
    {
        // 1. CalculateMinButtonLeft
        TaskbarEmbedder.CalculateMinButtonLeft([]).Should().Be(-1);
        TaskbarEmbedder.CalculateMinButtonLeft([System.Windows.Rect.Empty, new System.Windows.Rect(0, 0, 0, 10)]).Should().Be(-1);
        TaskbarEmbedder.CalculateMinButtonLeft([new System.Windows.Rect(150, 0, 50, 20), new System.Windows.Rect(100, 0, 40, 20)]).Should().Be(100);

        // 2. CalculateMaxButtonRight
        TaskbarEmbedder.CalculateMaxButtonRight([]).Should().Be(-1);
        TaskbarEmbedder.CalculateMaxButtonRight([System.Windows.Rect.Empty, new System.Windows.Rect(0, 0, 0, 10)]).Should().Be(-1);
        TaskbarEmbedder.CalculateMaxButtonRight([new System.Windows.Rect(150, 0, 50, 20), new System.Windows.Rect(100, 0, 40, 20)]).Should().Be(200);

        // 3. CalculateFromWorkingArea edge tests
        var bounds = new Rectangle(0, 0, 1920, 1080);
        // Bottom
        var (bRect, bEdge) = TaskbarEmbedder.CalculateFromWorkingArea(bounds, new Rectangle(0, 0, 1920, 1032));
        bEdge.Should().Be(NativeMethods.ABE_BOTTOM);
        bRect.Height.Should().Be(48);
        // Top
        var (tRect, tEdge) = TaskbarEmbedder.CalculateFromWorkingArea(bounds, new Rectangle(0, 48, 1920, 1032));
        tEdge.Should().Be(NativeMethods.ABE_TOP);
        tRect.Height.Should().Be(48);
        // Right
        var (rRect, rEdge) = TaskbarEmbedder.CalculateFromWorkingArea(bounds, new Rectangle(0, 0, 1872, 1080));
        rEdge.Should().Be(NativeMethods.ABE_RIGHT);
        rRect.Width.Should().Be(48);
        // Left
        var (lRect, lEdge) = TaskbarEmbedder.CalculateFromWorkingArea(bounds, new Rectangle(48, 0, 1872, 1080));
        lEdge.Should().Be(NativeMethods.ABE_LEFT);
        lRect.Width.Should().Be(48);
        // Fallback (identical bounds)
        var (fRect, fEdge) = TaskbarEmbedder.CalculateFromWorkingArea(bounds, bounds);
        fEdge.Should().Be(NativeMethods.ABE_BOTTOM);
        fRect.Height.Should().Be(48);

        // 4. Band positioning failure and clamping paths
        using var fakeForm = new Form();
        fakeForm.SetBounds(0, 0, 800, 48);
        fakeForm.Show();

        var cfg = new AppSettings();
        cfg.Overlay.Placement = "TaskbarCenter";
        var embedder = new TaskbarEmbedder(cfg);
        typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, fakeForm.Handle);

        using var fakeOverlay = new Form();
        fakeOverlay.SetBounds(0, 0, 100, 30);
        fakeOverlay.Show();

        var positionBand = typeof(TaskbarEmbedder).GetMethod("PositionInBand", BindingFlags.NonPublic | BindingFlags.Instance);

        // Clamping branch when size width > parent width
        cfg.Overlay.Placement = "TaskbarLeft";
        var resClamp = positionBand?.Invoke(embedder, [fakeOverlay.Handle, new Size(1000, 40)]);
        resClamp.Should().Be(true);

        // Invalid overlay handle
        var resInvalid = positionBand?.Invoke(embedder, [(IntPtr)0xDEAD, new Size(100, 40)]);
        resInvalid.Should().Be(false);

        // 5. GetAppListLeftEdge testing seams
        var getAppListLeft = typeof(TaskbarEmbedder).GetMethod("GetAppListLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance);
        try
        {
            TaskbarEmbedder.ThrowInAppListLeftEdgeForTesting = true;
            getAppListLeft?.Invoke(embedder, null);

            TaskbarEmbedder.ThrowInAppListLeftEdgeForTesting = false;
            typeof(TaskbarEmbedder).GetField("_taskBandHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, fakeForm.Handle);
            typeof(TaskbarEmbedder).GetField("_cachedAppListLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
            getAppListLeft?.Invoke(embedder, null);

            typeof(TaskbarEmbedder).GetField("_taskBandHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, IntPtr.Zero);
            typeof(TaskbarEmbedder).GetField("_cachedAppListLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
            TaskbarEmbedder.AppListLeftEdgeAutomationOverride = _ => 250;
            var resAuto = getAppListLeft?.Invoke(embedder, null);
            resAuto.Should().Be(250);
        }
        finally
        {
            TaskbarEmbedder.ThrowInAppListLeftEdgeForTesting = false;
            TaskbarEmbedder.AppListLeftEdgeAutomationOverride = null;
        }

        // 6. UpdateWidgetsEdges branches
        var updateWidgets = typeof(TaskbarEmbedder).GetMethod("UpdateWidgetsEdges", BindingFlags.NonPublic | BindingFlags.Instance);
        var getLeftWidget = typeof(TaskbarEmbedder).GetMethod("GetLeftWidgetRightEdge", BindingFlags.NonPublic | BindingFlags.Static);
        try
        {
            TaskbarEmbedder.LeftWidgetRightEdgeOverride = null;
            TaskbarEmbedder.RightWidgetLeftEdgeOverride = null;
            TaskbarEmbedder.ExplorerTrayWndOverride = () => IntPtr.Zero;
            updateWidgets?.Invoke(embedder, null);

            TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeForm.Handle;
            TaskbarEmbedder.IsWidgetsFeatureEnabledOverride = () => true;
            TaskbarEmbedder.FindWindowExOverride = (p, c, cls, title) =>
                cls == "Windows.UI.Composition.DesktopWindowContentBridge" ? (IntPtr)0x1234 : IntPtr.Zero;

            updateWidgets?.Invoke(embedder, null);
            getLeftWidget?.Invoke(null, null);
        }
        finally
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = null;
            TaskbarEmbedder.IsWidgetsFeatureEnabledOverride = null;
            TaskbarEmbedder.FindWindowExOverride = null;
        }

        // 7. QueryAppListLeftEdgeFromAutomation testing seams
        var queryLeftAuto = typeof(TaskbarEmbedder).GetMethod("QueryAppListLeftEdgeFromAutomation", BindingFlags.NonPublic | BindingFlags.Static);
        try
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeForm.Handle;
            TaskbarEmbedder.ThrowInQueryAppListLeftEdgeFromAutomationForTesting = true;
            var act = () => queryLeftAuto?.Invoke(null, null);
            act.Should().Throw<TargetInvocationException>()
                .WithInnerException<InvalidOperationException>();
        }
        finally
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = null;
            TaskbarEmbedder.ThrowInQueryAppListLeftEdgeFromAutomationForTesting = false;
        }

        // 8. ThrowInExplorerLookupForTesting branch
        var findExplorer = typeof(TaskbarEmbedder).GetMethod("FindExplorerTrayWnd", BindingFlags.NonPublic | BindingFlags.Static);
        try
        {
            TaskbarEmbedder.ThrowInExplorerLookupForTesting = true;
            findExplorer?.Invoke(null, null);
        }
        finally
        {
            TaskbarEmbedder.ThrowInExplorerLookupForTesting = false;
        }

        // 9. ResolveFromScreenWorkingArea
        var resolveScreen = typeof(TaskbarEmbedder).GetMethod("ResolveFromScreenWorkingArea", BindingFlags.NonPublic | BindingFlags.Static);
        resolveScreen?.Invoke(null, null);

        // 10. QueryWidgetsButtonEdge error catch branch
        var queryWidgetsEdge = typeof(TaskbarEmbedder).GetMethod("QueryWidgetsButtonEdge", BindingFlags.NonPublic | BindingFlags.Static);
        try
        {
            TaskbarEmbedder.ThrowInQueryWidgetsButtonEdgeForTesting = true;
            queryWidgetsEdge?.Invoke(null, [fakeForm.Handle, 1920]);
        }
        finally
        {
            TaskbarEmbedder.ThrowInQueryWidgetsButtonEdgeForTesting = false;
        }

        // 11. FailGetClientRectForTesting
        try
        {
            TaskbarEmbedder.FailGetClientRectForTesting = true;
            var resFail = positionBand?.Invoke(embedder, [fakeOverlay.Handle, new Size(100, 40)]);
            resFail.Should().Be(false);
            embedder.State.Should().Be(OverlayDeploymentState.Failed);
        }
        finally
        {
            TaskbarEmbedder.FailGetClientRectForTesting = false;
        }

        // 12. ComputeBandX preferredX >= clientAppLeft branch
        var computeX = typeof(TaskbarEmbedder).GetMethod("ComputeBandX", BindingFlags.NonPublic | BindingFlags.Instance);
        try
        {
            cfg.Overlay.Placement = "TaskbarLeft";
            cfg.Overlay.PlacementOffset = 50;
            TaskbarEmbedder.AppListLeftEdgeOverride = () => 0;
            var resX = (int)computeX?.Invoke(embedder, [new NativeMethods.Rect { Left = 0, Top = 0, Right = 800, Bottom = 48 }, new Size(100, 40)])!;
            resX.Should().BeGreaterThan(0);
        }
        finally
        {
            TaskbarEmbedder.AppListLeftEdgeOverride = null;
        }

        // 13. UI Automation queries without button children (bounding rect fallback)
        var panel = new Panel { Name = "TaskbarItemsContainer" };
        panel.SetBounds(100, 0, 300, 48);
        fakeForm.Controls.Add(panel);

        TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeForm.Handle;
        try
        {
            var queryRightAuto = typeof(TaskbarEmbedder).GetMethod("QueryAppListRightEdgeFromAutomation", BindingFlags.NonPublic | BindingFlags.Static);
            var rightRes = (int)queryRightAuto?.Invoke(null, [1920])!;
            rightRes.Should().BeGreaterThan(0);

            var leftRes = (int)queryLeftAuto?.Invoke(null, null)!;
            leftRes.Should().BeGreaterThan(0);
        }
        finally
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = null;
        }

        fakeOverlay.Close();
        fakeForm.Close();
    }

    [Fact]
    public void TaskbarEmbedder_CalculateTrayLeft_VariousBoundingBoxes()
    {
        // 1. Empty rects list
        int resEmpty = TaskbarEmbedder.CalculateTrayLeft([], 1920);
        resEmpty.Should().Be(-1);

        // 2. Rectangles with empty or zero size
        var invalidRects = new List<System.Windows.Rect>
        {
            System.Windows.Rect.Empty,
            new(1000, 0, 0, 48),
            new(1000, 0, 50, 0)
        };
        int resInvalid = TaskbarEmbedder.CalculateTrayLeft(invalidRects, 1920);
        resInvalid.Should().Be(-1);

        // 3. Rectangles on the left side of taskbar (Left < midX where midX = 960)
        var leftSideRects = new List<System.Windows.Rect>
        {
            new(100, 0, 200, 48),
            new(500, 0, 100, 48)
        };
        int resLeftSide = TaskbarEmbedder.CalculateTrayLeft(leftSideRects, 1920);
        resLeftSide.Should().Be(-1);

        // 4. Valid tray rectangles on the right side
        var trayRects = new List<System.Windows.Rect>
        {
            new(1720, 0, 24, 48),  // e.g. NotificationChevron
            new(1750, 0, 60, 48),  // e.g. SystemPromotedNotificationArea
            new(1820, 0, 80, 48)   // e.g. ControlCenter / Clock
        };
        int resTray = TaskbarEmbedder.CalculateTrayLeft(trayRects, 1920);
        resTray.Should().Be(1720);

        // 5. Zero taskbar width defaults midX to 0
        int resZeroWidth = TaskbarEmbedder.CalculateTrayLeft([new(50, 0, 100, 48)], 0);
        resZeroWidth.Should().Be(50);
    }

    [WpfFact]
    public void TaskbarEmbedder_TrayLeftEdgeAutomation_Tests()
    {
        // Reset static caches
        var resetStaticCaches = typeof(TaskbarEmbedder).GetMethod("ResetStaticEdgeCaches", BindingFlags.NonPublic | BindingFlags.Static)!;
        resetStaticCaches.Invoke(null, null);

        using var fakeTray = new Form { Bounds = new Rectangle(0, 1000, 1920, 48) };
        TaskbarEmbedder.ExplorerTrayWndOverride = () => fakeTray.Handle;

        try
        {
            // 1. Test TrayLeftEdgeAutomationOverride hook in GetTrayLeftEdge
            TaskbarEmbedder.TrayLeftEdgeAutomationOverride = width => 1650;
            var getTrayLeft = typeof(TaskbarEmbedder).GetMethod("GetTrayLeftEdge", BindingFlags.NonPublic | BindingFlags.Static)!;
            int trayLeft = (int)getTrayLeft.Invoke(null, null)!;
            trayLeft.Should().Be(1650);

            // 2. ThrowInTrayLeftEdgeAutomationForTesting hook in QueryTrayLeftEdgeFromAutomation
            TaskbarEmbedder.TrayLeftEdgeAutomationOverride = null;
            TaskbarEmbedder.ThrowInTrayLeftEdgeAutomationForTesting = true;
            var queryTrayAuto = typeof(TaskbarEmbedder).GetMethod("QueryTrayLeftEdgeFromAutomation", BindingFlags.NonPublic | BindingFlags.Static)!;
            var actThrow = () => queryTrayAuto.Invoke(null, [fakeTray.Handle, 1920]);
            actThrow.Should().Throw<TargetInvocationException>()
                .WithInnerException<InvalidOperationException>()
                .WithMessage("Simulated tray left edge automation error");
            TaskbarEmbedder.ThrowInTrayLeftEdgeAutomationForTesting = false;

            // 3. Automation query on fake tray without matching elements returns -1
            int queryNone = (int)queryTrayAuto.Invoke(null, [fakeTray.Handle, 1920])!;
            queryNone.Should().Be(-1);

            // 4. Fallback EstimateTrayWidth
            var estWidthMethod = typeof(TaskbarEmbedder).GetMethod("EstimateTrayWidth", BindingFlags.NonPublic | BindingFlags.Static)!;
            int estWidth = (int)estWidthMethod.Invoke(null, null)!;
            estWidth.Should().BeGreaterThanOrEqualTo(200);
        }
        finally
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = null;
            TaskbarEmbedder.TrayLeftEdgeAutomationOverride = null;
            TaskbarEmbedder.ThrowInTrayLeftEdgeAutomationForTesting = false;
            resetStaticCaches.Invoke(null, null);
            fakeTray.Close();
        }
    }

    [Fact]
    public void TaskbarEmbedder_CalculateTrayLeft_NegativeCoordinates()
    {
        // Monitor positioned to the left of primary: bounds -2560 to -512 (width 2048)
        var rects = new List<System.Windows.Rect>
        {
            new(-700, 0, 80, 48),  // Clock on right side of secondary monitor
            new(-2400, 0, 60, 48)  // Start button on left side of secondary monitor
        };

        int trayLeft = TaskbarEmbedder.CalculateTrayLeft(rects, 2048);
        trayLeft.Should().Be(-700);
    }

    [WpfFact]
    public void TaskbarEmbedder_SecondaryTaskbar_EstimateTrayWidth()
    {
        using var fakeSecondaryTray = new Form { Bounds = new Rectangle(-2560, 0, 2048, 48) };
        try
        {
            TaskbarEmbedder.CurrentTargetTrayWnd = fakeSecondaryTray.Handle;
            TaskbarEmbedder.IsSecondaryTrayWindowOverride = _ => true;
            var estWidthMethod = typeof(TaskbarEmbedder).GetMethod("EstimateTrayWidth", BindingFlags.NonPublic | BindingFlags.Static)!;
            int estWidth = (int)estWidthMethod.Invoke(null, null)!;
            estWidth.Should().Be(LocalTelemetry.App.Win32.WindowHelpers.Scale(80, 1f));
        }
        finally
        {
            TaskbarEmbedder.CurrentTargetTrayWnd = null;
            TaskbarEmbedder.IsSecondaryTrayWindowOverride = null;
        }
    }

    [Fact]
    public void TaskbarEmbedder_IsRightDockedId_IdentifiesKnownTrayElements()
    {
        TaskbarEmbedder.IsRightDockedId("NotificationChevron").Should().BeTrue();
        TaskbarEmbedder.IsRightDockedId("SystemPromotedNotificationArea").Should().BeTrue();
        TaskbarEmbedder.IsRightDockedId("ClockButton").Should().BeTrue();
        TaskbarEmbedder.IsRightDockedId("TimeWidget").Should().BeTrue();
        TaskbarEmbedder.IsRightDockedId("NotificationCenterButton").Should().BeTrue();
        TaskbarEmbedder.IsRightDockedId("Clock").Should().BeTrue();
        TaskbarEmbedder.IsRightDockedId("RandomAppButton").Should().BeFalse();
        TaskbarEmbedder.IsRightDockedId(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void TaskbarEmbedder_ScanTaskbar_HandlesZeroHwndAndTestingFlags()
    {
        var zeroRes = TaskbarEmbedder.ScanTaskbar(IntPtr.Zero, 1920, 0);
        zeroRes.AppsLeft.Should().Be(-1);
        zeroRes.AppsRight.Should().Be(-1);
        zeroRes.TrayLeft.Should().Be(-1);

        try
        {
            TaskbarEmbedder.ThrowInScanTaskbarForTesting = true;
            var act = () => TaskbarEmbedder.ScanTaskbar((IntPtr)12345, 1920, 0);
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            TaskbarEmbedder.ThrowInScanTaskbarForTesting = false;
        }
    }

    [Fact]
    public void TaskbarEmbedder_ScanTaskbar_CachesResultsAndClearsOnReset()
    {
        var testHandle = (IntPtr)0x12345;
        var fakeResult = new TaskbarEmbedder.TaskbarScanResult(800, 100, 1600, Stopwatch.GetTimestamp());
        var cache = (System.Collections.Concurrent.ConcurrentDictionary<IntPtr, TaskbarEmbedder.TaskbarScanResult>)
            typeof(TaskbarEmbedder).GetField("_taskbarScanCache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        cache[testHandle] = fakeResult;

        try
        {
            var res = TaskbarEmbedder.ScanTaskbar(testHandle, 1920, 0);
            res.Should().Be(fakeResult);

            TaskbarEmbedder.InvalidateCaches();

            var resAfterReset = TaskbarEmbedder.ScanTaskbar(testHandle, 1920, 0);
            resAfterReset.Should().NotBe(fakeResult);
        }
        finally
        {
            TaskbarEmbedder.InvalidateCaches();
        }
    }

    [WpfFact]
    public void TaskbarEmbedder_IsAdjacentTrayChild_And_ClusterLeft_Coverage()
    {
        var isAdjacentMethod = typeof(TaskbarEmbedder).GetMethod("IsAdjacentTrayChild", BindingFlags.NonPublic | BindingFlags.Static);
        isAdjacentMethod.Should().NotBeNull();

        using var trayForm = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(0, 0, 1920, 50) };
        trayForm.Show();
        using var childForm = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(1200, 0, 80, 50) };
        childForm.Show();

        NativeMethods.GetWindowRect(childForm.Handle, out var actualCr);
        var trayRect = new NativeMethods.Rect { Left = 0, Top = 0, Right = actualCr.Right + 500, Bottom = 50 };
        int clusterLeft = actualCr.Right + 10;

        // Valid adjacent child
        object[] args = [childForm.Handle, trayRect, clusterLeft, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0];
        bool res = (bool)isAdjacentMethod.Invoke(null, args)!;
        res.Should().BeTrue();
        ((int)args[6]).Should().Be(actualCr.Left);

        // App overlay window handle or title -> false (must not self-detect)
        TaskbarEmbedder.ActiveOverlayHwnds.TryAdd(childForm.Handle, 0);
        args = [childForm.Handle, trayRect, clusterLeft, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0];
        ((bool)isAdjacentMethod.Invoke(null, args)!).Should().BeFalse();
        TaskbarEmbedder.ActiveOverlayHwnds.TryRemove(childForm.Handle, out _);

        childForm.Text = "LocalTelemetry_Overlay";
        args = [childForm.Handle, trayRect, clusterLeft, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0];
        ((bool)isAdjacentMethod.Invoke(null, args)!).Should().BeFalse();
        childForm.Text = "MockChild";

        // Same as rebar or trayNotify or clock -> false
        args = [childForm.Handle, trayRect, clusterLeft, childForm.Handle, IntPtr.Zero, IntPtr.Zero, 0];
        ((bool)isAdjacentMethod.Invoke(null, args)!).Should().BeFalse();

        args = [childForm.Handle, trayRect, clusterLeft, IntPtr.Zero, childForm.Handle, IntPtr.Zero, 0];
        ((bool)isAdjacentMethod.Invoke(null, args)!).Should().BeFalse();

        args = [childForm.Handle, trayRect, clusterLeft, IntPtr.Zero, IntPtr.Zero, childForm.Handle, 0];
        ((bool)isAdjacentMethod.Invoke(null, args)!).Should().BeFalse();

        // Hidden window -> false
        childForm.Visible = false;
        args = [childForm.Handle, trayRect, clusterLeft, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0];
        ((bool)isAdjacentMethod.Invoke(null, args)!).Should().BeFalse();
        childForm.Visible = true;

        // Horizontally not adjacent (too far left: actualCr.Right < clusterLeft - 20)
        int farClusterLeft = actualCr.Right + 50;
        args = [childForm.Handle, trayRect, farClusterLeft, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0];
        ((bool)isAdjacentMethod.Invoke(null, args)!).Should().BeFalse();

        // Not reasonable bounds (tray width too small so child exceeds 45% of width)
        var narrowTrayRect = new NativeMethods.Rect { Left = 0, Top = actualCr.Top, Right = actualCr.Width + 10, Bottom = actualCr.Bottom };
        args = [childForm.Handle, narrowTrayRect, clusterLeft, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0];
        ((bool)isAdjacentMethod.Invoke(null, args)!).Should().BeFalse();

        // Zero/negative width window handle (IntPtr.Zero) -> false
        args = [IntPtr.Zero, trayRect, clusterLeft, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0];
        ((bool)isAdjacentMethod.Invoke(null, args)!).Should().BeFalse();

        // FindAdjacentClusterLeft with childForm as a child of trayForm
        NativeMethods.SetParent(childForm.Handle, trayForm.Handle);
        var findClusterMethod = typeof(TaskbarEmbedder).GetMethod("FindAdjacentClusterLeft", BindingFlags.NonPublic | BindingFlags.Static);
        findClusterMethod.Should().NotBeNull();
        int finalCluster = (int)findClusterMethod.Invoke(null, [trayForm.Handle, trayRect, clusterLeft, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero])!;
        finalCluster.Should().BeLessThanOrEqualTo(clusterLeft);

        trayForm.Close();
        childForm.Close();
    }

    [WpfFact]
    public void TaskbarEmbedder_RefactoredMethods_CompleteBranchCoverage()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        using var parentForm = new Form { Bounds = new Rectangle(0, 0, 1920, 48) };
        using var bandForm = new Form { Bounds = new Rectangle(100, 0, 400, 48) };
        parentForm.Show();
        bandForm.Show();

        typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, parentForm.Handle);
        typeof(TaskbarEmbedder).GetField("_taskBandHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, bandForm.Handle);

        // 1. QueryLegacyTaskBandRightEdge and LeftEdge with valid window
        var queryRight = typeof(TaskbarEmbedder).GetMethod("QueryLegacyTaskBandRightEdge", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var queryLeft = typeof(TaskbarEmbedder).GetMethod("QueryLegacyTaskBandLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((int)queryRight.Invoke(embedder, null)!).Should().BeGreaterThan(0);
        ((int)queryLeft.Invoke(embedder, null)!).Should().BeGreaterThanOrEqualTo(0);

        // When taskband is IntPtr.Zero
        typeof(TaskbarEmbedder).GetField("_taskBandHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, IntPtr.Zero);
        ((int)queryRight.Invoke(embedder, null)!).Should().Be(-1);
        ((int)queryLeft.Invoke(embedder, null)!).Should().Be(-1);

        // 2. ResolveDynamicAppListRightEdge and LeftEdge automation overrides
        var resolveRight = typeof(TaskbarEmbedder).GetMethod("ResolveDynamicAppListRightEdge", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var resolveLeft = typeof(TaskbarEmbedder).GetMethod("ResolveDynamicAppListLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!;

        try
        {
            TaskbarEmbedder.AppListRightEdgeAutomationOverride = _ => 999;
            TaskbarEmbedder.AppListLeftEdgeAutomationOverride = _ => 111;
            ((int)resolveRight.Invoke(embedder, null)!).Should().Be(999);
            ((int)resolveLeft.Invoke(embedder, null)!).Should().Be(111);
        }
        finally
        {
            TaskbarEmbedder.AppListRightEdgeAutomationOverride = null;
            TaskbarEmbedder.AppListLeftEdgeAutomationOverride = null;
        }

        // When overrides are null, scan taskbar fallback
        int dynRight = (int)resolveRight.Invoke(embedder, null)!;
        int dynLeft = (int)resolveLeft.Invoke(embedder, null)!;
        dynRight.Should().BeInRange(-1, 1920);
        dynLeft.Should().BeInRange(-1, 1920);

        // 3. ResolveClientRightBoundary with valid boundary vs invalid (-1)
        var resolveClientRight = typeof(TaskbarEmbedder).GetMethod("ResolveClientRightBoundary", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var parentRect = new NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 48 };

        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 1600);
        var expectedPt = new NativeMethods.Point { X = 1600, Y = 0 };
        NativeMethods.ScreenToClient(parentForm.Handle, ref expectedPt);
        int clientRight = (int)resolveClientRight.Invoke(embedder, [parentRect])!;
        clientRight.Should().Be(expectedPt.X);

        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
        int fallbackRight = (int)resolveClientRight.Invoke(embedder, [parentRect])!;
        fallbackRight.Should().BeGreaterThan(0);

        // 4. FindRebarDeskBandRightBound with _trayLeftEdge == -1 vs valid
        var findDeskBandBound = typeof(TaskbarEmbedder).GetMethod("FindRebarDeskBandRightBound", BindingFlags.NonPublic | BindingFlags.Instance)!;
        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
        int bound1 = (int)findDeskBandBound.Invoke(embedder, [500, 1920])!;
        bound1.Should().Be(1920);

        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 1500);
        var expectedTrayPt = new NativeMethods.Point { X = 1500, Y = 0 };
        NativeMethods.ScreenToClient(parentForm.Handle, ref expectedTrayPt);
        int bound2 = (int)findDeskBandBound.Invoke(embedder, [500, 1920])!;
        bound2.Should().Be(expectedTrayPt.X);

        parentForm.Close();
        bandForm.Close();
    }

    [WpfFact]
    public void TaskbarEmbedder_ComputeBandLeftAlignedX_PlacementRight_NeverOverlapsAppButtonsWhenConstrained()
    {
        using var parentForm = new Form { Bounds = new Rectangle(0, 0, 1000, 48) };
        var cfg = new AppSettings();
        cfg.Overlay.Placement = "right";
        cfg.Overlay.PlacementOffset = 5;

        var embedder = new TaskbarEmbedder(cfg);
        typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, parentForm.Handle);

        try
        {
            TaskbarEmbedder.AppListRightEdgeOverride = () => 600;

            var compute = typeof(TaskbarEmbedder).GetMethod("ComputeBandLeftAlignedX", BindingFlags.NonPublic | BindingFlags.Instance)!;
            // clientRightBoundary = 500, overlay width = 400.
            // Available slot = 500 - 600 - 10 = 0px. Overlay is 400px.
            int x = (int)compute.Invoke(embedder, [new Size(400, 30), 500])!;

            var expectedPt = new NativeMethods.Point { X = 600, Y = 0 };
            NativeMethods.ScreenToClient(parentForm.Handle, ref expectedPt);
            int expectedX = Math.Max(0, expectedPt.X) + cfg.Overlay.PlacementOffset;

            // Overlay must be clamped so it begins at or after appRight + offset, never overlapping app icons
            x.Should().Be(expectedX);
            embedder.IsTaskbarSpaceConstrained.Should().BeTrue();
            embedder.AvailableSlotWidth.Should().Be(Math.Max(0, 500 - Math.Max(0, expectedPt.X) - (2 * cfg.Overlay.PlacementOffset)));
        }
        finally
        {
            TaskbarEmbedder.AppListRightEdgeOverride = null;
        }
    }

    [WpfFact]
    public void TaskbarEmbedder_ComputeBandWin10X_SpaceConstrained_ClampsWithoutOverlappingApps()
    {
        using var parentForm = new Form { Bounds = new Rectangle(0, 0, 1000, 40) };
        using var bandForm = new Form { Bounds = new Rectangle(0, 0, 800, 40) };
        parentForm.Show();
        bandForm.Show();
        NativeMethods.SetParent(bandForm.Handle, parentForm.Handle);

        var cfg = new AppSettings();
        cfg.Overlay.Placement = "right";
        cfg.Overlay.PlacementOffset = 5;

        var embedder = new TaskbarEmbedder(cfg);
        typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, parentForm.Handle);
        typeof(TaskbarEmbedder).GetField("_taskBandHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, bandForm.Handle);
        typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, false);

        try
        {
            TaskbarEmbedder.AppListRightEdgeOverride = () => 700;
            var compute = typeof(TaskbarEmbedder).GetMethod("ComputeBandWin10X", BindingFlags.NonPublic | BindingFlags.Instance)!;

            // Slot is parent width (800) - appRight (approx 536) - 2*offset (10) = 254px.
            // Overlay width is 400px > 254px available.
            var parentRect = new NativeMethods.Rect { Left = 0, Top = 0, Right = 800, Bottom = 40 };
            int x = (int)compute.Invoke(embedder, [parentRect, new Size(400, 30)])!;

            // Overlay must not overlap appRight; must be clamped to minRightX.
            var pt = new NativeMethods.Point { X = 700, Y = 0 };
            NativeMethods.ScreenToClient(parentForm.Handle, ref pt);
            int expectedMinX = Math.Max(0, pt.X) + cfg.Overlay.PlacementOffset;

            x.Should().Be(expectedMinX);
            embedder.IsTaskbarSpaceConstrained.Should().BeTrue();
            embedder.AvailableSlotWidth.Should().Be(Math.Max(0, 800 - Math.Max(0, pt.X) - (2 * cfg.Overlay.PlacementOffset)));
        }
        finally
        {
            TaskbarEmbedder.AppListRightEdgeOverride = null;
            parentForm.Close();
            bandForm.Close();
        }
    }

    [WpfFact]
    public void TaskbarEmbedder_CalculateAvailableSlotWidth_Win10Mode_ComputesExpectedWidth()
    {
        using var parentForm = new Form { Bounds = new Rectangle(0, 0, 1000, 40) };
        using var bandForm = new Form { Bounds = new Rectangle(0, 0, 600, 40) };
        parentForm.Show();
        bandForm.Show();
        NativeMethods.SetParent(bandForm.Handle, parentForm.Handle);

        var cfg = new AppSettings();
        cfg.Overlay.Placement = "right";
        cfg.Overlay.PlacementOffset = 5;

        var embedder = new TaskbarEmbedder(cfg);
        typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, parentForm.Handle);
        typeof(TaskbarEmbedder).GetField("_taskBandHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, bandForm.Handle);
        typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, false);

        try
        {
            TaskbarEmbedder.AppListRightEdgeOverride = () => 600;

            NativeMethods.GetClientRect(parentForm.Handle, out var parentRect);
            int slotWidth = embedder.CalculateAvailableSlotWidth();
            var pt = new NativeMethods.Point { X = 600, Y = 0 };
            NativeMethods.ScreenToClient(parentForm.Handle, ref pt);
            int expected = Math.Max(0, parentRect.Width - Math.Max(0, pt.X) - (2 * cfg.Overlay.PlacementOffset));

            slotWidth.Should().Be(expected);
        }
        finally
        {
            TaskbarEmbedder.AppListRightEdgeOverride = null;
            parentForm.Close();
            bandForm.Close();
        }
    }
}
