using System.Reflection;
using FluentAssertions;
using LocalTelemetry.App.Overlay;
using LocalTelemetry.App.Settings;
using LocalTelemetry.App.Win32;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Models;
using Xunit;

namespace LocalTelemetry.App.Tests.Overlay;

public sealed class TaskbarOverlayManagerTests : IDisposable
{
    public TaskbarOverlayManagerTests()
    {
        TaskbarOverlay.SkipShowWindowForTesting = true;
        TaskbarOverlay.SkipMouseHookForTesting = true;
        TaskbarOverlay.SkipTimersForTesting = true;
        TaskbarOverlay.SkipEmbedForTesting = true;
        TaskbarOverlayManager.SkipDisplaySettingsChangedHookForTesting = true;
        DisplayInfo.SkipIsWindowCheckForTesting = true;
    }

    public void Dispose()
    {
        DisplayInfo.EnumerateDisplaysOverride = null;
        DisplayInfo.SkipIsWindowCheckForTesting = false;
        TaskbarEmbedder.ExplorerSecondaryTrayWndsOverride = null;
        TaskbarEmbedder.ExplorerTrayWndOverride = null;
    }

    [Fact]
    public void TaskbarOverlayManager_Show_WithPrimaryMode_SpawnsOnlyPrimary()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Visible = true;
        cfg.Overlay.DisplayMode = "primary";

        DisplayInfo.EnumerateDisplaysOverride = () =>
        [
            new DisplayInfo { Id = "primary", Index = 0, IsPrimary = true, TaskbarHwnd = new IntPtr(0x1001) },
            new DisplayInfo { Id = "display_2", Index = 1, IsPrimary = false, TaskbarHwnd = new IntPtr(0x2002) }
        ];

        var createdOverlays = new List<TaskbarOverlay>();
        using var manager = new TaskbarOverlayManager(cfg);
        manager.OverlayFactory = (c, d) =>
        {
            var overlay = new TaskbarOverlay(c, d);
            createdOverlays.Add(overlay);
            return overlay;
        };

        manager.Show();

