using System.Reflection;
using FluentAssertions;
using LocalTelemetry.App.Overlay;
using LocalTelemetry.App.Win32;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Models;
using Xunit;

namespace LocalTelemetry.App.Tests.Overlay;

public class TaskbarOverlayTests
{
    public TaskbarOverlayTests()
    {
        TaskbarOverlay.SkipShowWindowForTesting = true;
        TaskbarOverlay.SkipMouseHookForTesting = true;
        TaskbarOverlay.SkipTimersForTesting = true;
        TaskbarOverlay.SkipEmbedForTesting = true;
    }

    [Fact]
    public void ValuesChanged_IdenticalSnapshots_ReturnsFalse()
    {
        var method = typeof(TaskbarOverlay).GetMethod("ValuesChanged", BindingFlags.NonPublic | BindingFlags.Static);
        var snap = new TelemetrySnapshot
        {
            CpuUsagePct = 50f,
            CpuTempPackageC = 65f,
            CpuFreqGhz = 4.2f,
            CpuPackagePowerW = 85f,
            RamUsagePct = 60f,
            RamUsedGb = 16.4f,
            GpuUsagePct = 80f,
            GpuTempC = 72f,
            GpuVramUsedMb = 8000f,
            GpuFreqMHz = 2000f,
            GpuPowerW = 200f,
            NetDownBps = 1000000,
            NetUpBps = 500000,
            NetTotalBytes = 50000000,
            DiskReadMbps = 100f,
            DiskWriteMbps = 50f,
            NetInterfaceName = "Ethernet"
        };
        ((bool)method!.Invoke(null, [snap, snap])!).Should().BeFalse();
    }

    [Fact]
    public void ValuesChanged_DiffersOnAllProperties()
    {
        var method = typeof(TaskbarOverlay).GetMethod("ValuesChanged", BindingFlags.NonPublic | BindingFlags.Static);

        ((bool)method!.Invoke(null, [new TelemetrySnapshot { CpuUsagePct = 50f }, new TelemetrySnapshot { CpuUsagePct = 55f }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { CpuTempPackageC = 50f }, new TelemetrySnapshot { CpuTempPackageC = 60f }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { CpuFreqGhz = 3.5f }, new TelemetrySnapshot { CpuFreqGhz = 4.0f }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { CpuPackagePowerW = 40f }, new TelemetrySnapshot { CpuPackagePowerW = 60f }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { RamUsagePct = 40f }, new TelemetrySnapshot { RamUsagePct = 60f }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { RamUsedGb = 8f }, new TelemetrySnapshot { RamUsedGb = 16f }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { GpuUsagePct = 40f }, new TelemetrySnapshot { GpuUsagePct = 60f }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { GpuTempC = 50f }, new TelemetrySnapshot { GpuTempC = 70f }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { GpuVramUsedMb = 2000f }, new TelemetrySnapshot { GpuVramUsedMb = 4000f }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { GpuFreqMHz = 1000f }, new TelemetrySnapshot { GpuFreqMHz = 1500f }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { GpuPowerW = 50f }, new TelemetrySnapshot { GpuPowerW = 150f }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { NetDownBps = 1000 }, new TelemetrySnapshot { NetDownBps = 2000 }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { NetUpBps = 1000 }, new TelemetrySnapshot { NetUpBps = 2000 }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { NetTotalBytes = 100 }, new TelemetrySnapshot { NetTotalBytes = 200 }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { DiskReadMbps = 10f }, new TelemetrySnapshot { DiskReadMbps = 50f }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { DiskWriteMbps = 10f }, new TelemetrySnapshot { DiskWriteMbps = 50f }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new TelemetrySnapshot { NetInterfaceName = "eth" }, new TelemetrySnapshot { NetInterfaceName = "wifi" }])!).Should().BeTrue();
    }

