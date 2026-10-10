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
    private static string? _systemLogPath;
    private static string? _metricsPath;
    private static string? _logsDir;
    private static bool _metricsEnabled;
    private static DateOnly _currentDate = DateOnly.FromDateTime(DateTime.Today);
    private static bool _sessionEnded;

    /// <summary>Records the last error encountered during logger initialization, if any.</summary>
    public static Exception? LastInitError { get; internal set; }

    /// <summary>Records the last error encountered when writing to a log file, if any.</summary>
    public static Exception? LastWriteError { get; internal set; }

    /// <summary>Records the last error encountered during log rotation or rollover, if any.</summary>
    public static Exception? LastRotationError { get; internal set; }

    /// <summary>Records the last error encountered during metrics log operations, if any.</summary>
    public static Exception? LastMetricError { get; internal set; }

    /// <summary>Maximum size of the system log before it is rotated (default 5MB).</summary>
    internal static long MaxSystemLogBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Maximum number of archived rolling files to retain (default 3: .1.log, .2.log, .3.log).</summary>
    internal static int MaxRollingFiles { get; set; } = 3;

    /// <summary>Opens both log files for appending. System log is always active.</summary>
    public static void Init(string systemLogPath, string metricsLogPath, bool enableMetrics = true)
    {
        lock (_lock)
        {
            CloseWriters();
            _systemLogPath = systemLogPath;
            _metricsPath = metricsLogPath;
            _logsDir = Path.GetDirectoryName(systemLogPath);
            _metricsEnabled = enableMetrics;
            _currentDate = DateOnly.FromDateTime(DateTime.Today);
            _sessionEnded = false;

            RotateSystemLog(systemLogPath);
            try { _sysWriter = new StreamWriter(systemLogPath, append: true) { AutoFlush = true }; }
            catch (Exception ex)
            {
                LastInitError = ex;
                System.Diagnostics.Debug.WriteLine($"[Log.Init] Failed to open system log '{systemLogPath}': {ex}");
                _sysWriter = null;
            }
        }
    }

    private static void RotateSystemLog(string systemLogPath)
    {
        try
        {
            if (MaxSystemLogBytes <= 0) return;
            var fi = new FileInfo(systemLogPath);
            if (!fi.Exists || fi.Length < MaxSystemLogBytes) return;

            string dir = fi.DirectoryName ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(systemLogPath);
            string ext = fi.Extension;

            // Shift existing numbered archives: .3.log -> deleted, .2.log -> .3.log, .1.log -> .2.log
            for (int i = MaxRollingFiles; i >= 1; i--)
            {
                string curr = Path.Combine(dir, $"{baseName}.{i}{ext}");
                if (i == MaxRollingFiles)
                {
                    if (File.Exists(curr))
                    {
                        try { File.Delete(curr); }
                        catch (Exception ex)
                        {
                            LastRotationError = ex;
                            System.Diagnostics.Debug.WriteLine($"[Log.RotateSystemLog] Failed to delete archive '{curr}': {ex}");
                        }
                    }
                }
                else
                {
                    string next = Path.Combine(dir, $"{baseName}.{i + 1}{ext}");
                    if (File.Exists(curr))
                    {
                        try { File.Move(curr, next, overwrite: true); }
                        catch (Exception ex)
                        {
                            LastRotationError = ex;
                            System.Diagnostics.Debug.WriteLine($"[Log.RotateSystemLog] Failed to shift archive '{curr}' to '{next}': {ex}");
                        }
                    }
                }
            }

            // Current main file moves to .1.log
            string firstArchive = Path.Combine(dir, $"{baseName}.1{ext}");
            File.Move(systemLogPath, firstArchive, overwrite: true);
        }
        catch (Exception ex)
        {
            LastRotationError = ex;
            System.Diagnostics.Debug.WriteLine($"[Log.RotateSystemLog] System log rotation failed: {ex}");
        }
    }

    private static void OpenMetricsWriter()
    {
        try { _metricWriter = new StreamWriter(_metricsPath!, append: true) { AutoFlush = true }; }
        catch (Exception ex)
        {
            LastMetricError = ex;
            System.Diagnostics.Debug.WriteLine($"[Log.OpenMetricsWriter] Failed to open metrics log '{_metricsPath}': {ex}");
            _metricWriter = null;
        }
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
        if (_sysWriter is not null)
        {
            try { _sysWriter.Flush(); _sysWriter.Dispose(); }
            catch (Exception ex)
            {
                LastWriteError = ex;
                System.Diagnostics.Debug.WriteLine($"[Log.CloseWriters] Failed to dispose system writer: {ex}");
            }
            _sysWriter = null;
        }
        if (_metricWriter is not null)
        {
            try { _metricWriter.Flush(); _metricWriter.Dispose(); }
            catch (Exception ex)
            {
                LastMetricError = ex;
                System.Diagnostics.Debug.WriteLine($"[Log.CloseWriters] Failed to dispose metric writer: {ex}");
            }
            _metricWriter = null;
        }
    }

    private static string ModuleName(string? callerPath)
    {
        if (string.IsNullOrEmpty(callerPath)) return "?";

        string normalized = callerPath.Replace('\\', '/');

        if (normalized.Contains("/Overlay/", StringComparison.OrdinalIgnoreCase)) return "Overlay";
        if (normalized.Contains("/Settings/", StringComparison.OrdinalIgnoreCase)) return "Settings";
        if (normalized.Contains("/Tray/", StringComparison.OrdinalIgnoreCase)) return "Tray";
        if (normalized.Contains("AlertService", StringComparison.OrdinalIgnoreCase)) return "Alerts";
        if (normalized.Contains("UpdateCheckService", StringComparison.OrdinalIgnoreCase)) return "Update";
        if (normalized.Contains("NotificationClient", StringComparison.OrdinalIgnoreCase)) return "Notification";
        if (normalized.Contains("TaskbarEmbedder", StringComparison.OrdinalIgnoreCase)) return "Overlay";
        if (normalized.Contains("WindowHelpers", StringComparison.OrdinalIgnoreCase)) return "System";
        if (normalized.Contains("NetworkUsage", StringComparison.OrdinalIgnoreCase) || normalized.Contains("NetUsage", StringComparison.OrdinalIgnoreCase) || normalized.Contains("TrafficHistory", StringComparison.OrdinalIgnoreCase)) return "Network";
        if (normalized.Contains("EseNetwork", StringComparison.OrdinalIgnoreCase) || normalized.Contains("DatImporter", StringComparison.OrdinalIgnoreCase)) return "Network";
        if (normalized.Contains("/Hardware/", StringComparison.OrdinalIgnoreCase)) return "Hardware";
        if (normalized.Contains("/Config/", StringComparison.OrdinalIgnoreCase)) return "Config";
        if (normalized.Contains("/Models/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("BrandColor", StringComparison.OrdinalIgnoreCase)) return "Hardware";
        if (normalized.Contains("Notifier", StringComparison.OrdinalIgnoreCase)) return "Notifier";
        if (normalized.EndsWith("/App.xaml.cs", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith("/App.cs", StringComparison.OrdinalIgnoreCase)) return "App";

        string fileName = Path.GetFileNameWithoutExtension(callerPath);
        return string.IsNullOrEmpty(fileName) ? "System" : fileName;
    }

    /// <summary>Defines the logging severity levels.</summary>
    public enum Level
    {
        Off = 0,
        Error = 1,
        Warning = 2,
        Info = 3,
        Debug = 4
    }

    /// <summary>Active logging level filter for system logs.</summary>
    public static Level SystemLevel { get; set; } = Level.Info;

    /// <summary>Active logging level filter for metric logs.</summary>
    public static Level MetricsLevel { get; set; } = Level.Info;

    /// <summary>Parses a string into a <see cref="Level"/> (default: <see cref="Level.Info"/>).</summary>
    public static Level ParseLevel(string? levelStr)
    {
        return levelStr?.Trim().ToLowerInvariant() switch
        {
            "off" => Level.Off,
            "error" => Level.Error,
            "warn" or "warning" => Level.Warning,
            "debug" => Level.Debug,
            _ => Level.Info,
        };
    }

    private static void Write(StreamWriter? writer, Level level, string message, string? callerPath, Exception? ex)
    {
        if (writer is null) return;

        // Check if we are writing to the metrics log or system log and filter based on respective levels
        bool isMetrics = ReferenceEquals(writer, _metricWriter);
        Level activeFilter = isMetrics ? MetricsLevel : SystemLevel;

        if (level > activeFilter || activeFilter == Level.Off) return;

        string levelStr = level switch
        {
            Level.Error => "ERROR",
            Level.Warning => "WARNING",
            Level.Debug => "DEBUG",
            _ => "INFO"
        };

        string module = ModuleName(callerPath);
        string timestamp = DateTime.Now.ToString("dd-MM-yyyy hh:mm:ss.fff tt");
        string cleanMessage = message.TrimEnd('\r', '\n');

        // Automatically strip redundant module or class prefixes if present
        string[] redundantPrefixes =
        [
            module + ":",
            "App:", "Overlay:", "Hardware:", "Settings:", "Network:", "Config:", "System:", "Tray:", "Alerts:", "Notifier:",
            "BrandDetection:", "DiskQuery:", "SRUM:", "NetUsage:", "TrafficHistoryStore:", "SetStartup:"
        ];

        bool stripped;
        do
        {
            stripped = false;
            foreach (var p in redundantPrefixes)
            {
                if (cleanMessage.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                {
                    cleanMessage = cleanMessage[p.Length..].TrimStart();
                    stripped = true;
                }
            }
        } while (stripped);

        string line = ex is null
            ? $"{timestamp} [{levelStr}] {module}: {cleanMessage}"
            : $"{timestamp} [{levelStr}] {module}: {cleanMessage} | {ex.GetType().Name}: {ex.Message.TrimEnd('\r', '\n')}\n{ex.StackTrace}";

        lock (_lock)
        {
            CheckMidnightRollover();

            // Re-resolve writer in case rollover updated the metric writer
            StreamWriter? targetWriter = isMetrics ? _metricWriter : _sysWriter;
            if (targetWriter is null) return;

            try
            {
                targetWriter.WriteLine(line);

                // If this is the system writer, check if active file size crossed MaxSystemLogBytes
                if (!isMetrics && _systemLogPath is not null && MaxSystemLogBytes > 0)
                {
                    CheckLiveSystemLogRoll();
                }
            }
            catch (Exception writeEx)
            {
                LastWriteError = writeEx;
                System.Diagnostics.Debug.WriteLine($"[Log.Write] Stream write failure: {writeEx}");
            }
        }
    }

    private static void CheckMidnightRollover()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (today == _currentDate) return;

        _currentDate = today;

        // Print visual calendar date separator in system log
        if (_sysWriter is not null)
        {
            try
            {
                string dateStr = today.ToString("dd-MM-yyyy");
                _sysWriter.WriteLine($"---------------------------------- {dateStr} ----------------------------------");
            }
            catch (Exception ex)
            {
                LastWriteError = ex;
                System.Diagnostics.Debug.WriteLine($"[Log.CheckMidnightRollover] Failed to write calendar date divider: {ex}");
            }
        }

        // Rollover metrics log file to new date
        if (_logsDir is not null)
        {
            _metricsPath = Path.Combine(_logsDir, $"lt_{DateTime.Today:dd-MM-yyyy}.log");
            if (_metricWriter is not null)
            {
                try
                {
                    _metricWriter.Flush();
                    _metricWriter.Dispose();
                }
                catch (Exception ex)
                {
                    LastMetricError = ex;
                    System.Diagnostics.Debug.WriteLine($"[Log.CheckMidnightRollover] Failed to dispose metric writer during rollover: {ex}");
                }
                _metricWriter = null;

                if (_metricsEnabled)
                {
                    OpenMetricsWriter();
                }
            }
        }
    }

    private static void CheckLiveSystemLogRoll()
    {
        if (_sysWriter is null || _systemLogPath is null) return;
        try
        {
            long length = _sysWriter.BaseStream.Length;
            if (length >= MaxSystemLogBytes)
            {
                _sysWriter.Flush();
                _sysWriter.Dispose();
                _sysWriter = null;

                RotateSystemLog(_systemLogPath);

                try { _sysWriter = new StreamWriter(_systemLogPath, append: true) { AutoFlush = true }; }
                catch (Exception ex)
                {
                    LastInitError = ex;
                    System.Diagnostics.Debug.WriteLine($"[Log.CheckLiveSystemLogRoll] Failed to re-open system log writer: {ex}");
                    _sysWriter = null;
                }
            }
        }
        catch (Exception ex)
        {
            LastRotationError = ex;
            System.Diagnostics.Debug.WriteLine($"[Log.CheckLiveSystemLogRoll] Live system log roll failed: {ex}");
        }
    }

    /// <summary>Writes an unmistakable visual header denoting the start of an application session.</summary>
    public static void SessionStart(string version, bool isAdmin, string configPath, string mode, string? osName = null)
    {
        lock (_lock)
        {
            if (_sysWriter is null) return;
            try
            {
                string timestamp = DateTime.Now.ToString("dd-MM-yyyy hh:mm:ss.fff tt");
                string osDesc = !string.IsNullOrWhiteSpace(osName)
                    ? osName
                    : System.Runtime.InteropServices.RuntimeInformation.OSDescription;
                string divider = new('-', 80);

                _sysWriter.WriteLine(divider);
                _sysWriter.WriteLine($"  LocalTelemetry v{version} Started | Session: {timestamp}");
                _sysWriter.WriteLine($"  OS: {osDesc} | Admin: {(isAdmin ? "Yes" : "No")} | Mode: {mode}");
                _sysWriter.WriteLine($"  Config: {configPath}");
                _sysWriter.WriteLine(divider);
            }
            catch (Exception ex)
            {
                LastWriteError = ex;
                System.Diagnostics.Debug.WriteLine($"[Log.SessionStart] Failed to write session start banner: {ex}");
            }
        }
    }

    /// <summary>Writes a visual footer denoting the end of an application session.</summary>
    public static void SessionEnd(string reason, TimeSpan uptime)
    {
        lock (_lock)
        {
            if (_sysWriter is null || _sessionEnded) return;
            _sessionEnded = true;
            try
            {
                string timestamp = DateTime.Now.ToString("dd-MM-yyyy hh:mm:ss.fff tt");
                string uptimeStr = uptime.TotalHours >= 1
                    ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s"
                    : $"{uptime.Minutes}m {uptime.Seconds}s";

                string divider = new('-', 80);

                _sysWriter.WriteLine(divider);
                _sysWriter.WriteLine($"  LocalTelemetry Terminated | Reason: {reason}");
                _sysWriter.WriteLine($"  Timestamp: {timestamp} | Session Uptime: {uptimeStr}");
                _sysWriter.WriteLine(divider);
            }
            catch (Exception ex)
            {
                LastWriteError = ex;
                System.Diagnostics.Debug.WriteLine($"[Log.SessionEnd] Failed to write session end footer: {ex}");
            }
        }
    }

    // System log (always Full, for developer use)
    public static void Debug(string message, [CallerFilePath] string? callerPath = null)
    {
        Write(_sysWriter, Level.Debug, message, callerPath, null);
    }

    public static void Debug(Exception ex, string message, [CallerFilePath] string? callerPath = null)
    {
        Write(_sysWriter, Level.Debug, message, callerPath, ex);
    }

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
