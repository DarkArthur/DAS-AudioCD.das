using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAS.Design.Charts;
using DAS.Design.Controls;
using DiscCheck.App.Charts;
using DiscCheck.Core;
using Serilog;

namespace DiscCheck.App.ViewModels;

public sealed record DiscRowViewModel(long Id, string Label, string Medium, int TrackCount, string LastActivity,
                                      DateTimeOffset? LastActivityAt, DiscRecord Record);

public sealed record ScanRowViewModel(long Id, string Date, string Disc, string Drive, string Speed, string Scanner,
                                      string VerdictText, DasStatus Status, ScanRecord Record);

public sealed record VerificationRowViewModel(long Id, string Date, string Disc, string Result, int OffsetUsed,
                                              VerificationRecord Record);

public sealed record VerificationTrackRowViewModel(int TrackNo, string Crc32, string Sha256, string Outcome);

/// <summary>
/// Storico view: three lists (discs, scans, verifications) newest first, AND filters, scan summary and chart,
/// verification table, confirmed deletion of scans (Requirements 17.1-17.9). Reads through <see cref="IArchive"/>;
/// the chart goes through <see cref="ChartSampleMapper"/> and <c>DasChartView</c> only (Requirement 38.15).
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    public const string NoMatchMessage = "Nessun record corrisponde ai filtri.";
    public const string EmptyMessage = "Lo storico è vuoto.";
    public const string NotAvailable = "n/d";

    private readonly IArchive _archive;
    private HistorySnapshot _snapshot = HistorySnapshot.Empty;
    private bool _applying;
    private int _selectionVersion;

    private readonly SettingsStore? _settings;

    private readonly Exporter? _exporter;
    private IReadOnlyList<ScanSample> _detailSamples = [];

    public HistoryViewModel(IArchive archive, SettingsStore? settings = null, Exporter? exporter = null)
    {
        _exporter = exporter;
        _archive = archive;
        _settings = settings;
        if (settings is not null) settings.Changed += _ => UpdateThresholds();
    }

    public ChartAxisSpec AxisSpec { get; } = new("Tempo (mm:ss)", "Errori per campione", "Velocità (x)");

    // ---- filters (Requirement 17.2) ----------------------------------------------------------------------

    [ObservableProperty] private string _mediumFilter = string.Empty;
    [ObservableProperty] private string _speedFilter = string.Empty;
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;
    [ObservableProperty] private bool _filterOk;
    [ObservableProperty] private bool _filterWarn;
    [ObservableProperty] private bool _filterKo;

    partial void OnMediumFilterChanged(string value) => ApplyFilters();
    partial void OnSpeedFilterChanged(string value) => ApplyFilters();
    partial void OnFromDateChanged(DateTime? value) => ApplyFilters();
    partial void OnToDateChanged(DateTime? value) => ApplyFilters();
    partial void OnFilterOkChanged(bool value) => ApplyFilters();
    partial void OnFilterWarnChanged(bool value) => ApplyFilters();
    partial void OnFilterKoChanged(bool value) => ApplyFilters();

    // ---- lists -------------------------------------------------------------------------------------------

    [ObservableProperty] private IReadOnlyList<DiscRowViewModel> _discs = [];
    [ObservableProperty] private IReadOnlyList<ScanRowViewModel> _scans = [];
    [ObservableProperty] private IReadOnlyList<VerificationRowViewModel> _verifications = [];

    [ObservableProperty] private string? _discsEmptyMessage;
    [ObservableProperty] private string? _scansEmptyMessage;
    [ObservableProperty] private string? _verificationsEmptyMessage;

    [ObservableProperty] private string? _loadError;
    [ObservableProperty] private string? _filterError;
    [ObservableProperty] private string? _deleteMessage;

    public bool HasLoadError => !string.IsNullOrEmpty(LoadError);
    public bool HasFilterError => !string.IsNullOrEmpty(FilterError);
    public bool HasDeleteMessage => !string.IsNullOrEmpty(DeleteMessage);
    public bool HasDiscsEmptyMessage => !string.IsNullOrEmpty(DiscsEmptyMessage);
    public bool HasScansEmptyMessage => !string.IsNullOrEmpty(ScansEmptyMessage);
    public bool HasVerificationsEmptyMessage => !string.IsNullOrEmpty(VerificationsEmptyMessage);

    partial void OnLoadErrorChanged(string? value) => OnPropertyChanged(nameof(HasLoadError));
    partial void OnFilterErrorChanged(string? value) => OnPropertyChanged(nameof(HasFilterError));
    partial void OnDeleteMessageChanged(string? value) => OnPropertyChanged(nameof(HasDeleteMessage));
    partial void OnDiscsEmptyMessageChanged(string? value) => OnPropertyChanged(nameof(HasDiscsEmptyMessage));
    partial void OnScansEmptyMessageChanged(string? value) => OnPropertyChanged(nameof(HasScansEmptyMessage));
    partial void OnVerificationsEmptyMessageChanged(string? value) => OnPropertyChanged(nameof(HasVerificationsEmptyMessage));

    /// <summary>Reads the Database and refreshes the lists; on failure the last good lists stay visible (Requirement 17.9).</summary>
    public async Task LoadAsync()
    {
        try
        {
            var snapshot = await Task.Run(() => _archive.Execute(HistorySnapshot.Load));
            _snapshot = snapshot;
            LoadError = null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reading the history failed");
            LoadError = $"Impossibile leggere lo storico: {ex.Message}";
            return;
        }
        ApplyFilters();
    }

    [RelayCommand]
    public void ClearFilters()
    {
        _applying = true;
        try
        {
            MediumFilter = string.Empty;
            SpeedFilter = string.Empty;
            FromDate = null;
            ToDate = null;
            FilterOk = FilterWarn = FilterKo = false;
        }
        finally { _applying = false; }
        ApplyFilters();
    }

    private HistoryFilter BuildFilter()
    {
        double? speed = null;
        FilterError = null;
        string text = SpeedFilter.Trim().TrimEnd('x', 'X').Trim();
        if (text.Length > 0)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var s)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out s))
                speed = s;
            else
                FilterError = "Velocità non valida: il filtro velocità è ignorato.";
        }

        var verdicts = new HashSet<Verdict>();
        if (FilterOk) verdicts.Add(Verdict.OK);
        if (FilterWarn) verdicts.Add(Verdict.WARN);
        if (FilterKo) verdicts.Add(Verdict.KO);

        return new HistoryFilter(
            string.IsNullOrWhiteSpace(MediumFilter) ? null : MediumFilter,
            speed,
            FromDate is DateTime f ? DateOnly.FromDateTime(f) : null,
            ToDate is DateTime t ? DateOnly.FromDateTime(t) : null,
            verdicts.Count > 0 ? verdicts : null);
    }

    /// <summary>Applies all filters in AND to the three lists, each ordered by date descending (Requirements 17.1-17.3, 17.8).</summary>
    public void ApplyFilters()
    {
        if (_applying) return;
        var filter = BuildFilter();
        var snap = _snapshot;
        var media = snap.Media.ToDictionary(m => m.Id);
        var drives = snap.Drives.ToDictionary(d => d.Id);
        var discsById = snap.Discs.ToDictionary(d => d.Id);

        string MediumOf(DiscRecord? d) =>
            d?.MediaId is long id && media.TryGetValue(id, out var m) ? HistoryFilter.FormatMedium(m) : string.Empty;
        string DiscName(long id) => discsById.TryGetValue(id, out var d) ? DiscLabel(d) : $"#{id}";

        var ci = CultureInfo.CurrentCulture;
        var lastActivity = new Dictionary<long, DateTimeOffset>();
        foreach (var s in snap.Scans) Bump(lastActivity, s.DiscId, s.ScannedAt);
        foreach (var v in snap.Verifications) Bump(lastActivity, v.DiscId, v.VerifiedAt);

        Discs = snap.Discs
            .Select(d => (Disc: d, At: lastActivity.TryGetValue(d.Id, out var at) ? at : (DateTimeOffset?)null))
            .Where(x => filter.Matches(x.Disc, MediumOf(x.Disc), x.At))
            .OrderByDescending(x => x.At ?? DateTimeOffset.MinValue).ThenByDescending(x => x.Disc.Id)
            .Select(x => new DiscRowViewModel(x.Disc.Id, DiscLabel(x.Disc), MediumOf(x.Disc), x.Disc.TrackCount,
                x.At is { } a ? a.ToLocalTime().ToString("g", ci) : "-", x.At, x.Disc))
            .ToList();

        Scans = snap.Scans
            .Where(s => filter.Matches(s, MediumOf(discsById.GetValueOrDefault(s.DiscId))))
            .OrderByDescending(s => s.ScannedAt).ThenByDescending(s => s.Id)
            .Select(s => new ScanRowViewModel(s.Id, s.ScannedAt.ToLocalTime().ToString("g", ci), DiscName(s.DiscId),
                DriveName(drives, s.DriveId), HistoryFilter.FormatSpeed(s.SpeedX), s.Scanner.ToString(),
                s.Verdict.ToString(), ToStatus(s.Verdict), s))
            .ToList();

        Verifications = snap.Verifications
            .Where(v => filter.Matches(v, MediumOf(discsById.GetValueOrDefault(v.DiscId))))
            .OrderByDescending(v => v.VerifiedAt).ThenByDescending(v => v.Id)
            .Select(v => new VerificationRowViewModel(v.Id, v.VerifiedAt.ToLocalTime().ToString("g", ci),
                DiscName(v.DiscId), ResultText(v.Result), v.OffsetUsed, v))
            .ToList();

        DiscsEmptyMessage = EmptyText(Discs.Count, snap.Discs.Count, filter, "dischi");
        ScansEmptyMessage = EmptyText(Scans.Count, snap.Scans.Count, filter, "scansioni");
        VerificationsEmptyMessage = EmptyText(Verifications.Count, snap.Verifications.Count, filter, "verifiche");

        // A selection that no longer passes the filters is dropped.
        if (SelectedScan is { } sel && Scans.All(r => r.Id != sel.Id)) SelectedScan = null;
        if (SelectedVerification is { } ver && Verifications.All(r => r.Id != ver.Id)) SelectedVerification = null;
    }

    private static void Bump(Dictionary<long, DateTimeOffset> map, long discId, DateTimeOffset at)
    {
        if (!map.TryGetValue(discId, out var cur) || at > cur) map[discId] = at;
    }

    private static string? EmptyText(int shown, int total, HistoryFilter filter, string what) =>
        shown > 0 ? null
        : filter.IsActive && total > 0 ? NoMatchMessage
        : $"{EmptyMessage} Nessun elemento tra {what}.";

    private static string DiscLabel(DiscRecord d) =>
        !string.IsNullOrWhiteSpace(d.Label) ? d.Label! : $"Disco {d.TocHash[..Math.Min(8, d.TocHash.Length)]}";

    private static string DriveName(Dictionary<long, DriveRecord> drives, long id) =>
        drives.TryGetValue(id, out var d) ? $"{d.Vendor} {d.Model}".Trim() : $"#{id}";

    public static DasStatus ToStatus(Verdict v) =>
        v switch { Verdict.OK => DasStatus.Ok, Verdict.WARN => DasStatus.Warn, _ => DasStatus.Ko };

    private static string ResultText(VerificationResult r) => r switch
    {
        VerificationResult.Identical => "Identico",
        VerificationResult.Different => "Diverso",
        _ => "Errore",
    };

    // ---- scan selection: summary and chart (Requirement 17.4) --------------------------------------------

    [ObservableProperty] private ScanRowViewModel? _selectedScan;
    [ObservableProperty] private bool _hasScanDetail;
    [ObservableProperty] private DasStatus _detailStatus;
    [ObservableProperty] private string _c1Total = NotAvailable;
    [ObservableProperty] private string _c1Mean = NotAvailable;
    [ObservableProperty] private string _c1Max = NotAvailable;
    [ObservableProperty] private string _c2Total = NotAvailable;
    [ObservableProperty] private string _c2Max = NotAvailable;
    [ObservableProperty] private string _cuTotal = NotAvailable;
    [ObservableProperty] private IReadOnlyList<ChartSeries>? _chartSeriesList;
    [ObservableProperty] private ThresholdSettings? _chartThresholds;
    [ObservableProperty] private string? _detailError;
    [ObservableProperty] private bool _showThresholds = true;

    public bool HasDetailError => !string.IsNullOrEmpty(DetailError);
    partial void OnDetailErrorChanged(string? value) => OnPropertyChanged(nameof(HasDetailError));
    partial void OnShowThresholdsChanged(bool value) => UpdateThresholds();

    private ScannerKind _detailKind;

    private void UpdateThresholds() =>
        ChartThresholds = ChartSampleMapper.ToThresholdSettings(_settings?.Current.Thresholds ?? new Thresholds(), _detailKind, ShowThresholds);

    partial void OnSelectedScanChanged(ScanRowViewModel? value)
    {
        DeleteCommand.NotifyCanExecuteChanged();
        PendingDelete = null;
        _ = ShowScanAsync(value);
    }

    /// <summary>Shows summary, verdict and the chart of all saved samples of the selected scan.</summary>
    public async Task ShowScanAsync(ScanRowViewModel? row)
    {
        int version = Interlocked.Increment(ref _selectionVersion);
        DetailError = null;
        _detailSamples = [];
        if (row is null)
        {
            HasScanDetail = false;
            ChartSeriesList = null;
            return;
        }

        var s = row.Record;
        var ci = CultureInfo.CurrentCulture;
        DetailStatus = row.Status;
        C1Total = s.C1Total?.ToString(ci) ?? NotAvailable;
        C1Mean = s.C1Mean is double m ? m.ToString("0.00", ci) : NotAvailable;
        C1Max = s.C1Max?.ToString(ci) ?? NotAvailable;
        C2Total = s.C2Total?.ToString(ci) ?? NotAvailable;
        C2Max = s.C2Max?.ToString(ci) ?? NotAvailable;
        CuTotal = s.CuTotal?.ToString(ci) ?? NotAvailable;
        _detailKind = s.Scanner;
        UpdateThresholds();
        HasScanDetail = true;

        try
        {
            var samples = await Task.Run(() => _archive.Execute(r => r.Scans.GetSamples(s.Id)));
            if (version != Volatile.Read(ref _selectionVersion)) return;
            _detailSamples = samples;
            ChartSeriesList = [new ChartSeries("Scansione", ChartSampleMapper.ToChartSamples(samples), ChartSeriesKind.ScanBars, 0)];
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reading the samples of scan {Id} failed", s.Id);
            if (version != Volatile.Read(ref _selectionVersion)) return;
            ChartSeriesList = null;
            DetailError = $"Impossibile leggere i campioni della scansione: {ex.Message}";
        }
    }

    // ---- export of the selected scan (Requirements 21.1-21.8, 21.11) --------------------------------------

    /// <summary>Chart of the view, set by the view code-behind.</summary>
    public IChartImageSource? ChartExportSource { get; set; }

    [ObservableProperty] private string? _exportMessage;
    [ObservableProperty] private bool _exportFailed;

    public bool HasExportMessage => !string.IsNullOrEmpty(ExportMessage);
    partial void OnExportMessageChanged(string? value) => OnPropertyChanged(nameof(HasExportMessage));

    private static readonly ExportResult NoChart = new(ExportOutcome.Failed, null, "Grafico non disponibile per l'export.");

    private string SuggestedName(string ext) =>
        $"scansione-{(SelectedScan?.Record.ScannedAt ?? DateTimeOffset.Now).ToLocalTime():yyyyMMdd-HHmmss}.{ext}";

    private async Task RunExportAsync(Func<Exporter, Task<ExportResult>> run)
    {
        if (_exporter is null)
        {
            ExportFailed = true;
            ExportMessage = "Export non disponibile.";
            return;
        }
        if (SelectedScan is null)
        {
            ExportFailed = false;
            ExportMessage = "Selezionare una scansione da esportare.";
            return;
        }
        var result = await run(_exporter);
        ExportFailed = result.Outcome == ExportOutcome.Failed;
        ExportMessage = result.Message;
    }

    [RelayCommand]
    public Task ExportCsvAsync() => RunExportAsync(e => e.ExportCsvAsync(_detailSamples, SuggestedName("csv")));

    [RelayCommand]
    public Task ExportPngAsync() => RunExportAsync(e => ChartExportSource is { } c
        ? e.ExportPngAsync(_detailSamples, c, SuggestedName("png")) : Task.FromResult(NoChart));

    [RelayCommand]
    public Task ExportSvgAsync() => RunExportAsync(e => ChartExportSource is { } c
        ? e.ExportSvgAsync(_detailSamples, c, SuggestedName("svg")) : Task.FromResult(NoChart));

    [RelayCommand]
    public Task ExportHtmlAsync() => RunExportAsync(async e =>
    {
        var scan = SelectedScan!.Record;
        var snap = _snapshot;
        var disc = snap.Discs.FirstOrDefault(d => d.Id == scan.DiscId);
        var drive = snap.Drives.FirstOrDefault(d => d.Id == scan.DriveId);
        if (disc is null || drive is null)
            return new ExportResult(ExportOutcome.Failed, null, "Dati del disco o del drive non disponibili per il report.");
        ReportVerification? verification = null;
        try { verification = await Task.Run(() => _archive.Execute(r => ExportQueries.LatestVerification(r, disc.TocHash, drive))); }
        catch (Exception ex) { Log.Warning(ex, "Reading the verification for the report failed"); }
        return await e.ExportHtmlAsync(new HtmlReportData(disc, drive, scan, _detailSamples, verification),
                                       ChartExportSource, SuggestedName("html"));
    });

    // ---- verification selection (Requirement 17.5) -------------------------------------------------------

    [ObservableProperty] private VerificationRowViewModel? _selectedVerification;
    [ObservableProperty] private IReadOnlyList<VerificationTrackRowViewModel> _verificationTracks = [];
    [ObservableProperty] private string? _verificationError;

    public bool HasVerificationError => !string.IsNullOrEmpty(VerificationError);
    partial void OnVerificationErrorChanged(string? value) => OnPropertyChanged(nameof(HasVerificationError));

    partial void OnSelectedVerificationChanged(VerificationRowViewModel? value) => _ = ShowVerificationAsync(value);

    public async Task ShowVerificationAsync(VerificationRowViewModel? row)
    {
        VerificationError = null;
        if (row is null)
        {
            VerificationTracks = [];
            return;
        }

        try
        {
            var tracks = await Task.Run(() => _archive.Execute(r => r.Verifications.GetTracks(row.Id)));
            if (SelectedVerification?.Id != row.Id) return;
            VerificationTracks = tracks.OrderBy(t => t.TrackNo)
                .Select(t => new VerificationTrackRowViewModel(t.TrackNo, t.Crc32, t.Sha256, t.Identical ? "Identica" : "Diversa"))
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reading the tracks of verification {Id} failed", row.Id);
            VerificationTracks = [];
            VerificationError = $"Impossibile leggere gli esiti della verifica: {ex.Message}";
        }
    }

    // ---- deletion with confirmation (Requirements 17.6, 17.7) --------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmDeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelDeleteCommand))]
    private ScanRowViewModel? _pendingDelete;

    public bool HasPendingDelete => PendingDelete is not null;
    public string PendingDeleteText => PendingDelete is { } p
        ? $"Eliminare la scansione del {p.Date} ({p.Disc})? Verranno eliminati anche tutti i suoi campioni."
        : string.Empty;

    partial void OnPendingDeleteChanged(ScanRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasPendingDelete));
        OnPropertyChanged(nameof(PendingDeleteText));
    }

    private bool CanDelete() => SelectedScan is not null;

    /// <summary>Asks for confirmation; nothing is deleted until <see cref="ConfirmDeleteAsync"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    public void Delete()
    {
        DeleteMessage = null;
        PendingDelete = SelectedScan;
    }

    [RelayCommand(CanExecute = nameof(HasPendingDelete))]
    public void CancelDelete() => PendingDelete = null;

    [RelayCommand(CanExecute = nameof(HasPendingDelete))]
    public async Task ConfirmDeleteAsync()
    {
        if (PendingDelete is not { } row) return;
        PendingDelete = null;
        try
        {
            bool deleted = await Task.Run(() => _archive.Execute(r => r.Scans.Delete(row.Id)));
            if (!deleted)
            {
                DeleteMessage = "Eliminazione non riuscita: la scansione non esiste più.";
            }
            else
            {
                DeleteMessage = "Scansione eliminata.";
                _snapshot = _snapshot with { Scans = _snapshot.Scans.Where(s => s.Id != row.Id).ToList() };
                ApplyFilters();
                if (SelectedScan?.Id == row.Id) SelectedScan = null;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Deleting scan {Id} failed", row.Id);
            DeleteMessage = $"Eliminazione della scansione non riuscita: {ex.Message} La scansione resta nell'elenco.";
        }
    }
}
