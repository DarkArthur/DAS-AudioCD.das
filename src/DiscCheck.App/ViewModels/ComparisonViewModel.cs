using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAS.Design.Charts;
using DAS.Design.Controls;
using DiscCheck.App.Charts;
using DiscCheck.Core;
using Serilog;

namespace DiscCheck.App.ViewModels;

/// <summary>A row of the comparison table with display texts (Requirement 19.4). <see cref="Index"/> selects the series colour.</summary>
public sealed record ComparisonTableRow(int Index, string Legend, string C1Mean, string C1Max, string C2Total,
                                        string CuTotal, string VerdictText, DasStatus Status);

/// <summary>
/// Confronto view: 2 to 8 saved scans overlaid in a <c>DasChartView</c> (one <see cref="ChartSeriesKind.CompareCurve"/>
/// per scan), table, warnings and the speed wizard (Requirements 19.1-19.8). The logic lives in
/// <see cref="ComparisonService"/> and <see cref="SpeedProposer"/>; the chart goes through <see cref="ChartSampleMapper"/>.
/// </summary>
public sealed partial class ComparisonViewModel : ObservableObject
{
    private readonly IArchive _archive;
    private readonly DriveViewModel? _drives;
    private Dictionary<long, string> _mediumByScan = [];
    private IReadOnlyList<ScanRowViewModel> _selected = [];
    private int _version;

    public ComparisonViewModel(IArchive archive, DriveViewModel? drives = null)
    {
        _archive = archive;
        _drives = drives;
    }

    public IReadOnlyList<CompareMetric> Metrics { get; } = [CompareMetric.C1, CompareMetric.C2, CompareMetric.CU];

    [ObservableProperty] private CompareMetric _metric = CompareMetric.C1;
    [ObservableProperty] private IReadOnlyList<ScanRowViewModel> _scans = [];
    [ObservableProperty] private IReadOnlyList<ComparisonTableRow> _rows = [];
    [ObservableProperty] private IReadOnlyList<ChartSeries>? _chartSeriesList;
    [ObservableProperty] private ChartAxisSpec _axisSpec = BuildAxisSpec(CompareMetric.C1);
    [ObservableProperty] private string? _limitMessage;
    [ObservableProperty] private string? _minimumMessage;
    [ObservableProperty] private string? _warning;
    [ObservableProperty] private string? _loadError;
    [ObservableProperty] private string? _wizardMessage;
    [ObservableProperty] private IReadOnlyList<string> _proposedSpeeds = [];

    public bool HasLimitMessage => !string.IsNullOrEmpty(LimitMessage);
    public bool HasMinimumMessage => !string.IsNullOrEmpty(MinimumMessage);
    public bool HasWarning => !string.IsNullOrEmpty(Warning);
    public bool HasLoadError => !string.IsNullOrEmpty(LoadError);
    public bool HasWizardMessage => !string.IsNullOrEmpty(WizardMessage);
    public bool HasRows => Rows.Count > 0;

    partial void OnLimitMessageChanged(string? value) => OnPropertyChanged(nameof(HasLimitMessage));
    partial void OnMinimumMessageChanged(string? value) => OnPropertyChanged(nameof(HasMinimumMessage));
    partial void OnWarningChanged(string? value) => OnPropertyChanged(nameof(HasWarning));
    partial void OnLoadErrorChanged(string? value) => OnPropertyChanged(nameof(HasLoadError));
    partial void OnWizardMessageChanged(string? value) => OnPropertyChanged(nameof(HasWizardMessage));
    partial void OnRowsChanged(IReadOnlyList<ComparisonTableRow> value) => OnPropertyChanged(nameof(HasRows));
    partial void OnMetricChanged(CompareMetric value) => _ = RefreshAsync();

    /// <summary>Y axis named after the chosen metric with unit "errori/s" (Requirement 38.5).</summary>
    public static ChartAxisSpec BuildAxisSpec(CompareMetric metric) => new("Tempo (mm:ss)", $"{metric} (errori/s)");

