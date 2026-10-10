using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

[Collection("TrafficHistory")]
public class DatImporterTests
{
    [Fact]
    public void DatImporter_Import_HandlesEmptyAndInvalidHeader()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"DatImporter_{Guid.NewGuid():N}.jsonl");
        try
        {
            var history = new TrafficHistoryFile(tempFile);
            TrafficHistoryStore.Initialize(history);

            DatImporter.Import("").Should().Be(0);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public void DatImporter_Import_ParsesValidAndSkipsMalformedLines()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"DatImporter_{Guid.NewGuid():N}.jsonl");
        try
        {
            var history = new TrafficHistoryFile(tempFile);
            TrafficHistoryStore.Initialize(history);

            string content = @"Date Upload/Download KB
2026/08/01 1024/2048
invalid line
2026-08-02 100/200
2026/08 100/200
1999/01/01 100/200
2026/08/03 no_slash_here
2026/08/04 invalid_up/200
2026/08/05 100/invalid_down
2026/08/06 512/1024
";

            int imported = DatImporter.Import(content);
            imported.Should().Be(2);

            var monthRecords = TrafficHistoryStore.GetMonth(2026, 8);
            monthRecords.Should().HaveCount(2);

            var rec1 = monthRecords.FirstOrDefault(r => r.Day == 1);
            rec1.UpBytes.Should().Be(1024 * 1024);
            rec1.DownBytes.Should().Be(2048 * 1024);

            var rec2 = monthRecords.FirstOrDefault(r => r.Day == 6);
            rec2.UpBytes.Should().Be(512 * 1024);
            rec2.DownBytes.Should().Be(1024 * 1024);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }
}
