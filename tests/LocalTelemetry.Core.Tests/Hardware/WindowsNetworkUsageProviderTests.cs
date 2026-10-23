using System.Globalization;
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
        var method = typeof(WindowsNetworkUsageProvider).GetMethod("ResolveAdapterInterfaceName", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        // Non-existent GUID tests fallback logic
        string fakeGuid = Guid.NewGuid().ToString();
        ((string)method.Invoke(null, [fakeGuid, 6u])!).Should().Be("Ethernet");
        ((string)method.Invoke(null, [fakeGuid, 71u])!).Should().Be("WiFi");
        ((string)method.Invoke(null, [fakeGuid, 243u])!).Should().Be("WiFi");
        ((string)method.Invoke(null, [fakeGuid, 117u])!).Should().Be("IanaType117");
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_ImportDateRangeAsync_And_MergeHistoricalRecord()
    {
        var mergeRecord = typeof(WindowsNetworkUsageProvider).GetMethod("MergeHistoricalRecord", BindingFlags.NonPublic | BindingFlags.Static);

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
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_Logging_InstallDate_And_ImportFlows()
    {
        // 1. GetWindowsInstallDate
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
            var taskEmpty = (Task<bool>)importToday.Invoke(null, [historyFile, DateTime.Today, CancellationToken.None])!;
            await taskEmpty;

            // With cancellation token to hit cancelled path
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            await WindowsNetworkUsageProvider.ImportAndSaveHistoryAsync(historyFile, cts.Token);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_RefreshTodayUsageAsync_ExecutesGracefully()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficHistory_Refresh_{Guid.NewGuid():N}.json");
        try
        {
            var historyFile = new TrafficHistoryFile(tempPath);
            var act = async () => await WindowsNetworkUsageProvider.RefreshTodayUsageAsync(historyFile);
            await act.Should().NotThrowAsync();

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            var actCanceled = async () => await WindowsNetworkUsageProvider.RefreshTodayUsageAsync(historyFile, cts.Token);
            await actCanceled.Should().NotThrowAsync();
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
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
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
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

            var task = (Task<int>)importHist.Invoke(null, [historyFile, start, end, today, CancellationToken.None])!;
            int count = await task;
            count.Should().BeGreaterThanOrEqualTo(0);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
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
        await cts.CancelAsync();
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
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public void WindowsNetworkUsageProvider_InstallDate_Parsers_Coverage()
    {
        var parseTimeMethod = typeof(WindowsNetworkUsageProvider).GetMethod(
            "TryParseInstallTime", BindingFlags.NonPublic | BindingFlags.Static);
        parseTimeMethod.Should().NotBeNull();

        // 1. Valid long and invalid byte[] or null
        ((DateTime?)parseTimeMethod.Invoke(null, [null])).Should().BeNull();
        ((DateTime?)parseTimeMethod.Invoke(null, ["invalid string"])).Should().BeNull();
        ((DateTime?)parseTimeMethod.Invoke(null, [new byte[] { 1, 2, 3 }])).Should().BeNull();

        long validFileTime = DateTime.UtcNow.AddDays(-10).ToFileTimeUtc();
        ((DateTime?)parseTimeMethod.Invoke(null, [validFileTime])).Should().NotBeNull();

        // 2. TryParseInstallDate
        var parseDateMethod = typeof(WindowsNetworkUsageProvider).GetMethod(
            "TryParseInstallDate", BindingFlags.NonPublic | BindingFlags.Static);
        parseDateMethod.Should().NotBeNull();

        ((DateTime?)parseDateMethod.Invoke(null, [null])).Should().BeNull();
        ((DateTime?)parseDateMethod.Invoke(null, [-5])).Should().BeNull();
        ((DateTime?)parseDateMethod.Invoke(null, [0])).Should().BeNull();
        ((DateTime?)parseDateMethod.Invoke(null, [(long)-10])).Should().BeNull();

        long validSeconds = DateTimeOffset.UtcNow.AddDays(-20).ToUnixTimeSeconds();
        ((DateTime?)parseDateMethod.Invoke(null, [(int)validSeconds])).Should().NotBeNull();
        ((DateTime?)parseDateMethod.Invoke(null, [validSeconds])).Should().NotBeNull();

        // 3. Out-of-range dates (before 2000 or in future)
        long oldFileTime = new DateTime(1995, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        ((DateTime?)parseTimeMethod.Invoke(null, [oldFileTime])).Should().BeNull();

        long futureFileTime = DateTime.UtcNow.AddDays(30).ToFileTimeUtc();
        ((DateTime?)parseTimeMethod.Invoke(null, [futureFileTime])).Should().BeNull();

        long oldSeconds = new DateTimeOffset(1995, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        ((DateTime?)parseDateMethod.Invoke(null, [oldSeconds])).Should().BeNull();

        long futureSeconds = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        ((DateTime?)parseDateMethod.Invoke(null, [futureSeconds])).Should().BeNull();
    }

    [Fact]
    public void WindowsNetworkUsageProvider_ClampImportRange_ValidatesBoundaries()
    {
        var clampMethod = typeof(WindowsNetworkUsageProvider).GetMethod(
            "ClampImportRange", BindingFlags.NonPublic | BindingFlags.Static);
        clampMethod.Should().NotBeNull();

        DateTime today = DateTime.Today;
        DateTime past100Days = today.AddDays(-100);
        DateTime future10Days = today.AddDays(10);

        var clampedObj = clampMethod.Invoke(null, [past100Days, future10Days]);
        clampedObj.Should().NotBeNull();
        var tuple = ((DateTime Start, DateTime End))clampedObj;

        tuple.Start.Should().Be(today.AddDays(-60));
        tuple.End.Should().Be(today);

        DateTime inRangeStart = today.AddDays(-20);
        DateTime inRangeEnd = today.AddDays(-5);
        var inRangeObj = clampMethod.Invoke(null, [inRangeStart, inRangeEnd]);
        var inRangeTuple = ((DateTime Start, DateTime End))inRangeObj!;

        inRangeTuple.Start.Should().Be(inRangeStart);
        inRangeTuple.End.Should().Be(inRangeEnd);
    }

    [Fact]
    public void WindowsNetworkUsageProvider_MergeHistoricalRecord_InvalidDateFormats()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficInvalid_{Guid.NewGuid():N}.dat");
        try
        {
            var historyFile = new TrafficHistoryFile(tempPath);
            var mergeRecord = typeof(WindowsNetworkUsageProvider).GetMethod(
                "MergeHistoricalRecord", BindingFlags.NonPublic | BindingFlags.Static)!;

            // Non-three-part strings
            var twoParts = new WindowsNetworkUsageRecord { Date = "01-08", InterfaceType = "Ethernet" };
            ((bool)mergeRecord.Invoke(null, [historyFile, twoParts, DateTime.Today])!).Should().BeFalse();

            var fourParts = new WindowsNetworkUsageRecord { Date = "01-08-2026-00", InterfaceType = "Ethernet" };
            ((bool)mergeRecord.Invoke(null, [historyFile, fourParts, DateTime.Today])!).Should().BeFalse();

            // Non-numeric tokens
            var badDay = new WindowsNetworkUsageRecord { Date = "xx-08-2026", InterfaceType = "Ethernet" };
            ((bool)mergeRecord.Invoke(null, [historyFile, badDay, DateTime.Today])!).Should().BeFalse();

            var badMonth = new WindowsNetworkUsageRecord { Date = "01-yy-2026", InterfaceType = "Ethernet" };
            ((bool)mergeRecord.Invoke(null, [historyFile, badMonth, DateTime.Today])!).Should().BeFalse();

            var badYear = new WindowsNetworkUsageRecord { Date = "01-08-zzzz", InterfaceType = "Ethernet" };
            ((bool)mergeRecord.Invoke(null, [historyFile, badYear, DateTime.Today])!).Should().BeFalse();
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public void WindowsNetworkUsageProvider_MergeRecords_FiltersZeroTraffic_And_Aggregates()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficMerge_{Guid.NewGuid():N}.dat");
        try
        {
            var historyFile = new TrafficHistoryFile(tempPath);
            var mergeRecords = typeof(WindowsNetworkUsageProvider).GetMethod(
                "MergeRecords", BindingFlags.NonPublic | BindingFlags.Static);
            mergeRecords.Should().NotBeNull();

            DateTime yesterday = DateTime.Today.AddDays(-1);
            DateTime twoDaysAgo = DateTime.Today.AddDays(-2);

            var zeroTraffic = new WindowsNetworkUsageRecord { Date = yesterday.ToString("dd-MM-yyyy"), InterfaceType = "Ethernet", DownBytes = 0, UpBytes = 0 };
            var validTraffic1 = new WindowsNetworkUsageRecord { Date = yesterday.ToString("dd-MM-yyyy"), InterfaceType = "Ethernet", DownBytes = 4000, UpBytes = 2000 };
            var validTraffic2 = new WindowsNetworkUsageRecord { Date = twoDaysAgo.ToString("dd-MM-yyyy"), InterfaceType = "WiFi", DownBytes = 6000, UpBytes = 3000 };
            var invalidDateTraffic = new WindowsNetworkUsageRecord { Date = "invalid-date", InterfaceType = "Ethernet", DownBytes = 500, UpBytes = 250 };

            var uniqueDays = new HashSet<string>();
            var resultObj = mergeRecords.Invoke(null, [historyFile, new List<WindowsNetworkUsageRecord> { zeroTraffic, validTraffic1, validTraffic2, invalidDateTraffic }, uniqueDays]);
            resultObj.Should().NotBeNull();

            var tuple = ((long TotalDown, long TotalUp))resultObj;
            tuple.TotalDown.Should().Be(10000);
            tuple.TotalUp.Should().Be(5000);
            uniqueDays.Should().HaveCount(2);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public void WindowsNetworkUsageProvider_ResolveAdapterInterfaceName_Coverage()
    {
        string unknownGuid = Guid.NewGuid().ToString("D");
        WindowsNetworkUsageProvider.ResolveAdapterInterfaceName(unknownGuid, 6).Should().Be("Ethernet");
        WindowsNetworkUsageProvider.ResolveAdapterInterfaceName(unknownGuid, 71).Should().Be("WiFi");
        WindowsNetworkUsageProvider.ResolveAdapterInterfaceName(unknownGuid, 243).Should().Be("WiFi");
        WindowsNetworkUsageProvider.ResolveAdapterInterfaceName(unknownGuid, 999).Should().Be("IanaType999");

        // Real interface lookup if any present
        var firstNic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault();
        if (firstNic != null && !string.IsNullOrEmpty(firstNic.Name))
        {
            string cleanId = firstNic.Id?.Trim("{}".ToCharArray()) ?? string.Empty;
            WindowsNetworkUsageProvider.ResolveAdapterInterfaceName(cleanId, 6).Should().Be(firstNic.Name);
        }
    }

    [Fact]
    public void WindowsNetworkUsageProvider_TryParseInstallTime_And_TryParseInstallDate_Coverage()
    {
        var parseTime = typeof(WindowsNetworkUsageProvider).GetMethod(
            "TryParseInstallTime", BindingFlags.NonPublic | BindingFlags.Static)!;
        var parseDate = typeof(WindowsNetworkUsageProvider).GetMethod(
            "TryParseInstallDate", BindingFlags.NonPublic | BindingFlags.Static)!;

        // TryParseInstallTime
        parseTime.Invoke(null, [null]).Should().BeNull();
        parseTime.Invoke(null, ["invalid"]).Should().BeNull();
        parseTime.Invoke(null, [-1L]).Should().BeNull();
        parseTime.Invoke(null, [0L]).Should().BeNull();
        parseTime.Invoke(null, [new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc()]).Should().BeNull();
        parseTime.Invoke(null, [DateTime.UtcNow.AddYears(2).ToFileTimeUtc()]).Should().BeNull();

        var validTime = DateTime.UtcNow.AddYears(-1);
        var resTime = (DateTime?)parseTime.Invoke(null, [validTime.ToFileTimeUtc()]);
        resTime.Should().NotBeNull();
        resTime.Value.Year.Should().Be(validTime.Year);

        // TryParseInstallDate
        parseDate.Invoke(null, [null]).Should().BeNull();
        parseDate.Invoke(null, ["invalid"]).Should().BeNull();
        parseDate.Invoke(null, [-1]).Should().BeNull();
        parseDate.Invoke(null, [0]).Should().BeNull();
        parseDate.Invoke(null, [(int)DateTimeOffset.Parse("1990-01-01", CultureInfo.InvariantCulture).ToUnixTimeSeconds()]).Should().BeNull();
        parseDate.Invoke(null, [DateTimeOffset.UtcNow.AddYears(2).ToUnixTimeSeconds()]).Should().BeNull();

        var validSecondsInt = (int)DateTimeOffset.UtcNow.AddYears(-1).ToUnixTimeSeconds();
        var resDateInt = (DateTime?)parseDate.Invoke(null, [validSecondsInt]);
        resDateInt.Should().NotBeNull();
        resDateInt.Value.Year.Should().Be(validTime.Year);

        var validSecondsLong = DateTimeOffset.UtcNow.AddYears(-1).ToUnixTimeSeconds();
        var resDateLong = (DateTime?)parseDate.Invoke(null, [validSecondsLong]);
        resDateLong.Should().NotBeNull();
        resDateLong.Value.Year.Should().Be(validTime.Year);
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_ImportDateRangeAsync_DetailedOutcomes_Coverage()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficDetail_{Guid.NewGuid():N}.json");
        try
        {
            var historyFile = new TrafficHistoryFile(tempPath);
            DateTime startDate = DateTime.Today.AddDays(-5);
            DateTime endDate = DateTime.Today.AddDays(-1);

            // Import past range
            var result = await WindowsNetworkUsageProvider.ImportDateRangeAsync(historyFile, startDate, endDate);
            result.Success.Should().BeTrue();
            result.DaysImported.Should().BeGreaterThanOrEqualTo(0);

            // Re-import same range -> days imported should be 0 and message indicates up-to-date or no activity
            var reimport = await WindowsNetworkUsageProvider.ImportDateRangeAsync(historyFile, startDate, endDate);
            reimport.Success.Should().BeTrue();
            reimport.Message.Should().NotBeNullOrEmpty();
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_ImportHistoricalRecords_And_Baseline_DetailedCoverage()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficHistBase_{Guid.NewGuid():N}.json");
        try
        {
            var historyFile = new TrafficHistoryFile(tempPath);
            var importHist = typeof(WindowsNetworkUsageProvider).GetMethod("ImportHistoricalRecordsAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

            // Import across valid past date range
            DateTime start = DateTime.Today.AddDays(-4);
            DateTime end = DateTime.Today.AddDays(-1);
            var task = (Task<int>)importHist.Invoke(null, [historyFile, start, end, DateTime.Today, CancellationToken.None])!;
            int added = await task;
            added.Should().BeGreaterThanOrEqualTo(0);

            // Import today baseline
            var importToday = typeof(WindowsNetworkUsageProvider).GetMethod("ImportTodayBaselineAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
            var todayTask = (Task<bool>)importToday.Invoke(null, [historyFile, DateTime.Today, CancellationToken.None])!;
            await todayTask;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_ImportAndSaveHistoryAsync_EmptyHistory_And_Cancellation()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficImportSave_{Guid.NewGuid():N}.json");
        try
        {
            var historyFile = new TrafficHistoryFile(tempPath);

            // 1. Initial run without historical data
            var act = async () => await WindowsNetworkUsageProvider.ImportAndSaveHistoryAsync(historyFile, CancellationToken.None);
            await act.Should().NotThrowAsync();

            // 2. Cancellation handling
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var actCancel = async () => await WindowsNetworkUsageProvider.ImportAndSaveHistoryAsync(historyFile, cts.Token);
            await actCancel.Should().NotThrowAsync();
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_RefreshTodayUsageAsync_Coverage()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"TrafficRefresh_{Guid.NewGuid():N}.json");
        try
        {
            var historyFile = new TrafficHistoryFile(tempPath);

            // Normal refresh
            var act = async () => await WindowsNetworkUsageProvider.RefreshTodayUsageAsync(historyFile, CancellationToken.None);
            await act.Should().NotThrowAsync();

            // Cancelled refresh
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var actCancel = async () => await WindowsNetworkUsageProvider.RefreshTodayUsageAsync(historyFile, cts.Token);
            await actCancel.Should().NotThrowAsync();
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }

    [Fact]
    public async Task WindowsNetworkUsageProvider_GetUsageAsync_CancellationAndPastDate_Coverage()
    {
        // 1. Single-day query
        var singleDay = await WindowsNetworkUsageProvider.GetUsageAsync(DateTime.Today, DateTime.Today, CancellationToken.None);
        singleDay.Should().NotBeNull();

        // 2. Multi-day historical chunks query
        var multiDay = await WindowsNetworkUsageProvider.GetUsageAsync(DateTime.Today.AddDays(-3), DateTime.Today, CancellationToken.None);
        multiDay.Should().NotBeNull();

        // 3. Pre-cancelled query handles cancellation safely
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelAct = async () => await WindowsNetworkUsageProvider.GetUsageAsync(DateTime.Today.AddDays(-10), DateTime.Today, cts.Token);
        await cancelAct.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void WindowsNetworkUsageProvider_TryParseInstallTime_And_InstallDate_EdgeCases()
    {
        var parseInstallTime = typeof(WindowsNetworkUsageProvider).GetMethod("TryParseInstallTime", BindingFlags.NonPublic | BindingFlags.Static);
        var parseInstallDate = typeof(WindowsNetworkUsageProvider).GetMethod("TryParseInstallDate", BindingFlags.NonPublic | BindingFlags.Static);

        // TryParseInstallTime
        ((DateTime?)parseInstallTime!.Invoke(null, [-1L])).Should().BeNull();
        ((DateTime?)parseInstallTime.Invoke(null, [0L])).Should().BeNull();
        ((DateTime?)parseInstallTime.Invoke(null, ["invalid"])).Should().BeNull();
        // FileTime for 1995 (year < 2000)
        long fileTime1995 = new DateTime(1995, 1, 1).ToFileTimeUtc();
        ((DateTime?)parseInstallTime.Invoke(null, [fileTime1995])).Should().BeNull();
        // Valid FileTime for 2021
        long fileTime2021 = new DateTime(2021, 5, 1).ToFileTimeUtc();
        ((DateTime?)parseInstallTime.Invoke(null, [fileTime2021])).Should().Be(new DateTime(2021, 5, 1));

        // TryParseInstallDate
        ((DateTime?)parseInstallDate!.Invoke(null, [-5])).Should().BeNull();
        ((DateTime?)parseInstallDate.Invoke(null, [0])).Should().BeNull();
        ((DateTime?)parseInstallDate.Invoke(null, [-5L])).Should().BeNull();
        ((DateTime?)parseInstallDate.Invoke(null, [0L])).Should().BeNull();
        ((DateTime?)parseInstallDate.Invoke(null, ["not-a-number"])).Should().BeNull();
        // Unix timestamp for 1990
        ((DateTime?)parseInstallDate.Invoke(null, [631152000])).Should().BeNull();
        // Unix timestamp for 2022-01-01
        long unix2022 = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        ((DateTime?)parseInstallDate.Invoke(null, [unix2022])).Should().Be(new DateTime(2022, 1, 1));
        ((DateTime?)parseInstallDate.Invoke(null, [(int)unix2022])).Should().Be(new DateTime(2022, 1, 1));
    }

    [Fact]
    public void WindowsNetworkUsageProvider_ClampImportRange_EdgeCases()
    {
        var clampMethod = typeof(WindowsNetworkUsageProvider).GetMethod("ClampImportRange", BindingFlags.NonPublic | BindingFlags.Static);
        clampMethod.Should().NotBeNull();

        // 1. Future end date gets clamped to DateTime.Today
        var futureEnd = DateTime.Today.AddDays(5);
        var resFuture = clampMethod!.Invoke(null, [DateTime.Today.AddDays(-10), futureEnd]);
        var (start1, end1) = ((DateTime, DateTime))resFuture!;
        end1.Should().Be(DateTime.Today);

        // 2. Start date earlier than 60 days ago gets clamped
        var oldStart = DateTime.Today.AddDays(-100);
        var resOld = clampMethod.Invoke(null, [oldStart, DateTime.Today.AddDays(-5)]);
        var (start2, end2) = ((DateTime, DateTime))resOld!;
        start2.Should().Be(DateTime.Today.AddDays(-60));

        // 3. Normal range inside bounds stays unchanged
        var normalStart = DateTime.Today.AddDays(-15);
        var normalEnd = DateTime.Today.AddDays(-2);
        var resNormal = clampMethod.Invoke(null, [normalStart, normalEnd]);
        var (start3, end3) = ((DateTime, DateTime))resNormal!;
        start3.Should().Be(normalStart);
        end3.Should().Be(normalEnd);
    }
}
