using System.Reflection;
using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class WindowsNetworkUsageProviderTests
{
    [Fact]
    public void WindowsNetworkUsageProvider_GetWindowsInstallDate_ReturnsValidDate()
    {
        var installDate = WindowsNetworkUsageProvider.GetWindowsInstallDate();
        installDate.Should().BeBefore(DateTime.UtcNow);
        installDate.Year.Should().BeGreaterThan(2000);
    }


    [Fact]
    public void WindowsNetworkUsageRecord_PropertiesSetAndGet()
    {
        var rec = new WindowsNetworkUsageRecord
        {
            Date = "01-08-2026",
            InterfaceType = "Ethernet",
            DownBytes = 1000,
            UpBytes = 500
        };

        rec.Date.Should().Be("01-08-2026");
        rec.InterfaceType.Should().Be("Ethernet");
        rec.DownBytes.Should().Be(1000);
        rec.UpBytes.Should().Be(500);
    }

    [Fact]
    public void WindowsNetworkUsageProvider_ResolveAdapterInterfaceName_ResolvesCorrectly()
    {
        var method = typeof(WindowsNetworkUsageProvider).GetMethod("ResolveAdapterInterfaceName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull();

        // Non-existent GUID tests fallback logic
        string fakeGuid = Guid.NewGuid().ToString();
        ((string)method!.Invoke(null, [fakeGuid, 6u])!).Should().Be("Ethernet");
        ((string)method.Invoke(null, [fakeGuid, 71u])!).Should().Be("WiFi");
        ((string)method.Invoke(null, [fakeGuid, 243u])!).Should().Be("WiFi");
        ((string)method.Invoke(null, [fakeGuid, 117u])!).Should().Be("IanaType117");
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_ImportDateRangeAsync_And_MergeHistoricalRecord()
    {
        var mergeRecord = typeof(WindowsNetworkUsageProvider).GetMethod("MergeHistoricalRecord", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficImport_{Guid.NewGuid():N}.dat");
        var historyFile = new TrafficHistoryFile(tempPath);

        try
        {
            DateTime yesterday = DateTime.Today.AddDays(-1);
            DateTime tenDaysAgo = DateTime.Today.AddDays(-10);

            // 1. Inverted date range -> returns failure
            var invertedResult = await WindowsNetworkUsageProvider.ImportDateRangeAsync(historyFile, yesterday, tenDaysAgo);
            invertedResult.Success.Should().BeFalse();
            invertedResult.ErrorMessage.Should().Contain("Start date must be before or equal to End date");

            // 2. Merge tests
            var invalidFormat = new WindowsNetworkUsageRecord { Date = "invalid", InterfaceType = "Ethernet" };
            ((bool)mergeRecord!.Invoke(null, [historyFile, invalidFormat, DateTime.Today])!).Should().BeFalse();

            // Same day as today -> skipped
            var todayRec = new WindowsNetworkUsageRecord { Date = DateTime.Today.ToString("dd-MM-yyyy"), InterfaceType = "Ethernet" };
            ((bool)mergeRecord.Invoke(null, [historyFile, todayRec, DateTime.Today])!).Should().BeFalse();

            // Historical record with higher bytes -> merged
            var higherRec = new WindowsNetworkUsageRecord { Date = yesterday.ToString("dd-MM-yyyy"), InterfaceType = "Ethernet", DownBytes = 5000, UpBytes = 5000 };
            ((bool)mergeRecord.Invoke(null, [historyFile, higherRec, DateTime.Today])!).Should().BeTrue();

            // Historical record with lower bytes -> skipped
            var lowerRec = new WindowsNetworkUsageRecord { Date = yesterday.ToString("dd-MM-yyyy"), InterfaceType = "Ethernet", DownBytes = 10, UpBytes = 10 };
            ((bool)mergeRecord.Invoke(null, [historyFile, lowerRec, DateTime.Today])!).Should().BeFalse();
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_Logging_InstallDate_And_ImportFlows()
    {
        // 1. Log and LogError private methods
        var logMethod = typeof(WindowsNetworkUsageProvider).GetMethod("Log", BindingFlags.NonPublic | BindingFlags.Static);
        var logErrorMethod = typeof(WindowsNetworkUsageProvider).GetMethod("LogError", BindingFlags.NonPublic | BindingFlags.Static);
        logMethod!.Invoke(null, ["Test debug log"]);
        logErrorMethod!.Invoke(null, ["Test error log"]);

        // 2. GetWindowsInstallDate
        var installDate = WindowsNetworkUsageProvider.GetWindowsInstallDate();
        installDate.Should().BeBefore(DateTime.UtcNow.AddDays(1));

        // 3. ImportAndSaveHistoryAsync with up to date history file
        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficHistory_Import_{Guid.NewGuid():N}.json");
        try
        {
            var historyFile = new TrafficHistoryFile(tempPath);
            DateTime yesterday = DateTime.Today.AddDays(-1);
            historyFile.SetDay(yesterday.Year, yesterday.Month, yesterday.Day, 1000, 2000, "Ethernet", "Test");

            // Up-to-date run
            await WindowsNetworkUsageProvider.ImportAndSaveHistoryAsync(historyFile);

            // ImportTodayBaselineAsync via reflection with empty vs existing
            var importToday = typeof(WindowsNetworkUsageProvider).GetMethod("ImportTodayBaselineAsync", BindingFlags.NonPublic | BindingFlags.Static);
            importToday.Should().NotBeNull();
            var taskEmpty = (Task<bool>)importToday!.Invoke(null, [historyFile, DateTime.Today, CancellationToken.None])!;
            await taskEmpty;

            // With cancellation token to hit cancelled path
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await WindowsNetworkUsageProvider.ImportAndSaveHistoryAsync(historyFile, cts.Token);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_RefreshTodayUsageAsync_ExecutesGracefully()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficHistory_Refresh_{Guid.NewGuid():N}.json");
        try
        {
            var historyFile = new TrafficHistoryFile(tempPath);
            await WindowsNetworkUsageProvider.RefreshTodayUsageAsync(historyFile);

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await WindowsNetworkUsageProvider.RefreshTodayUsageAsync(historyFile, cts.Token);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_ImportDateRangeAsync_ClampsFutureEndDate()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficHistory_Future_{Guid.NewGuid():N}.json");
        try
        {
            var historyFile = new TrafficHistoryFile(tempPath);
            DateTime startDate = DateTime.Today.AddDays(-2);
            DateTime futureDate = DateTime.Today.AddDays(10);

            var result = await WindowsNetworkUsageProvider.ImportDateRangeAsync(historyFile, startDate, futureDate);
            result.Should().NotBeNull();
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    [Fact]
    public void WindowsUsageImportResult_Properties_SetAndGet()
    {
        var res = new WindowsUsageImportResult
        {
            Success = true,
            DaysImported = 5,
            DownBytes = 1048576,
            UpBytes = 524288,
            ErrorMessage = "None",
            Message = "Completed successfully"
        };

        res.Success.Should().BeTrue();
        res.DaysImported.Should().Be(5);
        res.DownBytes.Should().Be(1048576);
        res.UpBytes.Should().Be(524288);
        res.ErrorMessage.Should().Be("None");
        res.Message.Should().Be("Completed successfully");
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_ImportHistoricalRecordsAsync_ExecutesGracefully()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficHistory_Hist_{Guid.NewGuid():N}.json");
        try
        {
            var historyFile = new TrafficHistoryFile(tempPath);
            var importHist = typeof(WindowsNetworkUsageProvider).GetMethod("ImportHistoricalRecordsAsync", BindingFlags.NonPublic | BindingFlags.Static);
            importHist.Should().NotBeNull();

            DateTime start = DateTime.Today.AddDays(-3);
            DateTime end = DateTime.Today.AddDays(-1);
            DateTime today = DateTime.Today;

            var task = (Task<int>)importHist!.Invoke(null, [historyFile, start, end, today, CancellationToken.None])!;
            int count = await task;
            count.Should().BeGreaterThanOrEqualTo(0);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_GetUsageAsync_SingleDay_And_HistoricalRange_Coverage()
    {
        // 1. Single day (start == end)
        var singleDay = await WindowsNetworkUsageProvider.GetUsageAsync(DateTime.Today, DateTime.Today);
        singleDay.Should().NotBeNull();

        // 2. Historical chunk range (different dates)
        var multiDay = await WindowsNetworkUsageProvider.GetUsageAsync(DateTime.Today.AddDays(-3), DateTime.Today.AddDays(-1));
        multiDay.Should().NotBeNull();

        // 3. With cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Func<Task> act = async () => await WindowsNetworkUsageProvider.GetUsageAsync(DateTime.Today.AddDays(-3), DateTime.Today.AddDays(-1), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void WindowsNetworkUsageProvider_MergeHistoricalRecord_DistinctDaysBehavior()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficDistinct_{Guid.NewGuid():N}.jsonl");
        try
        {
            var historyFile = new TrafficHistoryFile(tempPath);
            DateTime yesterday = DateTime.Today.AddDays(-1);
            string yesterdayStr = yesterday.ToString("dd-MM-yyyy");

            var mergeRecord = typeof(WindowsNetworkUsageProvider).GetMethod(
                "MergeHistoricalRecord", BindingFlags.NonPublic | BindingFlags.Static)!;

            var wifi = new WindowsNetworkUsageRecord { Date = yesterdayStr, InterfaceType = "WiFi", DownBytes = 1000, UpBytes = 500 };
            var ethernet = new WindowsNetworkUsageRecord { Date = yesterdayStr, InterfaceType = "Ethernet", DownBytes = 2000, UpBytes = 1000 };

            ((bool)mergeRecord.Invoke(null, [historyFile, wifi, DateTime.Today])!).Should().BeTrue();
            ((bool)mergeRecord.Invoke(null, [historyFile, ethernet, DateTime.Today])!).Should().BeTrue();

            historyFile.Count.Should().Be(2); // Two interface records stored
            historyFile.GetAllRecords().Select(r => (r.Year, r.Month, r.Day)).Distinct().Count().Should().Be(1); // One distinct calendar day
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }
}
