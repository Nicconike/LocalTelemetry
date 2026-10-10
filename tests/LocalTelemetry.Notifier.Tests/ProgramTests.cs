using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace LocalTelemetry.Notifier.Tests;

public class ProgramTests
{
    private static readonly string[] ParentPidArgs = [Environment.ProcessId.ToString()];

    public ProgramTests()
    {
        Console.SetError(TextWriter.Null);
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
    public async Task ProcessMessageStreamAsync_ShowToastWithNullFields_UsesDefaultFallbackValues()
    {
        string input = JsonSerializer.Serialize(new Program.NotificationMessage { Action = "ShowToast", Title = null, Body = null });
        using var reader = new StringReader(input);
        using var cts = new CancellationTokenSource();

        string? receivedTitle = null;
        string? receivedBody = null;

        await Program.ProcessMessageStreamAsync(reader, cts, (title, body) =>
        {
            receivedTitle = title;
            receivedBody = body;
        });

        receivedTitle.Should().Be("LocalTelemetry");
        receivedBody.Should().Be(string.Empty);
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

    [WpfFact]
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

    [WpfFact]
    public void HiddenForm_InitializationAndCreateParams()
    {
        using var form = new Program.HiddenForm();
        form.Should().NotBeNull();
        form.WindowState.Should().Be(FormWindowState.Minimized);
        form.ShowInTaskbar.Should().BeFalse();
        form.FormBorderStyle.Should().Be(FormBorderStyle.None);
        form.Opacity.Should().Be(0);

        var cpProperty = typeof(Program.HiddenForm).GetProperty("CreateParams", BindingFlags.NonPublic | BindingFlags.Instance);
        cpProperty.Should().NotBeNull();

        var cp = (CreateParams)cpProperty!.GetValue(form)!;
        (cp.ExStyle & 0x00000080).Should().Be(0x00000080); // WS_EX_TOOLWINDOW
    }

    [Fact]
    public void NotificationMessage_PropertiesSetAndGet()
    {
        var msg = new Program.NotificationMessage
        {
            Action = "ShowToast",
            Title = "Title",
            Body = "Body"
        };

        msg.Action.Should().Be("ShowToast");
        msg.Title.Should().Be("Title");
        msg.Body.Should().Be("Body");
    }

    [Fact]
    public void NamedPipeServerStreamWrapper_ExercisesAllStreamMembers()
    {
        var wrapperType = typeof(Program).GetNestedType("NamedPipeServerStreamWrapper", BindingFlags.NonPublic);
        wrapperType.Should().NotBeNull();

        using var pipe = new System.IO.Pipes.NamedPipeServerStream("TestPipeNotifierWrapper", PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
        var wrapper = (Program.INotifierPipeServer)Activator.CreateInstance(wrapperType!, [pipe])!;

        wrapper.Stream.Should().NotBeNull();
        wrapper.Dispose();
    }

    [Fact]
    public void CreatePipeServer_FallbackToDefaultStream()
    {
        Func<Program.INotifierPipeServer>? originalFactory = Program.PipeServerFactory;
        string originalPipeName = Program.PipeName;
        try
        {
            Program.PipeServerFactory = null;
            Program.PipeName = "LocalTelemetryTestPipe_" + Guid.NewGuid().ToString("N");

            var createMethod = typeof(Program).GetMethod("CreatePipeServer", BindingFlags.NonPublic | BindingFlags.Static);
            createMethod.Should().NotBeNull();

            using var server = (Program.INotifierPipeServer)createMethod!.Invoke(null, null)!;
            server.Should().NotBeNull();
            server.Stream.Should().NotBeNull();
        }
        finally
        {
            Program.PipeServerFactory = originalFactory;
            Program.PipeName = originalPipeName;
        }
    }

    [Fact]
    public void Program_ResolveLogPath_ReturnsValidPath()
    {
        var resolveMethod = typeof(Program).GetMethod("ResolveLogPath", BindingFlags.NonPublic | BindingFlags.Static);
        resolveMethod.Should().NotBeNull();
        string logPath = (string)resolveMethod!.Invoke(null, null)!;
        logPath.Should().NotBeNullOrEmpty();
        logPath.Should().EndWith("lt_notifier.log");
    }

    [WpfFact]
    public void Program_Main_ExecutesFullLifecycleInStandaloneMode()
    {
        Func<Program.INotifierPipeServer>? originalFactory = Program.PipeServerFactory;
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true })
        {
            writer.Write(JsonSerializer.Serialize(new { Action = "Shutdown" }));
        }
        stream.Position = 0;

        var mockServer = Substitute.For<Program.INotifierPipeServer>();
        mockServer.Stream.Returns(stream);
        mockServer.WaitForConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        try
        {
            Program.PipeServerFactory = () => mockServer;
            var mainMethod = typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
            mainMethod.Should().NotBeNull();

            var mainThread = new Thread(() => mainMethod!.Invoke(null, [Array.Empty<string>()]));
            mainThread.SetApartmentState(ApartmentState.STA);
            mainThread.Start();

            bool finished = mainThread.Join(2000);
            finished.Should().BeTrue();
        }
        finally
        {
            Program.PipeServerFactory = originalFactory;
        }
    }

    [WpfFact]
    public void Program_Main_ExecutesFullLifecycleWithParentPid()
    {
        Func<Program.INotifierPipeServer>? originalFactory = Program.PipeServerFactory;
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true })
        {
            writer.Write(JsonSerializer.Serialize(new { Action = "Shutdown" }));
        }
        stream.Position = 0;

