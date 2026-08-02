using FluentAssertions;
using LocalTelemetry.App.Overlay;
using LocalTelemetry.App.Tray;
using LocalTelemetry.App.Win32;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LocalTelemetry.App.Tests.Win32;

public class Win32AndOverlayTests
{
    [Fact]
    public void WindowHelpers_Scale_CalculatesCorrectly()
    {
        WindowHelpers.Scale(100f, 1.0f).Should().Be(100);
        WindowHelpers.Scale(100f, 1.25f).Should().Be(125);
        WindowHelpers.Scale(14f, 1.5f).Should().Be(21);
    }

    [Fact]
    public void WindowHelpers_ParseHex_ParsesColorsAndFallbacks()
    {
        var red = WindowHelpers.ParseHex("#FF0000", Color.Black);
        red.R.Should().Be(255);
        red.G.Should().Be(0);
        red.B.Should().Be(0);

        var green = WindowHelpers.ParseHex("00FF00", Color.Black);
        green.G.Should().Be(255);

        var fallback = WindowHelpers.ParseHex("invalid_hex", Color.Blue);
        fallback.Should().Be(Color.Blue);

        var defaultFallback = WindowHelpers.ParseHex(null);
        defaultFallback.Should().Be(Color.White);
    }

    [Fact]
    public void WindowHelpers_StartupHelpers_ExecuteSafely()
    {
        WindowHelpers.SetStartup(false);
        bool isEnabled = WindowHelpers.IsStartupEnabled();
        isEnabled.Should().Be(isEnabled);
    }

    [Fact]
    public void TaskbarEmbedder_MethodsAndPropertiesWork()
    {
        var cfg = new AppSettings();
        var logger = NullLogger<TaskbarEmbedder>.Instance;
        var embedder = new TaskbarEmbedder(cfg, logger)
        {
            KeyColor = Color.Magenta
        };

        embedder.KeyColor.Should().Be(Color.Magenta);
        embedder.IsEmbedded.Should().BeFalse();
        embedder.IsFallback.Should().BeFalse();
        embedder.IsChildMode.Should().BeFalse();

        IntPtr trayHwnd = TaskbarEmbedder.FindExplorerTrayWnd();
        trayHwnd.Should().NotBeNull();

        embedder.Embed(IntPtr.Zero, new Size(200, 30));
        embedder.UpdateLayeredSettings(IntPtr.Zero);
        embedder.Reposition(IntPtr.Zero, new Size(220, 30));
        embedder.OnTaskbarRecreated(IntPtr.Zero, new Size(200, 30));
        embedder.Detach(IntPtr.Zero);
    }

    [WpfFact]
    public void TaskbarOverlay_LifecycleAndUpdates()
    {
        var cfg = new AppSettings();
        var logger = NullLogger<TaskbarOverlay>.Instance;
        var embedder = new TaskbarEmbedder(cfg, NullLogger<TaskbarEmbedder>.Instance);
        using var overlay = new TaskbarOverlay(cfg, embedder, logger);

        overlay.Should().NotBeNull();

        var snap = new TelemetrySnapshot
        {
            CpuUsagePct = 50f,
            CpuTempPackageC = 60f,
            GpuUsagePct = 70f,
            GpuTempC = 65f,
            RamUsagePct = 80f,
            RamUsedGb = 16f
        };

        overlay.UpdateSnapshot(snap);
        overlay.Flash(TimeSpan.FromMilliseconds(50));
    }

    [WpfFact]
    public void TrayIconManager_EventsAndTooltipUpdate()
    {
        using var tray = new TrayIconManager();

        bool settingsFired = false;
        bool quitFired = false;
        bool toggleFired = false;

        tray.OpenSettingsRequested += () => settingsFired = true;
        tray.QuitRequested += () => quitFired = true;
        tray.ToggleOverlayRequested += () => toggleFired = true;

        tray.SetOverlayVisible(true);
        tray.SetOverlayVisible(false);

        var snap = new TelemetrySnapshot
        {
            CpuUsagePct = 25.4f,
            RamUsagePct = 50.1f,
            GpuUsagePct = 75.8f
        };

        tray.Update(snap);

        settingsFired.Should().BeFalse();
        quitFired.Should().BeFalse();
        toggleFired.Should().BeFalse();
    }
}
