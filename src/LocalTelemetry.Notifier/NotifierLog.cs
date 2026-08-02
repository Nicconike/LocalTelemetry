using System.IO;
using System.Runtime.CompilerServices;

namespace LocalTelemetry.Notifier;

/// <summary>
/// Static logger for the Notifier process.
/// Output format matches <c>Log</c> in Core for consistent log parsing.
/// </summary>
internal static class NotifierLog
{
    private static string ModuleName(string? callerPath)
    {
        if (callerPath is null) return "?";
        int idx = callerPath.LastIndexOf("Notifier", StringComparison.Ordinal);
        if (idx < 0)
            return Path.GetFileNameWithoutExtension(callerPath);
        string relative = callerPath[idx..];
        relative = relative.Replace('\\', '.').Replace('/', '.');
        return relative.EndsWith(".cs", StringComparison.Ordinal)
            ? relative[..^3]
            : relative;
    }

    internal static void Info(string message, [CallerFilePath] string? callerPath = null)
    {
        string module = ModuleName(callerPath);
        string timestamp = DateTime.Now.ToString("dd-MM-yyyy hh:mm:ss.fff tt");
        Console.Error.WriteLine($"{timestamp} [INFO] {module}: {message}");
    }

    internal static void Warn(string message, [CallerFilePath] string? callerPath = null)
    {
        string module = ModuleName(callerPath);
        string timestamp = DateTime.Now.ToString("dd-MM-yyyy hh:mm:ss.fff tt");
        Console.Error.WriteLine($"{timestamp} [WARN] {module}: {message}");
    }

    internal static void Warn(Exception ex, string message, [CallerFilePath] string? callerPath = null)
    {
        string module = ModuleName(callerPath);
        string timestamp = DateTime.Now.ToString("dd-MM-yyyy hh:mm:ss.fff tt");
        Console.Error.WriteLine($"{timestamp} [WARN] {module}: {message} | {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    }

    internal static void Error(string message, [CallerFilePath] string? callerPath = null)
    {
        string module = ModuleName(callerPath);
        string timestamp = DateTime.Now.ToString("dd-MM-yyyy hh:mm:ss.fff tt");
        Console.Error.WriteLine($"{timestamp} [ERR] {module}: {message}");
    }

    internal static void Error(Exception ex, string message, [CallerFilePath] string? callerPath = null)
    {
        string module = ModuleName(callerPath);
        string timestamp = DateTime.Now.ToString("dd-MM-yyyy hh:mm:ss.fff tt");
        Console.Error.WriteLine($"{timestamp} [ERR] {module}: {message} | {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    }
}
