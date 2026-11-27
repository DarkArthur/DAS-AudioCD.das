using System.Text;

namespace DiscCheck.Core;

public enum ExportKind { Csv, Png, Svg, Html }

public enum ExportOutcome { Success, NoData, Cancelled, Declined, Failed }

/// <summary>Result of an export; <see cref="Message"/> is user-facing (Requirements 21.5, 21.7, 21.8).</summary>
public sealed record ExportResult(ExportOutcome Outcome, string? Path, string Message)
{
    public bool IsSuccess => Outcome == ExportOutcome.Success;
}

/// <summary>Produces PNG and SVG of the current chart (implemented by the App over <c>DasChartView</c>); UI thread only.</summary>
public interface IChartImageSource
{
    /// <summary>Writes the current chart (data, zoom, visible series) as a 2x PNG at <paramref name="path"/>.</summary>
    void ExportPng(string path);

    /// <summary>Writes the current chart as a vector SVG at <paramref name="path"/>.</summary>
    void ExportSvg(string path);
}

/// <summary>File dialogs of the export, abstracted so the Exporter is testable without UI.</summary>
public interface IExportDialogs
{
    /// <summary>Asks for the destination; null when the user cancels.</summary>
    Task<string?> PickSavePathAsync(ExportKind kind, string suggestedFileName);

    /// <summary>Asks whether the existing file at <paramref name="path"/> may be overwritten.</summary>
    Task<bool> ConfirmOverwriteAsync(string path);
}

/// <summary>Verification part of the HTML report (Requirement 21.4).</summary>
/// <param name="ReadOffset">Read_Offset of the Drive, null when unknown.</param>
/// <param name="CombinedOffset">Offset used by the verification (Combined_Offset or manual Read_Offset).</param>
public sealed record ReportVerification(DateTimeOffset VerifiedAt, VerificationResult Result, int? ReadOffset,
                                        int CombinedOffset, IReadOnlyList<VerificationTrackRecord> Tracks);

/// <summary>Everything the HTML report needs (Requirements 21.3, 21.4).</summary>
/// <param name="Attribution">Asset attribution required by the D5 licence option (Requirement 36.6); null when not required.</param>
public sealed record HtmlReportData(DiscRecord Disc, DriveRecord Drive, ScanRecord Scan,
                                    IReadOnlyList<ScanSample> Samples, ReportVerification? Verification = null,
                                    string? Attribution = null);

/// <summary>
/// Orchestrates CSV, PNG, SVG and HTML export: destination choice, overwrite confirmation, write through a
/// temporary file (the existing file is untouched on failure and the partial file is deleted), user-facing result
/// (Requirements 21.2-21.8, 21.11, 21.12). Call from the UI thread: chart images come from a UI control.
/// </summary>
public sealed class Exporter(IExportDialogs dialogs, Func<string?>? attribution = null)
{
    public const string NoDataMessage = "Non ci sono dati da esportare: la scansione non contiene campioni.";

    private readonly Func<string?> _attribution = attribution ?? (() => null);

    public Task<ExportResult> ExportCsvAsync(IReadOnlyList<ScanSample> samples, string suggestedName) =>
        RunAsync(ExportKind.Csv, samples, suggestedName, tmp =>
            File.WriteAllBytes(tmp, new UTF8Encoding(false).GetBytes(CsvExporter.Export(samples))));

    public Task<ExportResult> ExportPngAsync(IReadOnlyList<ScanSample> samples, IChartImageSource chart, string suggestedName)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return RunAsync(ExportKind.Png, samples, suggestedName, chart.ExportPng);
    }

    public Task<ExportResult> ExportSvgAsync(IReadOnlyList<ScanSample> samples, IChartImageSource chart, string suggestedName)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return RunAsync(ExportKind.Svg, samples, suggestedName, chart.ExportSvg);
    }

    /// <summary>Self-contained HTML report; <paramref name="chart"/> is embedded as a PNG data URI when available.</summary>
    public Task<ExportResult> ExportHtmlAsync(HtmlReportData data, IChartImageSource? chart, string suggestedName)
    {
        ArgumentNullException.ThrowIfNull(data);
        return RunAsync(ExportKind.Html, data.Samples, suggestedName, tmp =>
        {
            byte[]? png = null;
            if (chart is not null)
            {
                string imageTmp = tmp + ".png";
                try
                {
                    chart.ExportPng(imageTmp);
                    png = File.ReadAllBytes(imageTmp);
                }
                finally { TryDelete(imageTmp); }
            }
            var withAttribution = data.Attribution is null ? data with { Attribution = _attribution() } : data;
            File.WriteAllBytes(tmp, new UTF8Encoding(false).GetBytes(HtmlReportBuilder.Build(withAttribution, png)));
        });
    }

    private async Task<ExportResult> RunAsync(ExportKind kind, IReadOnlyList<ScanSample> samples, string suggestedName,
                                              Action<string> write)
    {
        if (samples.Count == 0)
            return new ExportResult(ExportOutcome.NoData, null, NoDataMessage); // no dialog, no file (Requirement 21.8)

        string? path = await dialogs.PickSavePathAsync(kind, suggestedName);
        if (string.IsNullOrWhiteSpace(path))
            return new ExportResult(ExportOutcome.Cancelled, null, "Export annullato.");

        if (File.Exists(path) && !await dialogs.ConfirmOverwriteAsync(path))
            return new ExportResult(ExportOutcome.Declined, path, "Export annullato: il file esistente non è stato modificato.");

        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            write(tmp);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            TryDelete(tmp); // no partial file is left behind (Requirement 21.7)
            return new ExportResult(ExportOutcome.Failed, path, $"Export non riuscito: {ex.Message} Percorso: {path}");
        }

        return new ExportResult(ExportOutcome.Success, Path.GetFullPath(path), $"Export completato: {Path.GetFullPath(path)}");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
