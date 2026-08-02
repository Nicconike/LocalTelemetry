using System.Diagnostics;
using System.Runtime.Versioning;
using LocalTelemetry.Core.Diagnostics;
using Microsoft.Isam.Esent.Interop;

namespace LocalTelemetry.Core.Hardware;

/// <summary>
/// Reads network usage data directly from the Windows SRUM ESE database (SRUDB.dat),
/// bypassing the <c>Windows.Networking.Connectivity</c> API which has a ~30-day lookback
/// limit for per-day granularity.
///
/// The Network Data Usage table stores per-connection records with an InterfaceLuid column
/// encoded as a NET_LUID structure (8 bytes). The interface type (IfType) resides in the
/// high 16 bits (bytes 6-7), NOT the low bytes. The previous implementation read bytes 0-1
/// (the Reserved field) instead, causing all records to be filtered out.
/// </summary>
[SupportedOSPlatform("windows")]
public static class EseNetworkUsageReader
{
    private const string NetworkDataUsageTable = "{973F5D5C-1D90-4944-BE8E-24B94231A174}";

    private const string ColTimeStamp = "TimeStamp";
    private const string ColBytesSent = "BytesSent";
    private const string ColBytesRecvd = "BytesRecvd";
    private const string ColInterfaceLuid = "InterfaceLuid";

    private static readonly HashSet<ushort> PhysicalInterfaceTypes = [6, 62, 69, 71, 117, 243];

    private static string GetInterfaceName(byte[] rawIf, ushort ianaType)
    {
        // Map IANA interface type to a friendly name.
        // We skip ConvertInterfaceLuidToNameW because SRUM often stores NET_LUID values
        // for historical adapters that no longer exist and the API returns machine-generated
        // names like "ethernet_32768" that are less useful than the generic type name.
        _ = rawIf;
        return ianaType switch
        {
            6 => "Ethernet",
            71 or 243 => "WiFi",
            62 => "Fiber Channel",
            69 => "FireWire",
            117 => "Gigabit Ethernet",
            _ => $"IanaType{ianaType}"
        };
    }

    /// <summary>
    /// Reads and aggregates per-day network usage from the SRUM database for the given
    /// date range. Returns records summed across all physical interfaces (Ethernet/WiFi).
    ///
    /// Uses esentutl VSS copy + /p repair - the standard forensic approach used by
    /// SrumECmd, SRUM-DUMP and KAPE. Falls back to WinRT (handled by caller) if
    /// esentutl is unavailable or the copy fails.
    /// </summary>
    public static async System.Threading.Tasks.Task<List<WindowsNetworkUsageRecord>> ReadUsageAsync(
        DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        string srumPath = Path.Combine(Environment.SystemDirectory, "sru", "SRUDB.dat");
        if (!File.Exists(srumPath))
        {
            Log.Info("SRUDB.dat not found");
            return [];
        }

        string esentutl = Path.Combine(Environment.SystemDirectory, "esentutl.exe");
        if (!File.Exists(esentutl))
        {
            Log.Info("esentutl.exe not found, caller will use WinRT fallback");
            return [];
        }

        string destPath = Path.Combine(
            Path.GetTempPath(), $"SRUDB_{Guid.NewGuid():N}.dat");

        try
        {
            // Step 1: VSS copy via esentutl (handles locked database)
            if (!await RunEsentutlCopy(esentutl, srumPath, destPath, "/vss", ct).ConfigureAwait(false)
                && !await RunEsentutlCopy(esentutl, srumPath, destPath, null, ct).ConfigureAwait(false))
            {
                Log.Warn("all copy methods failed");
                return [];
            }

            // Step 2: /p repair (VSS copies are always dirty; no log files exist)
            if (!RunRepair(esentutl, destPath))
            {
                Log.Warn("repair failed, caller will use WinRT fallback");
                return [];
            }

            // Step 3: read records from the repaired copy
            return ReadRecords(destPath, startDate.Date, endDate.Date);
        }
        finally
        {
            try { File.Delete(destPath); } catch { }
        }
    }

