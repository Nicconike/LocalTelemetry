using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

[Collection("TrafficHistory")]
public class JsonlTrafficImporterTests
{
    [Fact]
    public void JsonlTrafficImporter_Import_HandlesEmptyAndInvalidLines()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"JsonlImporter_{Guid.NewGuid():N}.jsonl");
        try
        {
            var history = new TrafficHistoryFile(tempFile);
            TrafficHistoryStore.Initialize(history);

            JsonlTrafficImporter.Import("").Should().Be(0);
            JsonlTrafficImporter.Import("   ").Should().Be(0);
            JsonlTrafficImporter.Import("not json\n{invalid json}").Should().Be(0);
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public void JsonlTrafficImporter_Import_ParsesStandardAndAlternativeFormat()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"JsonlImporter_{Guid.NewGuid():N}.jsonl");
        try
        {
            var history = new TrafficHistoryFile(tempFile);
            TrafficHistoryStore.Initialize(history);

            string content = """
                {"date":"15-08-2026","download_bytes":2097152,"upload_bytes":1048576,"total_bytes":3145728,"interface":"Wi-Fi","source":"LocalTelemetry"}
                {"date":"2026-08-16","down":4194304,"up":2097152,"interface":"Ethernet"}
                {"date":"17/08/2026","down_bytes":1000,"up_bytes":2000}
                {"date":"invalid-date","download_bytes":500,"upload_bytes":500}
                """;

            int imported = JsonlTrafficImporter.Import(content);
            imported.Should().Be(3);

            var monthRecords = TrafficHistoryStore.GetMonth(2026, 8);
            monthRecords.Should().HaveCount(3);

            var rec15 = monthRecords.FirstOrDefault(r => r.Day == 15);
            rec15.DownBytes.Should().Be(2097152);
            rec15.UpBytes.Should().Be(1048576);
            rec15.Interface.Should().Be("Wi-Fi");

            var rec16 = monthRecords.FirstOrDefault(r => r.Day == 16);
            rec16.DownBytes.Should().Be(4194304);
            rec16.UpBytes.Should().Be(2097152);
            rec16.Interface.Should().Be("Ethernet");

            var rec17 = monthRecords.FirstOrDefault(r => r.Day == 17);
            rec17.DownBytes.Should().Be(1000);
            rec17.UpBytes.Should().Be(2000);
            rec17.Interface.Should().Be("Backup");
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public void JsonlTrafficImporter_Import_HandlesInvalidFormatsAndDateBoundaries()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"JsonlImporter_{Guid.NewGuid():N}.jsonl");
        try
        {
            var history = new TrafficHistoryFile(tempFile);
            TrafficHistoryStore.Initialize(history);

            string content = """
                [1, 2, 3]
                {"other":"value"}
                {"date":"short"}
                {"date":"12-xx-2026","download_bytes":100,"upload_bytes":100}
                {"date":"2026-xx-12","download_bytes":100,"upload_bytes":100}
                {"date":"15-08-1990","download_bytes":100,"upload_bytes":100}
                {"date":"2026-08-15","download_bytes":12.34,"upload_bytes":100}
                {"date":"2026-08-20"}
                """;

            int imported = JsonlTrafficImporter.Import(content);
            imported.Should().Be(1);

            var monthRecords = TrafficHistoryStore.GetMonth(2026, 8);
            monthRecords.Should().ContainSingle(r => r.Day == 20 && r.DownBytes == 0 && r.UpBytes == 0);
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }
}
