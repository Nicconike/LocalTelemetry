using System.IO;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace LocalTelemetry.Notifier.Tests;

public class NotifierLogTests
{
    public NotifierLogTests()
    {
        Console.SetError(TextWriter.Null);
    }

    [Fact]
    public void ModuleName_ResolvesCallerPathsCorrectly()
    {
        NotifierLog.ModuleName(null).Should().Be("?");
        NotifierLog.ModuleName(@"C:\repo\src\LocalTelemetry.Notifier\Program.cs").Should().Be("Notifier.Program");
        NotifierLog.ModuleName(@"C:\repo\src\LocalTelemetry.Notifier\ToastForm").Should().Be("Notifier.ToastForm");
        NotifierLog.ModuleName(@"C:/repo/src/LocalTelemetry.Notifier/Program.cs").Should().Be("Notifier.Program");
        NotifierLog.ModuleName(@"C:\repo\src\SomethingElse\OtherFile.cs").Should().Be("OtherFile");
        NotifierLog.ModuleName(@"C:\repo\src\SomethingElse\OtherFile").Should().Be("OtherFile");
    }

    [Fact]
    public void NotifierLog_Info_LogsWithoutExceptions()
    {
        var act = () => NotifierLog.Info("Test info message");
        act.Should().NotThrow();
    }

    [Fact]
    public void NotifierLog_InitAndFileLogging_WritesToFileAndShutsDown()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"lt_notifier_test_{Guid.NewGuid():N}.log");
        try
        {
            NotifierLog.Init(tempPath);

            NotifierLog.Info("File logging info");
            NotifierLog.Warn("File logging warn");
            NotifierLog.Warn(new InvalidOperationException("Warn ex"), "File logging warn ex");
            NotifierLog.Error("File logging error");
            NotifierLog.Error(new InvalidOperationException("Error ex"), "File logging error ex");

            NotifierLog.Shutdown();

            File.Exists(tempPath).Should().BeTrue();
            string content = File.ReadAllText(tempPath);
            content.Should().Contain("File logging info");
            content.Should().Contain("File logging warn");
            content.Should().Contain("File logging error");
        }
        finally
        {
            NotifierLog.Shutdown();
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void NotifierLog_Shutdown_WhenWriterIsNull_DoesNotThrow()
    {
        NotifierLog.Shutdown();
        var act = () => NotifierLog.Shutdown();
        act.Should().NotThrow();
    }

    [Fact]
    public void NotifierLog_Shutdown_WhenWriterFlushThrows_CatchesGracefully()
    {
        var field = typeof(NotifierLog).GetField("_writer", BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();

        var throwingStream = new ThrowingTestStream();
        var sw = new StreamWriter(throwingStream);
        field!.SetValue(null, sw);

        var act = () => NotifierLog.Shutdown();
        act.Should().NotThrow();
        field.GetValue(null).Should().BeNull();
    }

    private sealed class ThrowingTestStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() => throw new IOException("Flush failure");
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Write failure");
    }

    [Fact]
    public void NotifierLog_FormatsMessagesAndExceptionsWithNewlinesCleanly()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"lt_notifier_test_{Guid.NewGuid():N}.log");
        try
        {
            NotifierLog.Init(tempPath);
            NotifierLog.Info("Info with newline\r\n");
            NotifierLog.Warn("Warn with newline\r\n");
            NotifierLog.Warn(new InvalidOperationException("Warn exception with newline\r\n"), "Warn msg\r\n");
            NotifierLog.Error("Error with newline\r\n");
            NotifierLog.Error(new InvalidOperationException("Error exception with newline\r\n"), "Error msg\r\n");
            NotifierLog.Shutdown();

            File.Exists(tempPath).Should().BeTrue();
            string content = File.ReadAllText(tempPath);
            content.Should().Contain("Info with newline");
            content.Should().Contain("Warn with newline");
            content.Should().Contain("Error with newline");
        }
        finally
        {
            NotifierLog.Shutdown();
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private sealed class ThrowOnWriteOnlyStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Write failure");
    }

    [Fact]
    public void NotifierLog_Write_WhenWriteLineThrows_CapturesLastError()
    {
        var field = typeof(NotifierLog).GetField("_writer", BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();

        var throwingStream = new ThrowOnWriteOnlyStream();
        var sw = new StreamWriter(throwingStream) { AutoFlush = true };
        field!.SetValue(null, sw);

        try
        {
            NotifierLog.Info("Should trigger write error");
            NotifierLog.LastError.Should().NotBeNull();
            NotifierLog.LastError.Should().BeOfType<IOException>();
        }
        finally
        {
            NotifierLog.Shutdown();
        }
    }
}