    private static async Task<bool> RunEsentutlCopy(
        string esentutl, string srumPath, string destPath, string? vssFlag, CancellationToken ct)
    {
        string args = $"/y \"{srumPath}\"{(vssFlag is not null ? " " + vssFlag : "")} /d \"{destPath}\"";
        var psi = new ProcessStartInfo(esentutl, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stderr = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        await stderr.ConfigureAwait(false);

        if (process.ExitCode != 0 || !File.Exists(destPath))
        {
            Log.Info($"esentutl copy failed (exit {process.ExitCode}, vss={vssFlag ?? "none"}): {stderr.Result}");
            try { File.Delete(destPath); } catch { }
            return false;
        }

        Log.Info($"copied via esentutl (vss={vssFlag ?? "none"}) to {destPath}");
        return true;
    }

    private static bool RunRepair(string esentutl, string dbPath)
    {
        try
        {
            var psi = new ProcessStartInfo(esentutl, $"/p \"{dbPath}\" /o")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetTempPath(),
            };
            using var proc = new Process { StartInfo = psi };
            proc.Start();
            proc.WaitForExit(60000);

            // esentutl /p writes .INTEG.RAW debris to the working directory
            try
            {
                foreach (string f in Directory.GetFiles(Path.GetTempPath(), "*.INTEG.RAW"))
                    File.Delete(f);
            }
            catch { }

            if (proc.ExitCode != 0)
            {
                Log.Warn($"esentutl repair exited {proc.ExitCode}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Info($"esentutl repair failed ({ex.Message})");
            return false;
        }
    }

    private static List<WindowsNetworkUsageRecord> ReadRecords(
        string dbPath, DateTime startDate, DateTime endDate)
    {
        var instance = JET_INSTANCE.Nil;
        var sesid = JET_SESID.Nil;
        var dbid = JET_DBID.Nil;
        var tableid = JET_TABLEID.Nil;

        try
        {
            Api.JetCreateInstance(out instance, "srum-reader");
            Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.Recovery, 0, "Off");
            Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.CircularLog, 0, null);

            try
            {
                Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.DatabasePageSize, 32768, null);
            }
            catch (EsentAlreadyInitializedException)
            {
                Log.Info("ESENT already initialized, skipping DatabasePageSize");
            }

            Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.CacheSizeMax, 256, null);
            Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.CacheSizeMin, 64, null);
            Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.MaxTemporaryTables, 0, null);
            Api.JetInit(ref instance);

            Api.JetBeginSession(instance, out sesid, null, null);
            Api.JetAttachDatabase(sesid, dbPath, AttachDatabaseGrbit.ReadOnly);
            Api.JetOpenDatabase(sesid, dbPath, null, out dbid, OpenDatabaseGrbit.ReadOnly);
            Api.JetOpenTable(
                sesid, dbid, NetworkDataUsageTable, null, 0,
                OpenTableGrbit.ReadOnly, out tableid);

            var colTs = Api.GetTableColumnid(sesid, tableid, ColTimeStamp);
            var colSent = Api.GetTableColumnid(sesid, tableid, ColBytesSent);
            var colRecv = Api.GetTableColumnid(sesid, tableid, ColBytesRecvd);
            var colIf = Api.GetTableColumnid(sesid, tableid, ColInterfaceLuid);

            var daily = new Dictionary<(DateTime Date, string Interface), (long Down, long Up)>();
            int totalRecords = 0;
            int filteredDate = 0;
            int filteredIfType = 0;
            int filteredZero = 0;

            if (!Api.TryMoveFirst(sesid, tableid))
            {
                Log.Info("no records in Network Data Usage table");
                return [];
            }

            do
            {
                totalRecords++;

                double? oleTs = Api.RetrieveColumnAsDouble(sesid, tableid, colTs);
                if (oleTs is null || oleTs.Value < 40000)
                {
                    filteredDate++;
                    continue;
                }

                var recordDate = new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc)
                    .AddDays(oleTs.Value)
                    .Date;

                if (recordDate < startDate || recordDate > endDate)
                    continue;

                byte[]? rawIf = Api.RetrieveColumn(sesid, tableid, colIf);
                if (rawIf is null || rawIf.Length < 2)
                {
                    filteredIfType++;
                    continue;
                }

                ushort ifType = rawIf.Length >= 8
                    ? (ushort)(rawIf[6] | (rawIf[7] << 8))
                    : (ushort)(rawIf[0] | (rawIf[1] << 8));

                if (!PhysicalInterfaceTypes.Contains(ifType))
                {
                    filteredIfType++;
                    continue;
                }

                string ifName = GetInterfaceName(rawIf, ifType);

                long sent = Api.RetrieveColumnAsInt64(sesid, tableid, colSent) ?? 0;
                long recv = Api.RetrieveColumnAsInt64(sesid, tableid, colRecv) ?? 0;

                if (sent == 0 && recv == 0)
                {
                    filteredZero++;
                    continue;
                }

                var key = (recordDate, ifName);
                if (daily.TryGetValue(key, out var existing))
                    daily[key] = (existing.Down + recv, existing.Up + sent);
                else
                    daily[key] = (recv, sent);
            }
            while (Api.TryMoveNext(sesid, tableid));

            Log.Info(
                $"{totalRecords} total, " +
                $"{filteredDate} date-filtered, {filteredIfType} iftype-filtered, " +
                $"{filteredZero} zero-skipped, {daily.Count} day results");

            if (daily.Count > 0)
            {
                var monthAccum = new Dictionary<(int Year, int Month), (int Days, long Down, long Up)>();
                foreach (var ((date, _), (down, up)) in daily)
                {
                    var mKey = (date.Year, date.Month);
                    if (monthAccum.TryGetValue(mKey, out var acc))
                        monthAccum[mKey] = (acc.Days + 1, acc.Down + down, acc.Up + up);
                    else
                        monthAccum[mKey] = (1, down, up);
                }

                var sortedKeys = new List<(int Year, int Month)>(monthAccum.Keys);
                sortedKeys.Sort((a, b) =>
                {
                    int c = a.Year.CompareTo(b.Year);
                    return c != 0 ? c : a.Month.CompareTo(b.Month);
                });
                foreach (var m in sortedKeys)
                {
                    var acc = monthAccum[m];
                    Log.Info(
                        $"month {m.Year:D4}-{m.Month:D2}: " +
                        $"{acc.Days} days, {acc.Down:N0} bytes down, {acc.Up:N0} bytes up");
                }
            }

            var results = new List<WindowsNetworkUsageRecord>(daily.Count);
            foreach (var ((date, ifName), (down, up)) in daily)
            {
                results.Add(new WindowsNetworkUsageRecord
                {
                    Date = date.ToString("dd-MM-yyyy"),
                    InterfaceType = ifName,
                    DownBytes = down,
                    UpBytes = up,
                });
            }

            Log.Info($"{results.Count} day records read from {dbPath}");
            return results;
        }
        catch (EsentErrorException ex)
        {
            Log.Error(ex, $"ESENT error reading {dbPath}");
            return [];
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"unexpected error reading {dbPath}");
            return [];
        }
        finally
        {
            try
            {
                if (tableid != JET_TABLEID.Nil)
                    Api.JetCloseTable(sesid, tableid);
                if (dbid != JET_DBID.Nil)
                {
                    Api.JetCloseDatabase(sesid, dbid, CloseDatabaseGrbit.None);
                    Api.JetDetachDatabase(sesid, dbPath);
                }
                if (sesid != JET_SESID.Nil)
                    Api.JetEndSession(sesid, EndSessionGrbit.None);
                if (instance != JET_INSTANCE.Nil)
                    Api.JetTerm(instance);
            }
            catch { /* cleanup */ }
        }
    }
}
