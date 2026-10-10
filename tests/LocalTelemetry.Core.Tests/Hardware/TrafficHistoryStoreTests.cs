using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

[Collection("TrafficHistory")]
public class TrafficHistoryStoreTests
{
    [Fact]
    public void TrafficHistoryStore_InitializeAndDelegates()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"TrafficHistoryStore_{Guid.NewGuid():N}.jsonl");

        lock (typeof(TrafficHistoryStore))
        {
            try
            {
                var history = new TrafficHistoryFile(tempFile);
                TrafficHistoryStore.Initialize(history);

                int nowYear = DateTime.Now.Year;
                int nowMonth = DateTime.Now.Month;
                int nowDay = DateTime.Now.Day;

                TrafficHistoryStore.SetDay(nowYear, nowMonth, nowDay, 1000, 2000, "Ethernet", "StoreTest");
                var today = TrafficHistoryStore.GetToday();
                today.Should().NotBeNull();

                var month = TrafficHistoryStore.GetMonth(nowYear, nowMonth);
                month.Should().NotBeEmpty();

                var months = TrafficHistoryStore.GetAvailableMonths();
                months.Should().NotBeEmpty();

                var all = TrafficHistoryStore.GetAll();
                all.Should().NotBeEmpty();

                TrafficHistoryStore.Save();
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }
}
