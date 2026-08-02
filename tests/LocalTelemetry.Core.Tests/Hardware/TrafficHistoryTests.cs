using FluentAssertions;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Hardware;
using LocalTelemetry.Core.Models;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class TrafficHistoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public TrafficHistoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TrafficHistoryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "internet_usage.jsonl");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TrafficHistoryFile_LoadAndSave_WorksCorrectly()
    {
        var file = new TrafficHistoryFile(_filePath);
        file.Load();
        file.Count.Should().Be(0);

        file.SetDay(2026, 8, 1, 1024, 2048, "Ethernet", "LocalTelemetry");
        file.SetDay(2026, 8, 2, 4096, 8192, "Ethernet", "LocalTelemetry");

        file.Count.Should().Be(2);
        file.Save();

        File.Exists(_filePath).Should().BeTrue();

        var reloaded = new TrafficHistoryFile(_filePath);
        reloaded.Load();
        reloaded.Count.Should().Be(2);

        var record1 = reloaded.Find(2026, 8, 1, "Ethernet");
        record1.Should().NotBeNull();
        record1!.Value.DownBytes.Should().Be(1024);
        record1.Value.UpBytes.Should().Be(2048);
        record1.Value.TotalBytes.Should().Be(3072);

        var month = reloaded.GetMonth(2026, 8);
        month.Should().HaveCount(2);

        var (_, _) = reloaded.GetToday();
        var availableMonths = reloaded.GetAvailableMonths();
        availableMonths.Should().Contain("2026-08");

        var all = reloaded.GetAllRecords();
        all.Should().HaveCount(2);
    }

    [Fact]
    public void TrafficHistoryFile_ParseLegacyFormatAndDuplicates()
    {
        string jsonlContent = """
        {"date":"01-08-2026","download_bytes":500,"upload_bytes":200,"interface":"eth0","source":"test"}
        {"date":"2026-08-02","down":1000,"up":400,"interface":"eth0","source":"test"}
        {"date":"01-08-2026","down_bytes":500,"up_bytes":200,"interface":"eth0","source":"test"}

        invalid line that gets skipped
        """;

        File.WriteAllText(_filePath, jsonlContent);

        var file = new TrafficHistoryFile(_filePath);
        file.Load();

        file.Count.Should().Be(2);
    }

    [Fact]
    public void TrafficHistoryStore_SingletonWrapper_WorksAsExpected()
    {
        var file = new TrafficHistoryFile(_filePath);
        TrafficHistoryStore.Initialize(file);

        TrafficHistoryStore.SetDay(2026, 7, 15, 100, 200, "WiFi", "LocalTelemetry");
        TrafficHistoryStore.Save();

        var month = TrafficHistoryStore.GetMonth(2026, 7);
        month.Should().HaveCount(1);

        var months = TrafficHistoryStore.GetAvailableMonths();
        months.Should().Contain("2026-07");

        var all = TrafficHistoryStore.GetAll();
        all.Should().NotBeEmpty();
    }

    [Fact]
    public void DatImporter_ParsesValidDatFormat()
    {
        var file = new TrafficHistoryFile(_filePath);
        TrafficHistoryStore.Initialize(file);

        string datContent = """
        # Date        Up/Down KB
        2026/08/01    1024/2048
        2026/08/02    512/1024
        invalid date format
        """;

        int imported = DatImporter.Import(datContent);

        imported.Should().Be(2);
        var month = TrafficHistoryStore.GetMonth(2026, 8);
        month.Should().HaveCount(2);
    }

    [Fact]
    public void NetUsageLogger_RecordsSnapshotsAndRotatesDays()
    {
        var appSettings = new AppSettings();
        appSettings.NetUsage.Enabled = true;

        var historyFile = new TrafficHistoryFile(_filePath);

        using (var logger = new NetUsageLogger(appSettings, historyFile))
        {
            var snap1 = new TelemetrySnapshot
            {
                NetInterfaceName = "Ethernet",
                NetDownBps = 1_000_000,
                NetUpBps = 500_000,
                Timestamp = DateTime.UtcNow
            };

            logger.Record(snap1);

            var snap2 = new TelemetrySnapshot
            {
                NetInterfaceName = "Ethernet",
                NetDownBps = 2_000_000,
                NetUpBps = 1_000_000,
                Timestamp = DateTime.UtcNow.AddSeconds(1)
            };
            logger.Record(snap2);
            logger.FlushFinal();
        }
        historyFile.Count.Should().BeGreaterThan(0);
    }
}
