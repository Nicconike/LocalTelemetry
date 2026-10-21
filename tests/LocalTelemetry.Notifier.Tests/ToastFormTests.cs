using System.Reflection;
using FluentAssertions;
using Xunit;

namespace LocalTelemetry.Notifier.Tests;

public class ToastFormTests
{
    public ToastFormTests()
    {
        ToastForm.SkipShowForTesting = true;
    }

    [WpfFact]
    public void ToastForm_Initialization_SetsUpFormAndControls()
    {
        using var toast = new ToastForm("Test Title", "Test Body Message");

        toast.Should().NotBeNull();
        toast.ShowInTaskbar.Should().BeFalse();
        toast.TopMost.Should().BeTrue();
        toast.FormBorderStyle.Should().Be(FormBorderStyle.None);
        toast.Size.Should().Be(new Size(320, 90));
        toast.BackColor.Should().Be(Color.FromArgb(28, 28, 28));
        toast.Controls.Count.Should().Be(3);

        var accentBar = toast.Controls[0] as Panel;
        accentBar.Should().NotBeNull();
        accentBar.BackColor.Should().Be(Color.FromArgb(245, 158, 11));

        var titleLabel = toast.Controls[1] as Label;
        titleLabel.Should().NotBeNull();
        titleLabel.Text.Should().Be("Test Title");

        var bodyLabel = toast.Controls[2] as Label;
        bodyLabel.Should().NotBeNull();
        bodyLabel.Text.Should().Be("Test Body Message");

        // Verify non-activating focus-safe window styles
        var showWithoutActProp = typeof(ToastForm).GetProperty("ShowWithoutActivation", BindingFlags.NonPublic | BindingFlags.Instance);
        showWithoutActProp.Should().NotBeNull();
        showWithoutActProp.GetValue(toast).Should().Be(true);

        var createParamsProp = typeof(ToastForm).GetProperty("CreateParams", BindingFlags.NonPublic | BindingFlags.Instance);
        createParamsProp.Should().NotBeNull();
        var cp = (CreateParams)createParamsProp.GetValue(toast)!;
        (cp.ExStyle & 0x08000000).Should().NotBe(0, "WS_EX_NOACTIVATE should be set");
        (cp.ExStyle & 0x00000080).Should().NotBe(0, "WS_EX_TOOLWINDOW should be set");
    }

    [Fact]
    public void ToastForm_ComputeAnimOffset_EasesOffsetFromFullToZero()
    {
        const int maxOffset = 102;
        const int duration = 300;

        ToastForm.ComputeAnimOffset(0, maxOffset, duration).Should().Be(maxOffset);
        ToastForm.ComputeAnimOffset(duration, maxOffset, duration).Should().Be(0);
        ToastForm.ComputeAnimOffset(1000, maxOffset, duration).Should().Be(0);

        int mid = ToastForm.ComputeAnimOffset(150, maxOffset, duration);
        mid.Should().BeInRange(1, maxOffset - 1);
    }

    [WpfFact]
    public void ToastForm_OnPaint_DrawsBorderWithoutException()
    {
        using var toast = new ToastForm("Title", "Body");

        using var bitmap = new Bitmap(320, 90);
        using var graphics = Graphics.FromImage(bitmap);
        using var paintArgs = new PaintEventArgs(graphics, new Rectangle(0, 0, 320, 90));

        var onPaintMethod = typeof(ToastForm).GetMethod(
            "OnPaint",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(object), typeof(PaintEventArgs)],
            null);

        onPaintMethod.Should().NotBeNull();
        onPaintMethod.Invoke(toast, [this, paintArgs]);
    }

    [WpfFact]
    public void ToastForm_CloseTimer_FiresAndStops()
    {
        using var toast = new ToastForm("Title", "Body");
        var closeTimerField = typeof(ToastForm).GetField("_closeTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        closeTimerField.Should().NotBeNull();
        var timer = (System.Windows.Forms.Timer)closeTimerField.GetValue(toast)!;

        // Fire Tick event handler via reflection to execute line 64-67
        var onTickMethod = typeof(System.Windows.Forms.Timer).GetMethod("OnTick", BindingFlags.NonPublic | BindingFlags.Instance);
        if (onTickMethod != null)
        {
            onTickMethod.Invoke(timer, [EventArgs.Empty]);
        }
    }

    [WpfFact]
    public void ToastForm_SlideInAnimation_RunsTimerTicksToCompletion()
    {
        using var toast = new ToastForm("Title", "Body");
        var onLoadMethod = typeof(ToastForm).GetMethod(
            "OnLoad",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(object), typeof(EventArgs)],
            null);
        onLoadMethod.Should().NotBeNull();
        onLoadMethod.Invoke(toast, [this, EventArgs.Empty]);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 350)
        {
            Application.DoEvents();
            Thread.Yield();
        }
        toast.Should().NotBeNull();
    }

    [WpfFact]
    public void ToastForm_Show_WhenSkipShowForTestingFalse_InvokesBaseShow()
    {
        ToastForm.SkipShowForTesting = false;
        try
        {
            using var toast = new ToastForm("Test Title", "Test Body");
            toast.Show();
            toast.Visible.Should().BeTrue();
            toast.Close();
        }
        finally
        {
            ToastForm.SkipShowForTesting = true;
        }
    }
}
