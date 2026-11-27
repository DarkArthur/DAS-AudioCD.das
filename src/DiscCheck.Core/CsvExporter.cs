using System.Globalization;
using System.Text;

namespace DiscCheck.Core;

/// <summary>
/// Esporta i campioni in CSV invariante rispetto alla cultura: intestazione
/// <c>TimeSeconds,C1,C2,CU,SpeedX</c>, UTF-8 (senza BOM), virgola, punto decimale, <c>\r\n</c>,
/// valori nulli (C1, CU, SpeedX) come campo vuoto (Req. 21.1, 21.9).
/// </summary>
public static class CsvExporter
{
    public const string Header = "TimeSeconds,C1,C2,CU,SpeedX";

    public static string Export(IEnumerable<ScanSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var sb = new StringBuilder();
        sb.Append(Header).Append("\r\n");
        foreach (var s in samples)
        {
            sb.Append(s.TimeSeconds.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(s.C1?.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(s.C2.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(s.CU?.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(s.SpeedX?.ToString("R", CultureInfo.InvariantCulture)).Append("\r\n");
        }
        return sb.ToString();
    }

    public static void Write(Stream stream, IEnumerable<ScanSample> samples)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var bytes = new UTF8Encoding(false).GetBytes(Export(samples));
        stream.Write(bytes, 0, bytes.Length);
    }
}
