using System.Reflection;
using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Microsoft.Isam.Esent.Interop;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

[Collection("TrafficHistory")]
public class EseNetworkUsageReaderTests
{
    private static readonly object SyncLock = new();

    [Fact]
    public void EseNetworkUsageReader_GetInterfaceName_MapsIanaTypesCorrectly()
    {
        var method = typeof(EseNetworkUsageReader).GetMethod("GetInterfaceName", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        byte[] rawIf = [0, 0];
        method!.Invoke(null, [rawIf, (ushort)6]).Should().Be("Ethernet");
        method.Invoke(null, [rawIf, (ushort)71]).Should().Be("WiFi");
        method.Invoke(null, [rawIf, (ushort)243]).Should().Be("WiFi");
        method.Invoke(null, [rawIf, (ushort)62]).Should().Be("Fiber Channel");
        method.Invoke(null, [rawIf, (ushort)69]).Should().Be("FireWire");
        method.Invoke(null, [rawIf, (ushort)117]).Should().Be("Gigabit Ethernet");
        method.Invoke(null, [rawIf, (ushort)999]).Should().Be("IanaType999");
    }


    [Fact]
    public async Task EseNetworkUsageReader_ReadUsageInternalAsync_WithMockDatabase_ReadsRecords()
    {
        string tempSrum = Path.Combine(Path.GetTempPath(), $"SRUDB_Mock_{Guid.NewGuid():N}.dat");
        await File.WriteAllTextAsync(tempSrum, "mock esent database content");

        try
        {
            var now = DateTime.UtcNow;
            var mockRecords = new List<WindowsNetworkUsageRecord>
            {
                new() { Date = "01-08-2026", InterfaceType = "Ethernet", DownBytes = 1048576, UpBytes = 524288 }
            };

            var records = await EseNetworkUsageReader.ReadUsageInternalAsync(
                now.AddDays(-7), now, tempSrum,
                (exe, src, dst, flag, token) => Task.FromResult(true),
                (exe, db) => true,
                (dbPath, start, end) => mockRecords);

            records.Should().HaveCount(1);
            records[0].InterfaceType.Should().Be("Ethernet");
            records[0].DownBytes.Should().Be(1048576);
        }
        finally
        {
            try { File.Delete(tempSrum); } catch (Exception) { /* Best-effort cleanup */ }
        }
    }

    [Fact]
    public void EseNetworkUsageReader_RunRepair_HandlesInvalidPathGracefully()
    {
        var method = typeof(EseNetworkUsageReader).GetMethod("RunRepair", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        bool ok = (bool)method!.Invoke(null, ["invalid_esentutl.exe", "invalid_db.dat"])!;
        ok.Should().BeFalse();
    }

    [Fact]
    public void EseNetworkUsageReader_RunRepair_WithProcessRunner_TestsSuccessAndFailure()
    {
        lock (SyncLock)
        {
            var method = typeof(EseNetworkUsageReader).GetMethod("RunRepair", BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();

            string tempDir = Path.Combine(Path.GetTempPath(), "LocalTelemetry");
            Directory.CreateDirectory(tempDir);
            string dummyInteg = Path.Combine(tempDir, $"test_{Guid.NewGuid():N}.INTEG.RAW");
            File.WriteAllText(dummyInteg, "temp");

            string testDbPath = Path.Combine(tempDir, "test.dat");

            var priorRunner = EseNetworkUsageReader.ProcessRunner;
            try
            {
                EseNetworkUsageReader.ProcessRunner = _ => 0;
                bool ok = (bool)method!.Invoke(null, ["esentutl.exe", testDbPath])!;
                ok.Should().BeTrue();

                for (int retry = 0; retry < 5 && File.Exists(dummyInteg); retry++)
                {
                    Thread.Yield();
                    method.Invoke(null, ["esentutl.exe", testDbPath]);
                }

                EseNetworkUsageReader.ProcessRunner = _ => 1;
                bool fail = (bool)method.Invoke(null, ["esentutl.exe", testDbPath])!;
                fail.Should().BeFalse();
            }
            finally
            {
                EseNetworkUsageReader.ProcessRunner = priorRunner;
                try { if (File.Exists(dummyInteg)) File.Delete(dummyInteg); } catch (Exception) { /* Best-effort cleanup */ }
            }
        }
    }

    [Fact]
    public async Task EseNetworkUsageReader_RunEsentutlCopy_WithProcessRunner_TestsBranches()
    {
        var method = typeof(EseNetworkUsageReader).GetMethod("RunEsentutlCopy", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        string tempDest = Path.Combine(Path.GetTempPath(), $"SRUDB_Copy_{Guid.NewGuid():N}.dat");
        await File.WriteAllTextAsync(tempDest, "mock");

        try
        {
            Task<bool> taskSuccess;
            Task<bool> taskFail;

            lock (SyncLock)
            {
                var priorRunner = EseNetworkUsageReader.ProcessRunner;
                try
                {
                    EseNetworkUsageReader.ProcessRunner = _ => 0;
                    taskSuccess = (Task<bool>)method!.Invoke(null, ["esentutl.exe", "srum.dat", tempDest, "/vss", CancellationToken.None])!;

                    EseNetworkUsageReader.ProcessRunner = _ => 1;
                    taskFail = (Task<bool>)method.Invoke(null, ["esentutl.exe", "srum.dat", tempDest, null, CancellationToken.None])!;
                }
                finally
                {
                    EseNetworkUsageReader.ProcessRunner = priorRunner;
                }
            }

            bool ok = await taskSuccess;
            ok.Should().BeTrue();

            bool fail = await taskFail;
            fail.Should().BeFalse();
        }
        finally
        {
            try { File.Delete(tempDest); } catch (Exception) { /* Best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task EseNetworkUsageReader_ReadUsageInternalAsync_MissingSrumDatabase_ReturnsEmpty()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"SRUDB_Missing_{Guid.NewGuid():N}.dat");

        var records = await EseNetworkUsageReader.ReadUsageInternalAsync(
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, missing,
            (exe, src, dst, flag, token) => Task.FromResult(true),
            (exe, db) => true,
            (dbPath, start, end) => [new WindowsNetworkUsageRecord()]);

        records.Should().BeEmpty();
    }

    [Fact]
    public async Task EseNetworkUsageReader_ReadUsageInternalAsync_AllCopyMethodsFail_ReturnsEmpty()
    {
        string tempSrum = Path.Combine(Path.GetTempPath(), $"SRUDB_CopyFail_{Guid.NewGuid():N}.dat");
        await File.WriteAllTextAsync(tempSrum, "mock esent database content");
        try
        {
            var records = await EseNetworkUsageReader.ReadUsageInternalAsync(
                DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, tempSrum,
                (exe, src, dst, flag, token) => Task.FromResult(false),
                (exe, db) => true,
                (dbPath, start, end) => [new WindowsNetworkUsageRecord()]);

            records.Should().BeEmpty();
        }
        finally
        {
            try { File.Delete(tempSrum); } catch (Exception) { /* Best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task EseNetworkUsageReader_ReadUsageInternalAsync_RepairFails_ReturnsEmpty()
    {
        string tempSrum = Path.Combine(Path.GetTempPath(), $"SRUDB_RepairFail_{Guid.NewGuid():N}.dat");
        await File.WriteAllTextAsync(tempSrum, "mock esent database content");
        try
        {
            var records = await EseNetworkUsageReader.ReadUsageInternalAsync(
                DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, tempSrum,
                (exe, src, dst, flag, token) => Task.FromResult(true),
                (exe, db) => false,
                (dbPath, start, end) => [new WindowsNetworkUsageRecord()]);

            records.Should().BeEmpty();
        }
        finally
        {
            try { File.Delete(tempSrum); } catch (Exception) { /* Best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task EseNetworkUsageReader_ReadUsageInternalAsync_FallsBackToSecondCopy_NormalizesDatesAndCleansUp()
    {
        string tempSrum = Path.Combine(Path.GetTempPath(), $"SRUDB_Fallback_{Guid.NewGuid():N}.dat");
        await File.WriteAllTextAsync(tempSrum, "mock esent database content");
        var flags = new List<string?>();
        DateTime? receivedStart = null;
        DateTime? receivedEnd = null;
        string? receivedDest = null;
        try
        {
            var records = await EseNetworkUsageReader.ReadUsageInternalAsync(
                new DateTime(2026, 8, 1, 12, 34, 56, DateTimeKind.Utc), new DateTime(2026, 8, 7, 23, 59, 59, DateTimeKind.Utc), tempSrum,
                (exe, src, dst, flag, token) =>
                {
                    flags.Add(flag);
                    return Task.FromResult(flag != "/vss");
                },
                (exe, db) => true,
                (dbPath, start, end) =>
                {
                    receivedDest = dbPath;
                    receivedStart = start;
                    receivedEnd = end;
                    return [new WindowsNetworkUsageRecord { Date = "01-08-2026", InterfaceType = "Ethernet", DownBytes = 1, UpBytes = 2 }];
                });

            records.Should().HaveCount(1);
            records[0].InterfaceType.Should().Be("Ethernet");
        }
        finally
        {
            try { File.Delete(tempSrum); } catch (Exception) { /* Best-effort cleanup */ }
        }

        flags.Count.Should().Be(2);
        flags[0].Should().Be("/vss");
        flags[1].Should().BeNull();
        receivedStart.Should().Be(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        receivedEnd.Should().Be(new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc));
        receivedDest.Should().NotBeNullOrEmpty();
        File.Exists(receivedDest!).Should().BeFalse();
    }

    private const string NetworkDataUsageTable = "{973F5D5C-1D90-4944-BE8E-24B94231A174}";
    private const string ColTimeStamp = "TimeStamp";
    private const string ColBytesSent = "BytesSent";
    private const string ColBytesRecvd = "BytesRecvd";
    private const string ColInterfaceLuid = "InterfaceLuid";

    private sealed record SrumRow(DateTime Timestamp, long Sent, long Recv, byte[]? Luid, double? RawOleTs = null);

    [Fact]
    public async Task EseNetworkUsageReader_ReadRecords_RealDatabase_ReadsAndAggregatesRecords()
    {
        // MaxTemporaryTables=0 forbids temporary tables, so Api.GetColumnDictionary cannot be
        // used; the reader must resolve each column id individually (Api.GetTableColumnid) and
        // scan the real ESENT database directly. This drives the full attach/open/scan path
        // against a real ESENT database and asserts the aggregated records.
        string dbPath = Path.Combine(Path.GetTempPath(), $"SRUDB_Build_{Guid.NewGuid():N}.dat");
        try
        {
            CreateSrumDatabase(dbPath, NetworkDataUsageTable,
            [
                ColTimeStamp, ColBytesSent, ColBytesRecvd, ColInterfaceLuid
            ],
            [
                new SrumRow(new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc), 1000, 2000, SrumLuid(6)),
                new SrumRow(new DateTime(2026, 8, 1, 13, 0, 0, DateTimeKind.Utc), 100, 200, SrumLuid(6)),
                new SrumRow(new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc), 3000, 4000, SrumLuid(71)),
                new SrumRow(new DateTime(2026, 8, 1, 14, 0, 0, DateTimeKind.Utc), 5000, 6000, new byte[4]),   // short luid -> skipped
                new SrumRow(new DateTime(2026, 8, 1, 15, 0, 0, DateTimeKind.Utc), 7000, 8000, SrumLuid(999)), // non-physical iana -> skipped
                new SrumRow(new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc), 9000, 10000, SrumLuid(6)), // out of range -> skipped
                new SrumRow(default, 1234, 5678, SrumLuid(6), RawOleTs: 0),                 // ole 0 (1899) -> skipped
                new SrumRow(default, 1234, 5678, SrumLuid(6), RawOleTs: double.NaN),        // non-finite -> skipped
            ]);

            var records = await EseNetworkUsageReader.ReadUsageInternalAsync(
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc), dbPath,
                (exe, src, dst, flag, token) =>
                {
                    File.Copy(src, dst, true);
                    return Task.FromResult(true);
                },
                (exe, db) => true,
                null);

            records.Should().HaveCount(2);
            records.Should().Contain(r => r.Date == "01-08-2026" && r.InterfaceType == "Ethernet" && r.DownBytes == 2200 && r.UpBytes == 1100);
            records.Should().Contain(r => r.Date == "02-08-2026" && r.InterfaceType == "WiFi" && r.DownBytes == 4000 && r.UpBytes == 3000);
        }
        finally
        {
            try { File.Delete(dbPath); } catch (Exception) { /* Best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task EseNetworkUsageReader_ReadRecords_TableNotFound_ReturnsEmpty()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"SRUDB_NoTable_{Guid.NewGuid():N}.dat");
        try
        {
            CreateSrumDatabase(dbPath, "SomeOtherTable",
            [
                ColTimeStamp, ColBytesSent, ColBytesRecvd, ColInterfaceLuid
            ], null);

            var records = await EseNetworkUsageReader.ReadUsageInternalAsync(
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc), dbPath,
                (exe, src, dst, flag, token) =>
                {
                    File.Copy(src, dst, true);
                    return Task.FromResult(true);
                },
                (exe, db) => true,
                null);

            records.Should().BeEmpty();
        }
        finally
        {
            try { File.Delete(dbPath); } catch (Exception) { /* Best-effort cleanup */ }
        }
    }