        manager.GetActiveOverlays().Should().HaveCount(1);
        manager.PrimaryOverlay.Should().NotBeNull();
        createdOverlays.Should().HaveCount(1);
        createdOverlays[0].TargetDisplay?.Id.Should().Be("primary");
    }

    [Fact]
    public void TaskbarOverlayManager_Show_WithAllMode_SpawnsAllAvailableTaskbars()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Visible = true;
        cfg.Overlay.DisplayMode = "all";

        DisplayInfo.EnumerateDisplaysOverride = () =>
        [
            new DisplayInfo { Id = "primary", Index = 0, IsPrimary = true, TaskbarHwnd = new IntPtr(0x1001) },
            new DisplayInfo { Id = "display_2", Index = 1, IsPrimary = false, TaskbarHwnd = new IntPtr(0x2002) },
            new DisplayInfo { Id = "display_3", Index = 2, IsPrimary = false, TaskbarHwnd = IntPtr.Zero }
        ];

        var createdOverlays = new List<TaskbarOverlay>();
        using var manager = new TaskbarOverlayManager(cfg);
        manager.OverlayFactory = (c, d) =>
        {
            var overlay = new TaskbarOverlay(c, d);
            createdOverlays.Add(overlay);
            return overlay;
        };

        manager.Show();

        manager.GetActiveOverlays().Should().HaveCount(2);
        createdOverlays.Should().HaveCount(2);
        createdOverlays.Select(o => o.TargetDisplay?.Id).Should().Contain(["primary", "display_2"]);
    }

    [Fact]
    public void TaskbarOverlayManager_Show_WithCustomMode_SpawnsOnlySelected()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Visible = true;
        cfg.Overlay.DisplayMode = "custom";
        cfg.Overlay.EnabledDisplays = ["display_2"];

        DisplayInfo.EnumerateDisplaysOverride = () =>
        [
            new DisplayInfo { Id = "primary", Index = 0, IsPrimary = true, TaskbarHwnd = new IntPtr(0x1001) },
            new DisplayInfo { Id = "display_2", Index = 1, IsPrimary = false, TaskbarHwnd = new IntPtr(0x2002) }
        ];

        var createdOverlays = new List<TaskbarOverlay>();
        using var manager = new TaskbarOverlayManager(cfg);
        manager.OverlayFactory = (c, d) =>
        {
            var overlay = new TaskbarOverlay(c, d);
            createdOverlays.Add(overlay);
            return overlay;
        };

        manager.Show();

        manager.GetActiveOverlays().Should().HaveCount(1);
        createdOverlays[0].TargetDisplay?.Id.Should().Be("display_2");
    }

    [Fact]
    public void TaskbarOverlayManager_UpdateSnapshot_BroadcastsToAllActiveOverlays()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Visible = true;
        cfg.Overlay.DisplayMode = "all";

        DisplayInfo.EnumerateDisplaysOverride = () =>
        [
            new DisplayInfo { Id = "primary", Index = 0, IsPrimary = true, TaskbarHwnd = new IntPtr(0x1001) },
            new DisplayInfo { Id = "display_2", Index = 1, IsPrimary = false, TaskbarHwnd = new IntPtr(0x2002) }
        ];

        var snap = new TelemetrySnapshot { CpuUsagePct = 42f };
        using var manager = new TaskbarOverlayManager(cfg);
        var createdOverlays = new List<TaskbarOverlay>();
        manager.OverlayFactory = (c, d) =>
        {
            var overlay = new TaskbarOverlay(c, d);
            createdOverlays.Add(overlay);
            return overlay;
        };

        manager.Show();
        manager.UpdateSnapshot(snap);

        foreach (var overlay in createdOverlays)
        {
            var snapField = typeof(TaskbarOverlay).GetField("_snap", BindingFlags.NonPublic | BindingFlags.Instance);
            snapField?.GetValue(overlay).Should().Be(snap);
        }
    }

    [Fact]
    public void TaskbarOverlayManager_DoubleClick_ForwardedToManagerEvent()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Visible = true;
        cfg.Overlay.DisplayMode = "primary";

        DisplayInfo.EnumerateDisplaysOverride = () =>
        [
            new DisplayInfo { Id = "primary", Index = 0, IsPrimary = true, TaskbarHwnd = new IntPtr(0x1001) }
        ];

        using var manager = new TaskbarOverlayManager(cfg);
        TaskbarOverlay? created = null;
        manager.OverlayFactory = (c, d) =>
        {
            created = new TaskbarOverlay(c, d);
            return created;
        };

        string? receivedAction = null;
        manager.OnDoubleClick += action => receivedAction = action;

        manager.Show();
        created.Should().NotBeNull();

        created.OnDoubleClick?.Invoke("taskmanager");

        receivedAction.Should().Be("taskmanager");
    }

    [Fact]
    public void SettingsDtoMapping_MultiDisplayProperties_RoundTrips()
    {
        var cfg = new AppSettings();
        cfg.Overlay.DisplayMode = "custom";
        cfg.Overlay.EnabledDisplays = ["primary", "display_2"];

        DisplayInfo.EnumerateDisplaysOverride = () =>
        [
            new DisplayInfo { Id = "primary", FriendlyName = "Primary Display", IsPrimary = true, TaskbarHwnd = new IntPtr(0x1001) },
            new DisplayInfo { Id = "display_2", FriendlyName = "Secondary Display", IsPrimary = false, TaskbarHwnd = new IntPtr(0x2002) }
        ];

        var dto = SettingsDtoMapping.ToDto(cfg);
        dto.Overlay.DisplayMode.Should().Be("custom");
        dto.Overlay.EnabledDisplays.Should().BeEquivalentTo("primary", "display_2");
        dto.Overlay.DetectedDisplays.Should().HaveCount(2);
        dto.Overlay.DetectedDisplays[0].Name.Should().Be("Primary Display");

        var targetCfg = new AppSettings();
        SettingsDtoMapping.ApplyTo(targetCfg, dto);
        targetCfg.Overlay.DisplayMode.Should().Be("custom");
        targetCfg.Overlay.EnabledDisplays.Should().BeEquivalentTo("primary", "display_2");
    }

    [Fact]
    public void TaskbarOverlayManager_Show_WithSecondaryMode_SpawnsOnlySecondary()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Visible = true;
        cfg.Overlay.DisplayMode = "secondary";

        DisplayInfo.EnumerateDisplaysOverride = () =>
        [
            new DisplayInfo { Id = "primary", Index = 0, IsPrimary = true, TaskbarHwnd = new IntPtr(0x1001) },
            new DisplayInfo { Id = "display_2", Index = 1, IsPrimary = false, TaskbarHwnd = new IntPtr(0x2002) }
        ];

        var createdOverlays = new List<TaskbarOverlay>();
        using var manager = new TaskbarOverlayManager(cfg);
        manager.OverlayFactory = (c, d) =>
        {
            var overlay = new TaskbarOverlay(c, d);
            createdOverlays.Add(overlay);
            return overlay;
        };

        manager.Show();

        manager.GetActiveOverlays().Should().HaveCount(1);
        createdOverlays.Should().HaveCount(1);
        createdOverlays[0].TargetDisplay?.Id.Should().Be("display_2");
    }

    [Fact]
    public void TaskbarOverlayManager_Show_WithSecondaryMode_SingleDisplayFallbackToPrimary()
    {
        var cfg = new AppSettings();
        cfg.Overlay.Visible = true;
        cfg.Overlay.DisplayMode = "secondary";

        DisplayInfo.EnumerateDisplaysOverride = () =>
        [
            new DisplayInfo { Id = "primary", Index = 0, IsPrimary = true, TaskbarHwnd = new IntPtr(0x1001) }
        ];

        var createdOverlays = new List<TaskbarOverlay>();
        using var manager = new TaskbarOverlayManager(cfg);
        manager.OverlayFactory = (c, d) =>
        {
            var overlay = new TaskbarOverlay(c, d);
            createdOverlays.Add(overlay);
            return overlay;
        };

        manager.Show();

        manager.GetActiveOverlays().Should().HaveCount(1);
        createdOverlays.Should().HaveCount(1);
        createdOverlays[0].TargetDisplay?.Id.Should().Be("primary");
    }
}
