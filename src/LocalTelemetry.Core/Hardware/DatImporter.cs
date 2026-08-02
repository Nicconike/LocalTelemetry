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
        int imported = 0;
        using var reader = new StringReader(content);

        string? header = reader.ReadLine();
        if (header is null) return 0;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            ReadOnlySpan<char> span = line.AsSpan();
            if (span.Length < 16) continue;

            if (span[4] != '/' || span[7] != '/') continue;
            if (!int.TryParse(span[..4], NumberStyles.None, CultureInfo.InvariantCulture, out int year)) continue;
            if (!int.TryParse(span[5..7], NumberStyles.None, CultureInfo.InvariantCulture, out int month)) continue;
            if (!int.TryParse(span[8..10], NumberStyles.None, CultureInfo.InvariantCulture, out int day)) continue;

            if (year < 2000 || year > 2100) continue;

            int spaceIdx = span[10..].IndexOf(' ');
            if (spaceIdx < 0) continue;
            spaceIdx += 10;

            var rest = span[(spaceIdx + 1)..];
            int slashIdx = rest.IndexOf('/');
            if (slashIdx < 0) continue;

            if (!long.TryParse(rest[..slashIdx], NumberStyles.Integer, CultureInfo.InvariantCulture, out long upKB)) continue;
            if (!long.TryParse(rest[(slashIdx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long downKB)) continue;

            long upBytes = upKB * 1024;
            long downBytes = downKB * 1024;

            TrafficHistoryStore.SetDay(year, month, day, downBytes, upBytes, "import", "import");
            imported++;
        }
        return imported;
    }
}
