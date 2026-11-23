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
    private static readonly Lock _lock = new();
    private static readonly Mutex? _crossProcessMutex = CreateCrossProcessMutex();
    private static StreamWriter? _sysWriter;
    private static StreamWriter? _metricWriter;
    private static string? _systemLogPath;
    private static string? _metricsPath;
    private static string? _logsDir;
    private static bool _metricsEnabled;
    private static DateOnly _currentDate = DateOnly.FromDateTime(DateTime.Today);
    private static bool _sessionEnded;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _loggedMessages = new();

    private static Mutex? CreateCrossProcessMutex()
    {
        try
        {
            string name = OperatingSystem.IsWindows()
                ? @"Local\LocalTelemetry_SystemLog_Mutex"
                : "LocalTelemetry_SystemLog_Mutex";
            return new Mutex(false, name);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Log] Failed to create cross-process mutex: {ex}");
            return null;
        }
    }

    private static void WriteLineAtomic(StreamWriter writer, string text)
    {
        try
        {
            lock (_lock)
            {
                bool hasMutex = false;
                if (_crossProcessMutex is not null)
                {
                    try
                    {
                        hasMutex = _crossProcessMutex.WaitOne(500);
                    }
                    catch (AbandonedMutexException)
                    {
                        hasMutex = true;
                    }
                }

                try
                {
                    if (writer.BaseStream.CanSeek)
                    {
                        writer.Flush();
                        writer.BaseStream.Seek(0, SeekOrigin.End);
                    }
                    writer.WriteLine(text);
                    writer.Flush();
                }
                finally
                {
                    if (hasMutex)
                    {
                        _crossProcessMutex!.ReleaseMutex();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LastWriteError = ex;
            System.Diagnostics.Debug.WriteLine($"[Log.WriteLineAtomic] Write failure: {ex}");
        }
    }

    /// <summary>Resets the session deduplication cache. Primarily used for unit testing.</summary>
    public static void ResetDeduplication() => _loggedMessages.Clear();

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
            _loggedMessages.Clear();

            bool hasMutex = false;
            if (_crossProcessMutex is not null)
            {
                try { hasMutex = _crossProcessMutex.WaitOne(1000); }
                catch (AbandonedMutexException) { hasMutex = true; }
            }

            try
            {
                RotateSystemLog(systemLogPath);
                try
                {
                    var fs = new FileStream(systemLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    _sysWriter = new StreamWriter(fs) { AutoFlush = true };
                }
                catch (Exception ex)
                {
                    LastInitError = ex;
                    System.Diagnostics.Debug.WriteLine($"[Log.Init] Failed to open system log '{systemLogPath}': {ex}");
                    _sysWriter = null;
                }
            }
            finally
            {
                if (hasMutex)
                {
                    _crossProcessMutex!.ReleaseMutex();
                }
            }
        }
    }

    private static void DeleteArchiveIfExists(string path)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path); }
        catch (Exception ex)
        {
            LastRotationError = ex;
            System.Diagnostics.Debug.WriteLine($"[Log.RotateSystemLog] Failed to delete archive '{path}': {ex}");
        }
    }

    private static void ShiftArchiveIfExists(string currentPath, string nextPath)
    {
        if (!File.Exists(currentPath)) return;
        try { File.Move(currentPath, nextPath, overwrite: true); }
        catch (Exception ex)
        {
            LastRotationError = ex;
            System.Diagnostics.Debug.WriteLine($"[Log.RotateSystemLog] Failed to shift archive '{currentPath}' to '{nextPath}': {ex}");
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

            // Delete oldest archive
            DeleteArchiveIfExists(Path.Combine(dir, $"{baseName}.{MaxRollingFiles}{ext}"));

            // Shift existing numbered archives: .2.log -> .3.log, .1.log -> .2.log
            for (int i = MaxRollingFiles - 1; i >= 1; i--)
            {
                string curr = Path.Combine(dir, $"{baseName}.{i}{ext}");
                string next = Path.Combine(dir, $"{baseName}.{i + 1}{ext}");
                ShiftArchiveIfExists(curr, next);
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
        try
        {
            var fs = new FileStream(_metricsPath!, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _metricWriter = new StreamWriter(fs) { AutoFlush = true };
        }
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

    private const string NetworkModuleName = "Network";
    private const string HardwareModuleName = "Hardware";

    private static readonly (string Pattern, string Module)[] ModuleMappings =
    [
        ("/Overlay/", "Overlay"),
        ("/Settings/", "Settings"),
        ("/Tray/", "Tray"),
        ("AlertService", "Alerts"),
        ("UpdateCheckService", "Update"),
        ("NotificationClient", "Notification"),
        ("TaskbarEmbedder", "Overlay"),
        ("WindowHelpers", "System"),
        ("NetworkUsage", NetworkModuleName),
        ("NetUsage", NetworkModuleName),
        ("TrafficHistory", NetworkModuleName),
        ("EseNetwork", NetworkModuleName),
        ("DatImporter", NetworkModuleName),
        ("/Hardware/", HardwareModuleName),
        ("/Config/", "Config"),
        ("/Models/", HardwareModuleName),
        ("BrandColor", HardwareModuleName),
        ("Notifier", "Notifier"),
    ];

    private static string ModuleName(string? callerPath)
    {
        if (string.IsNullOrEmpty(callerPath)) return "?";

        string normalized = callerPath.Replace('\\', '/');

        foreach (var (pattern, module) in ModuleMappings)
        {
            if (normalized.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return module;
        }

        if (normalized.EndsWith("/App.xaml.cs", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith("/App.cs", StringComparison.OrdinalIgnoreCase))
            return "App";

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

    private static string StripRedundantPrefixes(string message, string module)
    {
        string[] redundantPrefixes =
        [
            module + ":",
            "App:", "Overlay:", "Hardware:", "Settings:", "Network:", "Config:", "System:", "Tray:", "Alerts:", "Notifier:",
            "BrandDetection:", "DiskQuery:", "SRUM:", "NetUsage:", "TrafficHistoryStore:", "SetStartup:"
        ];

        string cleanMessage = message.TrimEnd('\r', '\n');
        bool stripped;
        do
        {
            stripped = false;
            string? match = redundantPrefixes.FirstOrDefault(p => cleanMessage.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                cleanMessage = cleanMessage[match.Length..].TrimStart();
                stripped = true;
            }
        } while (stripped);

        return cleanMessage;
    }

    private static void Write(StreamWriter? writer, Level level, string message, string? callerPath, Exception? ex)
    {
        if (writer is null) return;

        // Check if we are writing to the metrics log or system log and filter based on respective levels
        bool isMetrics = ReferenceEquals(writer, _metricWriter);
        Level activeFilter = isMetrics ? MetricsLevel : SystemLevel;

        if (level > activeFilter || activeFilter == Level.Off) return;
        string module = ModuleName(callerPath);
        string cleanMessage = StripRedundantPrefixes(message, module);

        // Deduplicate messages across all logging levels so identical messages, warnings, and failures are logged strictly once per session
        bool isMetricData = isMetrics && level == Level.Info;
        if (!isMetricData)
        {
            string dedupKey = $"{level}|{module}|{cleanMessage}|{ex?.GetType().FullName}";
            if (!_loggedMessages.TryAdd(dedupKey, 0)) return;
        }

        string levelStr = level switch
        {
            Level.Error => "ERROR",
            Level.Warning => "WARNING",
            Level.Debug => "DEBUG",
            _ => "INFO"
        };

        string timestamp = DateTime.Now.ToString("dd-MM-yyyy hh:mm:ss.fff tt");

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
                WriteLineAtomic(targetWriter, line);

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
                WriteLineAtomic(_sysWriter, $"---------------------------------- {dateStr} ----------------------------------");
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
            if (_sysWriter.BaseStream.Length >= MaxSystemLogBytes)
            {
                PerformLiveSystemLogRoll(_systemLogPath);
            }
        }
        catch (Exception ex)
        {
            LastRotationError = ex;
            System.Diagnostics.Debug.WriteLine($"[Log.CheckLiveSystemLogRoll] Live system log roll failed: {ex}");
        }
    }

    private static void PerformLiveSystemLogRoll(string path)
    {
        try
        {
            lock (_lock)
            {
                bool hasMutex = false;
                if (_crossProcessMutex is not null)
                {
                    try { hasMutex = _crossProcessMutex.WaitOne(1000); }
                    catch (AbandonedMutexException) { hasMutex = true; }
                }

                try
                {
                    _sysWriter?.Flush();
                    _sysWriter?.Dispose();
                    _sysWriter = null;

                    RotateSystemLog(path);
                    ReopenSystemLogWriter(path);
                }
                finally
                {
                    if (hasMutex)
                    {
                        _crossProcessMutex!.ReleaseMutex();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LastRotationError = ex;
            System.Diagnostics.Debug.WriteLine($"[Log.PerformLiveSystemLogRoll] Failed to rotate system log: {ex}");
        }
    }

    private static void ReopenSystemLogWriter(string path)
    {
        try
        {
            var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _sysWriter = new StreamWriter(fs) { AutoFlush = true };
        }
        catch (Exception ex)
        {
            LastInitError = ex;
            System.Diagnostics.Debug.WriteLine($"[Log.CheckLiveSystemLogRoll] Failed to re-open system log writer: {ex}");
            _sysWriter = null;
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
                string divider = new('-', 80);

                WriteLineAtomic(_sysWriter, divider);
                WriteLineAtomic(_sysWriter, $"LocalTelemetry v{version} Started | Session: {timestamp} | Admin: {(isAdmin ? "Yes" : "No")} | Mode: {mode}");
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
                WriteLineAtomic(_sysWriter, $"LocalTelemetry Terminated | Reason: {reason} | Timestamp: {timestamp} | Session Uptime: {uptimeStr}");
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
