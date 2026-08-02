using System.Runtime.CompilerServices;

namespace LocalTelemetry.Core.Diagnostics;

/// <summary>
/// Static logger with two output streams: <c>Info/Warn/Error</c> writes to the
/// system log (<c>lt_system.log</c>) - always on, for developer use. <c>InfoMetric/ErrorMetric</c>
/// writes to the per-date metrics log (<c>lt_DD-MM-YYYY.log</c>) - gated by the caller's
/// per-category <c>ShouldLogTick</c>. Both files are opened at <c>Init</c> and never write to
/// <c>Console.Error</c>.
/// </summary>
public static class Log
{
    private static readonly object _lock = new();
    private static StreamWriter? _sysWriter;
    private static StreamWriter? _metricWriter;
    private static string? _metricsPath;
    private static bool _metricsEnabled;

    /// <summary>Opens both log files for appending. System log is always active.</summary>
    public static void Init(string systemLogPath, string metricsLogPath, bool enableMetrics = true)
    {
        lock (_lock)
        {
            CloseWriters();
            try { _sysWriter = new StreamWriter(systemLogPath, append: true) { AutoFlush = true }; }
            catch { _sysWriter = null; }
            _metricsPath = metricsLogPath;
            _metricsEnabled = enableMetrics;
        }
    }

    private static void OpenMetricsWriter()
    {
        try { _metricWriter = new StreamWriter(_metricsPath!, append: true) { AutoFlush = true }; }
        catch { _metricWriter = null; }
    }

    /// <summary>Enables or disables writing to the metrics log file.</summary>
    public static void EnableMetrics(bool enabled)
    {
        lock (_lock)
        {
            if (enabled == _metricsEnabled) return;
            _metricsEnabled = enabled;
            if (!enabled && _metricWriter is not null)
            {
                _metricWriter.Flush();
                _metricWriter.Dispose();
                _metricWriter = null;
            }
        }
    }

    /// <summary>Flushes and closes both writers.</summary>
    public static void Shutdown()
    {
        lock (_lock)
        {
            CloseWriters();
        }
    }

    private static void CloseWriters()
    {
        if (_sysWriter is not null) { try { _sysWriter.Flush(); _sysWriter.Dispose(); } catch { } _sysWriter = null; }
        if (_metricWriter is not null) { try { _metricWriter.Flush(); _metricWriter.Dispose(); } catch { } _metricWriter = null; }
    }

    private static string ModuleName(string? callerPath)
    {
        if (callerPath is null) return "?";
        int idx = callerPath.LastIndexOf("LocalTelemetry", StringComparison.Ordinal);
        if (idx < 0)
            idx = callerPath.LastIndexOf("Notifier", StringComparison.Ordinal);
        if (idx < 0)
            return Path.GetFileNameWithoutExtension(callerPath);
        string relative = callerPath[(idx + "LocalTelemetry.".Length)..];
        relative = relative.Replace('\\', '.').Replace('/', '.');
        return relative.EndsWith(".cs", StringComparison.Ordinal)
            ? relative[..^3]
            : relative;
    }

    /// <summary>Defines the logging severity levels.</summary>
    public enum Level
    {
        Off = 0,
        Error = 1,
        Warning = 2,
        Info = 3
    }

    /// <summary>Active logging level filter for system logs.</summary>
    public static Level SystemLevel { get; set; } = Level.Info;

    /// <summary>Active logging level filter for metric logs.</summary>
    public static Level MetricsLevel { get; set; } = Level.Error;

    private static void Write(StreamWriter? writer, Level level, string message, string? callerPath, Exception? ex)
    {
        if (writer is null) return;

        // Check if we are writing to the metrics log or system log and filter based on respective levels
        bool isMetrics = ReferenceEquals(writer, _metricWriter);
        Level activeFilter = isMetrics ? MetricsLevel : SystemLevel;

        if (level > activeFilter || activeFilter == Level.Off) return;

        string levelStr = level switch
        {
            Level.Error => "ERR",
            Level.Warning => "WARN",
            _ => "INFO"
        };

        string module = ModuleName(callerPath);
        string timestamp = DateTime.Now.ToString("dd-MM-yyyy hh:mm:ss.fff tt");
        string line = ex is null
            ? $"{timestamp} [{levelStr}] {module}: {message}"
            : $"{timestamp} [{levelStr}] {module}: {message} | {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        lock (_lock)
        {
            try { writer.WriteLine(line); } catch { }
        }
    }

    // System log (always Full, for developer use)
    public static void Info(string message, [CallerFilePath] string? callerPath = null)
    {
        Write(_sysWriter, Level.Info, message, callerPath, null);
    }

    public static void Warn(string message, [CallerFilePath] string? callerPath = null)
    {
        Write(_sysWriter, Level.Warning, message, callerPath, null);
    }

    public static void Warn(Exception ex, string message, [CallerFilePath] string? callerPath = null)
    {
        Write(_sysWriter, Level.Warning, message, callerPath, ex);
    }

    public static void Error(string message, [CallerFilePath] string? callerPath = null)
    {
        Write(_sysWriter, Level.Error, message, callerPath, null);
    }

    public static void Error(Exception ex, string message, [CallerFilePath] string? callerPath = null)
    {
        Write(_sysWriter, Level.Error, message, callerPath, ex);
    }

    // Metrics log (gated by caller's ShouldLogTick, user-configurable)
    // Callers MUST check per-category log mode before calling these methods.
    private static void EnsureMetricWriter()
    {
        if (_metricWriter is not null) return;
        lock (_lock)
        {
            if (_metricWriter is null && _metricsEnabled)
                OpenMetricsWriter();
        }
    }

    public static void InfoMetric(string message, [CallerFilePath] string? callerPath = null)
    {
        EnsureMetricWriter();
        Write(_metricWriter, Level.Info, message, callerPath, null);
    }

    public static void ErrorMetric(string message, [CallerFilePath] string? callerPath = null)
    {
        EnsureMetricWriter();
        Write(_metricWriter, Level.Error, message, callerPath, null);
    }

    public static void ErrorMetric(Exception ex, string message, [CallerFilePath] string? callerPath = null)
    {
        EnsureMetricWriter();
        Write(_metricWriter, Level.Error, message, callerPath, ex);
    }
}
