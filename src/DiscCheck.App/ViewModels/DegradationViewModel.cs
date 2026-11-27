using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAS.Design.Charts;
using DiscCheck.App.Charts;
using DiscCheck.Core;
using Serilog;

namespace DiscCheck.App.ViewModels;

public sealed record DegradationDiscRow(long Id, string Label, string RecheckDue, bool IsWorsening, DiscRecord Record);

public sealed record RecheckRow(long DiscId, string Label, string DueDate, string StatusText, bool IsOverdue);

/// <summary>One trend chart: a single series with its own Y axis (name and unit of the series).</summary>
public sealed record TrendChart(string Title, IReadOnlyList<ChartSeries> Series, ChartAxisSpec Axis);

public sealed record LongevityRow(string Medium, string Lifetime);

/// <summary>
/// Archivio/Degrado view: trend per disc, overdue / due-soon list, "in peggioramento" highlight, storage advice,
/// longevity and the BLER target, plus the once-per-launch reminder (Requirements 20.1, 20.5-20.9, 20.13, 20.14).
/// All calculations are delegated to <see cref="DegradationMonitor"/>; charts go through <see cref="ChartSampleMapper"/>
/// and <c>DasChartView</c> only (Requirement 38.15).
/// </summary>
public sealed partial class DegradationViewModel : ObservableObject
{
    public const string NoScansMessage = "Nessuna scansione salvata per questo disco: il trend non è disponibile.";
    public const string NoDiscsMessage = "Nessun disco in archivio.";
    public const string NoDueMessage = "Nessun disco da ricontrollare.";
    public const string LoadErrorPrefix = "Impossibile caricare i dati dell'archivio: ";
    public const string TrendErrorPrefix = "Impossibile caricare il trend del disco: ";

    private readonly IArchive _archive;
    private readonly SettingsStore? _settings;
    private readonly TimeProvider _clock;
    private bool _reminderShown;
    private int _trendVersion;

    public DegradationViewModel(IArchive archive, SettingsStore? settings = null, TimeProvider? clock = null)
    {
        _archive = archive;
        _settings = settings;
        _clock = clock ?? TimeProvider.System;
    }

    // ---- static advice (Requirements 20.8, 20.9) -----------------------------------------------------------

    public IReadOnlyList<string> StorageAdvice { get; } =
    [
        "Conservare i dischi in posizione verticale.",
        "Usare custodie jewel case.",
        "Umidità relativa tra 20% e 50%.",
        "Temperatura tra −10 °C e 23 °C, mai sopra 32 °C.",
        "Riparare i dischi da luce e inquinanti.",
    ];

    public IReadOnlyList<LongevityRow> Longevity { get; } =
    [
        new("Ftalocianina + oro", "oltre 100 anni"),
        new("Ftalocianina + argento", "da 50 a 100 anni"),
        new("Cianina o AZO + argento", "da 20 a 50 anni"),
    ];

    public string BlerTarget => "Obiettivo: BLER iniziale inferiore a 50.";

    public string RecheckIntervalText =>
        $"Intervallo di ricontrollo: {_settings?.Current.RecheckYears ?? DegradationMonitor.DefaultRecheckYears} anni.";

    // ---- state -------------------------------------------------------------------------------------------

    [ObservableProperty] private IReadOnlyList<DegradationDiscRow> _discs = [];
    [ObservableProperty] private IReadOnlyList<RecheckRow> _dueDiscs = [];
    [ObservableProperty] private string? _loadError;
    [ObservableProperty] private string? _dueEmptyMessage;
    [ObservableProperty] private string? _discsEmptyMessage;

    [ObservableProperty] private DegradationDiscRow? _selectedDisc;
    [ObservableProperty] private IReadOnlyList<TrendChart> _charts = [];
    [ObservableProperty] private bool _isWorsening;
    [ObservableProperty] private string? _trendMessage;
    [ObservableProperty] private string? _trendError;

    public bool HasLoadError => !string.IsNullOrEmpty(LoadError);
    public bool HasDueEmptyMessage => !string.IsNullOrEmpty(DueEmptyMessage);
    public bool HasDiscsEmptyMessage => !string.IsNullOrEmpty(DiscsEmptyMessage);
    public bool HasTrendMessage => !string.IsNullOrEmpty(TrendMessage);
    public bool HasTrendError => !string.IsNullOrEmpty(TrendError);
    public bool HasCharts => Charts.Count > 0;
    public bool HasData => !HasLoadError;

