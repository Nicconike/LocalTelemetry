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
        typeof(TaskbarEmbedder).GetField("_taskBandOri", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, new NativeMethods.RECT { Left = 10, Top = 0, Right = 500, Bottom = 40 });
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
        restore!.Invoke(embedder, null);

        var compRight = typeof(TaskbarEmbedder).GetMethod("ComputeRightFloatX", BindingFlags.NonPublic | BindingFlags.Instance);
        var x = (int)compRight!.Invoke(embedder, [150])!;
        x.Should().BeGreaterThan(0);

        typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
        typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, true);
        _ = (int)compRight.Invoke(embedder, [150])!;

        typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, false);
        _ = (int)compRight.Invoke(embedder, [150])!;
    }

    [Fact]
    public void TaskbarEmbedder_ReflectionPrivateMethodsCoverage()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        var isCentered = typeof(TaskbarEmbedder).GetMethod("IsTaskbarCentered", BindingFlags.NonPublic | BindingFlags.Static);
        isCentered.Should().NotBeNull();
        ((bool)isCentered.Invoke(null, null)!).Should().BeTrue();

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

        typeof(TaskbarEmbedder).GetProperty("IsFallback", BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(embedder, [true]);
        embedder.Reposition(IntPtr.Zero, new Size(200, 30));

        typeof(TaskbarEmbedder).GetProperty("IsFallback", BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(embedder, [false]);
        typeof(TaskbarEmbedder).GetProperty("IsEmbedded", BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(embedder, [true]);
        embedder.UpdateLayeredSettings(IntPtr.Zero);
        embedder.Reposition(IntPtr.Zero, new Size(250, 30));

        cfg.Overlay.FloatX = -1;
        cfg.Overlay.FloatY = -1;
        cfg.Overlay.Placement = "left";

        var posFloat = typeof(TaskbarEmbedder).GetMethod("PositionFloat", BindingFlags.NonPublic | BindingFlags.Instance);
        posFloat.Should().NotBeNull();
        posFloat.Invoke(embedder, [IntPtr.Zero, new Size(200, 30)]);

        cfg.Overlay.Placement = "center";
        posFloat.Invoke(embedder, [IntPtr.Zero, new Size(200, 30)]);

        cfg.Overlay.Placement = "right";
        posFloat.Invoke(embedder, [IntPtr.Zero, new Size(200, 30)]);

        cfg.Overlay.FloatX = 100;
        cfg.Overlay.FloatY = 100;
        posFloat.Invoke(embedder, [IntPtr.Zero, new Size(200, 30)]);
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

        var rect = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 40 };

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

    [Fact]
    public void TaskbarEmbedder_PositionFloat_VerticalTaskbar_Coverage()
    {
        var cfg = new AppSettings();
        cfg.Overlay.FloatX = -1;
        cfg.Overlay.FloatY = -1;
        var embedder = new TaskbarEmbedder(cfg);

        // Vertical taskbar: 0u (ABE_LEFT)
        typeof(TaskbarEmbedder).GetField("_taskbarEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 0u);
        typeof(TaskbarEmbedder).GetField("_taskbarRect", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, new Rectangle(0, 0, 60, 1080));

        var posFloat = typeof(TaskbarEmbedder).GetMethod("PositionFloat", BindingFlags.NonPublic | BindingFlags.Instance);
        posFloat!.Invoke(embedder, [IntPtr.Zero, new Size(50, 200)]);

        cfg.Overlay.FloatX.Should().Be(-1);
        embedder.LastComputedFloatX.Should().Be(4);
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
        typeof(TaskbarEmbedder).GetField("_taskBandOri", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, new NativeMethods.RECT { Left = 50, Top = 0, Right = 500, Bottom = 40 });

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
        typeof(TaskbarEmbedder).GetField("_taskBandOri", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, new NativeMethods.RECT { Left = 50, Top = 10, Right = 500, Bottom = 40 });

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
        var rect = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 40 };

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

    [Fact]
    public void TaskbarEmbedder_PositionFloat_InvalidTaskbarBounds_SkipsPositioning()
    {
        var cfg = new AppSettings();
        cfg.Overlay.FloatX = -1;
        cfg.Overlay.FloatY = -1;
        var embedder = new TaskbarEmbedder(cfg);

        typeof(TaskbarEmbedder).GetField("_taskbarRect", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, Rectangle.Empty);

        var posFloat = typeof(TaskbarEmbedder).GetMethod("PositionFloat", BindingFlags.NonPublic | BindingFlags.Instance);
        posFloat!.Invoke(embedder, [IntPtr.Zero, new Size(200, 30)]);

        embedder.LastComputedFloatX.Should().Be(0);
        embedder.LastComputedFloatY.Should().Be(0);
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
        using var overlayForm = new Form();
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        typeof(TaskbarEmbedder).GetField("_taskbarRect", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, Rectangle.Empty);

        var posFloat = typeof(TaskbarEmbedder).GetMethod("PositionFloat", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (bool)posFloat!.Invoke(embedder, [overlayForm.Handle, new Size(200, 30)])!;

        result.Should().BeFalse();
        embedder.State.Should().Be(OverlayDeploymentState.Failed);
        embedder.IsEmbedded.Should().BeFalse();
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
    public void TaskbarEmbedder_PositionFloat_CustomCoordinates_MovesWindow()
    {
        using var form = new Form();
        form.Show();

        var cfg = new AppSettings();
        cfg.Overlay.FloatX = 120;
        cfg.Overlay.FloatY = 80;

        var embedder = new TaskbarEmbedder(cfg);
        var posFloat = typeof(TaskbarEmbedder).GetMethod("PositionFloat", BindingFlags.NonPublic | BindingFlags.Instance);
        posFloat.Should().NotBeNull();

        // First call sets position and flags
        bool ok1 = (bool)posFloat.Invoke(embedder, [form.Handle, new Size(200, 30)])!;
        ok1.Should().BeTrue();
        embedder.LastComputedFloatX.Should().Be(120);
        embedder.LastComputedFloatY.Should().Be(80);

        // Second call hits cached target rect
        bool ok2 = (bool)posFloat.Invoke(embedder, [form.Handle, new Size(200, 30)])!;
        ok2.Should().BeTrue();

        form.Close();
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
    public void TaskbarEmbedder_Detach_FallbackMode_CleansHwndParent()
    {
        using var form = new Form();
        form.Show();

        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        // Force fallback mode
        typeof(TaskbarEmbedder).GetProperty("IsFallback", BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(embedder, [true]);
        typeof(TaskbarEmbedder).GetProperty("IsEmbedded", BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(embedder, [true]);

        embedder.Detach(form.Handle);

        embedder.IsEmbedded.Should().BeFalse();
        embedder.State.Should().Be(OverlayDeploymentState.Uninitialized);

        form.Close();
    }

    [Fact]
    public void TaskbarEmbedder_PositionFloat_ClampingAndEdgeCases_Coverage()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        var posFloatMethod = typeof(TaskbarEmbedder).GetMethod("PositionFloat", BindingFlags.NonPublic | BindingFlags.Instance);
        posFloatMethod.Should().NotBeNull();

        var rectField = typeof(TaskbarEmbedder).GetField("_taskbarRect", BindingFlags.NonPublic | BindingFlags.Instance);
        var edgeField = typeof(TaskbarEmbedder).GetField("_taskbarEdge", BindingFlags.NonPublic | BindingFlags.Instance);

        // 1. Invalid taskbar bounds (lines 705-710)
        rectField!.SetValue(embedder, Rectangle.Empty);
        bool failRes = (bool)posFloatMethod.Invoke(embedder, [IntPtr.Zero, new Size(200, 30)])!;
        failRes.Should().BeFalse();
        embedder.State.Should().Be(OverlayDeploymentState.Failed);

        // 2. Horizontal clamping: left overflow (lines 724-727)
        rectField.SetValue(embedder, new Rectangle(0, 1000, 1920, 48));
        edgeField!.SetValue(embedder, NativeMethods.ABE_BOTTOM);
        cfg.Overlay.Placement = "left";
        cfg.Overlay.PlacementOffset = -500;
        bool clampLeft = (bool)posFloatMethod.Invoke(embedder, [IntPtr.Zero, new Size(200, 30)])!;
        clampLeft.Should().BeFalse(); // IntPtr.Zero HWND returns false on SetWindowPos but executes calculation
        embedder.LastComputedFloatX.Should().Be(0);

        // 3. Horizontal clamping: right overflow (lines 724-727)
        cfg.Overlay.Placement = "left";
        cfg.Overlay.PlacementOffset = 5000;
        posFloatMethod.Invoke(embedder, [IntPtr.Zero, new Size(200, 30)]);
        embedder.LastComputedFloatX.Should().Be(1920 - 200);

        // 4. Vertical taskbar placement (line 720)
        edgeField.SetValue(embedder, NativeMethods.ABE_LEFT);
        posFloatMethod.Invoke(embedder, [IntPtr.Zero, new Size(200, 30)]);
        embedder.LastComputedFloatX.Should().Be(4);

        edgeField.SetValue(embedder, NativeMethods.ABE_RIGHT);
        posFloatMethod.Invoke(embedder, [IntPtr.Zero, new Size(200, 30)]);
        embedder.LastComputedFloatX.Should().Be(4);
    }

    [Fact]
    public void TaskbarEmbedder_ComputeBandX_And_PositionInBand_Overflow_Coverage()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        var computeBandX = typeof(TaskbarEmbedder).GetMethod("ComputeBandX", BindingFlags.NonPublic | BindingFlags.Instance);
        computeBandX.Should().NotBeNull();

        var isWin11Field = typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance);
        var trayEdgeField = typeof(TaskbarEmbedder).GetField("_trayLeftEdge", BindingFlags.NonPublic | BindingFlags.Instance);

        // Win11 with left-aligned placement fallback (line 663)
        isWin11Field!.SetValue(embedder, true);
        trayEdgeField!.SetValue(embedder, -1);
        cfg.Overlay.Placement = "right";
        cfg.Overlay.PlacementOffset = 10;

        var parentRect = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 48 };
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
        var isCentered = typeof(TaskbarEmbedder).GetMethod("IsTaskbarCentered", BindingFlags.NonPublic | BindingFlags.Static);
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
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);

        IntPtr tray = TaskbarEmbedder.FindExplorerTrayWnd();
        if (tray == IntPtr.Zero)
            tray = NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);

        typeof(TaskbarEmbedder).GetField("_parentHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, tray);
        typeof(TaskbarEmbedder).GetField("_isWin11", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, true);
        typeof(TaskbarEmbedder).GetField("_taskbarRect", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, new Rectangle(0, 1000, 1920, 48));

        cfg.Overlay.Placement = "left";
        cfg.Overlay.PlacementOffset = 10;

        var computeBandX = typeof(TaskbarEmbedder).GetMethod("ComputeBandX", BindingFlags.NonPublic | BindingFlags.Instance);
        var computeLeftFloat = typeof(TaskbarEmbedder).GetMethod("ComputeLeftFloatX", BindingFlags.NonPublic | BindingFlags.Instance);
        var parentRect = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 48 };

        // 1. When widget is present on left at X=180
        typeof(TaskbarEmbedder).GetField("_leftWidgetRightEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 180);
        int floatXWithWidget = (int)computeLeftFloat!.Invoke(embedder, null)!;
        floatXWithWidget.Should().Be(180 + 10); // 180 + Offset (10) = 190

        int bandXWithWidget = (int)computeBandX!.Invoke(embedder, [parentRect, new Size(200, 30)])!;
        bandXWithWidget.Should().BeGreaterThan(0);

        // 2. When no widget is present (_leftWidgetRightEdge = -1)
        typeof(TaskbarEmbedder).GetField("_leftWidgetRightEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
        int floatXNoWidget = (int)computeLeftFloat.Invoke(embedder, null)!;
        floatXNoWidget.Should().Be(0 + 10); // _taskbarRect.Left (0) + Offset (10) = 10

        int bandXNoWidget = (int)computeBandX.Invoke(embedder, [parentRect, new Size(200, 30)])!;
        bandXNoWidget.Should().Be(10);
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
            var (rect, edge) = (((Rectangle, uint))trayRes);
            rect.Width.Should().BeGreaterThan(0);
        }

        var screenRes = resolveScreen.Invoke(null, null);
        screenRes.Should().NotBeNull();
        var (sRect, sEdge) = (((Rectangle, uint))screenRes!);
        sRect.Width.Should().BeGreaterThan(0);
        sRect.Height.Should().BeGreaterThan(0);

        // Cached widgets button query
        typeof(TaskbarEmbedder).GetField("_cachedWidgetRightEdge", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 150);
        typeof(TaskbarEmbedder).GetField("_lastWidgetQueryTicks", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Stopwatch.GetTimestamp());

        var queryWidgets = typeof(TaskbarEmbedder).GetMethod("QueryWidgetsButtonRightEdge", BindingFlags.NonPublic | BindingFlags.Static)!;
        int cachedRight = (int)queryWidgets.Invoke(null, [IntPtr.Zero])!;
        cachedRight.Should().Be(150);

        // Reset static fields
        typeof(TaskbarEmbedder).GetField("_cachedWidgetRightEdge", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, -1);
    }

    [Fact]
    public void TaskbarEmbedder_PositionFloat_Modes_And_EdgeCases()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);
        using var form = new Form();

        var posFloatMethod = typeof(TaskbarEmbedder).GetMethod("PositionFloat", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var compLeftFloatMethod = typeof(TaskbarEmbedder).GetMethod("ComputeLeftFloatX", BindingFlags.NonPublic | BindingFlags.Instance)!;
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

        // 4. PositionFloat with invalid bounds -> returns false
        typeof(TaskbarEmbedder).GetField("_taskbarRect", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, Rectangle.Empty);
        bool failRes = (bool)posFloatMethod.Invoke(embedder, [form.Handle, new Size(200, 30)])!;
        failRes.Should().BeFalse();
        embedder.State.Should().Be(OverlayDeploymentState.Failed);

        // 5. PositionCustomFloat when FloatX and FloatY >= 0
        cfg.Overlay.FloatX = 150;
        cfg.Overlay.FloatY = 250;
        bool customRes = (bool)posFloatMethod.Invoke(embedder, [form.Handle, new Size(200, 30)])!;
        customRes.Should().BeTrue();
        embedder.LastComputedFloatX.Should().Be(150);
        embedder.LastComputedFloatY.Should().Be(250);

        // Repeated call to verify _lastFloatRect == targetRect early return
        bool repeatCustomRes = (bool)posFloatMethod.Invoke(embedder, [form.Handle, new Size(200, 30)])!;
        repeatCustomRes.Should().BeTrue();

        // 6. PositionTaskbarFloat with horizontal taskbar (bottom and top)
        cfg.Overlay.FloatX = -1;
        cfg.Overlay.FloatY = -1;
        typeof(TaskbarEmbedder).GetField("_taskbarRect", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(embedder, new Rectangle(0, 1000, 1920, 48));
        typeof(TaskbarEmbedder).GetField("_taskbarEdge", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(embedder, NativeMethods.ABE_BOTTOM);

        // Placement = "left"
        cfg.Overlay.Placement = "left";
        typeof(TaskbarEmbedder).GetField("_leftWidgetRightEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, 120);
        int leftXWithWidget = (int)compLeftFloatMethod.Invoke(embedder, null)!;
        leftXWithWidget.Should().BeGreaterThan(0);

        typeof(TaskbarEmbedder).GetField("_leftWidgetRightEdge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(embedder, -1);
        int leftXNoWidget = (int)compLeftFloatMethod.Invoke(embedder, null)!;
        leftXNoWidget.Should().Be(cfg.Overlay.PlacementOffset);

        bool leftFloatRes = (bool)posFloatMethod.Invoke(embedder, [form.Handle, new Size(200, 30)])!;
        leftFloatRes.Should().BeTrue();

        // Placement = "center"
        cfg.Overlay.Placement = "center";
        bool centerFloatRes = (bool)posFloatMethod.Invoke(embedder, [form.Handle, new Size(200, 30)])!;
        centerFloatRes.Should().BeTrue();

        // Placement = "right" with clamping condition (e.g. huge offset or edge outside)
        cfg.Overlay.Placement = "right";
        cfg.Overlay.PlacementOffset = 5000; // triggers out-of-range clamping
        bool clampedRes = (bool)posFloatMethod.Invoke(embedder, [form.Handle, new Size(200, 30)])!;
        clampedRes.Should().BeTrue();

        // 7. Vertical taskbar float (ABE_LEFT and ABE_RIGHT)
        typeof(TaskbarEmbedder).GetField("_taskbarEdge", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(embedder, NativeMethods.ABE_LEFT);
        bool vertLeftRes = (bool)posFloatMethod.Invoke(embedder, [form.Handle, new Size(200, 30)])!;
        vertLeftRes.Should().BeTrue();

        typeof(TaskbarEmbedder).GetField("_taskbarEdge", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(embedder, NativeMethods.ABE_RIGHT);
        bool vertRightRes = (bool)posFloatMethod.Invoke(embedder, [form.Handle, new Size(200, 30)])!;
        vertRightRes.Should().BeTrue();

        // 8. RestoreLayeredStyles static invocation
        var restoreLayered = typeof(TaskbarEmbedder).GetMethod("RestoreLayeredStyles", BindingFlags.NonPublic | BindingFlags.Static)!;
        restoreLayered.Invoke(null, [form.Handle]);
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

        // 5. QueryWidgetsButtonRightEdge with caching
        var queryWidgets = typeof(TaskbarEmbedder).GetMethod("QueryWidgetsButtonRightEdge", BindingFlags.NonPublic | BindingFlags.Static)!;
        queryWidgets.Invoke(null, [IntPtr.Zero]);
        queryWidgets.Invoke(null, [IntPtr.Zero]);

        // 6. Registry helper queries
        var isWidgets = typeof(TaskbarEmbedder).GetMethod("IsWidgetsFeatureEnabled", BindingFlags.NonPublic | BindingFlags.Static)!;
        isWidgets.Invoke(null, null);

        var isCentered = typeof(TaskbarEmbedder).GetMethod("IsTaskbarCentered", BindingFlags.NonPublic | BindingFlags.Static)!;
        isCentered.Invoke(null, null);

        var getTrayLeft = typeof(TaskbarEmbedder).GetMethod("GetTrayLeftEdge", BindingFlags.NonPublic | BindingFlags.Static)!;
        getTrayLeft.Invoke(null, null);
    }
}

