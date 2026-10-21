using System.Text.Json;

namespace LocalTelemetry.Core.Hardware;

/// <summary>Persists daily network traffic records as JSON Lines (<c>.jsonl</c>).</summary>
public sealed class TrafficHistoryFile
{
    private readonly string _filePath;
    private List<DailyRecord> _records = [];
    private readonly object _lock = new();
    private bool _dirtySinceLastSave;

    public string FilePath => _filePath;
    public int Count { get; private set; }

    private static void Log(string msg) => Diagnostics.Log.Debug(msg);

    public TrafficHistoryFile(string filePath)
    {
        _filePath = filePath;
    }

    public void Load()
    {
        lock (_lock)
        {
            _records = [];
            _dirtySinceLastSave = false;
            if (!File.Exists(_filePath))
            {
                Log($"Traffic history file not found: {_filePath}, starting empty");
                Count = 0;
                return;
            }

            ReadAndParseRecords();
            SortRecords(_records);
            RemoveSupersededRecords(_records);

            int uniqueDays = _records.Select(r => (r.Year, r.Month, r.Day)).Distinct().Count();
            Count = _records.Count;
            Log($"Loaded {_records.Count} records across {uniqueDays} days of network usage history from {Path.GetFileName(_filePath)}");
        }
    }

    private void ReadAndParseRecords()
    {
        int lineCount = 0;
        var seen = new HashSet<(int, int, int, string)>();
        try
        {
            foreach (string line in File.ReadLines(_filePath))
            {
                lineCount++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    if (TryParseRecord(line, out var record, out var key) && seen.Add(key))
                    {
                        _records.Add(record);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Skipped malformed traffic history line {lineCount}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to read traffic history file: {ex.Message}");
        }
    }

    private static bool TryParseRecord(string line, out DailyRecord record, out (int, int, int, string) key)
    {
        record = default;
        key = default;

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        string? dateStr = root.TryGetProperty("date", out var d) ? d.GetString() : null;
        if (dateStr is null || dateStr.Length < 10) return false;

        if (!TryParseDate(dateStr, out int year, out int month, out int day)) return false;

        var (downBytes, upBytes) = ParseByteCounts(root);
        string iface = root.TryGetProperty("interface", out var i) ? i.GetString() ?? "" : "";
        string rawSource = root.TryGetProperty("source", out var s) ? s.GetString() : null;
        string source = NormalizeSource(rawSource);

        key = (year, month, day, iface);
        record = new DailyRecord(year, month, day, downBytes, upBytes, iface, source);
        return true;
    }

    private static (long downBytes, long upBytes) ParseByteCounts(JsonElement root)
    {
        if (root.TryGetProperty("download_bytes", out var db) && root.TryGetProperty("upload_bytes", out var ub))
        {
            return (db.GetInt64(), ub.GetInt64());
        }
        if (root.TryGetProperty("down", out var dn) && root.TryGetProperty("up", out var up))
        {
            return (dn.GetInt64(), up.GetInt64());
        }
        long downBytes = root.TryGetProperty("down_bytes", out var dnb) ? dnb.GetInt64() : 0;
        long upBytes = root.TryGetProperty("up_bytes", out var upb) ? upb.GetInt64() : 0;
        return (downBytes, upBytes);
    }

    private static string NormalizeSource(string? source)
    {
        if (string.Equals(source, "imported", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, "srum", StringComparison.OrdinalIgnoreCase))
        {
            return "WindowsImport";
        }
        if (string.Equals(source, "import", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, "dat", StringComparison.OrdinalIgnoreCase))
        {
            return "DatImport";
        }
        return string.IsNullOrEmpty(source) ? "LocalTelemetry" : source;
    }

    private static void SortRecords(List<DailyRecord> records)
    {
        records.Sort((a, b) =>
        {
            int c = b.Year.CompareTo(a.Year);
            if (c != 0) return c;
            c = b.Month.CompareTo(a.Month);
            if (c != 0) return c;
            return b.Day.CompareTo(a.Day);
        });
    }

    private static void RemoveSupersededRecords(List<DailyRecord> records)
    {
        var superseded = new HashSet<(int, int, int)>();
        foreach (var r in records.Where(r => !string.IsNullOrEmpty(r.Interface)))
        {
            superseded.Add((r.Year, r.Month, r.Day));
        }
        records.RemoveAll(r => string.IsNullOrEmpty(r.Interface) && superseded.Contains((r.Year, r.Month, r.Day)));
    }

    private static bool TryParseDate(string s, out int year, out int month, out int day)
    {
        year = month = day = 0;
        if (s[2] == '-' || s[2] == '/')
        {
            char sep = s[2];
            var p = s.Split(sep);
            if (p.Length == 3 && int.TryParse(p[2], out year) && int.TryParse(p[1], out month) && int.TryParse(p[0], out day))
                return true;
        }
        else if (s[4] == '-')
        {
            var p = s.Split('-');
            if (p.Length == 3 && int.TryParse(p[0], out year) && int.TryParse(p[1], out month) && int.TryParse(p[2], out day))
                return true;
        }
        return false;
    }

    public void Save()
    {
        lock (_lock)
        {
            if (!_dirtySinceLastSave) return;

            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using var stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream);

                // Sort descending so file always has newest-first order regardless of how records were added.
                _records.Sort((a, b) =>
                {
                    int c = b.Year.CompareTo(a.Year);
                    if (c != 0) return c;
                    c = b.Month.CompareTo(a.Month);
                    if (c != 0) return c;
                    return b.Day.CompareTo(a.Day);
                });

                foreach (var r in _records)
                {
                    writer.WriteLine(FormatLine(r));
                }

                _dirtySinceLastSave = false;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Error(ex, "Failed to save network traffic history");
            }
        }
    }

