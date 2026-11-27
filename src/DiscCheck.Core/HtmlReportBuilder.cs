using System.Globalization;
using System.Net;
using System.Text;

namespace DiscCheck.Core;

/// <summary>
/// Builds the self-contained HTML report: inline CSS, chart as <c>data:image/png;base64</c>, no external resource
/// (Requirements 21.3, 21.4). Numbers use the invariant culture; all text is HTML-encoded.
/// </summary>
public static class HtmlReportBuilder
{
    private const string Na = "n/d";

    public static string Build(HtmlReportData data, byte[]? chartPng)
    {
        ArgumentNullException.ThrowIfNull(data);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"it\">\n<head>\n<meta charset=\"utf-8\">\n<title>Report scansione DiscCheck</title>\n")
          .Append("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1b1b1b}")
          .Append("table{border-collapse:collapse;margin:8px 0}th,td{border:1px solid #888;padding:4px 10px;text-align:left}")
          .Append("img{max-width:100%;height:auto}.OK{color:#0a6b2d}.WARN{color:#8a5a00}.KO{color:#b00020}</style>\n</head>\n<body>\n");
        sb.Append("<h1>Report scansione</h1>\n");

        var d = data.Disc;
        sb.Append("<h2>Disco</h2>\n<table>\n");
        Row(sb, "Etichetta", d.Label);
        Row(sb, "Tracce", d.TrackCount.ToString(inv));
        Row(sb, "Settori totali", d.TotalSectors.ToString(inv));
        Row(sb, "TOC hash", d.TocHash);
        sb.Append("</table>\n");

        var dr = data.Drive;
        sb.Append("<h2>Drive</h2>\n<table>\n");
        Row(sb, "Produttore", dr.Vendor);
        Row(sb, "Modello", dr.Model);
        Row(sb, "Firmware", dr.Firmware);
        Row(sb, "Read_Offset", dr.ReadOffset?.ToString(inv));
        sb.Append("</table>\n");

        var s = data.Scan;
        sb.Append("<h2>Verdetto</h2>\n<p class=\"").Append(Enc(s.Verdict.ToString())).Append("\"><strong>")
          .Append(Enc(s.Verdict.ToString())).Append("</strong></p>\n<table>\n");
        Row(sb, "Data", s.ScannedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", inv));
        Row(sb, "Velocità", s.SpeedX.ToString("0.#", inv) + "x");
        Row(sb, "Scanner", s.Scanner.ToString());
        sb.Append("</table>\n");

        sb.Append("<h2>Riepilogo</h2>\n<table>\n<tr><th></th><th>Massimo</th><th>Media</th></tr>\n");
        Stat(sb, "C1", data.Samples.Where(x => x.C1 is not null).Select(x => (double)x.C1!.Value));
        Stat(sb, "C2", data.Samples.Select(x => (double)x.C2));
        Stat(sb, "CU", data.Samples.Where(x => x.CU is not null).Select(x => (double)x.CU!.Value));
        sb.Append("</table>\n");

        sb.Append("<h2>Grafico</h2>\n");
        if (chartPng is { Length: > 0 })
            sb.Append("<img alt=\"Grafico della scansione\" src=\"data:image/png;base64,")
              .Append(Convert.ToBase64String(chartPng)).Append("\">\n");
        else
            sb.Append("<p>Grafico non disponibile.</p>\n");

        if (data.Verification is { } v)
        {
            sb.Append("<h2>Verifica</h2>\n<table>\n");
            Row(sb, "Data", v.VerifiedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", inv));
            Row(sb, "Esito", v.Result.ToString());
            Row(sb, "Read_Offset", v.ReadOffset?.ToString(inv));
            Row(sb, "Combined_Offset", v.CombinedOffset.ToString(inv));
            Row(sb, "Campioni di bordo esclusi", v.Tracks.Sum(t => t.ExcludedEdgeSamples).ToString(inv));
            sb.Append("</table>\n<table>\n<tr><th>Traccia</th><th>Esito</th><th>Campioni diversi</th><th>Campioni di bordo esclusi</th></tr>\n");
            foreach (var t in v.Tracks.OrderBy(t => t.TrackNo))
                sb.Append("<tr><td>").Append(t.TrackNo.ToString(inv)).Append("</td><td>")
                  .Append(t.Identical ? "Identica" : "Diversa").Append("</td><td>")
                  .Append(t.DifferentSamples.ToString(inv)).Append("</td><td>")
                  .Append(t.ExcludedEdgeSamples.ToString(inv)).Append("</td></tr>\n");
            sb.Append("</table>\n");
        }

        if (!string.IsNullOrWhiteSpace(data.Attribution))
            sb.Append("<footer><p>").Append(Enc(data.Attribution)).Append("</p></footer>\n");

        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    private static void Row(StringBuilder sb, string label, string? value) =>
        sb.Append("<tr><th>").Append(Enc(label)).Append("</th><td>")
          .Append(Enc(string.IsNullOrWhiteSpace(value) ? Na : value)).Append("</td></tr>\n");

    private static void Stat(StringBuilder sb, string name, IEnumerable<double> values)
    {
        var list = values.ToList();
        string max = list.Count > 0 ? list.Max().ToString("0.##", CultureInfo.InvariantCulture) : Na;
        string mean = list.Count > 0 ? list.Average().ToString("0.00", CultureInfo.InvariantCulture) : Na;
        sb.Append("<tr><th>").Append(name).Append("</th><td>").Append(max).Append("</td><td>").Append(mean).Append("</td></tr>\n");
    }
}
