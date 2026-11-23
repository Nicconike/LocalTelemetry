using FluentAssertions;
using LocalTelemetry.App.Win32;
using LocalTelemetry.Core.Config;
using Xunit;

namespace LocalTelemetry.App.Tests.Win32;

public sealed class DisplayInfoTests : IDisposable
{
    public DisplayInfoTests()
    {
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
    public void DisplayInfo_EnumerateDisplays_WithMockDisplays_MapsPrimaryAndSecondary()
    {
        var mockDisplays = new List<DisplayInfo>
        {
            new DisplayInfo
            {
                Id = "primary",
                Index = 0,
                DeviceName = @"\\.\DISPLAY1",
                FriendlyName = "Display 1 (Primary - 2048 x 1280)",
                IsPrimary = true,
                Bounds = new Rectangle(0, 0, 2048, 1280),
                WorkingArea = new Rectangle(0, 0, 2048, 1232),
                TaskbarHwnd = new IntPtr(0x1001)
            },
            new DisplayInfo
            {
                Id = "display_2",
                Index = 1,
                DeviceName = @"\\.\DISPLAY2",
                FriendlyName = "Display 2 (1920 x 1080)",
                IsPrimary = false,
                Bounds = new Rectangle(2048, 0, 1920, 1080),
                WorkingArea = new Rectangle(2048, 0, 1920, 1032),
                TaskbarHwnd = new IntPtr(0x2002)
            },
            new DisplayInfo
            {
                Id = "display_3",
                Index = 2,
                DeviceName = @"\\.\DISPLAY3",
                FriendlyName = "Display 3 (2560 x 1440)",
                IsPrimary = false,
                Bounds = new Rectangle(-2560, 0, 2560, 1440),
                WorkingArea = new Rectangle(-2560, 0, 2560, 1440),
                TaskbarHwnd = IntPtr.Zero
            }
        };

        DisplayInfo.EnumerateDisplaysOverride = () => mockDisplays;

        var result = DisplayInfo.EnumerateDisplays();
        result.Should().HaveCount(3);
        result[0].IsPrimary.Should().BeTrue();
        result[0].Id.Should().Be("primary");
        result[0].TaskbarHwnd.Should().Be(new IntPtr(0x1001));

        result[1].IsPrimary.Should().BeFalse();
        result[1].Id.Should().Be("display_2");
        result[1].TaskbarHwnd.Should().Be(new IntPtr(0x2002));

        result[2].IsPrimary.Should().BeFalse();
        result[2].Id.Should().Be("display_3");
        result[2].HasTaskbar.Should().BeFalse();
    }

    [Fact]
    public void TaskbarEmbedder_WithSecondaryDisplay_ReturnsTargetTrayWnd()
    {
        var cfg = new AppSettings();
        var secDisplay = new DisplayInfo
        {
            Id = "display_2",
            Index = 1,
            IsPrimary = false,
            TaskbarHwnd = new IntPtr(0x5555)
        };

        var embedder = new TaskbarEmbedder(cfg, secDisplay);
        embedder.TargetDisplay.Should().Be(secDisplay);
        embedder.GetTargetTrayWnd().Should().Be(new IntPtr(0x5555));
    }
}
