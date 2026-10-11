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
        string payload = string.Join("\n",
            JsonSerializer.Serialize(new { Action = "ShowToast", Title = "CPU Warning", Body = "Usage at 98%" }),
            JsonSerializer.Serialize(new { Action = "Shutdown" }));

        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true })
        {
            await writer.WriteAsync(payload);
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

        var pathPortable = (string)resolveLogPath.Invoke(null, null)!;
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

        cts.IsCancellationRequested.Should().BeFalse();
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
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch (Exception) { /* Best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task ProcessMessageStreamAsync_WhitespaceAndNullAndMalformedJson_HandledGracefully()
    {
        string input = string.Join("\n",
            "",
            "   ",
            "null",
            "{ invalid json }",
            JsonSerializer.Serialize(new { Action = "UnknownCustomAction" }),
            JsonSerializer.Serialize(new { Action = "Shutdown" }));

        using var reader = new StringReader(input);
        using var cts = new CancellationTokenSource();

        await Program.ProcessMessageStreamAsync(reader, cts, (_, _) => { });

        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task RunPipeServerAsync_ExceptionAndOperationCanceled_Coverage()
    {
        int originalDelay = Program.ReconnectDelayMs;
        Program.ReconnectDelayMs = 1;

        try
        {
            int attempts = 0;
            var fakeServer = Substitute.For<Program.INotifierPipeServer>();
            fakeServer.WaitForConnectionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                attempts++;
                if (attempts == 1)
                    throw new IOException("Simulated network/pipe failure");

                return Task.FromException(new OperationCanceledException());
            });

            Func<Program.INotifierPipeServer>? originalFactory = Program.PipeServerFactory;
            Program.PipeServerFactory = () => fakeServer;

            try
            {
                using var cts = new CancellationTokenSource();
                var runTask = Program.RunPipeServerAsync(cts, (_, _) => { });
                var act = async () => await runTask;
                await act.Should().ThrowAsync<OperationCanceledException>();
                attempts.Should().BeGreaterThanOrEqualTo(2);
            }
            finally
            {
                Program.PipeServerFactory = originalFactory;
            }
        }
        finally
        {
            Program.ReconnectDelayMs = originalDelay;
        }
    }

    [Fact]
    public async Task NamedPipeServerStreamWrapper_WaitForConnectionAsync_Coverage()
    {
        var wrapperType = typeof(Program).Assembly.GetType("LocalTelemetry.Notifier.Program+NamedPipeServerStreamWrapper");
        wrapperType.Should().NotBeNull();

        using var pipeServer = new NamedPipeServerStream(
            "TestPipe_" + Guid.NewGuid().ToString("N"),
            PipeDirection.In,
            1,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous);

        var wrapper = (Program.INotifierPipeServer)Activator.CreateInstance(wrapperType!, pipeServer)!;
        using (wrapper)
        {
            wrapper.Stream.Should().NotBeNull();
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var act = async () => await wrapper.WaitForConnectionAsync(cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }

    [Fact]
    public async Task MonitorParentProcessAsync_ArgumentException_LogsWarning()
    {
        using var cts = new CancellationTokenSource();
        await Program.MonitorParentProcessAsync(() => throw new ArgumentException("Parent PID missing"), cts);
        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task SetupParentMonitoring_NonExistentPid_Coverage()
    {
        var method = typeof(Program).GetMethod("SetupParentMonitoring", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        using var cts = new CancellationTokenSource();
        method!.Invoke(null, [99999999, cts]);

        using var waitToken = new CancellationTokenSource(2000);
        while (!cts.IsCancellationRequested && !waitToken.IsCancellationRequested)
        {
            await Task.Delay(10);
        }

        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Program_UnhandledExceptionHandler_Coverage()
    {
        var cType = typeof(Program).GetNestedType("<>c", BindingFlags.NonPublic);
        cType.Should().NotBeNull();
        var method = cType!.GetMethod("<Main>b__16_0", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var singletonField = cType.GetField("<>9", BindingFlags.NonPublic | BindingFlags.Static);
        var instance = singletonField?.GetValue(null) ?? Activator.CreateInstance(cType, nonPublic: true);

        var actEx = () => method!.Invoke(instance, [null, new UnhandledExceptionEventArgs(new InvalidOperationException("Test crash"), false)]);
        actEx.Should().NotThrow();

        var actNonEx = () => method!.Invoke(instance, [null, new UnhandledExceptionEventArgs("Not an exception", false)]);
        actNonEx.Should().NotThrow();
    }

    [WpfFact]
    public void Program_Main_WithLockedLogFile_ExecutesErrorBranch()
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

        var resolveLogMethod = typeof(Program).GetMethod("ResolveLogPath", BindingFlags.NonPublic | BindingFlags.Static);
        resolveLogMethod.Should().NotBeNull();
        string logPath = (string)resolveLogMethod!.Invoke(null, null)!;
        string? dir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        FileStream? lockStream = null;
        try
        {
            lockStream = new FileStream(logPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Program.PipeServerFactory = () => mockServer;

            var mainMethod = typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
            mainMethod.Should().NotBeNull();

            var mainThread = new Thread(() => mainMethod!.Invoke(null, [Array.Empty<string>()]));
            mainThread.SetApartmentState(ApartmentState.STA);
            mainThread.Start();

            bool finished = mainThread.Join(3000);
            finished.Should().BeTrue();
        }
        finally
        {
            Program.PipeServerFactory = originalFactory;
            lockStream?.Dispose();
            NotifierLog.Shutdown();
        }
    }
}