        var mockServer = Substitute.For<Program.INotifierPipeServer>();
        mockServer.Stream.Returns(stream);
        mockServer.WaitForConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        try
        {
            Program.PipeServerFactory = () => mockServer;
            var mainMethod = typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
            mainMethod.Should().NotBeNull();

            var mainThread = new Thread(() => mainMethod!.Invoke(null, [ParentPidArgs]));
            mainThread.SetApartmentState(ApartmentState.STA);
            mainThread.Start();

            bool finished = mainThread.Join(2000);
            finished.Should().BeTrue();
        }
        finally
        {
            Program.PipeServerFactory = originalFactory;
        }
    }

    [Fact]
    public void Program_ResolveLogPath_HandlesStandardAndPortableModes()
    {
        var resolveLogPath = typeof(Program).GetMethod("ResolveLogPath", BindingFlags.NonPublic | BindingFlags.Static);
        resolveLogPath.Should().NotBeNull();

        string markerFile = Path.Combine(AppContext.BaseDirectory, "app.mode");
        bool createdMarker = false;
        try
        {
            if (!File.Exists(markerFile))
            {
                File.WriteAllText(markerFile, "standard");
                createdMarker = true;
            }

            var pathStandard = (string)resolveLogPath!.Invoke(null, null)!;
            pathStandard.Should().EndWith("lt_notifier.log");
        }
        finally
        {
            if (createdMarker && File.Exists(markerFile))
            {
                File.Delete(markerFile);
            }
        }

        var pathPortable = (string)resolveLogPath!.Invoke(null, null)!;
        pathPortable.Should().EndWith("lt_notifier.log");
    }

    [WpfFact]
    public void Program_SetupShutdownListener_WhenHandleNotCreated_HooksLoad()
    {
        var setupListener = typeof(Program).GetMethod("SetupShutdownListener", BindingFlags.NonPublic | BindingFlags.Static);
        setupListener.Should().NotBeNull();

        var hiddenForm = new Program.HiddenForm();
        using var cts = new CancellationTokenSource();

        // Signal shutdown before handle is created
        cts.Cancel();
        setupListener!.Invoke(null, [hiddenForm, cts]);

        // Simulating form load trigger
        var onLoad = typeof(Form).GetMethod("OnLoad", BindingFlags.NonPublic | BindingFlags.Instance);
        onLoad?.Invoke(hiddenForm, [EventArgs.Empty]);

        hiddenForm.Dispose();
    }

    [Fact]
    public async Task ProcessMessageStreamAsync_HandlesUnknownActionAndInvalidJson()
    {
        using var cts = new CancellationTokenSource();

        // 1. Unknown action
        string unknownInput = JsonSerializer.Serialize(new { Action = "UnknownAction123" });
        using (var reader = new StringReader(unknownInput))
        {
            await Program.ProcessMessageStreamAsync(reader, cts, (_, _) => { });
        }

        // 2. Invalid JSON string
        using (var reader = new StringReader("THIS IS NOT VALID JSON"))
        {
            await Program.ProcessMessageStreamAsync(reader, cts, (_, _) => { });
        }
    }

    [WpfFact]
    public void Program_ShowToast_CatchesExceptionsGracefully()
    {
        Func<string, string, ToastForm> original = Program.ToastFactory;
        try
        {
            Program.ToastFactory = (_, _) => throw new InvalidOperationException("Simulated toast failure");
            var act = () => Program.ShowToast("Test Title", "Test Body");
            act.Should().NotThrow();
        }
        finally
        {
            Program.ToastFactory = original;
        }
    }

    [Fact]
    public void NotifierLog_DirectoryCreation_And_InvalidPath_Coverage()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "NotifierLogTest_" + Guid.NewGuid().ToString("N"));
        string nestedLog = Path.Combine(tempDir, "subDir", "lt_notifier.log");
        try
        {
            NotifierLog.Init(nestedLog);
            NotifierLog.LastError.Should().BeNull();
            NotifierLog.Info("Test message in new dir");
            NotifierLog.Shutdown();
            File.Exists(nestedLog).Should().BeTrue();

            NotifierLog.Init("Z:\\InvalidDirName:\\test.log");
            NotifierLog.LastError.Should().NotBeNull();
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }
}
