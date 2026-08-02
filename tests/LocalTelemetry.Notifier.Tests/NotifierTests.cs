using System.IO;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace LocalTelemetry.Notifier.Tests;

public class NotifierTests
{
    [Fact]
    public void NotifierLog_FormatsAndLogsWithoutExceptions()
    {
        NotifierLog.Info("Test info message");
        NotifierLog.Warn("Test warn message");
        NotifierLog.Warn(new InvalidOperationException("Test ex"), "Test warn exception");
        NotifierLog.Error("Test error message");
        NotifierLog.Error(new InvalidOperationException("Test ex"), "Test error exception");
    }

    [WpfFact]
    public void ToastForm_InitializationAndPaintEvents()
    {
        using var toast = new ToastForm("CPU High", "Usage reached 95%");
        toast.Should().NotBeNull();

        // Exercise paint event
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
        onPaintMethod!.Invoke(toast, [this, paintArgs]);

        // Exercise load animation event
        var onLoadMethod = typeof(ToastForm).GetMethod(
            "OnLoad",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(object), typeof(EventArgs)],
            null);

        onLoadMethod.Should().NotBeNull();
        onLoadMethod!.Invoke(toast, [this, EventArgs.Empty]);
    }

    [WpfFact]
    public void HiddenForm_CreateParams_IncludesToolWindowExStyle()
    {
        using var form = new Program.HiddenForm();
        form.Should().NotBeNull();

        var cpProperty = typeof(Program.HiddenForm).GetProperty("CreateParams", BindingFlags.NonPublic | BindingFlags.Instance);
        cpProperty.Should().NotBeNull();

        var cp = (CreateParams)cpProperty!.GetValue(form)!;
        (cp.ExStyle & 0x00000080).Should().Be(0x00000080); // WS_EX_TOOLWINDOW
    }

    [Fact]
    public async Task ProcessMessageStreamAsync_ProcessesShowToastAction()
    {
        string input = JsonSerializer.Serialize(new { Action = "ShowToast", Title = "CPU Warning", Body = "Usage at 98%" });
        using var reader = new StringReader(input);
        using var cts = new CancellationTokenSource();

        string? receivedTitle = null;
        string? receivedBody = null;

        await Program.ProcessMessageStreamAsync(reader, cts, (title, body) =>
        {
            receivedTitle = title;
            receivedBody = body;
        });

        receivedTitle.Should().Be("CPU Warning");
        receivedBody.Should().Be("Usage at 98%");
        cts.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessMessageStreamAsync_ProcessesShutdownAction()
    {
        string input = JsonSerializer.Serialize(new { Action = "Shutdown" });
        using var reader = new StringReader(input);
        using var cts = new CancellationTokenSource();

        bool toastFired = false;
        await Program.ProcessMessageStreamAsync(reader, cts, (_, _) => toastFired = true);

        toastFired.Should().BeFalse();
        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessMessageStreamAsync_HandlesEmptyLinesAndUnknownActions()
    {
        string input = string.Join("\n", [
            "   ",
            "null",
            JsonSerializer.Serialize(new { Action = "UnknownAction" }),
            "invalid_json"
        ]);

        using var reader = new StringReader(input);
        using var cts = new CancellationTokenSource();

        bool toastFired = false;
        await Program.ProcessMessageStreamAsync(reader, cts, (_, _) => toastFired = true);

        toastFired.Should().BeFalse();
        cts.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void Program_ShowToast_ExecutesSafely()
    {
        Program.ShowToast("Test Title", "Test Body");
    }
}
