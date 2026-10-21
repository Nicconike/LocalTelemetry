using System.Globalization;

namespace LocalTelemetry.Core.Hardware;

/// <summary>Imports daily network usage records from external .dat files (plain text, KB values).</summary>
public static class DatImporter
{
    /// <summary>Parses .dat file content and writes records into <see cref="TrafficHistoryStore"/>.</summary>
    /// <param name="content">Raw text content of the .dat file.</param>
    /// <returns>Number of days successfully imported.</returns>
    public static int Import(string content)
    {
        var uniqueDays = new HashSet<(int year, int month, int day)>();
        using var reader = new StringReader(content);

        string? header = reader.ReadLine();
        if (header is null) return 0;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (TryParseLine(line.AsSpan(), out int year, out int month, out int day, out long downBytes, out long upBytes))
            {
                TrafficHistoryStore.SetDay(year, month, day, downBytes, upBytes, "Dat File", "DatImport");
                uniqueDays.Add((year, month, day));
            }
        }
        return uniqueDays.Count;
    }

    private static bool TryParseLine(ReadOnlySpan<char> span, out int year, out int month, out int day, out long downBytes, out long upBytes)
    {
        year = month = day = 0;
        downBytes = upBytes = 0;
        if (span.Length < 16 || !TryParseDate(span, out year, out month, out day))
        {
            return false;
        }

        return TryParseBandwidth(span, out downBytes, out upBytes);
    }

    private static bool TryParseDate(ReadOnlySpan<char> span, out int year, out int month, out int day)
    {
        year = month = day = 0;
        if (span[4] != '/' || span[7] != '/') return false;
        if (!int.TryParse(span[..4], NumberStyles.None, CultureInfo.InvariantCulture, out year)) return false;
        if (!int.TryParse(span[5..7], NumberStyles.None, CultureInfo.InvariantCulture, out month)) return false;
        if (!int.TryParse(span[8..10], NumberStyles.None, CultureInfo.InvariantCulture, out day)) return false;

        return year >= 2000 && year <= 2100;
    }

    private static bool TryParseBandwidth(ReadOnlySpan<char> span, out long downBytes, out long upBytes)
    {
        downBytes = upBytes = 0;
        int spaceIdx = span[10..].IndexOf(' ');
        if (spaceIdx < 0) return false;

        var rest = span[(spaceIdx + 11)..];
        int slashIdx = rest.IndexOf('/');
        if (slashIdx < 0) return false;

        if (!long.TryParse(rest[..slashIdx], NumberStyles.Integer, CultureInfo.InvariantCulture, out long upKb)) return false;
        if (!long.TryParse(rest[(slashIdx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long downKb)) return false;

        upBytes = upKb * 1024;
        downBytes = downKb * 1024;
        return true;
    }
}