    private static string FormatLine(DailyRecord r)
    {
        return "{\"date\":\"" +
            $"{r.Day:D2}-{r.Month:D2}-{r.Year:D4}" +
            "\",\"download_bytes\":" + r.DownBytes +
            ",\"upload_bytes\":" + r.UpBytes +
            ",\"total_bytes\":" + r.TotalBytes +
            ",\"interface\":\"" + EscapeJson(r.Interface) +
            "\",\"source\":\"" + EscapeJson(r.Source) +
            "\"}";
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>Finds a record for the given date and interface name.</summary>
    public DailyRecord? Find(int year, int month, int day, string interfaceName)
    {
        lock (_lock)
        {
            return _records.FirstOrDefault(r =>
                r.Year == year && r.Month == month && r.Day == day &&
                string.Equals(r.Interface, interfaceName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Replaces any existing record for the given (date, interface) or adds a new one.</summary>
    public void SetDay(int year, int month, int day, long downBytes, long upBytes, string interfaceName, string source)
    {
        lock (_lock)
        {
            int idx = _records.FindIndex(r =>
                r.Year == year && r.Month == month && r.Day == day &&
                string.Equals(r.Interface, interfaceName, StringComparison.OrdinalIgnoreCase));

            if (idx >= 0)
            {
                _records[idx] = new DailyRecord(year, month, day, downBytes, upBytes, interfaceName, source);
            }
            else
            {
                _records.Add(new DailyRecord(year, month, day, downBytes, upBytes, interfaceName, source));
            }
            _dirtySinceLastSave = true;
            Count = _records.Count;
        }
    }

    public List<DailyRecord> GetMonth(int year, int month)
    {
        lock (_lock)
        {
            return [.. _records.Where(r => r.Year == year && r.Month == month)];
        }
    }

    /// <summary>Returns whether any record exists for the given date.</summary>
    public bool HasRecordOn(DateTime date)
    {
        lock (_lock)
        {
            return _records.Any(r => r.Year == date.Year && r.Month == date.Month && r.Day == date.Day);
        }
    }

    public (long downBytes, long upBytes) GetToday()
    {
        lock (_lock)
        {
            var today = DateTime.Now.Date;
            long down = 0, up = 0;
            foreach (var r in _records)
            {
                if (r.Year == today.Year && r.Month == today.Month && r.Day == today.Day)
                {
                    down += r.DownBytes;
                    up += r.UpBytes;
                }
            }
            return (down, up);
        }
    }

    public List<string> GetAvailableMonths()
    {
        lock (_lock)
        {
            var months = new HashSet<string>();
            foreach (var r in _records)
            {
                if (r.Year > 1900 && r.Month >= 1 && r.Month <= 12)
                    months.Add($"{r.Year:D4}-{r.Month:D2}");
            }
            return [.. months.OrderByDescending(m => m)];
        }
    }

    public List<DailyRecord> GetAllRecords()
    {
        lock (_lock)
        {
            return [.. _records];
        }
    }
}

public readonly record struct DailyRecord(int Year, int Month, int Day, long DownBytes, long UpBytes, string Interface, string Source)
{
    public long TotalBytes => DownBytes + UpBytes;
}
