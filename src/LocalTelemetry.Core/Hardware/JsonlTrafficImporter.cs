using System.Globalization;
using System.Text.Json;

namespace LocalTelemetry.Core.Hardware;

/// <summary>Imports network traffic usage records from JSON Lines (<c>.jsonl</c>) or JSON array backup files.</summary>
public static class JsonlTrafficImporter
{
    /// <summary>Parses JSONL or JSON array content and merges valid records into <see cref="TrafficHistoryStore"/>.</summary>
    /// <param name="content">Raw string containing line-delimited JSON or a JSON array of usage records.</param>
    /// <returns>Number of distinct days successfully imported or updated.</returns>
    public static int Import(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0;

        string trimmed = content.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (string.IsNullOrWhiteSpace(trimmed)) return 0;

        var uniqueDays = new HashSet<(int year, int month, int day)>();

        if (trimmed.StartsWith('['))
        {
            ImportJsonArray(trimmed, uniqueDays);
        }
        else
        {
            ImportJsonLines(trimmed, uniqueDays);
        }

        return uniqueDays.Count;
    }

    private static void ImportJsonArray(string content, HashSet<(int year, int month, int day)> uniqueDays)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object && TryParseElement(element, out var record))
                {
                    ApplyRecord(record, uniqueDays);
                }
            }
        }
        catch (JsonException)
        {
            // Fall back to line-by-line reading if parsing entire array fails
            ImportJsonLines(content, uniqueDays);
        }
    }

    private static void ImportJsonLines(string content, HashSet<(int year, int month, int day)> uniqueDays)
    {
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (TryParseRecord(line, out var record))
            {
                ApplyRecord(record, uniqueDays);
            }
        }
    }

    private static void ApplyRecord(DailyRecord record, HashSet<(int year, int month, int day)> uniqueDays)
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

    private static bool TryParseRecord(string line, out DailyRecord record)
    {
        record = default;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            return TryParseElement(root, out record);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseElement(JsonElement root, out DailyRecord record)
    {
        record = default;

        string? dateStr = root.TryGetProperty("date", out var d) ? d.GetString() : null;
        if (string.IsNullOrWhiteSpace(dateStr)) return false;

        dateStr = dateStr.Trim();
        int tIdx = dateStr.IndexOfAny(['T', 't', ' ']);
        if (tIdx > 0)
        {
            dateStr = dateStr[..tIdx];
        }

        if (dateStr.Length < 10) return false;

        if (!TryParseDate(dateStr, out int year, out int month, out int day)) return false;

        if (!TryParseByteCounts(root, out long downBytes, out long upBytes)) return false;

        string iface = SanitizeString(root.TryGetProperty("interface", out var i) ? i.GetString() : null, 64, "Backup");
        string source = SanitizeString(root.TryGetProperty("source", out var s) ? s.GetString() : null, 64, "JsonlImport");

        record = new DailyRecord(year, month, day, downBytes, upBytes, iface, source);
        return true;
    }

    private static string SanitizeString(string? raw, int maxLength, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        string cleaned = raw.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (cleaned.Length > maxLength) cleaned = cleaned[..maxLength].Trim();
        return string.IsNullOrEmpty(cleaned) ? fallback : cleaned;
    }

    private static bool TryParseDate(string s, out int year, out int month, out int day)
    {
        year = month = day = 0;
        if (s.Length >= 10 && (s[2] == '-' || s[2] == '/'))
        {
            char sep = s[2];
            var p = s.Split(sep);
            if (p.Length == 3 && int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out year)
                && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out month)
                && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out day))
            {
                return year >= 2000 && year <= 2100 && month >= 1 && month <= 12 && day >= 1 && day <= 31;
            }
        }
        else if (s.Length >= 10 && (s[4] == '-' || s[4] == '/'))
        {
            char sep = s[4];
            var p = s.Split(sep);
            if (p.Length == 3 && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out year)
                && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out month)
                && int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out day))
            {
                return year >= 2000 && year <= 2100 && month >= 1 && month <= 12 && day >= 1 && day <= 31;
            }
        }

        return false;
    }

    private static bool TryParseByteCounts(JsonElement root, out long downBytes, out long upBytes)
    {
        downBytes = 0;
        upBytes = 0;

        if (root.TryGetProperty("download_bytes", out _) || root.TryGetProperty("upload_bytes", out _))
        {
            return TryReadByteCount(root, "download_bytes", out downBytes)
                && TryReadByteCount(root, "upload_bytes", out upBytes);
        }

        if (root.TryGetProperty("down", out _) || root.TryGetProperty("up", out _))
        {
            return TryReadByteCount(root, "down", out downBytes)
                && TryReadByteCount(root, "up", out upBytes);
        }

        if (root.TryGetProperty("down_bytes", out _) || root.TryGetProperty("up_bytes", out _))
        {
            return TryReadByteCount(root, "down_bytes", out downBytes)
                && TryReadByteCount(root, "up_bytes", out upBytes);
        }

        if (root.TryGetProperty("rx_bytes", out _) || root.TryGetProperty("tx_bytes", out _))
        {
            return TryReadByteCount(root, "rx_bytes", out downBytes)
                && TryReadByteCount(root, "tx_bytes", out upBytes);
        }

        if (root.TryGetProperty("bytes_in", out _) || root.TryGetProperty("bytes_out", out _))
        {
            return TryReadByteCount(root, "bytes_in", out downBytes)
                && TryReadByteCount(root, "bytes_out", out upBytes);
        }

        return true;
    }

    private static bool TryReadByteCount(JsonElement root, string propName, out long bytes)
    {
        bytes = 0;
        if (!root.TryGetProperty(propName, out var el))
        {
            return true;
        }

        if (el.ValueKind == JsonValueKind.Number)
        {
            return el.TryGetInt64(out bytes) && bytes >= 0;
        }

        if (el.ValueKind == JsonValueKind.String)
        {
            string? s = el.GetString();
            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out bytes) && bytes >= 0;
        }

        return false;
    }
}
