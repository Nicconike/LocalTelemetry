using System.Reflection;
using FluentAssertions;
using LocalTelemetry.Core.Diagnostics;
using Xunit;

namespace LocalTelemetry.Core.Tests.Diagnostics;

[Collection("TrafficHistory")]
public sealed class LogTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sysLogPath;
    private readonly string _metricLogPath;

    public LogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LogTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _sysLogPath = Path.Combine(_tempDir, "sys.log");
        _metricLogPath = Path.Combine(_tempDir, "metric.log");

        Log.Init(_sysLogPath, _metricLogPath, enableMetrics: true);
        Log.SystemLevel = Log.Level.Debug;
        Log.MetricsLevel = Log.Level.Info;
    }

    public void Dispose()
    {
        Log.Shutdown();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
    }

    [Fact]
    public void ModuleName_ParsesCallerPathsCorrectly()
    {
        var method = typeof(Log).GetMethod("ModuleName", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        method.Invoke(null, [null]).Should().Be("?");
        method.Invoke(null, ["C:\\Project\\LocalTelemetry.Core\\Hardware\\DiskQuery.cs"]).Should().Be("Hardware");
        method.Invoke(null, ["C:\\Project\\LocalTelemetry.Notifier\\ToastForm.cs"]).Should().Be("Notifier");
        method.Invoke(null, ["C:\\CustomPath\\MyClass.cs"]).Should().Be("MyClass");
        method.Invoke(null, ["C:\\Project\\LocalTelemetry.App\\App.xaml.cs"]).Should().Be("App");
        method.Invoke(null, ["C:\\Project\\LocalTelemetry.App\\Settings\\SettingsShell.xaml.cs"]).Should().Be("Settings");
        method.Invoke(null, ["C:\\Project\\LocalTelemetry.App\\Overlay\\TaskbarOverlay.cs"]).Should().Be("Overlay");
        method.Invoke(null, ["C:\\Project\\LocalTelemetry.App\\Tray\\TrayIconManager.cs"]).Should().Be("Tray");
        method.Invoke(null, ["C:\\Project\\LocalTelemetry.Core\\Hardware\\WindowsNetworkUsageProvider.cs"]).Should().Be("Network");
        method.Invoke(null, ["C:\\Project\\AlertService.cs"]).Should().Be("Alerts");
        method.Invoke(null, ["C:\\Project\\UpdateCheckService.cs"]).Should().Be("Update");
        method.Invoke(null, ["C:\\Project\\NotificationClient.cs"]).Should().Be("Notification");
        method.Invoke(null, ["C:\\Project\\TaskbarEmbedder.cs"]).Should().Be("Overlay");
        method.Invoke(null, ["C:\\Project\\WindowHelpers.cs"]).Should().Be("System");
        method.Invoke(null, ["C:\\Project\\EseNetwork.cs"]).Should().Be("Network");
        method.Invoke(null, ["C:\\Project\\LocalTelemetry.Core\\Config\\AppSettings.cs"]).Should().Be("Config");
    }

    [Fact]
    public void SystemLog_WritesAllLogLevelsAndExceptions()
    {
        Log.Debug("Debug message test");
        Log.Debug(new InvalidOperationException("Debug exception"), "Debug ex message");
        Log.Info("Info message test");
        Log.Warn("Warn message test");
        Log.Warn(new InvalidOperationException("Warn exception"), "Warn ex message");
        Log.Error("Error message test");
        Log.Error(new InvalidOperationException("Error exception"), "Error ex message");

        Log.Shutdown();

        File.Exists(_sysLogPath).Should().BeTrue();
        string content = File.ReadAllText(_sysLogPath);

        content.Should().Contain("Debug message test");
        content.Should().Contain("Info message test");
        content.Should().Contain("Warn message test");
        content.Should().Contain("Warn ex message");
        content.Should().Contain("Error message test");
        content.Should().Contain("Error ex message");
        content.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public void SystemLog_LevelFiltering_HonorsSystemLevelOff()
    {
        Log.SystemLevel = Log.Level.Off;

        Log.Info("Should be skipped off");
        Log.Error("Should be skipped off");

        Log.Shutdown();

        string content = File.ReadAllText(_sysLogPath);
        content.Should().NotContain("Should be skipped off");
    }

    [Fact]
    public void MetricLog_WritesMetricMessages()
    {
        Log.InfoMetric("Metric info test");
        Log.ErrorMetric("Metric error test");
        Log.ErrorMetric(new ArgumentException("Bad arg"), "Metric error ex");

        Log.Shutdown();

        File.Exists(_metricLogPath).Should().BeTrue();
        string content = File.ReadAllText(_metricLogPath);

        content.Should().Contain("Metric info test");
        content.Should().Contain("Metric error test");
        content.Should().Contain("Metric error ex");
    }

    [Fact]
    public void EnableMetrics_TogglesMetricsLog()
    {
        Log.EnableMetrics(false);
        Log.EnableMetrics(false); // test early return
        Log.InfoMetric("Metric when disabled");

        Log.EnableMetrics(true);
        Log.InfoMetric("Metric when re-enabled");

        Log.Shutdown();

        if (File.Exists(_metricLogPath))
        {
            string content = File.ReadAllText(_metricLogPath);
            content.Should().NotContain("Metric when disabled");
            content.Should().Contain("Metric when re-enabled");
        }
    }

    [Fact]
    public void Log_Init_HandlesInvalidPathGracefully()
    {
        var act = () => Log.Init("Z:\\InvalidPath\\sys.log", "Z:\\InvalidPath\\metric.log");
        act.Should().NotThrow();

        Log.Info("Message when sysWriter is null");

        Log.Init(_sysLogPath, _metricLogPath);
    }

    [Fact]
    public void SystemLog_RotatesWhenOversized()
    {
        var maxBytesProp = typeof(Log).GetProperty("MaxSystemLogBytes", BindingFlags.NonPublic | BindingFlags.Static);
        maxBytesProp.Should().NotBeNull();

        long original = (long)maxBytesProp.GetValue(null)!;

        try
        {
            maxBytesProp.SetValue(null, 64L);

            Log.Info("This message pushes the log over the tiny 64-byte cap");
            Log.Shutdown();

            string backupPath = _sysLogPath.Replace(".log", ".1.log");
            Log.Init(_sysLogPath, _metricLogPath);

            File.Exists(backupPath).Should().BeTrue();
        }
        finally
        {
            maxBytesProp.SetValue(null, original);
        }
    }

    [Fact]
    public void SystemLog_LiveRollsWhenThresholdExceededDuringWrite()
    {
        var maxBytesProp = typeof(Log).GetProperty("MaxSystemLogBytes", BindingFlags.NonPublic | BindingFlags.Static);
        maxBytesProp.Should().NotBeNull();
        long original = (long)maxBytesProp.GetValue(null)!;

        try
        {
            maxBytesProp.SetValue(null, 100L);

            Log.Info("First line");
            Log.Info("Second line pushing over 100 bytes limit");
            Log.Info("Third line into new rolled file");

            Log.Shutdown();

            string archive1 = _sysLogPath.Replace(".log", ".1.log");
            File.Exists(archive1).Should().BeTrue();
            File.Exists(_sysLogPath).Should().BeTrue();
        }
        finally
        {
            maxBytesProp.SetValue(null, original);
        }
    }

    [Fact]
    public void SessionStartAndEnd_WritesBanners_StartsAtLine1_AndDeduplicatesTermination()
    {
        string dedicatedSysLog = Path.Combine(_tempDir, "sys_banner_" + Guid.NewGuid().ToString("N") + ".log");
        string dedicatedMetricLog = Path.Combine(_tempDir, "metric_banner_" + Guid.NewGuid().ToString("N") + ".log");
        Log.Init(dedicatedSysLog, dedicatedMetricLog, enableMetrics: true);

        Log.SessionStart("1.0.0-test", isAdmin: true, configPath: "test_config.json", mode: "Standard", osName: "Windows 11 Pro (build 26200)");
        Log.SessionEnd("Windows System Shutdown", TimeSpan.FromMinutes(5));
        // Simulate secondary invocation from OnExit
        Log.SessionEnd("Application Quit", TimeSpan.FromMinutes(5));

        Log.Shutdown();

        string content = File.ReadAllText(dedicatedSysLog);
        // Verify starts on line 1 without leading blank line
        content.StartsWith('-').Should().BeTrue();
        content.Should().Contain("LocalTelemetry v1.0.0-test Started");
        content.Should().Contain("OS: Windows 11 Pro (build 26200)");
        content.Should().Contain("LocalTelemetry Terminated | Reason: Windows System Shutdown");
        content.Should().NotContain("Application Quit"); // deduplicated
        content.Should().Contain("Session Uptime: 5m 0s");
    }

    [Fact]
    public void MidnightRollover_SwitchesDateAndEmitsBanner()
    {
        var dateField = typeof(Log).GetField("_currentDate", BindingFlags.NonPublic | BindingFlags.Static);
        dateField.Should().NotBeNull();

        // Simulate yesterday
        dateField.SetValue(null, DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));

        Log.Info("Message after date change");
        Log.Shutdown();

        string content = File.ReadAllText(_sysLogPath);
        string todayStr = DateTime.Today.ToString("dd-MM-yyyy");
        content.Should().Contain($"--- {todayStr} ---");
        content.Should().Contain("Message after date change");
    }

    [Fact]
    public void ParseLevel_MapsAllStringsCorrectly()
    {
        Log.ParseLevel("off").Should().Be(Log.Level.Off);
        Log.ParseLevel("OFF").Should().Be(Log.Level.Off);
        Log.ParseLevel("error").Should().Be(Log.Level.Error);
        Log.ParseLevel("ERROR").Should().Be(Log.Level.Error);
        Log.ParseLevel("warn").Should().Be(Log.Level.Warning);
        Log.ParseLevel("warning").Should().Be(Log.Level.Warning);
        Log.ParseLevel("WARN").Should().Be(Log.Level.Warning);
        Log.ParseLevel("debug").Should().Be(Log.Level.Debug);
        Log.ParseLevel("DEBUG").Should().Be(Log.Level.Debug);
        Log.ParseLevel("info").Should().Be(Log.Level.Info);
        Log.ParseLevel("INFO").Should().Be(Log.Level.Info);
        Log.ParseLevel(null).Should().Be(Log.Level.Info);
        Log.ParseLevel("").Should().Be(Log.Level.Info);
        Log.ParseLevel("unknown").Should().Be(Log.Level.Info);
    }

    [Fact]
    public void RedundantPrefixes_AreStrippedFromMessage()
    {
        Log.Info("Hardware: CPU at 85%");
        Log.Info("Overlay: FPS counter updated");
        Log.Info("Settings: Config reloaded");
        Log.Info("Alerts: Memory threshold reached");

        Log.Shutdown();

        string content = File.ReadAllText(_sysLogPath);
        content.Should().Contain("CPU at 85%");
        content.Should().NotContain("Hardware: Hardware: CPU at 85%");
        content.Should().Contain("FPS counter updated");
        content.Should().Contain("Config reloaded");
        content.Should().Contain("Memory threshold reached");
    }

    [Fact]
    public void SessionStart_WithNullOsName_UsesFallbackOsDescription()
    {
        string dedicatedSysLog = Path.Combine(_tempDir, "sys_osfallback_" + Guid.NewGuid().ToString("N") + ".log");
        string dedicatedMetricLog = Path.Combine(_tempDir, "metric_osfallback_" + Guid.NewGuid().ToString("N") + ".log");
        Log.Init(dedicatedSysLog, dedicatedMetricLog, enableMetrics: false);

        Log.SessionStart("1.2.3", isAdmin: false, configPath: "test.json", mode: "Floating", osName: null);
        Log.Shutdown();

        string content = File.ReadAllText(dedicatedSysLog);
        content.Should().Contain("LocalTelemetry v1.2.3 Started");
        content.Should().Contain("OS:");
        content.Should().Contain("Admin: No");
    }

    [Fact]
    public void SessionEnd_WithUptimeOverOneHour_FormatsHoursCorrectly()
    {
        string dedicatedSysLog = Path.Combine(_tempDir, "sys_longuptime_" + Guid.NewGuid().ToString("N") + ".log");
        string dedicatedMetricLog = Path.Combine(_tempDir, "metric_longuptime_" + Guid.NewGuid().ToString("N") + ".log");
        Log.Init(dedicatedSysLog, dedicatedMetricLog, enableMetrics: false);

        TimeSpan uptime = TimeSpan.FromHours(4).Add(TimeSpan.FromMinutes(32)).Add(TimeSpan.FromSeconds(15));
        Log.SessionEnd("User Logout", uptime);
        Log.Shutdown();

        string content = File.ReadAllText(dedicatedSysLog);
        content.Should().Contain("Session Uptime: 4h 32m 15s");
        content.Should().Contain("Reason: User Logout");
    }

    [Fact]
    public void RotateSystemLog_CascadesAllExistingArchives()
    {
        string dedicatedSysLog = Path.Combine(_tempDir, "cascade_test.log");
        string dedicatedMetricLog = Path.Combine(_tempDir, "cascade_metric.log");

        // Pre-create .1.log, .2.log, .3.log, and main .log
        string archive1 = Path.Combine(_tempDir, "cascade_test.1.log");
        string archive2 = Path.Combine(_tempDir, "cascade_test.2.log");
        string archive3 = Path.Combine(_tempDir, "cascade_test.3.log");

        File.WriteAllText(archive1, "old archive 1");
        File.WriteAllText(archive2, "old archive 2");
        File.WriteAllText(archive3, "old archive 3 to be deleted");
        File.WriteAllText(dedicatedSysLog, new string('A', 200));

        var maxBytesProp = typeof(Log).GetProperty("MaxSystemLogBytes", BindingFlags.NonPublic | BindingFlags.Static);
        long original = (long)maxBytesProp!.GetValue(null)!;

        try
        {
            maxBytesProp.SetValue(null, 100L); // Trigger rotation

            Log.Init(dedicatedSysLog, dedicatedMetricLog, enableMetrics: false);
            Log.Shutdown();

            // Archive 3 should have been overwritten by archive 2
            File.Exists(archive3).Should().BeTrue();
            File.ReadAllText(archive3).Should().Be("old archive 2");

            // Archive 2 should have been overwritten by archive 1
            File.Exists(archive2).Should().BeTrue();
            File.ReadAllText(archive2).Should().Be("old archive 1");

            // Archive 1 should contain the original 200 'A' characters
            File.Exists(archive1).Should().BeTrue();
            File.ReadAllText(archive1).Should().Contain("AAAA");
        }
        finally
        {
            maxBytesProp.SetValue(null, original);
        }
    }

    [Fact]
    public void MidnightRollover_WithActiveMetricWriter_ReopensNewDayLog()
    {
        string dedicatedSysLog = Path.Combine(_tempDir, "sys_midroll_" + Guid.NewGuid().ToString("N") + ".log");
        string dedicatedMetricLog = Path.Combine(_tempDir, $"lt_{DateTime.Today.AddDays(-1):dd-MM-yyyy}.log");
        Log.Init(dedicatedSysLog, dedicatedMetricLog, enableMetrics: true);

        // Prime metric writer
        Log.InfoMetric("Metric before rollover");

        // Simulate midnight date change
        var dateField = typeof(Log).GetField("_currentDate", BindingFlags.NonPublic | BindingFlags.Static);
        dateField!.SetValue(null, DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));

        // Write after rollover
        Log.InfoMetric("Metric after midnight rollover");
        Log.Shutdown();

        string todayMetricFile = Path.Combine(_tempDir, $"lt_{DateTime.Today:dd-MM-yyyy}.log");
        File.Exists(todayMetricFile).Should().BeTrue();
        string content = File.ReadAllText(todayMetricFile);
        content.Should().Contain("Metric after midnight rollover");
    }

    [Fact]
    public void ErrorProperties_CanBeSetAndQueried()
    {
        var ex = new InvalidOperationException("Test error");
        Log.LastInitError = ex;
        Log.LastWriteError = ex;
        Log.LastRotationError = ex;
        Log.LastMetricError = ex;

        Log.LastInitError.Should().BeSameAs(ex);
        Log.LastWriteError.Should().BeSameAs(ex);
        Log.LastRotationError.Should().BeSameAs(ex);
        Log.LastMetricError.Should().BeSameAs(ex);
    }

    [Fact]
    public void RotateSystemLog_WithExistingArchives_ShiftsFilesCorrectly()
    {
        string logPath = Path.Combine(_tempDir, "sys_rot_shift.log");
        File.WriteAllText(logPath, "current_log_content");
        File.WriteAllText(Path.Combine(_tempDir, "sys_rot_shift.1.log"), "archive_1");
        File.WriteAllText(Path.Combine(_tempDir, "sys_rot_shift.2.log"), "archive_2");
        File.WriteAllText(Path.Combine(_tempDir, "sys_rot_shift.3.log"), "archive_3");

        var maxBytesProp = typeof(Log).GetProperty("MaxSystemLogBytes", BindingFlags.NonPublic | BindingFlags.Static);
        long original = (long)maxBytesProp!.GetValue(null)!;
        maxBytesProp.SetValue(null, 10L); // threshold = 10 bytes

        try
        {
            var rotateMethod = typeof(Log).GetMethod("RotateSystemLog", BindingFlags.NonPublic | BindingFlags.Static);
            rotateMethod!.Invoke(null, [logPath]);

            File.ReadAllText(Path.Combine(_tempDir, "sys_rot_shift.1.log")).Should().Be("current_log_content");
            File.ReadAllText(Path.Combine(_tempDir, "sys_rot_shift.2.log")).Should().Be("archive_1");
            File.ReadAllText(Path.Combine(_tempDir, "sys_rot_shift.3.log")).Should().Be("archive_2");
        }
        finally
        {
            maxBytesProp.SetValue(null, original);
        }
    }

    [Fact]
    public void SessionStartEnd_And_MetricsDisable_And_LiveRoll_Coverage()
    {
        string dedicatedSysLog = Path.Combine(_tempDir, "sys_session_" + Guid.NewGuid().ToString("N") + ".log");
        string dedicatedMetricLog = Path.Combine(_tempDir, "metric_session_" + Guid.NewGuid().ToString("N") + ".log");
        Log.Init(dedicatedSysLog, dedicatedMetricLog, enableMetrics: true);

        // 1. SessionStart with custom osName
        Log.SessionStart("1.0.0", isAdmin: true, "C:\\test\\config.json", "Full", "Windows 11 Enterprise");
        Log.SessionStart("1.0.0", isAdmin: false, "C:\\test\\config.json", "Overlay", null); // default osName

        // 2. SessionEnd with <1h and >=1h
        Log.SessionEnd("User Exit", TimeSpan.FromMinutes(35));
        // Reset _sessionEnded to test >= 1h branch
        typeof(Log).GetField("_sessionEnded", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, false);
        Log.SessionEnd("System Shutdown", TimeSpan.FromHours(3.25));

        // 3. EnableMetrics toggling and disposing active writer
        Log.InfoMetric("Metric before disabling");
        Log.EnableMetrics(false);
        Log.EnableMetrics(false); // idempotent early return
        Log.EnableMetrics(true);

        // 4. Live system log roll via CheckLiveSystemLogRoll
        var maxBytesProp = typeof(Log).GetProperty("MaxSystemLogBytes", BindingFlags.NonPublic | BindingFlags.Static);
        long original = (long)maxBytesProp!.GetValue(null)!;
        maxBytesProp.SetValue(null, 10L); // 10 bytes

        try
        {
            var checkRoll = typeof(Log).GetMethod("CheckLiveSystemLogRoll", BindingFlags.NonPublic | BindingFlags.Static);
            checkRoll?.Invoke(null, null);

            // Write through public method to trigger live log roll
            Log.Info("Trigger live system log roll with long line exceeding threshold");
        }
        finally
        {
            maxBytesProp.SetValue(null, original);
            Log.Shutdown();
        }

        File.Exists(dedicatedSysLog).Should().BeTrue();
    }

    [Fact]
    public void Log_ErrorBranches_And_RotationEdgeCases()
    {
        // 1. DeleteArchiveIfExists with non-existent and un-deletable file
        var delMethod = typeof(Log).GetMethod("DeleteArchiveIfExists", BindingFlags.NonPublic | BindingFlags.Static);
        delMethod.Should().NotBeNull();
        delMethod.Invoke(null, [Path.Combine(_tempDir, "non_existent_file.log")]);

        string lockedFile = Path.Combine(_tempDir, "locked_to_delete.log");
        File.WriteAllText(lockedFile, "locked content");
        using (var fs = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            delMethod.Invoke(null, [lockedFile]);
            Log.LastRotationError.Should().NotBeNull();
        }

        // 2. ShiftArchiveIfExists with non-existent and locked target file
        var shiftMethod = typeof(Log).GetMethod("ShiftArchiveIfExists", BindingFlags.NonPublic | BindingFlags.Static);
        shiftMethod.Should().NotBeNull();
        shiftMethod.Invoke(null, [Path.Combine(_tempDir, "non_existent_source.log"), Path.Combine(_tempDir, "target.log")]);

        string sourceFile = Path.Combine(_tempDir, "shift_src.log");
        string destFile = Path.Combine(_tempDir, "shift_dst.log");
        File.WriteAllText(sourceFile, "src content");
        File.WriteAllText(destFile, "dst content");
        using (var fs = new FileStream(destFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            shiftMethod.Invoke(null, [sourceFile, destFile]);
            Log.LastRotationError.Should().NotBeNull();
        }

        // 3. RotateSystemLog exception handling
        var rotateMethod = typeof(Log).GetMethod("RotateSystemLog", BindingFlags.NonPublic | BindingFlags.Static);
        rotateMethod.Should().NotBeNull();
        rotateMethod.Invoke(null, ["invalid_path_with_illegal_char|<>:?.log"]);
        Log.LastRotationError.Should().NotBeNull();

        // 4. SessionStart / SessionEnd error handling when stream throws
        var originalWriter = typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        var throwStream = new ThrowOnWriteOnlyStream();
        var throwWriter = new StreamWriter(throwStream) { AutoFlush = true };
        try
        {
            typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, throwWriter);

            Log.SessionStart("1.0.0", true, "config.json", "Test");
            Log.LastWriteError.Should().NotBeNull();

            typeof(Log).GetField("_sessionEnded", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, false);
            Log.SessionEnd("Test Exit", TimeSpan.FromMinutes(10));
            Log.LastWriteError.Should().NotBeNull();
        }
        finally
        {
            typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, originalWriter);
            throwStream.ShouldThrow = false;
            throwWriter.Dispose();
            throwStream.Dispose();
        }
    }

    [Fact]
    public void Log_LiveSystemLogRoll_And_MidnightRollover_Coverage()
    {
        long originalMax = Log.MaxSystemLogBytes;
        Log.LastWriteError = null;
        try
        {
            Log.MaxSystemLogBytes = 50;
            Log.Info("Trigger first line that exceeds live limit and rotates the system log file");
            Log.Info("Trigger second line after rotation has executed");

            string archivePath = Path.Combine(_tempDir, "sys.1.log");
            File.Exists(archivePath).Should().BeTrue();

            var currentDateField = typeof(Log).GetField("_currentDate", BindingFlags.NonPublic | BindingFlags.Static)!;
            currentDateField.SetValue(null, DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));

            Log.LastWriteError = null;
            Log.Info("Trigger midnight rollover with calendar line and new metrics file");
            Log.LastWriteError.Should().BeNull();
        }
        finally
        {
            Log.MaxSystemLogBytes = originalMax;
        }
    }

    [Fact]
    public void Log_RotateSystemLog_Exception_Coverage()
    {
        var rotateMethod = typeof(Log).GetMethod("RotateSystemLog", BindingFlags.NonPublic | BindingFlags.Static);
        rotateMethod.Should().NotBeNull();

        // 2. Lock file exclusively with length >= MaxSystemLogBytes so File.Move throws IOException
        long origMax = Log.MaxSystemLogBytes;
        try
        {
            Log.MaxSystemLogBytes = 1;
            string conflictDir = Path.Combine(_tempDir, "rotate_conflict");
            Directory.CreateDirectory(conflictDir);
            string fakeLog = Path.Combine(conflictDir, "app.log");
            File.WriteAllText(fakeLog, "content exceeding length");
            using (var lockStream = File.Open(fakeLog, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Log.LastRotationError = null;
                rotateMethod.Invoke(null, [fakeLog]);
                Log.LastRotationError.Should().NotBeNull();
            }
        }
        finally
        {
            Log.MaxSystemLogBytes = origMax;
        }
    }

    [Fact]
    public void Log_CloseWriters_Exception_Coverage()
    {
        var origSys = typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        var origMetric = typeof(Log).GetField("_metricWriter", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

        var throwStreamSys = new ThrowOnWriteOnlyStream { ShouldThrow = false, ThrowOnFlush = true };
        var throwStreamMetric = new ThrowOnWriteOnlyStream { ShouldThrow = false, ThrowOnFlush = true };

        try
        {
            typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, new StreamWriter(throwStreamSys));
            typeof(Log).GetField("_metricWriter", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, new StreamWriter(throwStreamMetric));

            Log.LastWriteError = null;
            Log.LastMetricError = null;

            var closeMethod = typeof(Log).GetMethod("CloseWriters", BindingFlags.NonPublic | BindingFlags.Static);
            closeMethod!.Invoke(null, null);

            Log.LastWriteError.Should().NotBeNull();
            Log.LastMetricError.Should().NotBeNull();
        }
        finally
        {
            typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, origSys);
            typeof(Log).GetField("_metricWriter", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, origMetric);
        }
    }

    [Fact]
    public void Log_Write_Exception_Coverage()
    {
        var origSys = typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        var throwStream = new ThrowOnWriteOnlyStream { ShouldThrow = true };
        var throwWriter = new StreamWriter(throwStream) { AutoFlush = true };

        try
        {
            typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, throwWriter);

            Log.LastWriteError = null;
            Log.Error("Write failure test message");
            Log.LastWriteError.Should().NotBeNull();
        }
        finally
        {
            typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, origSys);
            throwStream.ShouldThrow = false;
            throwWriter.Dispose();
            throwStream.Dispose();
        }
    }

    [Fact]
    public void Log_CheckMidnightRollover_Exceptions_Coverage()
    {
        var origSys = typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        var origMetric = typeof(Log).GetField("_metricWriter", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        var currentDateField = typeof(Log).GetField("_currentDate", BindingFlags.NonPublic | BindingFlags.Static)!;
        var origDate = currentDateField.GetValue(null);

        var throwStreamSys = new ThrowOnWriteOnlyStream { ShouldThrow = true };
        var throwStreamMetric = new ThrowOnWriteOnlyStream { ShouldThrow = false, ThrowOnFlush = true };

        try
        {
            typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, new StreamWriter(throwStreamSys) { AutoFlush = true });
            typeof(Log).GetField("_metricWriter", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, new StreamWriter(throwStreamMetric));

            currentDateField.SetValue(null, DateOnly.FromDateTime(DateTime.Today.AddDays(-2)));

            Log.LastWriteError = null;
            Log.LastMetricError = null;

            var rolloverMethod = typeof(Log).GetMethod("CheckMidnightRollover", BindingFlags.NonPublic | BindingFlags.Static);
            rolloverMethod!.Invoke(null, null);

            Log.LastWriteError.Should().NotBeNull();
            Log.LastMetricError.Should().NotBeNull();
        }
        finally
        {
            typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, origSys);
            typeof(Log).GetField("_metricWriter", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, origMetric);
            currentDateField.SetValue(null, origDate);
        }
    }

    [Fact]
    public void Log_CheckLiveSystemLogRoll_Exceptions_Coverage()
    {
        var origSys = typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        var origPath = typeof(Log).GetField("_systemLogPath", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        var origMax = Log.MaxSystemLogBytes;

        var checkMethod = typeof(Log).GetMethod("CheckLiveSystemLogRoll", BindingFlags.NonPublic | BindingFlags.Static);

        try
        {
            // Case 1: BaseStream.Length throws
            var throwStream = new ThrowOnWriteOnlyStream { ShouldThrow = false, ThrowOnLength = true };
            typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, new StreamWriter(throwStream));
            typeof(Log).GetField("_systemLogPath", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, Path.Combine(_tempDir, "test.log"));

            Log.LastRotationError = null;
            checkMethod!.Invoke(null, null);
            Log.LastRotationError.Should().NotBeNull();

            // Case 2: Length exceeds limit, but re-opening StreamWriter fails
            Log.MaxSystemLogBytes = 1;
            string testFile = Path.Combine(_tempDir, "test_roll.log");
            File.WriteAllText(testFile, "initial content exceeding limit");
            using (var fileStream = new FileStream(testFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                var writer = new StreamWriter(fileStream);
                typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, writer);
                typeof(Log).GetField("_systemLogPath", BindingFlags.NonPublic | BindingFlags.Static)!
                    .SetValue(null, @"Z:\NonExistentDriveDirectory\sub\sys.log");

                Log.LastInitError = null;
                checkMethod.Invoke(null, null);
                Log.LastInitError.Should().NotBeNull();
            }
        }
        finally
        {
            typeof(Log).GetField("_sysWriter", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, origSys);
            typeof(Log).GetField("_systemLogPath", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, origPath);
            Log.MaxSystemLogBytes = origMax;
        }
    }

    [Fact]
    public void GlobalDeduplication_SuppressesRepeatedMessagesAcrossAllLevels()
    {
        Log.ResetDeduplication();

        // Write identical messages multiple times
        for (int i = 0; i < 5; i++)
        {
            Log.Error("Recurring ioctl error");
            Log.Warn("Recurring memory warning");
            Log.Info("Recurring state info");
            Log.Debug("Recurring debug trace");
            Log.ErrorMetric("Recurring metric failure");
            Log.InfoMetric("Tick metric telemetry row");
        }

        Log.Shutdown();

        string sysContent = File.ReadAllText(_sysLogPath);
        string metricContent = File.ReadAllText(_metricLogPath);

        // System log messages must appear exactly once
        sysContent.Split("Recurring ioctl error").Length.Should().Be(2, "Recurring error must be logged strictly once");
        sysContent.Split("Recurring memory warning").Length.Should().Be(2, "Recurring warning must be logged strictly once");
        sysContent.Split("Recurring state info").Length.Should().Be(2, "Recurring info must be logged strictly once");
        sysContent.Split("Recurring debug trace").Length.Should().Be(2, "Recurring debug must be logged strictly once");

        // ErrorMetric must appear strictly once
        metricContent.Split("Recurring metric failure").Length.Should().Be(2, "Recurring metric failure must be logged strictly once");

        // InfoMetric (telemetry tick data) must NOT be suppressed
        metricContent.Split("Tick metric telemetry row").Length.Should().Be(6, "InfoMetric telemetry rows must be recorded on each tick");
    }

    private sealed class ThrowOnWriteOnlyStream : Stream
    {
        public bool ShouldThrow { get; set; } = true;
        public bool ThrowOnLength { get; set; }
        public bool ThrowOnFlush { get; set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => ThrowOnLength ? throw new IOException("Length error") : 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush()
        {
            if (ThrowOnFlush) throw new IOException("Flush error");
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (ShouldThrow) throw new IOException("Simulated disk error");
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (ShouldThrow) throw new IOException("Simulated disk error");
        }
    }
}