    /// <summary>Reads the saved scans, newest first, each time the view is opened.</summary>
    public async Task LoadAsync()
    {
        try
        {
            var snap = await Task.Run(() => _archive.Execute(HistorySnapshot.Load));
            var media = snap.Media.ToDictionary(m => m.Id);
            var discs = snap.Discs.ToDictionary(d => d.Id);
            var drives = snap.Drives.ToDictionary(d => d.Id);
            var ci = CultureInfo.CurrentCulture;

            string MediumOf(long discId) =>
                discs.TryGetValue(discId, out var d) && d.MediaId is long id && media.TryGetValue(id, out var m)
                    ? HistoryFilter.FormatMedium(m) : string.Empty;

            _mediumByScan = snap.Scans.ToDictionary(s => s.Id, s => MediumOf(s.DiscId));
            Scans = snap.Scans.OrderByDescending(s => s.ScannedAt).ThenByDescending(s => s.Id)
                .Select(s => new ScanRowViewModel(s.Id, s.ScannedAt.ToLocalTime().ToString("g", ci),
                    discs.TryGetValue(s.DiscId, out var d) && !string.IsNullOrWhiteSpace(d.Label) ? d.Label! : $"#{s.DiscId}",
                    drives.TryGetValue(s.DriveId, out var dr) ? $"{dr.Vendor} {dr.Model}".Trim() : $"#{s.DriveId}",
                    HistoryFilter.FormatSpeed(s.SpeedX), s.Scanner.ToString(), s.Verdict.ToString(),
                    HistoryViewModel.ToStatus(s.Verdict), s))
                .ToList();
            LoadError = null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reading the scans for the comparison failed");
            LoadError = $"Impossibile leggere le scansioni: {ex.Message}";
        }
    }

    /// <summary>
    /// Sets the selected scans (in selection order). Only the first 8 are kept (Requirement 19.2); returns
    /// the number of scans kept so the view can drop the others from its selection.
    /// </summary>
    public async Task<int> SetSelectionAsync(IReadOnlyList<ScanRowViewModel> selected)
    {
        _selected = selected.Take(ComparisonService.MaxScans).ToList();
        await RefreshAsync(selected.Count);
        return _selected.Count;
    }

    private Task RefreshAsync() => RefreshAsync(_selected.Count);

    private async Task RefreshAsync(int requestedCount)
    {
        int version = Interlocked.Increment(ref _version);
        var selection = _selected;
        var metric = Metric;
        try
        {
            var inputs = await Task.Run(() => _archive.Execute(r => selection
                .Select(row => new ComparisonInput(row.Record, r.Scans.GetSamples(row.Id), row.Drive,
                                                   _mediumByScan.GetValueOrDefault(row.Id, string.Empty)))
                .ToList()));
            if (version != Volatile.Read(ref _version)) return;

            var result = ComparisonService.Compare(inputs);
            LimitMessage = requestedCount > ComparisonService.MaxScans ? ComparisonService.LimitMessage : result.LimitMessage;
            MinimumMessage = result.MinimumMessage;
            Warning = result.MismatchWarning;
            Rows = result.Rows.Select(ToTableRow).ToList();
            AxisSpec = BuildAxisSpec(metric);
            // A single scan is not overlaid (Requirement 19.5).
            ChartSeriesList = result.CanOverlay
                ? result.Accepted.Select((a, i) => ChartSampleMapper.ToCompareSeries(ComparisonService.Legend(a), i, a.Samples, metric)).ToList()
                : null;
            LoadError = null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Building the comparison failed");
            if (version != Volatile.Read(ref _version)) return;
            ChartSeriesList = null;
            Rows = [];
            LoadError = $"Impossibile leggere i campioni delle scansioni: {ex.Message}";
        }
    }

    private static ComparisonTableRow ToTableRow(ComparisonRow r)
    {
        var ci = CultureInfo.CurrentCulture;
        string na = ComparisonService.NotAvailable;
        return new ComparisonTableRow(r.Index, r.Legend,
            r.C1Mean is double m ? m.ToString("0.00", ci) : na,
            r.C1Max?.ToString(ci) ?? na,
            r.C2Total?.ToString(ci) ?? na,
            r.CuTotal?.ToString(ci) ?? na,
            r.Verdict.ToString(), HistoryViewModel.ToStatus(r.Verdict));
    }

    /// <summary>
    /// "Ricerca velocità ottimale": proposes the speeds of the selected drive (Requirements 19.6, 19.7). The current
    /// selection of scans is left unchanged.
    /// </summary>
    [RelayCommand]
    public void ProposeSpeeds()
    {
        var speeds = _drives?.SelectedDrive?.Item.Info?.SupportedSpeedsKBps ?? [];
        var proposal = SpeedProposer.Propose(speeds);
        var ci = CultureInfo.CurrentCulture;
        ProposedSpeeds = proposal.SpeedsX.Select(x => $"{x.ToString("0.#", ci)}x").ToList();
        WizardMessage = proposal.Message
            ?? $"Velocità da provare: {string.Join(", ", ProposedSpeeds)}. Esegui una scansione a ciascuna velocità e confrontale qui.";
    }
}