    private static byte[] SrumLuid(ushort ianaType)
    {
        var b = new byte[16];
        BitConverter.GetBytes(ianaType).CopyTo(b, 6);
        return b;
    }

    private static void CreateSrumDatabase(string dbPath, string tableName, string[] columns, List<SrumRow>? rows)
    {
        var instance = JET_INSTANCE.Nil;
        var sesid = JET_SESID.Nil;
        try
        {
            Api.JetCreateInstance(out instance, $"build-{Guid.NewGuid():N}");
            ConfigureJetInstance(instance);
            Api.JetInit(ref instance);

            Api.JetBeginSession(instance, out sesid, null, null);
            Api.JetCreateDatabase(sesid, dbPath, null, out var dbid, CreateDatabaseGrbit.None);
            Api.JetCreateTable(sesid, dbid, tableName, 1, 100, out var tableid);

            var colids = DefineSrumColumns(sesid, tableid, columns);
            InsertSrumRows(sesid, tableid, colids, rows);

            Api.JetCloseTable(sesid, tableid);
            Api.JetCloseDatabase(sesid, dbid, CloseDatabaseGrbit.None);
            Api.JetEndSession(sesid, EndSessionGrbit.None);
            sesid = JET_SESID.Nil;
        }
        finally
        {
            if (sesid != JET_SESID.Nil)
            {
                try { Api.JetEndSession(sesid, EndSessionGrbit.None); } catch (Exception) { /* Best-effort cleanup */ }
            }
            if (instance != JET_INSTANCE.Nil)
            {
                try { Api.JetTerm(instance); } catch (Exception) { /* Best-effort cleanup */ }
            }
        }
    }