    partial void OnLoadErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(HasLoadError));
        OnPropertyChanged(nameof(HasData));
    }

    partial void OnDueEmptyMessageChanged(string? value) => OnPropertyChanged(nameof(HasDueEmptyMessage));
    partial void OnDiscsEmptyMessageChanged(string? value) => OnPropertyChanged(nameof(HasDiscsEmptyMessage));
    partial void OnTrendMessageChanged(string? value) => OnPropertyChanged(nameof(HasTrendMessage));
    partial void OnTrendErrorChanged(string? value) => OnPropertyChanged(nameof(HasTrendError));
    partial void OnChartsChanged(IReadOnlyList<TrendChart> value) => OnPropertyChanged(nameof(HasCharts));

    // ---- loading (Requirements 20.5, 20.13) ----------------------------------------------------------------

    private sealed record Loaded(IReadOnlyList<DiscRecord> Discs, IReadOnlyList<RecheckEntry> Due, HashSet<long> Worsening);

    /// <summary>
    /// Reads discs, due list and worsening flags in one pass. On failure nothing partial is shown: lists and
    /// trend are cleared and only the error message remains (Requirement 20.13).
    /// </summary>
    public async Task LoadAsync()
    {
        RefreshIntervalText();
        long? keep = SelectedDisc?.Id;
        Loaded loaded;
        try
        {
            loaded = await Task.Run(() => _archive.Execute(r =>
            {
                var monitor = new DegradationMonitor(r.Scans, r.Discs, _clock);
                var discs = r.Discs.List();
                var due = monitor.GetDueDiscs();
                var worsening = discs.Where(d => monitor.GetTrend(d.Id).IsWorsening).Select(d => d.Id).ToHashSet();
                return new Loaded(discs, due, worsening);
            }));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reading the degradation data failed");
            Discs = [];
            DueDiscs = [];
            DiscsEmptyMessage = null;
            DueEmptyMessage = null;
            ClearTrend();
            SelectedDisc = null;
            LoadError = LoadErrorPrefix + ex.Message;
            return;
        }

        LoadError = null;
        var ci = CultureInfo.CurrentCulture;
        string Label(DiscRecord d) => !string.IsNullOrWhiteSpace(d.Label) ? d.Label!
            : $"Disco {d.TocHash[..Math.Min(8, d.TocHash.Length)]}";

        DueDiscs = loaded.Due
            .Select(e => new RecheckRow(e.Disc.Id, Label(e.Disc), e.DueDate.ToString("d", ci),
                e.Status == RecheckStatus.Overdue ? "scaduto" : "in scadenza", e.Status == RecheckStatus.Overdue))
            .ToList();
        DueEmptyMessage = DueDiscs.Count == 0 ? NoDueMessage : null;

        Discs = loaded.Discs
            .Select(d => new DegradationDiscRow(d.Id, Label(d), FormatDue(d.RecheckDue, ci), loaded.Worsening.Contains(d.Id), d))
            .ToList();
        DiscsEmptyMessage = Discs.Count == 0 ? NoDiscsMessage : null;

        var reselect = keep is long id ? Discs.FirstOrDefault(d => d.Id == id) : null;
        SelectedDisc = reselect; // a new row instance reloads the trend; null clears it
    }

    private static string FormatDue(string? due, CultureInfo ci) =>
        due is not null && DateOnly.TryParseExact(due, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("d", ci) : "-";

    private void RefreshIntervalText() => OnPropertyChanged(nameof(RecheckIntervalText));

    // ---- trend of the selected disc (Requirements 20.1, 20.7, 20.10, 20.11, 20.14) ---------------------------

    partial void OnSelectedDiscChanged(DegradationDiscRow? value) => _ = ShowTrendAsync(value);

    private void ClearTrend()
    {
        Charts = [];
        IsWorsening = false;
        TrendMessage = null;
        TrendError = null;
    }

    public async Task ShowTrendAsync(DegradationDiscRow? row)
    {
        int version = Interlocked.Increment(ref _trendVersion);
        ClearTrend();
        if (row is null) return;

        DegradationTrend trend;
        try
        {
            trend = await Task.Run(() => _archive.Execute(r => new DegradationMonitor(r.Scans, r.Discs, _clock).GetTrend(row.Id)));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reading the trend of disc {Id} failed", row.Id);
            if (version != Volatile.Read(ref _trendVersion)) return;
            TrendError = TrendErrorPrefix + ex.Message;
            return;
        }
        if (version != Volatile.Read(ref _trendVersion)) return;

        if (!trend.HasScans)
        {
            TrendMessage = NoScansMessage; // no chart, recheck_due untouched (Requirement 20.11)
            return;
        }

        IsWorsening = trend.IsWorsening;
        double origin = ChartSampleMapper.TrendOrigin(trend.Points);
        TrendChart Make(string title, string yLabel, Func<TrendPoint, double?> value) =>
            new(title, [ChartSampleMapper.ToTrendSeries(title, 0, trend.Points, origin, value)],
                ChartSampleMapper.ToTrendAxisSpec(yLabel, origin));

        Charts =
        [
            Make("C1 medio", "C1 medio (errori/s)", p => p.C1Mean),
            Make("C1 massimo", "C1 massimo (errori/s)", p => p.C1Max),
            Make("C2 totale", "C2 totale (errori)", p => p.C2Total),
        ];
    }

    // ---- startup reminder (Requirement 20.6) ---------------------------------------------------------------

    [ObservableProperty] private string? _reminderText;
    public bool HasReminder => !string.IsNullOrEmpty(ReminderText);
    partial void OnReminderTextChanged(string? value) => OnPropertyChanged(nameof(HasReminder));

    /// <summary>
    /// Shows the reminder with the number of overdue discs, at most once per launch: later calls do nothing, whatever
    /// the first outcome was. A failed read shows no reminder (the Archivio view reports the error).
    /// </summary>
    public async Task CheckReminderAsync()
    {
        if (_reminderShown) return;
        _reminderShown = true;
        try
        {
            int overdue = await Task.Run(() => _archive.Execute(r =>
                new DegradationMonitor(r.Scans, r.Discs, _clock).GetDueDiscs().Count(e => e.Status == RecheckStatus.Overdue)));
            if (overdue > 0)
                ReminderText = overdue == 1
                    ? "1 disco ha il ricontrollo scaduto."
                    : $"{overdue} dischi hanno il ricontrollo scaduto.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Reading the recheck reminder failed");
        }
    }

    [RelayCommand]
    public void DismissReminder() => ReminderText = null;
}
