using System.Reflection;
using FluentAssertions;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Hardware;
using LocalTelemetry.Core.Models;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class NetUsageLoggerTests
{
    [Fact]
    public void NetUsageLogger_RecordAndFlushFinal_AccumulatesBytesAndSaves()
    {
        var cfg = new AppSettings();
        cfg.NetUsage.Enabled = true;

        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficHistory_{Guid.NewGuid():N}.dat");
        var historyFile = new TrafficHistoryFile(tempPath);

        try
        {
            using var logger = new NetUsageLogger(cfg, historyFile);
            logger.HistoryFile.Should().BeSameAs(historyFile);

            var snap = new TelemetrySnapshot
            {
                Timestamp = DateTime.Now,
                NetInterfaceName = "Ethernet",
                NetDownBps = 10000,
                NetUpBps = 5000
            };

            logger.Record(snap);

            // Record a second snap 1 second later
            var snap2 = new TelemetrySnapshot
            {
                Timestamp = snap.Timestamp.AddSeconds(1),
                NetInterfaceName = "Ethernet",
                NetDownBps = 20000,
                NetUpBps = 10000
            };
            logger.Record(snap2);

            logger.FlushFinal();

            var today = DateTime.Now.Date;
            var monthRecords = historyFile.GetMonth(today.Year, today.Month);
            monthRecords.Should().NotBeEmpty();
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public void NetUsageLogger_DisabledConfig_DoesNotRecord()
    {
        var cfg = new AppSettings();
        cfg.NetUsage.Enabled = false;

        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficHistory_{Guid.NewGuid():N}.dat");
        var historyFile = new TrafficHistoryFile(tempPath);

        try
        {
            using var logger = new NetUsageLogger(cfg, historyFile);
            var snap = new TelemetrySnapshot
            {
                Timestamp = DateTime.Now,
                NetInterfaceName = "Ethernet",
                NetDownBps = 10000,
                NetUpBps = 5000
            };

            logger.Record(snap);
            logger.FlushFinal();
            historyFile.GetAllRecords().Should().BeEmpty();
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public void NetUsageLogger_Constructor_DefaultPath_LoadsWithoutError()
    {
        var cfg = new AppSettings();
        cfg.NetUsage.Enabled = true;

        using var logger = new NetUsageLogger(cfg);
        logger.HistoryFile.Should().NotBeNull();
    }

    [Fact]
    public void NetUsageLogger_Constructor_LoadsTodayNamedRecord_IntoPerInterface()
    {
        var cfg = new AppSettings();
        cfg.NetUsage.Enabled = true;

        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficHistory_{Guid.NewGuid():N}.dat");
        var today = DateTime.Now.Date;
        try
        {
            var historyFile = new TrafficHistoryFile(tempPath);
            historyFile.SetDay(today.Year, today.Month, today.Day, 333, 444, "Ethernet", "Test");
            historyFile.Save();

            var loaded = new TrafficHistoryFile(tempPath);
            loaded.Load();
            using var logger = new NetUsageLogger(cfg, loaded);

            logger.Record(new TelemetrySnapshot
            {
                Timestamp = DateTime.Now,
                NetInterfaceName = "Ethernet",
                NetDownBps = 1000,
                NetUpBps = 500
            });
            logger.FlushFinal();

            var rec = loaded.Find(today.Year, today.Month, today.Day, "Ethernet");
            rec.Should().NotBeNull();
            rec.Value.DownBytes.Should().BeGreaterThanOrEqualTo(333);
            rec.Value.UpBytes.Should().BeGreaterThanOrEqualTo(444);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public void NetUsageLogger_Constructor_LoadsEmptyInterfaceRecord_IntoDefault()
    {
        var cfg = new AppSettings();
        cfg.NetUsage.Enabled = true;

        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficHistory_{Guid.NewGuid():N}.dat");
        var today = DateTime.Now.Date;
        try
        {
            var historyFile = new TrafficHistoryFile(tempPath);
            historyFile.SetDay(today.Year, today.Month, today.Day, 111, 222, "", "Test");
            historyFile.Save();

            var loaded = new TrafficHistoryFile(tempPath);
            loaded.Load();
            using var logger = new NetUsageLogger(cfg, loaded);

            var monthRecords = loaded.GetMonth(today.Year, today.Month);
            monthRecords.Should().Contain(r => r.Interface == "" && r.DownBytes == 111 && r.UpBytes == 222);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public void NetUsageLogger_DayRollover_SavesPreviousDayAndResets()
    {
        var cfg = new AppSettings();
        cfg.NetUsage.Enabled = true;

        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficHistory_{Guid.NewGuid():N}.dat");
        var today = DateTime.Now.Date;
        var yesterday = today.AddDays(-1);
        try
        {
            var historyFile = new TrafficHistoryFile(tempPath);
            historyFile.SetDay(today.Year, today.Month, today.Day, 333, 444, "Ethernet", "Test");
            historyFile.Save();

            var loaded = new TrafficHistoryFile(tempPath);
            loaded.Load();
            using var logger = new NetUsageLogger(cfg, loaded);

            typeof(NetUsageLogger).GetField("_lastDate", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(logger, yesterday);

            logger.Record(new TelemetrySnapshot
            {
                Timestamp = DateTime.Now,
                NetInterfaceName = "Ethernet",
                NetDownBps = 1000,
                NetUpBps = 500
            });
            logger.FlushFinal();

            var prev = loaded.Find(yesterday.Year, yesterday.Month, yesterday.Day, "Ethernet");
            prev.Should().NotBeNull();
            prev.Value.DownBytes.Should().Be(333);
            prev.Value.UpBytes.Should().Be(444);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public void NetUsageLogger_GetTodayTotalGb_And_GetMonthTotalGb()
    {
        var cfg = new AppSettings();
        cfg.NetUsage.Enabled = true;

        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficTotals_{Guid.NewGuid():N}.dat");
        var historyFile = new TrafficHistoryFile(tempPath);

        try
        {
            using var logger = new NetUsageLogger(cfg, historyFile);

            var snap = new TelemetrySnapshot
            {
                Timestamp = DateTime.Now,
                NetInterfaceName = "Ethernet",
                NetDownBps = 1024 * 1024 * 10,
                NetUpBps = 1024 * 1024 * 5
            };
            logger.Record(snap);

            var snap2 = new TelemetrySnapshot
            {
                Timestamp = snap.Timestamp.AddSeconds(1),
                NetInterfaceName = "Ethernet",
                NetDownBps = 1024 * 1024 * 10,
                NetUpBps = 1024 * 1024 * 5
            };
            logger.Record(snap2);

            double todayGb = logger.GetTodayTotalGb();
            todayGb.Should().BeGreaterThan(0);

            double monthGb = logger.GetMonthTotalGb();
            monthGb.Should().BeGreaterThan(0);

            logger.FlushFinal();
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public void NetUsageLogger_Record_WithLargeTimeGap_ClampsDelta_ToAvoidSleepSpikes()
    {
        var cfg = new AppSettings();
        cfg.NetUsage.Enabled = true;
        cfg.Monitoring.PollIntervalMs = 1000;

        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficClamp_{Guid.NewGuid():N}.dat");
        var historyFile = new TrafficHistoryFile(tempPath);

        try
        {
            using var logger = new NetUsageLogger(cfg, historyFile);

            var snap1 = new TelemetrySnapshot
            {
                Timestamp = DateTime.Now,
                NetInterfaceName = "Ethernet",
                NetDownBps = 1_000_000,
                NetUpBps = 500_000
            };
            logger.Record(snap1);

            // Simulate resuming from sleep 3600 seconds later
            var snap2 = new TelemetrySnapshot
            {
                Timestamp = snap1.Timestamp.AddSeconds(3600),
                NetInterfaceName = "Ethernet",
                NetDownBps = 1_000_000,
                NetUpBps = 500_000
            };
            logger.Record(snap2);

            // Delta should have been clamped to 1.0s rather than 3600s
            // Snap 1 ~ 1MB down, Snap 2 ~ 1MB down => ~2MB down total
            var (down, up) = historyFile.GetToday();
            down.Should().BeLessThan(5_000_000);
            up.Should().BeLessThan(2_500_000);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public void NetUsageLogger_Record_WithNegativeOrNaN_IsIgnored()
    {
        var cfg = new AppSettings();
        cfg.NetUsage.Enabled = true;

        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficNaN_{Guid.NewGuid():N}.dat");
        var historyFile = new TrafficHistoryFile(tempPath);

        try
        {
            using var logger = new NetUsageLogger(cfg, historyFile);

            logger.Record(new TelemetrySnapshot
            {
                Timestamp = DateTime.Now,
                NetInterfaceName = "Ethernet",
                NetDownBps = double.NaN,
                NetUpBps = -1000
            });

            var (down, up) = historyFile.GetToday();
            down.Should().Be(0);
            up.Should().Be(0);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }
}
