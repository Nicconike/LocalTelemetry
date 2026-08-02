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

    private static void Log(string msg) => Diagnostics.Log.Info(msg);

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
                Log($"file not found: {_filePath}, starting empty");
                Count = 0;
                return;
            }

            int lineCount = 0, parsed = 0, skipped = 0;
            var seen = new HashSet<(int, int, int, string)>();
            try
            {
                foreach (string line in File.ReadLines(_filePath))
                {
                    lineCount++;
                    if (string.IsNullOrWhiteSpace(line)) { skipped++; continue; }

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        string? dateStr = root.TryGetProperty("date", out var d) ? d.GetString() : null;
                        if (dateStr is null || dateStr.Length < 10) { skipped++; continue; }

                        if (!TryParseDate(dateStr, out int year, out int month, out int day))
                        { skipped++; continue; }

                        long downBytes = 0, upBytes = 0;
                        if (root.TryGetProperty("download_bytes", out var db) && root.TryGetProperty("upload_bytes", out var ub))
                        {
                            downBytes = db.GetInt64();
                            upBytes = ub.GetInt64();
                        }
                        else if (root.TryGetProperty("down", out var dn) && root.TryGetProperty("up", out var up))
                        {
                            downBytes = dn.GetInt64();
                            upBytes = up.GetInt64();
                        }
                        else
                        {
                            downBytes = root.TryGetProperty("down_bytes", out var dnb) ? dnb.GetInt64() : 0;
                            upBytes = root.TryGetProperty("up_bytes", out var upb) ? upb.GetInt64() : 0;
                        }

                        string iface = root.TryGetProperty("interface", out var i) ? i.GetString() ?? "" : "";
                        string source = root.TryGetProperty("source", out var s) ? s.GetString() ?? "LocalTelemetry" : "LocalTelemetry";

                        var key = (year, month, day, iface);
                        if (!seen.Add(key))
                        {
                            skipped++;
                            continue;
                        }

                        _records.Add(new DailyRecord(year, month, day, downBytes, upBytes, iface, source));
                        parsed++;
                    }
                    catch (Exception ex)
                    {
                        skipped++;
                        Log($"skipped line {lineCount}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"read error: {ex.Message}");
            }

            _records.Sort((a, b) =>
            {
                int c = b.Year.CompareTo(a.Year);
                if (c != 0) return c;
                c = b.Month.CompareTo(a.Month);
                if (c != 0) return c;
                return b.Day.CompareTo(a.Day);
            });

            // Cleanup: remove empty-interface records superseded by a named-interface record for the same date
            var superseded = new HashSet<(int, int, int)>();
            foreach (var r in _records)
                if (!string.IsNullOrEmpty(r.Interface))
                    superseded.Add((r.Year, r.Month, r.Day));
            _records.RemoveAll(r =>
                string.IsNullOrEmpty(r.Interface) && superseded.Contains((r.Year, r.Month, r.Day)));

            Count = _records.Count;
            Log($"loaded {lineCount} lines, parsed {parsed} records, skipped {skipped}");
        }
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

                Log($"saved {_records.Count} records to {_filePath}");
                _dirtySinceLastSave = false;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Error($"save failed: {ex.Message}");
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
                _dirtySinceLastSave = true;
            }
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