    private static void ConfigureJetInstance(JET_INSTANCE instance)
    {
        try
        {
            Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.Recovery, 0, "Off");
            Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.CircularLog, 0, null);
            Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.DatabasePageSize, 32768, null);
        }
        catch (EsentAlreadyInitializedException) { /* Engine already configured */ }
    }

    private static Dictionary<string, JET_COLUMNID> DefineSrumColumns(JET_SESID sesid, JET_TABLEID tableid, string[] columns)
    {
        var colids = new Dictionary<string, JET_COLUMNID>();
        foreach (string c in columns)
        {
            var coldef = c switch
            {
                ColTimeStamp => new JET_COLUMNDEF { coltyp = JET_coltyp.DateTime, grbit = ColumndefGrbit.ColumnNotNULL },
                ColInterfaceLuid => new JET_COLUMNDEF { coltyp = JET_coltyp.Binary, cbMax = 16 },
                _ => new JET_COLUMNDEF { coltyp = (JET_coltyp)15, grbit = ColumndefGrbit.ColumnNotNULL },
            };
            Api.JetAddColumn(sesid, tableid, c, coldef, null, 0, out var colid);
            colids[c] = colid;
        }
        return colids;
    }

    private static void InsertSrumRows(JET_SESID sesid, JET_TABLEID tableid, Dictionary<string, JET_COLUMNID> colids, List<SrumRow>? rows)
    {
        if (rows is null) return;

        foreach (var r in rows)
        {
            Api.JetPrepareUpdate(sesid, tableid, JET_prep.Insert);
            Api.SetColumn(sesid, tableid, colids[ColTimeStamp], r.RawOleTs ?? r.Timestamp.ToOADate());
            Api.SetColumn(sesid, tableid, colids[ColBytesSent], r.Sent);
            Api.SetColumn(sesid, tableid, colids[ColBytesRecvd], r.Recv);
            if (r.Luid is not null)
                Api.SetColumn(sesid, tableid, colids[ColInterfaceLuid], r.Luid);
            Api.JetUpdate(sesid, tableid);
        }
    }

    [Fact]
    public void EseNetworkUsageReader_TryResolveColumns_MissingColumn_ReturnsFalse()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"missing_col_{Guid.NewGuid():N}.edb");
        // Create table with only 1 column, missing the required columns
        CreateSrumDatabase(dbPath, "{FEE4E40E-212D-4F39-93E9-74147024E1EC}", ["InterfaceLuid"], null);

        var instance = JET_INSTANCE.Nil;
        var sesid = JET_SESID.Nil;
        try
        {
            Api.JetCreateInstance(out instance, $"test-missing-{Guid.NewGuid():N}");
            ConfigureJetInstance(instance);
            Api.JetInit(ref instance);
            Api.JetBeginSession(instance, out sesid, null, null);
            Api.JetAttachDatabase(sesid, dbPath, AttachDatabaseGrbit.ReadOnly);
            Api.JetOpenDatabase(sesid, dbPath, null, out var dbid, OpenDatabaseGrbit.ReadOnly);
            Api.JetOpenTable(sesid, dbid, "{FEE4E40E-212D-4F39-93E9-74147024E1EC}", null, 0, OpenTableGrbit.ReadOnly, out var tableid);

            var tryResolve = typeof(EseNetworkUsageReader).GetMethod("TryResolveColumns", BindingFlags.NonPublic | BindingFlags.Static);
            tryResolve.Should().NotBeNull();
            object[] args = [sesid, tableid, null!];
            bool success = (bool)tryResolve!.Invoke(null, args)!;
            success.Should().BeFalse();

            Api.JetCloseTable(sesid, tableid);
            Api.JetCloseDatabase(sesid, dbid, CloseDatabaseGrbit.None);
            Api.JetDetachDatabase(sesid, dbPath);
        }
        finally
        {
            if (sesid != JET_SESID.Nil)
            {
                try { Api.JetEndSession(sesid, EndSessionGrbit.None); } catch (Exception) { /* Best-effort cleanup */ }
            }
            if (instance != JET_INSTANCE.Nil)
            {
                try { Api.JetTerm(instance); } catch (Exception) { /* Best-effort cleanup */ }
            }
            try { File.Delete(dbPath); } catch (Exception) { /* Best-effort cleanup */ }
        }
    }
}

