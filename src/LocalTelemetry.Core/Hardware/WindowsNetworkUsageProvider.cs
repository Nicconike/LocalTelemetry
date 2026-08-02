using System.Net.NetworkInformation;
using System.Runtime.Versioning;
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
    private static void Log(string msg) { Diagnostics.Log.Info(msg); }
    private static void LogError(string msg) { Diagnostics.Log.Error(msg); }

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
            LogError($"WinRT GetInternetConnectionProfile failed: {ex.Message}");
            return results;
        }

        if (profile?.NetworkAdapter is null)
            return results;

        uint ianaType = profile.NetworkAdapter.IanaInterfaceType;
        if (!PhysicalIanaTypes.Contains(ianaType))
            return results;

        string adapterGuid = profile.NetworkAdapter.NetworkAdapterId.ToString();
        string interfaceType;
        try
        {
            string? name = null;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                string id = ni.Id?.Trim("{}".ToCharArray()) ?? string.Empty;
                if (id.Equals(adapterGuid, StringComparison.OrdinalIgnoreCase))
                {
                    name = ni.Name;
                    break;
                }
            }
            interfaceType = !string.IsNullOrEmpty(name) ? name : ianaType switch
            {
                6 => "Ethernet",
                71 or 243 => "WiFi",
                _ => $"IanaType{ianaType}"
            };
        }
        catch
        {
            interfaceType = ianaType switch { 6 => "Ethernet", 71 or 243 => "WiFi", _ => $"IanaType{ianaType}" };
        }

        // Query in 20-day chunks to avoid E_INVALIDARG on large PerDay ranges
        var chunkStart = start;
        while (chunkStart < end)
        {
            ct.ThrowIfCancellationRequested();
            var chunkEnd = chunkStart.AddDays(20);
            if (chunkEnd > end) chunkEnd = end;

            try
            {
                var usageList = await profile.GetNetworkUsageAsync(
                    chunkStart.ToUniversalTime(),
                    chunkEnd.ToUniversalTime(),
                    DataUsageGranularity.PerDay,
                    new NetworkUsageStates());

                int dayOffset = 0;
                foreach (var usage in usageList)
                {
                    results.Add(new WindowsNetworkUsageRecord
                    {
                        Date = chunkStart.AddDays(dayOffset).ToString("dd-MM-yyyy"),
                        InterfaceType = interfaceType,
                        DownBytes = (long)usage.BytesReceived,
                        UpBytes = (long)usage.BytesSent,
                    });
                    dayOffset++;
                }
            }
            catch (Exception ex)
            {
                Log($"WinRT fallback: chunk {chunkStart:yyyy-MM-dd} to {chunkEnd:yyyy-MM-dd} skipped ({profile.ProfileName}): {ex.Message}");
            }

            chunkStart = chunkEnd;
        }

        return results;
    }

    /// <summary>Retrieves the Windows installation date from the registry.</summary>
    /// <returns>Installation date or 30 days ago as a fallback.</returns>
    public static DateTime GetWindowsInstallDate()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is not null && key.GetValue("InstallDate") is int unixTime)
                return DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime;
        }
        catch (Exception ex) { LogError($"GetWindowsInstallDate failed: {ex.Message}"); }
        return DateTime.UtcNow.AddDays(-30);
    }

    /// <summary>
    /// Imports SRUM usage history into the given <see cref="TrafficHistoryFile"/>,
    /// skipping records already present.
    /// <para>Uses direct ESE database access for historical data (no range limit) and
    /// the WinRT API as a fallback for today's partial-day data and when ESE is unavailable.</para>
    /// </summary>
    public static async Task ImportAndSaveHistoryAsync(TrafficHistoryFile historyFile, CancellationToken ct = default)
    {
        try
        {
            DateTime start = GetWindowsInstallDate().ToLocalTime();
            DateTime end = DateTime.Now;
            DateTime yesterday = end.Date.AddDays(-1);

            // Phase 1: ESE reader for all historical data (no range limit, covers all interfaces)
            var eseRecords = await EseNetworkUsageReader.ReadUsageAsync(start, yesterday, ct);
            Log($"ESE import: {eseRecords.Count} records from {start:dd-MM-yyyy} to {yesterday:dd-MM-yyyy}");

            // Phase 2: WinRT for today's partial-day data (more current than ESE)
            var todayRecords = await GetUsageAsync(DateTime.Today, end, ct);
            if (todayRecords.Count > 0)
                Log($"Today WinRT query returned {todayRecords.Count} record(s)");

            var allRecords = new List<WindowsNetworkUsageRecord>(eseRecords);

            // Prefer today's WinRT result over ESE (WinRT has sub-day granularity)
            foreach (var tr in todayRecords)
            {
                var existing = allRecords.FindIndex(r =>
                    r.Date == tr.Date &&
                    string.Equals(r.InterfaceType, tr.InterfaceType, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0)
                    allRecords[existing] = tr;
                else
                    allRecords.Add(tr);
            }

            // If ESE returned nothing, fall back to WinRT for the full range
            if (allRecords.Count == 0)
            {
                Log("ESE import returned nothing, falling back to WinRT full range");
                allRecords = await GetUsageAsync(start, end, ct);
            }

            if (allRecords.Count == 0) return;

            int added = 0;
            foreach (var r in allRecords)
            {
                var parts = r.Date.Split('-');
                if (parts.Length != 3) continue;
                if (!int.TryParse(parts[0], out int day)) continue;
                if (!int.TryParse(parts[1], out int month)) continue;
                if (!int.TryParse(parts[2], out int year)) continue;

                var existing = historyFile.Find(year, month, day, r.InterfaceType);
                if (existing.HasValue)
                {
                    long existingTotal = existing.Value.UpBytes + existing.Value.DownBytes;
                    long srumTotal = r.UpBytes + r.DownBytes;
                    if (srumTotal <= existingTotal) continue;
                }

                historyFile.SetDay(year, month, day, r.DownBytes, r.UpBytes, r.InterfaceType, "imported");
                added++;
            }

            if (added > 0)
            {
                historyFile.Save();
                Log($"SRUM import: added/updated {added} records");
            }
        }
        catch (Exception ex)
        {
            LogError($"SRUM import failed: {ex.Message}");
        }
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
