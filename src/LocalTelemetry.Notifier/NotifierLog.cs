using System.IO;
using System.Runtime.CompilerServices;

namespace LocalTelemetry.Notifier;

/// <summary>
/// Static logger for the Notifier process.
/// Output format matches <c>Log</c> in Core for consistent log parsing.
/// Writes to <c>lt_notifier.log</c> after <see cref="Init"/>.
/// </summary>
internal static class NotifierLog
{
    private static readonly object _lock = new();
    private static StreamWriter? _writer;

    /// <summary>Captures the last error encountered during logging operations.</summary>
    internal static Exception? LastError { get; private set; }

    /// <summary>Opens the notifier log file for appending.</summary>
    internal static void Init(string logPath)
    {
        lock (_lock)
        {
            try
            {
                string? dir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                _writer = new StreamWriter(logPath, append: true) { AutoFlush = true };
                LastError = null;
            }
            catch (Exception ex)
            {
                _writer = null;
                LastError = ex;
            }
        }
    }

    /// <summary>Flushes and closes the log writer.</summary>
    internal static void Shutdown()
    {
        lock (_lock)
        {
            if (_writer is not null)
            {
                try
                {
                    _writer.Flush();
                    _writer.Dispose();
                }
                catch (Exception ex)
                {
                    LastError = ex;
                }
                finally
                {
                    _writer = null;
                }
            }
        }
    }

    internal static string ModuleName(string? callerPath)
    {
        if (callerPath is null) return "?";
        int idx = callerPath.LastIndexOf("Notifier", StringComparison.Ordinal);
        if (idx < 0)
            return Path.GetFileNameWithoutExtension(callerPath);
        string relative = callerPath[idx..];
        relative = relative.Replace('\\', '.').Replace('/', '.');
        if (relative.EndsWith(".cs", StringComparison.Ordinal))
            relative = relative[..^3];
        return relative;
    }

    private static void Write(string levelStr, string message, string? callerPath, Exception? ex)
    {
        string module = ModuleName(callerPath);
        string timestamp = DateTime.Now.ToString("dd-MM-yyyy hh:mm:ss.fff tt");
        string cleanMessage = message.TrimEnd('\r', '\n');
        string line = ex is null
            ? $"{timestamp} [{levelStr}] {module}: {cleanMessage}"
            : $"{timestamp} [{levelStr}] {module}: {cleanMessage} | {ex.GetType().Name}: {ex.Message.TrimEnd('\r', '\n')}\n{ex.StackTrace}";
        lock (_lock)
        {
            if (_writer is null) return;
            try
            {
                _writer.WriteLine(line);
            }
            catch (Exception writeEx)
            {
                LastError = writeEx;
            }
        }
    }

    internal static void Debug(string message, [CallerFilePath] string? callerPath = null)
    {
        Write("DEBUG", message, callerPath, null);
    }

    internal static void Info(string message, [CallerFilePath] string? callerPath = null)
    {
        Write("INFO", message, callerPath, null);
    }

    internal static void Warn(string message, [CallerFilePath] string? callerPath = null)
    {
        Write("WARNING", message, callerPath, null);
    }

    internal static void Warn(Exception ex, string message, [CallerFilePath] string? callerPath = null)
    {
        Write("WARNING", message, callerPath, ex);
    }

    internal static void Error(string message, [CallerFilePath] string? callerPath = null)
    {
        Write("ERROR", message, callerPath, null);
    }

    internal static void Error(Exception ex, string message, [CallerFilePath] string? callerPath = null)
    {
        Write("ERROR", message, callerPath, ex);
    }
}
