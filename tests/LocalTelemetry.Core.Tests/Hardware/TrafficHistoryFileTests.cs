using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class TrafficHistoryFileTests
{
    [Fact]
    public void TrafficHistoryFile_LoadSaveFindSetDay_FunctionsCorrectly()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"TrafficHistoryFile_{Guid.NewGuid():N}.jsonl");

        try
        {
            var history = new TrafficHistoryFile(tempFile);
            history.Load(); // file does not exist initially
            history.Count.Should().Be(0);

            history.SetDay(2026, 8, 1, 1048576, 524288, "Ethernet", "Test");
            history.SetDay(2026, 8, 2, 2097152, 1048576, "WiFi", "Test");
            history.Count.Should().Be(2);

            history.Save();

            // Load saved file in a new instance
            var loadedHistory = new TrafficHistoryFile(tempFile);
            loadedHistory.Load();
            loadedHistory.Count.Should().Be(2);

            var rec1 = loadedHistory.Find(2026, 8, 1, "Ethernet");
            rec1.HasValue.Should().BeTrue();
            rec1!.Value.DownBytes.Should().Be(1048576);
            rec1.Value.UpBytes.Should().Be(524288);
            rec1.Value.TotalBytes.Should().Be(1048576 + 524288);

            var months = loadedHistory.GetAvailableMonths();
            months.Should().Contain("2026-08");

            var monthRecords = loadedHistory.GetMonth(2026, 8);
            monthRecords.Should().HaveCount(2);

            var allRecords = loadedHistory.GetAllRecords();
            allRecords.Should().HaveCount(2);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public void TrafficHistoryFile_FilePath_ReturnsConfiguredPath()
    {
        var history = new TrafficHistoryFile(@"C:\some\path\history.jsonl");
        history.FilePath.Should().Be(@"C:\some\path\history.jsonl");
    }

    [Fact]
    public void TrafficHistoryFile_Load_AlternateKeys_BadLines_AndDuplicates()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"TrafficHistoryFile_{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllLines(tempFile,
            [
                "{\"date\":\"01-08-2026\",\"interface\":\"A\",\"down\":111,\"up\":222}",
                "{\"date\":\"02-08-2026\",\"interface\":\"B\",\"down_bytes\":333,\"up_bytes\":444}",
                "{\"date\":\"2026-08-03\",\"interface\":\"C\",\"download_bytes\":555,\"upload_bytes\":666}",
                "{\"date\":\"abcd-ef-gh\",\"interface\":\"D\",\"download_bytes\":1,\"upload_bytes\":2}",
                "{\"date\":\"01-08-2026\",\"interface\":\"A\",\"download_bytes\":999,\"upload_bytes\":1000}",
                "not json at all",
                "{\"date\":\"short\",\"interface\":\"X\",\"download_bytes\":1,\"upload_bytes\":2}",
                "{\"date\":\"04-08-2026\",\"interface\":\"E\",\"download_bytes\":777,\"upload_bytes\":888}"
            ]);

            var history = new TrafficHistoryFile(tempFile);
            history.Load();

            history.Count.Should().Be(4);
            history.Find(2026, 8, 1, "A")!.Value.DownBytes.Should().Be(111);
            history.Find(2026, 8, 1, "A")!.Value.UpBytes.Should().Be(222);
            history.Find(2026, 8, 2, "B")!.Value.DownBytes.Should().Be(333);
            history.Find(2026, 8, 3, "C")!.Value.DownBytes.Should().Be(555);
            history.Find(2026, 8, 4, "E")!.Value.DownBytes.Should().Be(777);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public void TrafficHistoryFile_Load_LockedFile_HandlesReadError()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"TrafficHistoryFile_{Guid.NewGuid():N}.jsonl");
        using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var history = new TrafficHistoryFile(tempFile);
            history.Load();
            history.Count.Should().Be(0);
        }
        try { File.Delete(tempFile); } catch { }
    }

    [Fact]
    public void TrafficHistoryFile_Save_CreatesMissingDirectory()
    {
        string subDir = Path.Combine(Path.GetTempPath(), $"TrafficHistoryFile_sub_{Guid.NewGuid():N}");
        string tempFile = Path.Combine(subDir, "nested.jsonl");
        try
        {
            var history = new TrafficHistoryFile(tempFile);
            history.SetDay(2026, 8, 1, 1, 2, "Ethernet", "Test");
            history.Save();
            File.Exists(tempFile).Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(subDir, true); } catch { }
        }
    }

    [Fact]
    public void TrafficHistoryFile_Save_DirectoryPath_HandlesWriteError()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"TrafficHistoryFile_dir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var history = new TrafficHistoryFile(dir);
            history.SetDay(2026, 8, 1, 1, 2, "Ethernet", "Test");
            var act = () => history.Save();
            act.Should().NotThrow();
        }
        finally
        {
            try { Directory.Delete(dir); } catch { }
        }
    }

    [Fact]
    public void TrafficHistoryFile_Load_NormalizesLegacySources()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"TrafficHistoryFile_Sources_{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllLines(tempFile,
            [
                "{\"date\":\"01-08-2026\",\"interface\":\"Ethernet\",\"down\":100,\"up\":200,\"source\":\"imported\"}",
                "{\"date\":\"02-08-2026\",\"interface\":\"WiFi\",\"down\":300,\"up\":400,\"source\":\"srum\"}",
                "{\"date\":\"03-08-2026\",\"interface\":\"Dat File\",\"down\":500,\"up\":600,\"source\":\"import\"}",
                "{\"date\":\"04-08-2026\",\"interface\":\"Dat File\",\"down\":700,\"up\":800,\"source\":\"dat\"}",
                "{\"date\":\"05-08-2026\",\"interface\":\"Ethernet\",\"down\":900,\"up\":1000,\"source\":\"WindowsImport\"}",
                "{\"date\":\"06-08-2026\",\"interface\":\"Dat File\",\"down\":1100,\"up\":1200,\"source\":\"DatImport\"}"
            ]);

            var history = new TrafficHistoryFile(tempFile);
            history.Load();

            history.Find(2026, 8, 1, "Ethernet")!.Value.Source.Should().Be("WindowsImport");
            history.Find(2026, 8, 2, "WiFi")!.Value.Source.Should().Be("WindowsImport");
            history.Find(2026, 8, 3, "Dat File")!.Value.Source.Should().Be("DatImport");
            history.Find(2026, 8, 4, "Dat File")!.Value.Source.Should().Be("DatImport");
            history.Find(2026, 8, 5, "Ethernet")!.Value.Source.Should().Be("WindowsImport");
            history.Find(2026, 8, 6, "Dat File")!.Value.Source.Should().Be("DatImport");
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }
}
