using System.Diagnostics;
using System.Runtime.Versioning;
using LocalTelemetry.Core.Config;
using LocalTelemetry.Core.Diagnostics;
using Microsoft.Isam.Esent.Interop;

namespace LocalTelemetry.Core.Hardware;

/// <summary>
/// Reads network usage data directly from the Windows SRUM ESE database (SRUDB.dat),
/// bypassing the <c>Windows.Networking.Connectivity</c> API which has a ~30-day lookback
/// limit for per-day granularity. The Network Data Usage table's TimeStamp column is an
/// ESE DateTime (OLE Automation double, epoch 1899-12-30), read here as a double and
/// bucketed per UTC day.
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

    /// <summary>Test seam: replaces Process.Start for esentutl copies and repairs.</summary>
    internal static Func<ProcessStartInfo, int?>? ProcessRunner { get; set; }

    private static string GetInterfaceName(byte[] rawIf, ushort ianaType)
    {
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
    /// </summary>
    public static async Task<List<WindowsNetworkUsageRecord>> ReadUsageAsync(
        DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        return await ReadUsageInternalAsync(startDate, endDate, null, null, null, null, ct).ConfigureAwait(false);
    }

    internal static async Task<List<WindowsNetworkUsageRecord>> ReadUsageInternalAsync(
        DateTime startDate,
        DateTime endDate,
        string? overrideSrumPath,
        Func<string, string, string, string?, CancellationToken, Task<bool>>? copyRunner,
        Func<string, string, bool>? repairRunner,
        Func<string, DateTime, DateTime, List<WindowsNetworkUsageRecord>>? recordReader,
        CancellationToken ct = default)
    {
        string srumPath = overrideSrumPath ?? Path.Combine(Environment.SystemDirectory, "sru", "SRUDB.dat");
        if (!File.Exists(srumPath))
        {
            Log.Debug("System resource usage database file was not found");
            return [];
        }

        string esentutl = Path.Combine(Environment.SystemDirectory, "esentutl.exe");
        if (!File.Exists(esentutl))
        {
            Log.Warn("Database utility was not found, falling back to runtime provider");
            return [];
        }

        string tempDir = Path.Combine(AppSettings.DataDir, "temp");
        Directory.CreateDirectory(tempDir);
        string destPath = Path.Combine(tempDir, $"SRUDB_{Guid.NewGuid():N}.dat");

        try
        {
            Func<string, string, string, string?, CancellationToken, Task<bool>> doCopy =
                copyRunner ?? new Func<string, string, string, string?, CancellationToken, Task<bool>>(RunEsentutlCopy);

            if (!await doCopy(esentutl, srumPath, destPath, "/vss", ct).ConfigureAwait(false)
                && !await doCopy(esentutl, srumPath, destPath, null, ct).ConfigureAwait(false))
            {
                Log.Warn("All database copy methods failed");
                return [];
            }

            Func<string, string, bool> doRepair =
                repairRunner ?? new Func<string, string, bool>(RunRepair);

            if (!doRepair(esentutl, destPath))
            {
                Log.Warn("Database repair failed, falling back to runtime provider");
                return [];
            }

            var reader = recordReader ?? ReadRecords;
            return reader(destPath, startDate.Date, endDate.Date);
        }
        finally
        {
            try { File.Delete(destPath); }
            catch (Exception ex) { Log.Debug(ex, $"Failed to delete temporary ESE database file '{destPath}'"); }
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

        int exitCode;
        string stderr;
        if (ProcessRunner is not null)
        {
            exitCode = ProcessRunner(psi) ?? -1;
            stderr = string.Empty;
        }
        else
        {
            using var process = new Process { StartInfo = psi };
            process.Start();

            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
            exitCode = process.ExitCode;
        }

        if (exitCode != 0 || !File.Exists(destPath))
        {
            Log.Warn($"Database copy operation failed with exit code {exitCode}: {stderr}");
            try { File.Delete(destPath); }
            catch (Exception ex) { Log.Debug(ex, $"Failed to delete incomplete ESE database copy '{destPath}'"); }
            return false;
        }

        Log.Debug($"Database copied successfully to {destPath}");
        return true;
    }

    private static bool RunRepair(string esentutl, string dbPath)
    {
        try
        {
            string tempDir = Path.Combine(AppSettings.DataDir, "temp");
            Directory.CreateDirectory(tempDir);

            var psi = new ProcessStartInfo(esentutl, $"/p \"{dbPath}\" /o")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = tempDir,
            };

            int exitCode;
            if (ProcessRunner is not null)
            {
                exitCode = ProcessRunner(psi) ?? -1;
            }
            else
            {
                using var proc = new Process { StartInfo = psi };
                proc.Start();
                proc.WaitForExit(60000);
                exitCode = proc.ExitCode;
            }

            try
            {
                foreach (string f in Directory.GetFiles(tempDir, "*.INTEG.RAW"))
                    File.Delete(f);
            }
            catch (Exception ex) { Log.Debug(ex, "Failed to delete temporary ESE repair files"); }

            if (exitCode != 0)
            {
                Log.Warn($"Database repair utility exited with code {exitCode}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Database repair utility failed: {ex.Message}");
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
            InitJetEngine(dbPath, out instance, out sesid, out dbid, out tableid);
            if (tableid == JET_TABLEID.Nil) return [];

            if (!TryResolveColumns(sesid, tableid, out var cols))
                return [];

            var dailyMap = new Dictionary<(string dateStr, string iface), (long down, long up)>();

            if (Api.TryMoveFirst(sesid, tableid))
            {
                do
                {
                    ProcessRowRecord(sesid, tableid, cols, startDate, endDate, dailyMap);
                } while (Api.TryMoveNext(sesid, tableid));
            }

            var results = new List<WindowsNetworkUsageRecord>(dailyMap.Count);
            foreach (var (key, val) in dailyMap)
            {
                results.Add(new WindowsNetworkUsageRecord
                {
                    Date = key.dateStr,
                    InterfaceType = key.iface,
                    DownBytes = val.down,
                    UpBytes = val.up,
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read records from database");
            return [];
        }
        finally
        {
            CleanupJetResources(instance, sesid, dbid, tableid);
        }
    }

    private static void InitJetEngine(
        string dbPath,
        out JET_INSTANCE instance,
        out JET_SESID sesid,
        out JET_DBID dbid,
        out JET_TABLEID tableid)
    {
        instance = JET_INSTANCE.Nil;
        sesid = JET_SESID.Nil;
        dbid = JET_DBID.Nil;
        tableid = JET_TABLEID.Nil;

        Api.JetCreateInstance(out instance, $"srum-reader-{Guid.NewGuid():N}");
        Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.Recovery, 0, "Off");
        Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.CircularLog, 0, null);

        try
        {
            Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.DatabasePageSize, 32768, null);
        }
        catch (EsentAlreadyInitializedException)
        {
            int enginePageSize = 0;
            try
            {
                Api.JetGetSystemParameter(instance, JET_SESID.Nil, JET_param.DatabasePageSize, ref enginePageSize, out _, 0);
            }
            catch (Exception ex) { Log.Debug(ex, "Failed to query database page size"); }
            Log.Debug($"Database engine already initialized, skipping page size configuration (current size: {enginePageSize})");
        }

        Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.CacheSizeMax, 256, null);
        Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.CacheSizeMin, 64, null);
        Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.MaxTemporaryTables, 0, null);
        Api.JetInit(ref instance);

        Api.JetBeginSession(instance, out sesid, null, null);
        Api.JetAttachDatabase(sesid, dbPath, AttachDatabaseGrbit.ReadOnly);
        Api.JetOpenDatabase(sesid, dbPath, null, out dbid, OpenDatabaseGrbit.ReadOnly);

        try
        {
            Api.JetOpenTable(sesid, dbid, NetworkDataUsageTable, null, 0, OpenTableGrbit.ReadOnly, out tableid);
        }
        catch (EsentObjectNotFoundException)
        {
            Log.Warn($"Network data usage table '{NetworkDataUsageTable}' was not found in database");
        }
    }

    private sealed record ColumnHandles(JET_COLUMNID ColTime, JET_COLUMNID ColSent, JET_COLUMNID ColRecv, JET_COLUMNID ColIfLuid);

    private static bool TryResolveColumns(JET_SESID sesid, JET_TABLEID tableid, out ColumnHandles cols)
    {
        try
        {
            var colTime = Api.GetTableColumnid(sesid, tableid, ColTimeStamp);
            var colSent = Api.GetTableColumnid(sesid, tableid, ColBytesSent);
            var colRecv = Api.GetTableColumnid(sesid, tableid, ColBytesRecvd);
            var colIfLuid = Api.GetTableColumnid(sesid, tableid, ColInterfaceLuid);
            cols = new ColumnHandles(colTime, colSent, colRecv, colIfLuid);
            return true;
        }
        catch (EsentColumnNotFoundException)
        {
            Log.Warn("Required network usage columns are missing from table");
            cols = new ColumnHandles(JET_COLUMNID.Nil, JET_COLUMNID.Nil, JET_COLUMNID.Nil, JET_COLUMNID.Nil);
            return false;
        }
    }

    private static void ProcessRowRecord(
        JET_SESID sesid,
        JET_TABLEID tableid,
        ColumnHandles cols,
        DateTime startDate,
        DateTime endDate,
        Dictionary<(string dateStr, string iface), (long down, long up)> dailyMap)
    {
        double? oleTs = Api.RetrieveColumnAsDouble(sesid, tableid, cols.ColTime);
        if (!oleTs.HasValue || !double.IsFinite(oleTs.Value) || oleTs.Value < 40000) return;
        DateTime utcDt = new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc).AddDays(oleTs.Value);
        DateTime localDt = TimeZoneInfo.ConvertTimeFromUtc(utcDt, TimeZoneInfo.Local);
        DateTime dt = localDt.Date;
        if (dt < startDate || dt > endDate) return;

        byte[]? ifLuid = Api.RetrieveColumn(sesid, tableid, cols.ColIfLuid);
        if (ifLuid is null || ifLuid.Length < 8) return;

        ushort ianaType = BitConverter.ToUInt16(ifLuid, 6);
        if (!PhysicalInterfaceTypes.Contains(ianaType)) return;

        long sent = Math.Max(0, Api.RetrieveColumnAsInt64(sesid, tableid, cols.ColSent) ?? 0);
        long recv = Math.Max(0, Api.RetrieveColumnAsInt64(sesid, tableid, cols.ColRecv) ?? 0);
        if (sent == 0 && recv == 0) return;

        string dateStr = dt.ToString("dd-MM-yyyy");
        string ifaceName = GetInterfaceName(ifLuid, ianaType);
        var key = (dateStr, ifaceName);

        if (dailyMap.TryGetValue(key, out var current))
            dailyMap[key] = (current.down + recv, current.up + sent);
        else
            dailyMap[key] = (recv, sent);
    }

    private static void CleanupJetResources(JET_INSTANCE instance, JET_SESID sesid, JET_DBID dbid, JET_TABLEID tableid)
    {
        if (tableid != JET_TABLEID.Nil)
            try { Api.JetCloseTable(sesid, tableid); }
            catch (Exception ex) { Log.Debug(ex, "Failed to close Jet table during cleanup"); }

        if (dbid != JET_DBID.Nil)
            try { Api.JetCloseDatabase(sesid, dbid, CloseDatabaseGrbit.None); }
            catch (Exception ex) { Log.Debug(ex, "Failed to close Jet database during cleanup"); }

        if (sesid != JET_SESID.Nil)
            try { Api.JetEndSession(sesid, EndSessionGrbit.None); }
            catch (Exception ex) { Log.Debug(ex, "Failed to end Jet session during cleanup"); }

        if (instance != JET_INSTANCE.Nil)
            try { Api.JetTerm(instance); }
            catch (Exception ex) { Log.Debug(ex, "Failed to terminate Jet instance during cleanup"); }
    }
}