    [WpfFact]
    public void InternalDrawing_Row1AndRow2_FlashAndThemeRendering()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Row1 = ["cpu_pct", "cpu_temp", "ram_pct", "net_down", "gpu_pct", "gpu_temp", "ram_used", "net_up"];
        cfg.Overlay.MetricColors["cpu_pct"] = "#FF0000";

        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);

        typeof(TaskbarOverlay).GetField("_flash", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(overlay, true);
        typeof(TaskbarOverlay).GetField("_isLightTheme", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(overlay, true);

        var snap = new TelemetrySnapshot
        {
            CpuUsagePct = 50f,
            CpuTempPackageC = 65f,
            CpuFreqGhz = 4.2f,
            CpuPackagePowerW = 85f,
            RamUsagePct = 60f,
            RamUsedGb = 16.4f,
            GpuUsagePct = 80f,
            GpuTempC = 72f,
            GpuVramUsedMb = 8000f,
            GpuFreqMHz = 2000f,
            GpuPowerW = 200f,
            NetDownBps = 1000000,
            NetUpBps = 500000,
            NetTotalBytes = 50000000,
            DiskReadMbps = 100f,
            DiskWriteMbps = 50f,
            BatteryPct = 85f,
            BatteryChargeRateW = -15f,
            NetInterfaceName = "Ethernet"
        };
        snap.Disks.Add(new DiskSnapshot { Id = "disk0", Label = "Disk 0", BusType = "NVMe", ReadMBps = 100f });
        typeof(TaskbarOverlay).GetField("_snap", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(overlay, snap);

        using var bmp = new Bitmap(400, 80);
        using var g = Graphics.FromImage(bmp);

        var drawContent = typeof(TaskbarOverlay).GetMethod("DrawContent", BindingFlags.NonPublic | BindingFlags.Instance);
        var actDraw1 = () => drawContent!.Invoke(overlay, [g]);
        actDraw1.Should().NotThrow();

        // Toggle flash off
        typeof(TaskbarOverlay).GetField("_flash", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(overlay, false);
        var actDraw2 = () => drawContent!.Invoke(overlay, [g]);
        actDraw2.Should().NotThrow();
    }

    [Fact]
    public void IsMetricEnabled_ReturnsCorrectForAllCategories()
    {
        var method = typeof(TaskbarOverlay).GetMethod("IsMetricEnabled", BindingFlags.NonPublic | BindingFlags.Static);
        var mc = new MonitoringConfig
        {
            EnableCpu = true,
            EnableRam = true,
            EnableGpu = true,
            EnableNet = true,
            EnableBattery = true,
            EnableDisk = true,
        };

        ((bool)method!.Invoke(null, [Metrics.CpuPct, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.CpuTemp, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.CpuFreq, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.CpuPower, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.RamPct, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.RamUsed, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.GpuPct, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.GpuTemp, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.GpuVram, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.GpuFreq, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.GpuPower, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.NetDown, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.NetUp, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.NetTotal, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.BatteryPct, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.BatteryRate, mc])!).Should().BeTrue();
        ((bool)method.Invoke(null, ["disk_disk0_read", mc])!).Should().BeTrue();

        ((bool)method.Invoke(null, [Metrics.CpuPct, new MonitoringConfig { EnableCpu = false }])!).Should().BeFalse();
        ((bool)method.Invoke(null, [Metrics.RamPct, new MonitoringConfig { EnableRam = false }])!).Should().BeFalse();
        ((bool)method.Invoke(null, [Metrics.GpuPct, new MonitoringConfig { EnableGpu = false }])!).Should().BeFalse();
        ((bool)method.Invoke(null, [Metrics.NetDown, new MonitoringConfig { EnableNet = false }])!).Should().BeFalse();
        ((bool)method.Invoke(null, [Metrics.BatteryPct, new MonitoringConfig { EnableBattery = false }])!).Should().BeFalse();
        ((bool)method.Invoke(null, ["disk_disk0_read", new MonitoringConfig { EnableDisk = false }])!).Should().BeFalse();
        ((bool)method.Invoke(null, ["unrecognized_custom_metric", mc])!).Should().BeTrue();
    }

    [Fact]
    public void IsMetricAvailable_BatteryOnlyAvailableWhenNonZero()
    {
        var method = typeof(TaskbarOverlay).GetMethod("IsMetricAvailable", BindingFlags.NonPublic | BindingFlags.Static);
        ((bool)method!.Invoke(null, [Metrics.BatteryPct, new TelemetrySnapshot { BatteryPct = 50f }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.BatteryPct, new TelemetrySnapshot { BatteryPct = 0f }])!).Should().BeFalse();
        ((bool)method.Invoke(null, [Metrics.BatteryRate, new TelemetrySnapshot { BatteryPct = 50f }])!).Should().BeTrue();
        ((bool)method.Invoke(null, [Metrics.BatteryRate, new TelemetrySnapshot { BatteryPct = 0f }])!).Should().BeFalse();
    }

    [Fact]
    public void AdjustForTheme_DarkensAndBrightensCorrectly()
    {
        var method = typeof(TaskbarOverlay).GetMethod("AdjustForTheme", BindingFlags.NonPublic | BindingFlags.Static);
        var bright = Color.FromArgb(255, 255, 200);
        ((Color)method!.Invoke(null, [bright, true])!).R.Should().BeLessThan(bright.R);

        var dark = Color.FromArgb(20, 20, 20);
        ((Color)method.Invoke(null, [dark, false])!).R.Should().BeGreaterThan(dark.R);

        // No adjustment needed
        ((Color)method.Invoke(null, [Color.Black, true])!).Should().Be(Color.Black);
        ((Color)method.Invoke(null, [Color.White, false])!).Should().Be(Color.White);
    }

    [WpfFact]
    public void MakeValueBrush_CachesBrushesByArgb()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);

        var makeBrush = typeof(TaskbarOverlay).GetMethod("MakeValueBrush", BindingFlags.NonPublic | BindingFlags.Instance);
        var brush1 = (SolidBrush)makeBrush!.Invoke(overlay, [Metrics.CpuPct, cfg.Overlay])!;
        var brush2 = (SolidBrush)makeBrush.Invoke(overlay, [Metrics.CpuPct, cfg.Overlay])!;

        brush1.Should().BeSameAs(brush2);
    }

    [WpfFact]
    public void TaskbarOverlay_FullWindowLifecycleAndWndProc()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Visible = true;
        cfg.Overlay.FollowWindowsTheme = true;
        cfg.Overlay.DoubleClickAction = "settings";
        cfg.Overlay.Row1 = [
            "cpu_pct", "cpu_temp", "cpu_freq", "cpu_power",
            "ram_pct", "ram_used",
            "gpu_pct", "gpu_temp", "gpu_vram", "gpu_freq", "gpu_power",
            "net_down", "net_up", "net_total",
            "battery_pct", "battery_rate",
            "disk_disk0_read", "disk_disk0_write"
        ];

        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);

        string? clickedAction = null;
        overlay.OnDoubleClick = (act) => clickedAction = act;

        overlay.Show();
        overlay.Visible.Should().BeTrue();

        var snap = new TelemetrySnapshot
        {
            CpuUsagePct = 50f,
            CpuTempPackageC = 65f,
            CpuFreqGhz = 4.2f,
            CpuPackagePowerW = 85f,
            RamUsagePct = 60f,
            RamUsedGb = 16.4f,
            GpuUsagePct = 80f,
            GpuTempC = 72f,
            GpuVramUsedMb = 8000f,
            GpuFreqMHz = 2000f,
            GpuPowerW = 200f,
            NetDownBps = 1000000,
            NetUpBps = 500000,
            NetTotalBytes = 50000000,
            DiskReadMbps = 100f,
            DiskWriteMbps = 50f,
            BatteryPct = 85f,
            BatteryChargeRateW = -15f,
            NetInterfaceName = "Ethernet"
        };
        snap.Disks.Add(new DiskSnapshot { Id = "disk0", Label = "Disk 0", BusType = "NVMe", ReadMBps = 100f });

        overlay.UpdateSnapshot(snap);
        overlay.Flash(TimeSpan.FromMilliseconds(200));
        overlay.RefreshOverlay();

        // WndProc messages
        var wndProc = typeof(TaskbarOverlay).GetMethod("WndProc", BindingFlags.NonPublic | BindingFlags.Instance);
        wndProc!.Invoke(overlay, [Message.Create(overlay.Handle, NativeMethods.WM_PAINT, IntPtr.Zero, IntPtr.Zero)]);
        wndProc.Invoke(overlay, [Message.Create(overlay.Handle, NativeMethods.WM_ERASEBKGND, IntPtr.Zero, IntPtr.Zero)]);
        wndProc.Invoke(overlay, [Message.Create(overlay.Handle, NativeMethods.WM_DPICHANGED, IntPtr.Zero, IntPtr.Zero)]);
        wndProc.Invoke(overlay, [Message.Create(overlay.Handle, NativeMethods.WM_TABLET_QUERYSYSTEMGESTURESTATUS, IntPtr.Zero, IntPtr.Zero)]);
        wndProc.Invoke(overlay, [Message.Create(overlay.Handle, NativeMethods.WM_NCHITTEST, IntPtr.Zero, IntPtr.Zero)]);
        wndProc.Invoke(overlay, [Message.Create(overlay.Handle, NativeMethods.WM_LBUTTONDOWN, IntPtr.Zero, IntPtr.Zero)]);
        wndProc.Invoke(overlay, [Message.Create(overlay.Handle, NativeMethods.WM_LBUTTONDBLCLK, IntPtr.Zero, IntPtr.Zero)]);

        clickedAction.Should().Be("settings");

        // Reflection invocations for timer handlers & rendering
        typeof(TaskbarEmbedder).GetProperty("IsFallback", BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(embedder, [false]);
        typeof(TaskbarEmbedder).GetProperty("IsEmbedded", BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(embedder, [false]);

        var renderFloat = typeof(TaskbarOverlay).GetMethod("RenderFloat", BindingFlags.NonPublic | BindingFlags.Instance);
        renderFloat!.Invoke(overlay, null);

        var posTick = typeof(TaskbarOverlay).GetMethod("PosTick", BindingFlags.NonPublic | BindingFlags.Instance);
        posTick!.Invoke(overlay, null);

        var watchTick = typeof(TaskbarOverlay).GetMethod("WatchTick", BindingFlags.NonPublic | BindingFlags.Instance);
        watchTick!.Invoke(overlay, null);

        var invalidate = typeof(TaskbarOverlay).GetMethod("Invalidate", BindingFlags.NonPublic | BindingFlags.Instance);
        invalidate!.Invoke(overlay, null);

        var getDpi = typeof(TaskbarOverlay).GetMethod("GetDpi", BindingFlags.NonPublic | BindingFlags.Instance);
        ((float)getDpi!.Invoke(overlay, null)!).Should().BeGreaterThan(0f);

        var screenBounds = typeof(TaskbarOverlay).GetMethod("GetScreenBounds", BindingFlags.NonPublic | BindingFlags.Instance);
        ((Rectangle)screenBounds!.Invoke(overlay, null)!).Should().NotBeNull();

        overlay.Hide();
        overlay.Visible.Should().BeFalse();
    }

    [WpfFact]
    public void TaskbarOverlay_ThemeTick_MeasureSize_And_MouseHook_Coverage()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Visible = true;
        cfg.Overlay.Row1 = [Metrics.CpuPct, Metrics.RamPct, Metrics.GpuPct, Metrics.NetDown];

        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);

        overlay.OnOpenSettingsRequested = () => { };
        overlay.OnToggleOverlayRequested = () => { };
        overlay.OnExitRequested = () => { };

        overlay.Show();

        var themeTick = typeof(TaskbarOverlay).GetMethod("ThemeTick", BindingFlags.NonPublic | BindingFlags.Instance);
        themeTick!.Invoke(overlay, null);

        var measureSize = typeof(TaskbarOverlay).GetMethod("MeasureSize", BindingFlags.NonPublic | BindingFlags.Instance);
        var sz = (Size)measureSize!.Invoke(overlay, null)!;
        sz.Width.Should().BeGreaterThan(0);

        // Exercise mouse hook procedure
        var mouseProc = typeof(TaskbarOverlay).GetMethod("MouseHookCallback", BindingFlags.NonPublic | BindingFlags.Instance);
        var structPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MSLLHOOKSTRUCT>());
        try
        {
            System.Runtime.InteropServices.Marshal.StructureToPtr(new NativeMethods.MSLLHOOKSTRUCT { pt = new NativeMethods.POINT { X = 10, Y = 10 } }, structPtr, false);

            // WM_RBUTTONUP (context menu)
            mouseProc!.Invoke(overlay, [0, (IntPtr)NativeMethods.WM_RBUTTONUP, structPtr]);

            // WM_LBUTTONDOWN & WM_LBUTTONDBLCLK
            mouseProc.Invoke(overlay, [0, (IntPtr)NativeMethods.WM_LBUTTONDOWN, structPtr]);
            mouseProc.Invoke(overlay, [0, (IntPtr)NativeMethods.WM_LBUTTONDBLCLK, structPtr]);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(structPtr);
        }

        overlay.Hide();
    }

    [WpfFact]
    public void TaskbarOverlay_GetDiskLabel_FormatsSingleAndMultipleDisksCorrectly()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);

        var snap = new TelemetrySnapshot
        {
            PrimaryDiskType = "SSD",
            Disks =
            [
                new DiskSnapshot { Id = "disk0", BusType = "NVMe", ReadMBps = 10f },
                new DiskSnapshot { Id = "disk1", BusType = "NVMe", ReadMBps = 20f },
                new DiskSnapshot { Id = "disk2", BusType = "SATA SSD", ReadMBps = 30f },
                new DiskSnapshot { Id = "disk3", BusType = "HDD", ReadMBps = 5f },
                new DiskSnapshot { Id = "disk4", BusType = "SCSI", ReadMBps = 15f }
            ]
        };
        typeof(TaskbarOverlay).GetField("_snap", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(overlay, snap);

        var getDiskLabel = typeof(TaskbarOverlay).GetMethod("GetDiskLabel", BindingFlags.NonPublic | BindingFlags.Instance);
        getDiskLabel.Should().NotBeNull();

        // Disks of same type get numbered
        ((string)getDiskLabel!.Invoke(overlay, ["disk_disk0_read"])!).Should().Be("NVMe1R");
        ((string)getDiskLabel.Invoke(overlay, ["disk_disk1_write"])!).Should().Be("NVMe2W");
        ((string)getDiskLabel.Invoke(overlay, ["disk_disk2_read"])!).Should().Be("SATAR");
        ((string)getDiskLabel.Invoke(overlay, ["disk_disk3_write"])!).Should().Be("HDDW");
        ((string)getDiskLabel.Invoke(overlay, ["disk_disk4_read"])!).Should().Be("SCSIR");

        // Malformed or invalid indices
        ((string)getDiskLabel.Invoke(overlay, ["disk_"])!).Should().Be("SSD");
        ((string)getDiskLabel.Invoke(overlay, ["disk_disk99_read"])!).Should().Be("DISK99R");
    }

    [WpfFact]
    public void TaskbarOverlay_DrawItems_SingleRow_And_CustomSettings_Coverage()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Row1 = [Metrics.CpuPct]; // Single row layout
        cfg.Overlay.FontBold = false;
        cfg.Overlay.ScalePct = 120;
        cfg.Overlay.ValueColor = "#00FF00";
        cfg.Overlay.MetricColors.Clear();

        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);

        var snap = new TelemetrySnapshot { CpuUsagePct = 25f };
        typeof(TaskbarOverlay).GetField("_snap", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(overlay, snap);

        using var bmp = new Bitmap(200, 40);
        using var g = Graphics.FromImage(bmp);

        var drawContent = typeof(TaskbarOverlay).GetMethod("DrawContent", BindingFlags.NonPublic | BindingFlags.Instance);
        var actDrawSingle = () => drawContent!.Invoke(overlay, [g]);
        actDrawSingle.Should().NotThrow();

        // Empty row
        cfg.Overlay.Row1 = [];
        var actDrawEmpty = () => drawContent!.Invoke(overlay, [g]);
        actDrawEmpty.Should().NotThrow();
    }

    [WpfFact]
    public void TaskbarOverlay_HandleOverlayMouseMessage_ExercisesAllMouseActions()
    {
        var cfg = new AppSettings();
        cfg.Overlay.DoubleClickAction = "taskmanager";
        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);

        Point? contextMenuPoint = null;
        overlay.ShowContextMenuAction = pt => contextMenuPoint = pt;

        string? dblClickAction = null;
        overlay.OnDoubleClick = act => dblClickAction = act;

        var handleMouse = typeof(TaskbarOverlay).GetMethod("HandleOverlayMouseMessage", BindingFlags.NonPublic | BindingFlags.Instance);
        handleMouse.Should().NotBeNull();

        // WM_SETCURSOR / WM_MOUSEMOVE
        object[] setCursorArgs = [NativeMethods.WM_SETCURSOR, IntPtr.Zero];
        ((bool)handleMouse!.Invoke(overlay, setCursorArgs)!).Should().BeFalse();

        // WM_RBUTTONDOWN
        object[] rBtnDownArgs = [NativeMethods.WM_RBUTTONDOWN, IntPtr.Zero];
        ((bool)handleMouse.Invoke(overlay, rBtnDownArgs)!).Should().BeTrue();
        ((IntPtr)rBtnDownArgs[1]).Should().Be((IntPtr)1);

        // WM_RBUTTONUP
        object[] rBtnUpArgs = [NativeMethods.WM_RBUTTONUP, IntPtr.Zero];
        ((bool)handleMouse.Invoke(overlay, rBtnUpArgs)!).Should().BeTrue();

        // WM_LBUTTONDBLCLK
        object[] lBtnDblClkArgs = [NativeMethods.WM_LBUTTONDBLCLK, IntPtr.Zero];
        ((bool)handleMouse.Invoke(overlay, lBtnDblClkArgs)!).Should().BeTrue();

        // Unhandled
        object[] unhandledArgs = [0x9999, IntPtr.Zero];
        ((bool)handleMouse.Invoke(overlay, unhandledArgs)!).Should().BeFalse();
    }

    [WpfFact]
    public void TaskbarOverlay_ReShow_And_DuplicateDispose_Lifecycle()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Visible = true;
        var embedder = new TaskbarEmbedder(cfg);
        var overlay = new TaskbarOverlay(cfg, embedder);

        overlay.Show();
        overlay.Visible.Should().BeTrue();

        // Second show when already visible is a no-op
        overlay.Show();
        overlay.Visible.Should().BeTrue();

        // Hide then re-show exercises existing Handle path
        overlay.Hide();
        overlay.Visible.Should().BeFalse();
        overlay.Show();
        overlay.Visible.Should().BeTrue();

        // Dispose twice is idempotent and safe
        var disposeAction = () => overlay.Dispose();
        disposeAction.Should().NotThrow();
        disposeAction.Should().NotThrow();
    }

    [WpfFact]
    public void TaskbarOverlay_UpdateSnapshot_WhenDisabledOrDisposed_IsIgnored()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Visible = false;
        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);

        var act = () => overlay.UpdateSnapshot(new TelemetrySnapshot { CpuUsagePct = 50f });
        act.Should().NotThrow();

        overlay.Dispose();
        var actDisposed = () => overlay.UpdateSnapshot(new TelemetrySnapshot { CpuUsagePct = 50f });
        actDisposed.Should().NotThrow();
    }

    [WpfFact]
    public void TaskbarOverlay_PosTick_And_WatchTick_ChildModeAndTrayChanged()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Visible = true;
        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);
        overlay.Show();

        typeof(TaskbarEmbedder).GetProperty("IsEmbedded", BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(embedder, [true]);
        typeof(TaskbarEmbedder).GetProperty("IsFallback", BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(embedder, [false]);

        // Exercise PosTick when IsChildMode is true and DPI changes
        typeof(TaskbarOverlay).GetField("_dpi", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(overlay, 0.5f);
        var posTick = typeof(TaskbarOverlay).GetMethod("PosTick", BindingFlags.NonPublic | BindingFlags.Instance);
        var actPos = () => posTick!.Invoke(overlay, null);
        actPos.Should().NotThrow();

        // Exercise WatchTick when _lastTrayHwnd differs
        typeof(TaskbarOverlay).GetField("_lastTrayHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(overlay, (IntPtr)12345);
        var watchTick = typeof(TaskbarOverlay).GetMethod("WatchTick", BindingFlags.NonPublic | BindingFlags.Instance);
        var actWatch = () => watchTick!.Invoke(overlay, null);
        actWatch.Should().NotThrow();

        // Exercise WndProc DPI changed messages
        var wndProc = typeof(TaskbarOverlay).GetMethod("WndProc", BindingFlags.NonPublic | BindingFlags.Instance);
        wndProc!.Invoke(overlay, [Message.Create(overlay.Handle, NativeMethods.WM_DPICHANGED_BEFOREPARENT, IntPtr.Zero, IntPtr.Zero)]);
        wndProc.Invoke(overlay, [Message.Create(overlay.Handle, NativeMethods.WM_DPICHANGED_AFTERPARENT, IntPtr.Zero, IntPtr.Zero)]);
        wndProc.Invoke(overlay, [Message.Create(overlay.Handle, NativeMethods.WM_CONTEXTMENU, IntPtr.Zero, IntPtr.Zero)]);
        wndProc.Invoke(overlay, [Message.Create(overlay.Handle, NativeMethods.WM_RBUTTONUP, IntPtr.Zero, IntPtr.Zero)]);

        overlay.Hide();
    }

    [WpfFact]
    public void TaskbarOverlay_UpdateKeyColor_InvalidHexAndLightTheme_HandlesFallbackGracefully()
    {
        var cfg = new AppSettings();
        cfg.Overlay.FollowWindowsTheme = true;
        cfg.Overlay.BgColor = "invalid_hex_string";
        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);

        // Test with light theme
        typeof(TaskbarOverlay).GetField("_isLightTheme", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(overlay, true);
        var updateKeyColor = typeof(TaskbarOverlay).GetMethod("UpdateKeyColor", BindingFlags.NonPublic | BindingFlags.Instance);
        var actLight = () => updateKeyColor!.Invoke(overlay, null);
        actLight.Should().NotThrow();

        // Test with dark theme and invalid hex fallback
        typeof(TaskbarOverlay).GetField("_isLightTheme", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(overlay, false);
        var actDark = () => updateKeyColor!.Invoke(overlay, null);
        actDark.Should().NotThrow();
    }

    [WpfFact]
    public void TaskbarOverlay_TimerTicks_And_TaskbarRecovery_Coverage()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Visible = true;
        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);
        overlay.Show();

        bool priorSkipTimers = TaskbarOverlay.SkipTimersForTesting;
        bool priorSkipEmbed = TaskbarOverlay.SkipEmbedForTesting;
        bool priorSkipShow = TaskbarOverlay.SkipShowWindowForTesting;

        TaskbarOverlay.SkipTimersForTesting = false;
        TaskbarOverlay.SkipEmbedForTesting = false;
        TaskbarOverlay.SkipShowWindowForTesting = false;

        try
        {
            // 1. RecoverTaskbar with embed and show enabled
            var actRecover = () => overlay.RecoverTaskbar("unit test recovery");
            actRecover.Should().NotThrow();

            // 2. PosTick with DPI mismatch and dimension change
            typeof(TaskbarEmbedder).GetProperty("IsEmbedded", BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(embedder, [true]);
            typeof(TaskbarOverlay).GetField("_dpi", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(overlay, 0.1f);
            var posTick = typeof(TaskbarOverlay).GetMethod("PosTick", BindingFlags.NonPublic | BindingFlags.Instance);
            posTick!.Invoke(overlay, null);
            System.Windows.Forms.Application.DoEvents();

            // PosTick with dimension mismatch
            typeof(TaskbarOverlay).GetField("_overlayWidth", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(overlay, 1);
            posTick.Invoke(overlay, null);
            System.Windows.Forms.Application.DoEvents();

            // 3. WatchTick with tray window change
            TaskbarEmbedder.ExplorerTrayWndOverride = () => (IntPtr)12345;
            typeof(TaskbarOverlay).GetField("_lastTrayHwnd", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(overlay, (IntPtr)999999);
            var watchTick = typeof(TaskbarOverlay).GetMethod("WatchTick", BindingFlags.NonPublic | BindingFlags.Instance);
            watchTick!.Invoke(overlay, null);
            System.Windows.Forms.Application.DoEvents();

            // 4. ThemeTick with flipped theme flag
            bool currentIsLight = (bool)typeof(TaskbarOverlay).GetField("_isLightTheme", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(overlay)!;
            typeof(TaskbarOverlay).GetField("_isLightTheme", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(overlay, !currentIsLight);
            var themeTick = typeof(TaskbarOverlay).GetMethod("ThemeTick", BindingFlags.NonPublic | BindingFlags.Instance);
            themeTick!.Invoke(overlay, null);

            // 5. ApplyOverlayDimensions directly
            var applyDimensions = typeof(TaskbarOverlay).GetMethod("ApplyOverlayDimensions", BindingFlags.NonPublic | BindingFlags.Instance);
            var actDimensions = () => applyDimensions!.Invoke(overlay, [new Size(999, 999)]);
            actDimensions.Should().NotThrow();

            // Pump message loop to execute posted delegates
            System.Windows.Forms.Application.DoEvents();
        }
        finally
        {
            TaskbarEmbedder.ExplorerTrayWndOverride = null;
            TaskbarOverlay.SkipTimersForTesting = priorSkipTimers;
            TaskbarOverlay.SkipEmbedForTesting = priorSkipEmbed;
            TaskbarOverlay.SkipShowWindowForTesting = priorSkipShow;
            overlay.Hide();
        }
    }

    [WpfFact]
    public void TaskbarOverlay_MouseHook_ClosesContextMenuWhenClickedOutside()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Visible = true;
        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);
        overlay.Show();

        bool closeContextMenuCalled = false;
        overlay.IsContextMenuOpen = () => true;
        overlay.GetContextMenuBounds = () => new Rectangle(300, 300, 150, 150);
        overlay.CloseContextMenuAction = () => closeContextMenuCalled = true;

        var mouseProc = typeof(TaskbarOverlay).GetMethod("MouseHookCallback", BindingFlags.NonPublic | BindingFlags.Instance);
        var structPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MSLLHOOKSTRUCT>());
        try
        {
            // Click outside the context menu bounds (at 10, 10)
            System.Runtime.InteropServices.Marshal.StructureToPtr(new NativeMethods.MSLLHOOKSTRUCT { pt = new NativeMethods.POINT { X = 10, Y = 10 } }, structPtr, false);
            mouseProc!.Invoke(overlay, [0, (IntPtr)NativeMethods.WM_LBUTTONDOWN, structPtr]);

            closeContextMenuCalled.Should().BeTrue();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(structPtr);
            overlay.Hide();
        }
    }

    [WpfFact]
    public void TaskbarOverlay_ShowAndHide_RealLifecycleAndSnapshotDispatch()
    {
        bool priorShow = TaskbarOverlay.SkipShowWindowForTesting;
        bool priorEmbed = TaskbarOverlay.SkipEmbedForTesting;
        bool priorMouse = TaskbarOverlay.SkipMouseHookForTesting;

        TaskbarOverlay.SkipShowWindowForTesting = false;
        TaskbarOverlay.SkipEmbedForTesting = false;
        TaskbarOverlay.SkipMouseHookForTesting = false;

        try
        {
            var cfg = new AppSettings();
            cfg.Overlay.Visible = true;
            cfg.Overlay.Row1 = ["cpu_pct", "ram_pct"];

            var embedder = new TaskbarEmbedder(cfg);
            using var overlay = new TaskbarOverlay(cfg, embedder);

            // 1. Initial Show (creates handle, embeds, hooks, shows)
            overlay.Show();
            overlay.Visible.Should().BeTrue();

            // 2. UpdateSnapshot posts to UI thread
            var snap = new TelemetrySnapshot { CpuUsagePct = 45f, RamUsagePct = 65f };
            overlay.UpdateSnapshot(snap);
            System.Windows.Forms.Application.DoEvents();

            // 3. Hide (unhooks, hides, detaches)
            overlay.Hide();
            overlay.Visible.Should().BeFalse();

            // 4. Re-show existing window (hits line 91 branch)
            overlay.Show();
            overlay.Visible.Should().BeTrue();

            // 5. Hide again
            overlay.Hide();
        }
        finally
        {
            TaskbarOverlay.SkipShowWindowForTesting = priorShow;
            TaskbarOverlay.SkipEmbedForTesting = priorEmbed;
            TaskbarOverlay.SkipMouseHookForTesting = priorMouse;
        }
    }

    [WpfFact]
    public void TaskbarOverlay_WndProc_MessagesAndWmPaint()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Visible = true;
        cfg.Overlay.DoubleClickAction = "settings";

        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);
        overlay.Show();

        bool doubleClickFired = false;
        overlay.OnDoubleClick += action =>
        {
            if (action == "settings") doubleClickFired = true;
        };

        var wndProc = typeof(TaskbarOverlay).GetMethod("WndProc", BindingFlags.NonPublic | BindingFlags.Instance);
        var handle = overlay.Handle;

        // 1. WM_SETCURSOR
        var msgCursor = Message.Create(handle, NativeMethods.WM_SETCURSOR, IntPtr.Zero, IntPtr.Zero);
        wndProc!.Invoke(overlay, [msgCursor]);

        // 2. WM_DESTROY / WM_NCDESTROY
        var msgDestroy = Message.Create(handle, NativeMethods.WM_DESTROY, IntPtr.Zero, IntPtr.Zero);
        wndProc.Invoke(overlay, [msgDestroy]);
        var msgNcDestroy = Message.Create(handle, NativeMethods.WM_NCDESTROY, IntPtr.Zero, IntPtr.Zero);
        wndProc.Invoke(overlay, [msgNcDestroy]);

        // 3. WM_DISPLAYCHANGE
        var msgDisplay = Message.Create(handle, NativeMethods.WM_DISPLAYCHANGE, IntPtr.Zero, IntPtr.Zero);
        wndProc.Invoke(overlay, [msgDisplay]);

        // 4. WM_DEVICECHANGE with DBT_DEVICEARRIVAL
        var msgDevice = Message.Create(handle, NativeMethods.WM_DEVICECHANGE, (IntPtr)NativeMethods.DBT_DEVICEARRIVAL, IntPtr.Zero);
        wndProc.Invoke(overlay, [msgDevice]);

        // 5. WM_DPICHANGED
        var msgDpi = Message.Create(handle, NativeMethods.WM_DPICHANGED, IntPtr.Zero, IntPtr.Zero);
        wndProc.Invoke(overlay, [msgDpi]);

        // 6. Rapid double click via WM_LBUTTONDOWN
        var msgClick1 = Message.Create(handle, NativeMethods.WM_LBUTTONDOWN, IntPtr.Zero, IntPtr.Zero);
        wndProc.Invoke(overlay, [msgClick1]);
        var msgClick2 = Message.Create(handle, NativeMethods.WM_LBUTTONDOWN, IntPtr.Zero, IntPtr.Zero);
        wndProc.Invoke(overlay, [msgClick2]);
        doubleClickFired.Should().BeTrue();

        // 7. WmPaint
        var wmPaint = typeof(TaskbarOverlay).GetMethod("WmPaint", BindingFlags.NonPublic | BindingFlags.Instance);
        var actPaint = () => wmPaint!.Invoke(overlay, null);
        actPaint.Should().NotThrow();

        // 8. ShowOverlayContextMenu when action is null or throws
        overlay.ShowContextMenuAction = null;
        var showMenu = typeof(TaskbarOverlay).GetMethod("ShowOverlayContextMenu", BindingFlags.NonPublic | BindingFlags.Instance);
        var actNull = () => showMenu!.Invoke(overlay, null);
        actNull.Should().NotThrow();

        overlay.ShowContextMenuAction = _ => throw new InvalidOperationException("Failed to display context menu");
        var actMenu = () => showMenu!.Invoke(overlay, null);
        actMenu.Should().NotThrow();

        overlay.Hide();
    }

    [WpfFact]
    public void TaskbarOverlay_HandleDpiChanged_And_DetectSystemTheme_Coverage()
    {
        var cfg = new AppSettings();
        var embedder = new TaskbarEmbedder(cfg);
        using var overlay = new TaskbarOverlay(cfg, embedder);
        overlay.Show();

        var handleDpi = typeof(TaskbarOverlay).GetMethod("HandleDpiChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        var actDpi = () => handleDpi!.Invoke(overlay, null);
        actDpi.Should().NotThrow();

        var detectTheme = typeof(TaskbarOverlay).GetMethod("DetectSystemTheme", BindingFlags.NonPublic | BindingFlags.Instance);
        var actTheme = () => detectTheme!.Invoke(overlay, null);
        actTheme.Should().NotThrow();

        overlay.Hide();
    }
}
