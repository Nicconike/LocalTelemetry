using System.Text.Json;

namespace LocalTelemetry.Core.Hardware;

/// <summary>Imports network traffic usage records from JSON Lines (<c>.jsonl</c>) backup files.</summary>
public static class JsonlTrafficImporter
{
    /// <summary>Parses JSONL content and merges valid records into <see cref="TrafficHistoryStore"/>.</summary>
    /// <param name="content">Raw JSONL string containing line-delimited JSON usage records.</param>
    /// <returns>Number of distinct days successfully imported or updated.</returns>
    public static int Import(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0;

        var uniqueDays = new HashSet<(int year, int month, int day)>();
        using var reader = new StringReader(content);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (TryParseRecord(line, out var record))
            {
                TrafficHistoryStore.SetDay(
                    record.Year,
                    record.Month,
                    record.Day,
                    record.DownBytes,
                    record.UpBytes,
                    string.IsNullOrEmpty(record.Interface) ? "Backup" : record.Interface,
                    string.IsNullOrEmpty(record.Source) ? "JsonlImport" : record.Source);

                uniqueDays.Add((record.Year, record.Month, record.Day));
            }
        }

        return uniqueDays.Count;
    }

    private static bool TryParseRecord(string line, out DailyRecord record)
    {
        record = default;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            string? dateStr = root.TryGetProperty("date", out var d) ? d.GetString() : null;
            if (string.IsNullOrWhiteSpace(dateStr) || dateStr.Length < 10) return false;

            if (!TryParseDate(dateStr, out int year, out int month, out int day)) return false;

            var (downBytes, upBytes) = ParseByteCounts(root);
            string iface = root.TryGetProperty("interface", out var i) ? i.GetString() ?? "" : "";
            string source = root.TryGetProperty("source", out var s) ? s.GetString() ?? "JsonlImport" : "JsonlImport";

            record = new DailyRecord(year, month, day, downBytes, upBytes, iface, source);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryParseDate(string s, out int year, out int month, out int day)
    {
        year = month = day = 0;
        if (s.Length >= 10 && (s[2] == '-' || s[2] == '/'))
        {
            char sep = s[2];
            var p = s.Split(sep);
            if (p.Length == 3 && int.TryParse(p[2], out year) && int.TryParse(p[1], out month) && int.TryParse(p[0], out day))
                return year >= 2000 && year <= 2100 && month >= 1 && month <= 12 && day >= 1 && day <= 31;
        }
        else if (s.Length >= 10 && s[4] == '-')
        {
            var p = s.Split('-');
            if (p.Length == 3 && int.TryParse(p[0], out year) && int.TryParse(p[1], out month) && int.TryParse(p[2], out day))
                return year >= 2000 && year <= 2100 && month >= 1 && month <= 12 && day >= 1 && day <= 31;
        }

        return false;
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
}
