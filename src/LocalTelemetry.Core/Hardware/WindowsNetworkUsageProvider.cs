using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using LocalTelemetry.Core.Models;
using Windows.Networking.Connectivity;

namespace LocalTelemetry.Core.Hardware;

/// <summary>
/// Queries historical network usage from the Windows SRUM database. Uses direct ESE
/// database access (via <see cref="EseNetworkUsageReader"/>) for long-range historical
/// data (bypassing the ~30-day PerDay limit of the WinRT API) and the
/// <c>Windows.Networking.Connectivity</c> API for today's partial-day data.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsNetworkUsageProvider
{

    // Matches PhysicalInterfaceTypes in EseNetworkUsageReader
    //   6   = Ethernet, 62 = Fiber Channel, 69 = IEEE 1394,
    //   71  = IEEE 802.11 (WiFi), 117 = Gigabit Ethernet, 243 = WiFi (older)
    private static readonly HashSet<uint> PhysicalIanaTypes = [6, 62, 69, 71, 117, 243];

    /// <summary>
    /// Retrieves per-day network usage records between <paramref name="start"/> and <paramref name="end"/>
    /// using the WinRT <c>GetNetworkUsageAsync</c> API.
    /// <para>This is a fallback for near-current data (<see langword="true"/>) used when the ESE
    /// reader is unavailable.</para>
    /// </summary>
    public static async Task<List<WindowsNetworkUsageRecord>> GetUsageAsync(DateTime start, DateTime end, CancellationToken ct = default)
    {
        var results = new List<WindowsNetworkUsageRecord>();
        ConnectionProfile? profile = null;
        try
        {
            profile = NetworkInformation.GetInternetConnectionProfile();
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error(ex, "WinRT GetInternetConnectionProfile failed");
            return results;
        }

        if (profile?.NetworkAdapter is null)
            return results;

        uint ianaType = profile.NetworkAdapter.IanaInterfaceType;
        if (!PhysicalIanaTypes.Contains(ianaType))
            return results;

        string adapterGuid = profile.NetworkAdapter.NetworkAdapterId.ToString();
        string interfaceType = ResolveAdapterInterfaceName(adapterGuid, ianaType);

        if (start.Date == end.Date)
        {
            await QuerySingleDayUsageAsync(profile, start, end, interfaceType, results);
            return results;
        }

        await QueryHistoricalChunksAsync(profile, start, end, interfaceType, results, ct);
        return results;
    }

    internal static string ResolveAdapterInterfaceName(string adapterGuid, uint ianaType)
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                string id = ni.Id?.Trim("{}".ToCharArray()) ?? string.Empty;
                if (id.Equals(adapterGuid, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(ni.Name))
                    return ni.Name;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, $"Failed to resolve adapter interface name for GUID '{adapterGuid}'; falling back to IANA type lookup");
        }

        return ianaType switch
        {
            6 => "Ethernet",
            71 or 243 => "WiFi",
            _ => $"IanaType{ianaType}"
        };
    }

    private static async Task QuerySingleDayUsageAsync(
        ConnectionProfile profile, DateTime start, DateTime end, string interfaceType, List<WindowsNetworkUsageRecord> results)
    {
        try
        {
            var usageList = await profile.GetNetworkUsageAsync(
                start.ToUniversalTime(),
                end.ToUniversalTime(),
                DataUsageGranularity.Total,
                new NetworkUsageStates());

            long down = 0, up = 0;
            foreach (var usage in usageList)
            {
                down += (long)usage.BytesReceived;
                up += (long)usage.BytesSent;
            }

            results.Add(new WindowsNetworkUsageRecord
            {
                Date = start.ToString("dd-MM-yyyy"),
                InterfaceType = interfaceType,
                DownBytes = down,
                UpBytes = up,
            });
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, $"WinRT today total query skipped ({profile.ProfileName})");
        }
    }

    private static async Task QueryHistoricalChunksAsync(
        ConnectionProfile profile, DateTime start, DateTime end, string interfaceType, List<WindowsNetworkUsageRecord> results, CancellationToken ct)
    {
        var chunkStart = start;
        while (chunkStart < end)
        {
            ct.ThrowIfCancellationRequested();
            var chunkEnd = chunkStart.AddDays(30);
            if (chunkEnd > end) chunkEnd = end;

            bool success = await QueryChunkSliceAsync(profile, chunkStart, chunkEnd, interfaceType, results, ct);
            if (!success)
            {
                // Fallback to 7-day slices if the 30-day query failed
                var subStart = chunkStart;
                while (subStart < chunkEnd)
                {
                    ct.ThrowIfCancellationRequested();
                    var subEnd = subStart.AddDays(7);
                    if (subEnd > chunkEnd) subEnd = chunkEnd;

                    await QueryChunkSliceAsync(profile, subStart, subEnd, interfaceType, results, ct);
                    subStart = subEnd;
                }
            }

            chunkStart = chunkEnd;
        }
    }

    private static async Task<bool> QueryChunkSliceAsync(
        ConnectionProfile profile, DateTime sliceStart, DateTime sliceEnd, string interfaceType, List<WindowsNetworkUsageRecord> results, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var usageList = await profile.GetNetworkUsageAsync(
                sliceStart.ToUniversalTime(),
                sliceEnd.ToUniversalTime(),
                DataUsageGranularity.PerDay,
                new NetworkUsageStates());

            int dayOffset = 0;
            foreach (var usage in usageList)
            {
                results.Add(new WindowsNetworkUsageRecord
                {
                    Date = sliceStart.AddDays(dayOffset).ToString("dd-MM-yyyy"),
                    InterfaceType = interfaceType,
                    DownBytes = (long)usage.BytesReceived,
                    UpBytes = (long)usage.BytesSent,
                });
                dayOffset++;
            }
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, $"WinRT network usage query {sliceStart:dd-MM-yyyy} to {sliceEnd:dd-MM-yyyy} skipped ({profile.ProfileName})");
            return false;
        }
    }

    private static bool IsValidInstallDate(DateTime dt) => dt.Year >= 2000 && dt <= DateTime.Today;

    private static DateTime? TryParseInstallTime(object? val)
    {
        if (val is long fileTime && fileTime > 0)
        {
            var dt = DateTime.FromFileTimeUtc(fileTime).ToLocalTime().Date;
            if (IsValidInstallDate(dt)) return dt;
        }
        return null;
    }

    private static DateTime? TryParseInstallDate(object? val)
    {
        long? unixSeconds = val switch
        {
            int i when i > 0 => i,
            long l when l > 0 => l,
            _ => null
        };

        if (unixSeconds.HasValue)
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value).LocalDateTime.Date;
            if (IsValidInstallDate(dt)) return dt;
        }
        return null;
    }

    /// <summary>Retrieves the Windows installation date from the registry without arbitrary day limits.</summary>
    /// <returns>Installation date or 30 days ago as an emergency fallback.</returns>
    public static DateTime GetWindowsInstallDate()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is not null)
            {
                var dt = TryParseInstallTime(key.GetValue("InstallTime"))
                    ?? TryParseInstallDate(key.GetValue("InstallDate"));
                if (dt.HasValue) return dt.Value;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error(ex, "GetWindowsInstallDate failed");
        }
        return DateTime.Today.AddDays(-30);
    }

    /// <summary>
    /// Imports SRUM usage history into the given <see cref="TrafficHistoryFile"/> on first launch
    /// (when history is empty) from the Windows installation date.
    /// <para>On subsequent launches, only today's baseline is refreshed, ensuring zero startup lag.</para>
    /// </summary>
    public static async Task ImportAndSaveHistoryAsync(TrafficHistoryFile historyFile, CancellationToken ct = default)
    {
        try
        {
            DateTime todayDate = DateTime.Today;
            DateTime yesterday = todayDate.AddDays(-1);

            bool hasHistorical = historyFile.GetAllRecords().Any(r => new DateTime(r.Year, r.Month, r.Day, 0, 0, 0, DateTimeKind.Unspecified) < todayDate);
            int historicalAdded = 0;

            if (!hasHistorical)
            {
                DateTime start = GetWindowsInstallDate().Date;
                DateTime maxRetentionStart = todayDate.AddDays(-60);
                if (start < maxRetentionStart)
                    start = maxRetentionStart;

                if (start <= yesterday)
                {
                    historicalAdded = await ImportHistoricalRecordsAsync(historyFile, start, yesterday, todayDate, ct);
                }
            }

            bool todayUpdated = await ImportTodayBaselineAsync(historyFile, todayDate, ct);

            if (historicalAdded > 0 || todayUpdated)
            {
                historyFile.Save();
                TrafficHistoryStore.NotifyHistoryUpdated();
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error(ex, "ImportAndSaveHistoryAsync failed");
        }
    }

    /// <summary>
    /// Refreshes network usage strictly for the current date (<see cref="DateTime.Today"/>)
    /// without querying long-range historical records or touching the SRUM ESE database.
    /// </summary>
    public static async Task RefreshTodayUsageAsync(TrafficHistoryFile historyFile, CancellationToken ct = default)
    {
        try
        {
            DateTime todayDate = DateTime.Today;
            bool todayUpdated = await ImportTodayBaselineAsync(historyFile, todayDate, ct);
            if (todayUpdated)
            {
                historyFile.Save();
                TrafficHistoryStore.NotifyHistoryUpdated();
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error(ex, "RefreshTodayUsageAsync failed");
        }
    }

    private static (DateTime Start, DateTime End) ClampImportRange(DateTime start, DateTime end)
    {
        DateTime maxAllowed = DateTime.Today;
        DateTime effectiveEnd = end.Date > maxAllowed ? maxAllowed : end.Date;

        DateTime earliestAllowed = DateTime.Today.AddDays(-60);
        DateTime effectiveStart = start.Date < earliestAllowed ? earliestAllowed : start.Date;

        return (effectiveStart, effectiveEnd);
    }

    private static async Task<List<WindowsNetworkUsageRecord>> FetchRangeRecordsAsync(
        DateTime start, DateTime end, CancellationToken ct)
    {
        var records = await EseNetworkUsageReader.ReadUsageAsync(start, end, ct);
        return records.Count > 0 ? records : await GetUsageAsync(start, end, ct);
    }

    private static (long TotalDown, long TotalUp) MergeRecords(
        TrafficHistoryFile historyFile, List<WindowsNetworkUsageRecord> records, HashSet<string> uniqueDays)
    {
        long totalDown = 0;
        long totalUp = 0;
        foreach (var r in records)
        {
            if (r.DownBytes <= 0 && r.UpBytes <= 0)
                continue;

            if (!MergeHistoricalRecord(historyFile, r, DateTime.Today))
                continue;

            uniqueDays.Add(r.Date);
            totalDown += r.DownBytes;
            totalUp += r.UpBytes;
        }
        return (totalDown, totalUp);
    }

    /// <summary>
    /// Imports network usage history for a user-specified date range, merging records
    /// strictly when non-zero traffic is present.
    /// </summary>
    public static async Task<WindowsUsageImportResult> ImportDateRangeAsync(
        TrafficHistoryFile historyFile, DateTime start, DateTime end, CancellationToken ct = default)
    {
        try
        {
            if (start.Date > end.Date)
            {
                return new WindowsUsageImportResult
                {
                    Success = false,
                    ErrorMessage = "Start date must be before or equal to End date.",
                };
            }

            var (startDate, endDate) = ClampImportRange(start, end);
            var allRecords = await FetchRangeRecordsAsync(startDate, endDate, ct);

            var uniqueDays = new HashSet<string>();
            var (totalDown, totalUp) = MergeRecords(historyFile, allRecords, uniqueDays);

            if (endDate >= DateTime.Today && await ImportTodayBaselineAsync(historyFile, DateTime.Today, ct))
            {
                uniqueDays.Add(DateTime.Today.ToString("dd-MM-yyyy"));
            }

            int addedCount = uniqueDays.Count;
            if (addedCount > 0)
            {
                historyFile.Save();
                TrafficHistoryStore.NotifyHistoryUpdated();
            }

            string? infoMsg = null;
            if (addedCount == 0)
            {
                infoMsg = allRecords.Count == 0
                    ? "No network activity found in Windows for this range. (Note: Windows only retains the last 60 days of activity.)"
                    : "Selected date range is already up to date in LocalTelemetry.";
            }

            return new WindowsUsageImportResult
            {
                Success = true,
                DaysImported = addedCount,
                DownBytes = totalDown,
                UpBytes = totalUp,
                Message = infoMsg,
            };
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error(ex, "ImportDateRangeAsync failed");
            return new WindowsUsageImportResult
            {
                Success = false,
                ErrorMessage = ex.Message,
            };
        }
    }

    private static async Task<int> ImportHistoricalRecordsAsync(
        TrafficHistoryFile historyFile, DateTime start, DateTime end, DateTime todayDate, CancellationToken ct)
    {
        var allRecords = await EseNetworkUsageReader.ReadUsageAsync(start, end, ct);
        Diagnostics.Log.Debug($"Windows network history scan found {allRecords.Count} records ({start:dd-MM-yyyy} to {end:dd-MM-yyyy})");

        if (allRecords.Count == 0)
        {
            Diagnostics.Log.Debug("System database returned no entries, falling back to Windows Network API");
            allRecords = await GetUsageAsync(start, end, ct);
        }

        var uniqueDays = new HashSet<string>();
        foreach (var r in allRecords)
        {
            if (r.DownBytes <= 0 && r.UpBytes <= 0) continue;
            if (MergeHistoricalRecord(historyFile, r, todayDate))
                uniqueDays.Add(r.Date);
        }

        int historicalAdded = uniqueDays.Count;

        if (historicalAdded > 0)
            Diagnostics.Log.Info($"Imported {historicalAdded} days of network usage history ({start:dd-MM-yyyy} to {end:dd-MM-yyyy})");
        else
            Diagnostics.Log.Info("Network usage history is up to date");

        return historicalAdded;
    }

    private static bool MergeHistoricalRecord(TrafficHistoryFile historyFile, WindowsNetworkUsageRecord r, DateTime todayDate)
    {
        var parts = r.Date.Split('-');
        if (parts.Length != 3 || !int.TryParse(parts[0], out int day) || !int.TryParse(parts[1], out int month) || !int.TryParse(parts[2], out int year))
            return false;

        if (year == todayDate.Year && month == todayDate.Month && day == todayDate.Day)
            return false;

        var existing = historyFile.Find(year, month, day, r.InterfaceType);
        if (existing.HasValue)
        {
            long existingTotal = existing.Value.UpBytes + existing.Value.DownBytes;
            long srumTotal = r.UpBytes + r.DownBytes;
            if (srumTotal <= existingTotal) return false;
        }

        historyFile.SetDay(year, month, day, r.DownBytes, r.UpBytes, r.InterfaceType, "WindowsImport");
        return true;
    }

    private static async Task<bool> ImportTodayBaselineAsync(TrafficHistoryFile historyFile, DateTime todayDate, CancellationToken ct)
    {
        bool todayUpdated = false;
        try
        {
            DateTime now = DateTime.Now;
            var todayRecords = await GetUsageAsync(todayDate, now, ct);
            foreach (var tr in todayRecords)
            {
                if (tr.DownBytes <= 0 && tr.UpBytes <= 0) continue;

                var curToday = historyFile.Find(todayDate.Year, todayDate.Month, todayDate.Day, tr.InterfaceType);
                if (!curToday.HasValue)
                {
                    historyFile.SetDay(todayDate.Year, todayDate.Month, todayDate.Day, tr.DownBytes, tr.UpBytes, tr.InterfaceType, "WindowsImport");
                    todayUpdated = true;
                    Diagnostics.Log.Info($"Today's network usage baseline with download {Metrics.Format("net_down", new() { NetDownBps = tr.DownBytes }, false, false).Replace("/s", "")}, upload {Metrics.Format("net_up", new() { NetUpBps = tr.UpBytes }, false, false).Replace("/s", "")}");
                }
                else
                {
                    long existingTotal = curToday.Value.UpBytes + curToday.Value.DownBytes;
                    long winTotal = tr.UpBytes + tr.DownBytes;
                    if (winTotal > existingTotal)
                    {
                        long finalDown = Math.Max(curToday.Value.DownBytes, tr.DownBytes);
                        long finalUp = Math.Max(curToday.Value.UpBytes, tr.UpBytes);
                        historyFile.SetDay(todayDate.Year, todayDate.Month, todayDate.Day, finalDown, finalUp, tr.InterfaceType, "WindowsImport");
                        todayUpdated = true;
                        Diagnostics.Log.Info($"Updated today's network usage baseline with download {Metrics.Format("net_down", new() { NetDownBps = finalDown }, false, false).Replace("/s", "")}, upload {Metrics.Format("net_up", new() { NetUpBps = finalUp }, false, false).Replace("/s", "")}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Debug(ex, "Today baseline query skipped");
        }
        return todayUpdated;
    }
}

/// <summary>Describes a single day's network usage from SRUM data.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkUsageRecord
{
    /// <summary>Date string in <c>"dd-MM-yyyy"</c> format.</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Network interface type description (e.g. <c>"Ethernet"</c>, <c>"WiFi"</c>, <c>"ESE Import"</c>).</summary>
    public string InterfaceType { get; set; } = string.Empty;
    /// <summary>Total bytes downloaded on this date.</summary>
    public long DownBytes { get; set; }
    /// <summary>Total bytes uploaded on this date.</summary>
    public long UpBytes { get; set; }
}

/// <summary>Result summary returned after a manual Windows usage import.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUsageImportResult
{
    /// <summary>Whether the import operation completed successfully.</summary>
    public bool Success { get; set; }
    /// <summary>Number of day records added or updated.</summary>
    public int DaysImported { get; set; }
    /// <summary>Total download bytes merged during this import.</summary>
    public long DownBytes { get; set; }
    /// <summary>Total upload bytes merged during this import.</summary>
    public long UpBytes { get; set; }
    /// <summary>Error message if the import failed.</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>Informational feedback message for the user.</summary>
    public string? Message { get; set; }
}

