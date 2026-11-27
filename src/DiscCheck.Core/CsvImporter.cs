using System.Globalization;

namespace DiscCheck.Core;

/// <summary>
/// Parsing inverso di <see cref="CsvExporter"/>; usato solo nei test (Req. 21.10). Il CSV non contiene
/// l'LBA, quindi i campioni importati hanno <c>Lba</c> = 0.
/// </summary>
public static class CsvImporter
{
    public static IReadOnlyList<ScanSample> Import(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        var lines = csv.Split(["\r\n", "\n"], StringSplitOptions.None);
        if (lines.Length == 0 || lines[0].TrimStart('\uFEFF') != CsvExporter.Header)
            throw new FormatException("Intestazione CSV non valida.");

        var result = new List<ScanSample>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            var f = lines[i].Split(',');
            if (f.Length != 5) throw new FormatException($"Riga {i + 1}: attesi 5 campi.");
            result.Add(new ScanSample(
                0,
                int.Parse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture),
                f[1].Length == 0 ? null : int.Parse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture),
                int.Parse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture),
                f[3].Length == 0 ? null : int.Parse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture),
                f[4].Length == 0 ? null : double.Parse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture)));
        }
        return result;
    }
}
