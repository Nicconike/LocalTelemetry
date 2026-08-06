using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
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

    [Fact]
    public void NotifierLog_ModuleName_ResolvesNotifierRelativePath()
    {
        NotifierLog.ModuleName(null).Should().Be("?");
        NotifierLog.ModuleName(@"C:\repo\src\LocalTelemetry.Notifier\Program.cs").Should().Be("Notifier.Program");
        NotifierLog.ModuleName(@"C:\repo\src\SomethingElse\OtherFile.cs").Should().Be("OtherFile");
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

    [Fact]
    public async Task RunPipeServerAsync_DispatchesToastAndStopsOnShutdown()
    {
        string payload = string.Join("\n", [
            JsonSerializer.Serialize(new { Action = "ShowToast", Title = "CPU Warning", Body = "Usage at 98%" }),
            JsonSerializer.Serialize(new { Action = "Shutdown" })
        ]);

        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true })
        {
            writer.Write(payload);
        }
        stream.Position = 0;

        var fake = Substitute.For<Program.INotifierPipeServer>();
        fake.Stream.Returns(stream);
        fake.WaitForConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        Func<Program.INotifierPipeServer>? originalFactory = Program.PipeServerFactory;
        try
        {
            Program.PipeServerFactory = () => fake;
            using var cts = new CancellationTokenSource();

            string? receivedTitle = null;
            string? receivedBody = null;

            await Program.RunPipeServerAsync(cts, (title, body) =>
            {
                receivedTitle = title;
                receivedBody = body;
            });

            receivedTitle.Should().Be("CPU Warning");
            receivedBody.Should().Be("Usage at 98%");
            cts.IsCancellationRequested.Should().BeTrue();
        }
        finally
        {
            Program.PipeServerFactory = originalFactory;
        }
    }

    [Fact]
    public async Task RunPipeServerAsync_ReconnectsAfterConnectionError()
    {
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true })
        {
            writer.Write(JsonSerializer.Serialize(new { Action = "Shutdown" }));
        }
        stream.Position = 0;

        var throwing = Substitute.For<Program.INotifierPipeServer>();
        throwing.WaitForConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("pipe unavailable")));

        var healthy = Substitute.For<Program.INotifierPipeServer>();
        healthy.Stream.Returns(stream);
        healthy.WaitForConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        Func<Program.INotifierPipeServer>? originalFactory = Program.PipeServerFactory;
        int originalDelay = Program.ReconnectDelayMs;
        try
        {
            Program.ReconnectDelayMs = 1;
            int callCount = 0;
            Program.PipeServerFactory = () => ++callCount == 1 ? throwing : healthy;

            using var cts = new CancellationTokenSource();

            await Program.RunPipeServerAsync(cts, (_, _) => { });

            cts.IsCancellationRequested.Should().BeTrue();
            callCount.Should().Be(2);
        }
        finally
        {
            Program.PipeServerFactory = originalFactory;
            Program.ReconnectDelayMs = originalDelay;
        }
    }

    [Fact]
    public async Task MonitorParentProcessAsync_CancelsCtsAfterParentExit()
    {
        using var cts = new CancellationTokenSource();
        bool waitInvoked = false;

        await Program.MonitorParentProcessAsync(() =>
        {
            waitInvoked = true;
            return Task.CompletedTask;
        }, cts);

        waitInvoked.Should().BeTrue();
        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task MonitorParentProcessAsync_CancelsCtsWhenParentNotFound()
    {
        using var cts = new CancellationTokenSource();

        await Program.MonitorParentProcessAsync(
            () => Task.FromException(new ArgumentException("No process with given ID")), cts);

        cts.IsCancellationRequested.Should().BeTrue();
    }

    [StaFact]
    public void Program_ShowToast_UsesToastFactory()
    {
        Func<string, string, ToastForm> original = Program.ToastFactory;
        ToastForm? created = null;
        try
        {
            Program.ToastFactory = (title, body) =>
            {
                created = new ToastForm(title, body);
                return created;
            };

            Program.ShowToast("CPU High", "Usage at 95%");

            created.Should().NotBeNull();
        }
        finally
        {
            Program.ToastFactory = original;
            created?.Dispose();
        }
    }
}
