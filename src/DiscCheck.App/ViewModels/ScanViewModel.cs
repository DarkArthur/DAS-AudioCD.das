using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAS.Design.Charts;
using DAS.Design.Controls;
using DiscCheck.App.Charts;
using DiscCheck.App.Startup;
using DiscCheck.Core;
using Serilog;

namespace DiscCheck.App.ViewModels;

/// <summary>An entry of the speed selector; <see cref="KBps"/> is null for "massima" (Requirement 10.1).</summary>
public sealed record ScanSpeedOption(string Label, int? KBps);

/// <summary>State of the Scansione view (design: state machine).</summary>
public enum ScanState { Idle, Running }

/// <summary>
/// Scansione view: scanner and speed selection, Avvia/Annulla, live chart and summary. Orchestrates only:
/// scanner choice is <see cref="ScannerSelector"/>, the run is <see cref="ScanPipeline"/>, verdict and totals are
/// pure Core functions and the chart data goes through <see cref="ChartSampleMapper"/>. No colours and no
/// ScottPlot code (Requirements 18, 35.4, 38.15).
/// </summary>
public sealed partial class ScanViewModel : ObservableObject
{
    public const string NotAvailable = "n/d";
    public const string ComparisonWarning = "Confronta solo scansioni fatte con lo stesso drive alla stessa velocità";
    public const string NoDiscMessage = "Inserire un disco audio nel drive selezionato.";
    public const string NoDriveMessage = "Selezionare un drive nella vista Drive.";
    public const string StandardLimitsNote =
        "Lo scanner standard misura solo C2 e non C1. I valori dipendono dal firmware del drive: " +
        "sono un indicatore universale, non una misura di precisione.";
    public const string LiteOnNote =
        "Il C1 dei drive PC è un'approssimazione del BLER e va usato per confronti relativi " +
        "sullo stesso drive alla stessa velocità.";
    public const string ResetDriveMessage =
        "Il drive può essere ancora in Test_Mode. Usare il comando \"Reset drive\" nella vista Drive.";

    public const string FirmwareIncompatibleMessage =
        "Il firmware del drive non è compatibile con i comandi di scansione Lite-On.";
    public const string UsbBridgeMessage =
        "Il drive è collegato tramite USB: il bridge USB potrebbe filtrare i comandi vendor. " +
        "Provare con un collegamento SATA interno.";
    public const string FallbackOfferMessage =
        "Usare la scansione standard (solo C2) al posto della scansione Lite-On?";
    public const string MediaTekWarning =
        "Drive con chipset MediaTek: il C1 può essere sottostimato; per il verdetto KO contano C2 e CU.";

    public const string StatusRunning = "in corso";
    public const string StatusCompleted = "completata";
    public const string StatusCancelled = "annullata";
    public const string StatusError = "errore";

    private const int DefaultChartWidthPx = 1000;

    private readonly DriveViewModel _drives;
    private readonly DiscViewModel _disc;
    private readonly ScannerSelector _selector;
    private readonly IScanSpeedService _speed;
    private readonly ScanPipeline _pipeline;
    private readonly bool _scansEnabled;
    private readonly IArchive? _archive;
    private readonly ILiteOnMethodSetting _liteOn;
    private readonly IDriveHardwareHints? _hints;
    private PendingSave? _pendingSave;
    private PendingSave? _lastCompleted;
    private readonly Exporter? _exporter;
    private readonly SettingsStore? _settings;
    private IReadOnlyList<ScanSpeedOption> _speeds = SpeedOptions;

    private readonly object _gate = new();
    private readonly List<ScanSample> _live = [];
    private int _refreshPending;
    private SynchronizationContext? _ui;
    private CancellationTokenSource? _cts;

    private IQualityScanner? _scanner;
    private int _probeVersion;
    private int? _confirmedKBps;
    private bool _revertingSpeed;
    private int _widthPx = DefaultChartWidthPx;
    private AxisRange? _visibleRange;
    private IReadOnlyList<ScanSample> _finalSamples = [];

    public ScanViewModel(DriveViewModel drives, DiscViewModel disc, ScannerSelector selector,
                         IScanSpeedService speed, ScanPipeline pipeline, StartupInfo startup,
                         IArchive? archive = null, ILiteOnMethodSetting? liteOn = null,
                         IDriveHardwareHints? hints = null, SettingsStore? settings = null,
                         Exporter? exporter = null)
    {
        _exporter = exporter;
        _settings = settings;
        _liteOn = liteOn ?? new LiteOnMethodSetting();
        _hints = hints;
        _archive = archive;
        _drives = drives;
        _disc = disc;
        _selector = selector;
        _speed = speed;
        _pipeline = pipeline;
        _scansEnabled = startup.ScansEnabled;

        ApplyDefaultSpeed(); // default speed from the settings, 8x unless changed (Requirements 10.1, 22.1)
        if (settings is not null) settings.Changed += OnSettingsChanged;
        drives.PropertyChanged += OnDrivesChanged;
        disc.PropertyChanged += OnDiscChanged;
        RefreshAvailability();
    }

    public static IReadOnlyList<ScanSpeedOption> SpeedOptions { get; } =
    [
        new("4x", ScanSpeeds.ToKBps(4)),
        new("8x", ScanSpeeds.ToKBps(8)),
        new("16x", ScanSpeeds.ToKBps(16)),
        new("24x", ScanSpeeds.ToKBps(24)),
        new("massima", null),
    ];

    public IReadOnlyList<ScanSpeedOption> Speeds => _speeds;

    /// <summary>Thresholds in use: the stored ones, or the defaults without a Settings_Store (Requirement 22.1).</summary>
    private Thresholds CurrentThresholds => _settings?.Current.Thresholds ?? new Thresholds();

    private int DefaultSpeedX => _settings?.Current.DefaultSpeedX ?? 8;

    /// <summary>Preselects the default speed; a value outside the standard list is added as an extra entry.</summary>
    private void ApplyDefaultSpeed()
    {
        int kBps = ScanSpeeds.ToKBps(DefaultSpeedX);
        var list = SpeedOptions;
        var match = list.FirstOrDefault(o => o.KBps == kBps);
        if (match is null)
        {
            var extra = new ScanSpeedOption($"{DefaultSpeedX}x", kBps);
            list = [.. SpeedOptions.Where(o => o.KBps is not null).Append(extra).OrderBy(o => o.KBps), .. SpeedOptions.Where(o => o.KBps is null)];
            match = extra;
        }
        if (!ReferenceEquals(list, _speeds))
        {
            _speeds = list;
            OnPropertyChanged(nameof(Speeds));
        }
        SelectedSpeed = match;
    }

    private void OnSettingsChanged(Settings s)
    {
        UpdateThresholds();
        if (State == ScanState.Idle) ApplyDefaultSpeed();
    }

    public ChartAxisSpec AxisSpec { get; } = new("Tempo (mm:ss)", "Errori per campione", "Velocità (x)");

    public string ComparisonWarningText => ComparisonWarning;

    // ---- selection ---------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private ScanSpeedOption? _selectedSpeed;

    [ObservableProperty]
    private bool _forceStandard;

    [ObservableProperty]
    private string? _scannerName;

    [ObservableProperty]
    private string? _scannerMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isProbing;

    [ObservableProperty]
    private string? _speedConfirmMessage;

    [ObservableProperty]
    private string? _speedNote;

    // ---- state -------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private ScanState _state = ScanState.Idle;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _availabilityMessage;

    [ObservableProperty]
    private bool _showResetDriveHint;

    /// <summary>The Lite-On init failed: the user is asked whether to run StandardC2Scanner instead (Requirements 8.8, 26.3).</summary>
    [ObservableProperty]
    private bool _offerStandardFallback;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _speedText = NotAvailable;

    // ---- chart -------------------------------------------------------------------------------------------

    [ObservableProperty]
    private IReadOnlyList<ChartSeries>? _chartSeriesList;

    [ObservableProperty]
    private ThresholdSettings? _chartThresholds;

    [ObservableProperty]
    private bool _showThresholds = true;

    // ---- summary -----------------------------------------------------------------------------------------

    [ObservableProperty]
    private bool _hasSummary;

    [ObservableProperty]
    private string _c1Total = NotAvailable;

    [ObservableProperty]
    private string _c1Mean = NotAvailable;

    [ObservableProperty]
    private string _c1Max = NotAvailable;

    [ObservableProperty]
    private string _c2Total = NotAvailable;

    [ObservableProperty]
    private string _c2Max = NotAvailable;

    [ObservableProperty]
    private string _cuTotal = NotAvailable;

    [ObservableProperty]
    private bool _hasVerdict;

    [ObservableProperty]
    private DasStatus _verdictStatus;

    // ---- saving (Requirements 9.8, 16.12) ----------------------------------------------------------------

    [ObservableProperty]
    private string? _saveMessage;

    [ObservableProperty]
    private bool _saveFailed;

    public bool HasSaveMessage => !string.IsNullOrEmpty(SaveMessage);

    partial void OnSaveMessageChanged(string? value) => OnPropertyChanged(nameof(HasSaveMessage));

    public bool IsRunning => State == ScanState.Running;
    public bool IsIdle => State == ScanState.Idle;
    public bool HasStatusText => !string.IsNullOrEmpty(StatusText);
    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasAvailabilityMessage => !string.IsNullOrEmpty(AvailabilityMessage);
    public bool HasScannerMessage => !string.IsNullOrEmpty(ScannerMessage);
    public bool HasSpeedConfirm => !string.IsNullOrEmpty(SpeedConfirmMessage);
    public bool HasSpeedNote => !string.IsNullOrEmpty(SpeedNote);
    public bool ShowStandardNote => _scanner?.Name == ScannerSelector.StandardC2Name;
    public bool ShowLiteOnNote => HasVerdict && _scanner?.Name == ScannerSelector.LiteOnName;
    public bool ShowMediaTekWarning => HasVerdict && _scanner?.Name == ScannerSelector.LiteOnName
        && SelectedInfo is { } mt && _hints?.IsMediaTek(mt) == true;
    public string MediaTekWarningText => MediaTekWarning;
    public string FallbackOfferText => FallbackOfferMessage;
    public string StandardNoteText => StandardLimitsNote;
    public string LiteOnNoteText => LiteOnNote;
    public string ResetHintText => ResetDriveMessage;

    /// <summary>Scan samples collected so far (all of them, before downsampling).</summary>
    public IReadOnlyList<ScanSample> Samples
    {
        get { lock (_gate) return _live.ToArray(); }
    }

    partial void OnStateChanged(ScanState value)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsIdle));
    }

    partial void OnStatusTextChanged(string? value) => OnPropertyChanged(nameof(HasStatusText));
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasErrorMessage));
    partial void OnAvailabilityMessageChanged(string? value) => OnPropertyChanged(nameof(HasAvailabilityMessage));
    partial void OnScannerMessageChanged(string? value) => OnPropertyChanged(nameof(HasScannerMessage));
    partial void OnSpeedConfirmMessageChanged(string? value) => OnPropertyChanged(nameof(HasSpeedConfirm));
    partial void OnSpeedNoteChanged(string? value) => OnPropertyChanged(nameof(HasSpeedNote));
    partial void OnHasVerdictChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowLiteOnNote));
        OnPropertyChanged(nameof(ShowMediaTekWarning));
    }

    partial void OnForceStandardChanged(bool value) => _ = UpdateScannerAsync();

    partial void OnShowThresholdsChanged(bool value) => UpdateThresholds();

    // ---- speed selection (Requirement 10.6) ---------------------------------------------------------------

    private DriveInfoEx? SelectedInfo => _drives.SelectedDrive?.Item.Info;

    private ScanSpeedOption? _speedBeforePending;
    private int? _pendingKBps;

    partial void OnSelectedSpeedChanged(ScanSpeedOption? oldValue, ScanSpeedOption? newValue)
    {
        if (_revertingSpeed) return;
        _confirmedKBps = null;
        _pendingKBps = null;
        SpeedConfirmMessage = null;
        SpeedNote = null;
        StartCommand.NotifyCanExecuteChanged();

        if (newValue?.KBps is not int requested || SelectedInfo is not { } info
            || ScanSpeeds.IsSupported(info.SupportedSpeedsKBps, requested))
            return;

        if (ScanSpeeds.Nearest(info.SupportedSpeedsKBps, requested) is not int nearest)
            return;

        _speedBeforePending = oldValue;
        _pendingKBps = nearest;
        SpeedConfirmMessage = $"La velocità {newValue.Label} non è supportata dal drive. " +
                              $"Usare la velocità supportata più vicina ({FormatX(nearest)})?";
        StartCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void ConfirmSpeed()
    {
        if (_pendingKBps is not int nearest) return;
        _confirmedKBps = nearest;
        _pendingKBps = null;
        SpeedConfirmMessage = null;
        SpeedNote = $"La scansione userà la velocità {FormatX(nearest)}.";
        StartCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void CancelSpeed()
    {
        if (_pendingKBps is null) return;
        _pendingKBps = null;
        SpeedConfirmMessage = null;
        _revertingSpeed = true;
        try { SelectedSpeed = _speedBeforePending ?? Speeds[1]; }
        finally { _revertingSpeed = false; }
        _confirmedKBps = null;
        StartCommand.NotifyCanExecuteChanged();
    }

    private static string FormatX(int kBps) => $"{ScanSpeeds.ToX(kBps).ToString("0.#", CultureInfo.CurrentCulture)}x";

    private int EffectiveKBps()
    {
        if (_confirmedKBps is int c) return c;
        if (SelectedSpeed?.KBps is int k) return k;
        return SelectedInfo is { MaxReadSpeedKBps: > 0 } i ? i.MaxReadSpeedKBps : ScanSpeeds.MaximumKBps;
    }

    // ---- drive / disc / scanner tracking -----------------------------------------------------------------

    private void OnDrivesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DriveViewModel.SelectedDrive)) return;
        _confirmedKBps = null;
        if (_pendingKBps is not null) CancelSpeed();
        _ = UpdateScannerAsync();
        RefreshAvailability();
    }

    private void OnDiscChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DiscViewModel.HasDisc) or nameof(DiscViewModel.ScanAndVerifyAllowed)
            or nameof(DiscViewModel.StatusMessage))
            RefreshAvailability();
    }

    private void RefreshAvailability()
    {
        AvailabilityMessage = (State, _drives.SelectedDrive, _disc) switch
        {
            _ when !_scansEnabled => "Le scansioni sono disabilitate: servono i privilegi di amministratore.",
            (_, not { IsIdentified: true }, _) => NoDriveMessage,
            (_, _, { ScanAndVerifyAllowed: false }) => "Il supporto inserito non è un CD: scansione non disponibile.",
            (_, _, { HasDisc: false } d) => string.IsNullOrEmpty(d.StatusMessage) ? NoDiscMessage : $"{NoDiscMessage} {d.StatusMessage}",
            _ => null,
        };
        StartCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Called when the view opens: reads the disc if needed and probes the scanners (Requirement 6.2).</summary>
    public async Task OnViewOpenedAsync()
    {
        if (!_disc.HasDisc && _drives.SelectedDrive is { IsIdentified: true } && _disc.ReadDiscCommand.CanExecute(null))
            await _disc.ReadDiscAsync();
        if (_scanner is null && !IsProbing)
            await UpdateScannerAsync();
        RefreshAvailability();
    }

    /// <summary>Probes the scanners of the selected drive (5 s each at most) and proposes one (Requirements 6.2-6.6).</summary>
    public async Task UpdateScannerAsync()
    {
        int version = Interlocked.Increment(ref _probeVersion);
        _scanner = null;
        ScannerName = null;
        ScannerMessage = null;
        NotifyNotes();
        StartCommand.NotifyCanExecuteChanged();

        if (SelectedInfo is not { } info)
            return;

        IsProbing = true;
        try
        {
            bool force = ForceStandard;
            var selection = await Task.Run(() => _selector.Select(info, force));
            if (version != Volatile.Read(ref _probeVersion)) return; // superseded by a newer request
            _scanner = selection.Scanner;
            ScannerName = selection.Scanner?.Name;
            ScannerMessage = selection.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Scanner selection failed for drive {Drive}", info.Letter);
            if (version == Volatile.Read(ref _probeVersion))
                ScannerMessage = $"Selezione dello scanner non riuscita: {ex.Message}";
        }
        finally
        {
            if (version == Volatile.Read(ref _probeVersion)) IsProbing = false;
            NotifyNotes();
            UpdateThresholds();
            StartCommand.NotifyCanExecuteChanged();
        }
    }

    private void NotifyNotes()
    {
        OnPropertyChanged(nameof(ShowStandardNote));
        OnPropertyChanged(nameof(ShowLiteOnNote));
        OnPropertyChanged(nameof(ShowMediaTekWarning));
    }

    private ScannerKind CurrentKind =>
        _scanner?.Name == ScannerSelector.LiteOnName ? ScannerKind.LiteOn : ScannerKind.StandardC2;

    private void UpdateThresholds() =>
        ChartThresholds = ChartSampleMapper.ToThresholdSettings(CurrentThresholds, CurrentKind, ShowThresholds);

    // ---- start / cancel ----------------------------------------------------------------------------------

    private bool CanStart() =>
        State == ScanState.Idle && !IsProbing && _scansEnabled && _scanner is not null && _pendingKBps is null
        && SelectedSpeed is not null && _drives.SelectedDrive is { IsIdentified: true }
        && _disc.HasDisc && _disc.ScanAndVerifyAllowed && _disc.CurrentToc is not null;

    [RelayCommand(CanExecute = nameof(CanStart))]
    public async Task StartAsync()
    {
        if (_scanner is not { } scanner || SelectedInfo is not { } info || _disc.CurrentToc is not { } toc)
            return;

        ResetRun();
        State = ScanState.Running;
        StatusText = StatusRunning;
        RefreshAvailability();
        _ui = SynchronizationContext.Current;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        int kBps = EffectiveKBps();
        UpdateThresholds();

        try
        {
            // SET CD SPEED goes out before the first read; the scanners never send it (Requirement 10.2).
            string? speedError = await Task.Run(() => _speed.SetSpeed(info.Letter, kBps), CancellationToken.None);
            if (speedError is not null)
            {
                ErrorMessage = speedError;
                StatusText = StatusError;
                return;
            }

            if (ct.IsCancellationRequested)
            {
                StatusText = StatusCancelled;
                return;
            }

            var options = new ScanOptions(info.Letter, kBps, _liteOn.Method, toc);
            var progress = new Progress<ScanProgress>(OnProgress);
            var result = await _pipeline.RunAsync(scanner, options, progress, OnSampleAsync, ct);
            Finish(result);
            if (result.Status == ScanRunStatus.Completed) // a cancelled or failed scan is never saved (Requirement 9.8)
            {
                _pendingSave = _lastCompleted = BuildPendingSave(result.Samples, info, toc, kBps);
                await SaveAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Scan failed unexpectedly");
            ErrorMessage = $"Scansione non riuscita: {ex.Message}";
            StatusText = StatusError;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            State = ScanState.Idle;
            RefreshAvailability();
        }
    }

    /// <summary>The user accepted the fallback: switches to StandardC2Scanner and starts the scan (Requirement 26.3).</summary>
    [RelayCommand]
    public async Task AcceptFallbackAsync()
    {
        if (!OfferStandardFallback || State != ScanState.Idle || SelectedInfo is not { } info)
            return;
        OfferStandardFallback = false;
        var selection = await Task.Run(() => _selector.Select(info, forceStandard: true));
        _scanner = selection.Scanner;
        ScannerName = selection.Scanner?.Name;
        ScannerMessage = selection.Message;
        NotifyNotes();
        UpdateThresholds();
        StartCommand.NotifyCanExecuteChanged();
        if (selection.Scanner is not null && StartCommand.CanExecute(null))
            await StartAsync();
    }

    [RelayCommand]
    public void DeclineFallback() => OfferStandardFallback = false;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel() => _cts?.Cancel();

    private bool CanCancel() => State == ScanState.Running;

    private void ResetRun()
    {
        lock (_gate) _live.Clear();
        _finalSamples = [];
        ChartSeriesList = null;
        ErrorMessage = null;
        StatusText = null;
        ShowResetDriveHint = false;
        OfferStandardFallback = false;
        _pendingSave = null;
        _lastCompleted = null;
        ExportMessage = null;
        SaveMessage = null;
        SaveFailed = false;
        RetrySaveCommand.NotifyCanExecuteChanged();
        ProgressPercent = 0;
        SpeedText = NotAvailable;
        HasSummary = false;
        HasVerdict = false;
        C1Total = C1Mean = C1Max = C2Total = C2Max = CuTotal = NotAvailable;
    }

    private void OnProgress(ScanProgress p)
    {
        ProgressPercent = p.Percent;
        SpeedText = p.SpeedX is double x ? $"{x.ToString("0.0", CultureInfo.CurrentCulture)}x" : NotAvailable;
    }

    private ValueTask OnSampleAsync(ScanSample sample)
    {
        lock (_gate) _live.Add(sample);
        // Coalesce: at most one pending refresh on the UI thread, so a slow UI never queues work.
        if (Interlocked.Exchange(ref _refreshPending, 1) == 0)
        {
            if (_ui is { } ui)
                ui.Post(_ => RefreshChartNow(), null);
            else
                RefreshChartNow();
        }
        return ValueTask.CompletedTask;
    }

    private void RefreshChartNow()
    {
        Volatile.Write(ref _refreshPending, 0);
        ScanSample[] snapshot;
        lock (_gate) snapshot = _live.ToArray();
        PublishChart(snapshot);
    }

    private void PublishChart(IReadOnlyList<ScanSample> samples)
    {
        var visible = ChartSampleMapper.ToVisibleChartSamples(
            samples, _widthPx, _visibleRange?.Min, _visibleRange?.Max);
        ChartSeriesList = [new ChartSeries("Scansione", visible, ChartSeriesKind.ScanBars, 0)];
    }

    /// <summary>
    /// Pixel width of the plot and visible X range reported by the chart control; the range is null while the
    /// chart is in auto-fit. The chart data are re-reduced to at most one point per pixel (Requirement 18.6).
    /// </summary>
    public void UpdateViewport(int widthPx, AxisRange? visibleRange)
    {
        int width = widthPx > 0 ? widthPx : DefaultChartWidthPx;
        if (width == _widthPx && Equals(visibleRange, _visibleRange)) return;
        _widthPx = width;
        _visibleRange = visibleRange;
        IReadOnlyList<ScanSample> snapshot = State == ScanState.Running ? Samples : _finalSamples;
        if (snapshot.Count > 0) PublishChart(snapshot);
    }

    private void Finish(ScanRunResult result)
    {
        _finalSamples = result.Samples;
        PublishChart(result.Samples);

        switch (result.Status)
        {
            case ScanRunStatus.Completed:
                StatusText = StatusCompleted;
                ShowSummary(result.Samples);
                break;
            case ScanRunStatus.Cancelled:
                StatusText = StatusCancelled; // no verdict (Requirement 18.7)
                break;
            default:
                StatusText = StatusError;
                ErrorMessage = result.Error?.Message ?? "Scansione non riuscita.";
                break;
        }

        if (result.Error is TestModeEndFailedException end)
        {
            Log.Error("End-of-scan command failed: CDB {Cdb}, sense {Sense}: {Message}", end.Cdb, end.Sense, end.Message);
            ShowResetDriveHint = true; // Requirement 9.12
        }
        else if (result.Error is ScanInitFailedException init && _scanner?.Name == ScannerSelector.LiteOnName)
        {
            Log.Error(init, "Lite-On init failed");
            ErrorMessage = $"{init.Message} {FirmwareIncompatibleMessage}";
            if (SelectedInfo is { } drive && _hints?.IsUsbBus(drive.Letter) == true)
                ErrorMessage += $" {UsbBridgeMessage}";
            OfferStandardFallback = true; // StandardC2Scanner starts only after the user accepts
        }
        else if (result.Error is not null && result.Status == ScanRunStatus.Failed)
        {
            Log.Error(result.Error, "Scan failed");
        }
    }

    private void ShowSummary(IReadOnlyList<ScanSample> samples)
    {
        var kind = CurrentKind;
        var s = ScanSummary.Compute(samples);
        var ci = CultureInfo.CurrentCulture;
        C1Total = s.C1Total?.ToString(ci) ?? NotAvailable;
        C1Mean = s.C1Mean is double m ? m.ToString("0.00", ci) : NotAvailable;
        C1Max = s.C1Max?.ToString(ci) ?? NotAvailable;
        C2Total = s.C2Total.ToString(ci);
        C2Max = s.C2Max.ToString(ci);
        // CU null = not measured (Lite-On Metodo_Nuovo): "n/d", never 0.
        CuTotal = kind == ScannerKind.LiteOn ? s.CuTotal?.ToString(ci) ?? NotAvailable : NotAvailable;
        HasSummary = samples.Count > 0;

        if (Evaluator.Evaluate(samples, kind, CurrentThresholds) is { } verdict)
        {
            VerdictStatus = verdict switch { Verdict.OK => DasStatus.Ok, Verdict.WARN => DasStatus.Warn, _ => DasStatus.Ko };
            HasVerdict = true;
        }
        NotifyNotes();
    }

    // ---- export (Requirements 21.1-21.8, 21.11) -----------------------------------------------------------

    /// <summary>Chart of the view, set by the view code-behind: source of PNG, SVG and of the HTML report image.</summary>
    public IChartImageSource? ChartExportSource { get; set; }

    [ObservableProperty]
    private string? _exportMessage;

    [ObservableProperty]
    private bool _exportFailed;

    public bool HasExportMessage => !string.IsNullOrEmpty(ExportMessage);

    partial void OnExportMessageChanged(string? value) => OnPropertyChanged(nameof(HasExportMessage));

    private IReadOnlyList<ScanSample> ExportSamples => State == ScanState.Running ? Samples : _finalSamples;

    private static string SuggestedName(string ext) => $"scansione-{DateTime.Now:yyyyMMdd-HHmmss}.{ext}";

    private async Task RunExportAsync(Func<Exporter, Task<ExportResult>> run)
    {
        if (_exporter is null)
        {
            ExportFailed = true;
            ExportMessage = "Export non disponibile.";
            return;
        }
        if (State == ScanState.Running)
        {
            ExportFailed = true;
            ExportMessage = "Export non disponibile durante la scansione.";
            return;
        }
        var result = await run(_exporter);
        ExportFailed = result.Outcome == ExportOutcome.Failed;
        ExportMessage = result.Message;
    }

    [RelayCommand]
    public Task ExportCsvAsync() => RunExportAsync(e => e.ExportCsvAsync(ExportSamples, SuggestedName("csv")));

    [RelayCommand]
    public Task ExportPngAsync() => RunExportAsync(e => ChartExportSource is { } c
        ? e.ExportPngAsync(ExportSamples, c, SuggestedName("png"))
        : Task.FromResult(NoChart));

    [RelayCommand]
    public Task ExportSvgAsync() => RunExportAsync(e => ChartExportSource is { } c
        ? e.ExportSvgAsync(ExportSamples, c, SuggestedName("svg"))
        : Task.FromResult(NoChart));

    private static readonly ExportResult NoChart = new(ExportOutcome.Failed, null, "Grafico non disponibile per l'export.");

    /// <summary>HTML report of the last completed scan (disc, drive and verdict exist only for a completed scan).</summary>
    [RelayCommand]
    public Task ExportHtmlAsync() => RunExportAsync(async e =>
    {
        if (_lastCompleted is not { } done)
            return new ExportResult(ExportOutcome.NoData, null, Exporter.NoDataMessage);
        ReportVerification? verification = null;
        try { verification = await Task.Run(() => LoadLatestVerification(done.Disc.TocHash, done.Drive)); }
        catch (Exception ex) { Log.Warning(ex, "Reading the verification for the report failed"); }
        var data = new HtmlReportData(done.Disc, done.Drive, done.Scan, done.Samples, verification);
        return await e.ExportHtmlAsync(data, ChartExportSource, SuggestedName("html"));
    });

    private ReportVerification? LoadLatestVerification(string tocHash, DriveRecord drive) =>
        _archive?.Execute(r => ExportQueries.LatestVerification(r, tocHash, drive));

    // ---- saving ------------------------------------------------------------------------------------------

    private sealed record PendingSave(DriveRecord Drive, DiscRecord Disc, ScanRecord Scan, IReadOnlyList<ScanSample> Samples);

    private PendingSave? BuildPendingSave(IReadOnlyList<ScanSample> samples, DriveInfoEx info, Toc toc, int kBps)
    {
        var kind = CurrentKind;
        if (Evaluator.Evaluate(samples, kind, CurrentThresholds) is not Verdict verdict)
        {
            SaveMessage = "Nessun campione: la scansione non è stata salvata.";
            return null;
        }

        var s = ScanSummary.Compute(samples);
        var drive = new DriveRecord(0, info.Vendor, info.Model, info.Firmware, info.C2Supported, info.ReadOffset,
                                    info.CachesAudio, info.CacheKb, info.C2Reliable);
        var disc = new DiscRecord(0, null, null, null, null, null, null, _disc.CurrentTocHash ?? TocHasher.Hash(toc),
                                  toc.Tracks.Count, toc.TotalSectors, null, null);
        double speedX = kBps >= ScanSpeeds.MaximumKBps
            ? ScanSpeeds.ToX(info.MaxReadSpeedKBps > 0 ? info.MaxReadSpeedKBps : kBps)
            : ScanSpeeds.ToX(kBps);
        var scan = new ScanRecord(0, 0, 0, DateTimeOffset.Now, speedX, kind, verdict, s.C1Total, s.C1Mean, s.C1Max,
                                  s.C2Total, s.C2Max, kind == ScannerKind.LiteOn ? s.CuTotal : null);
        return new PendingSave(drive, disc, scan, samples);
    }

    private bool CanRetrySave() => _pendingSave is not null && State == ScanState.Idle;

    /// <summary>Saves the completed scan; on failure the samples stay in memory for a new attempt (Requirement 16.12).</summary>
    [RelayCommand(CanExecute = nameof(CanRetrySave))]
    public async Task RetrySaveAsync() => await SaveAsync();

    private async Task SaveAsync()
    {
        if (_pendingSave is not { } pending) return;
        if (_archive is null)
        {
            SaveMessage = "Archivio non disponibile: la scansione non è stata salvata.";
            return;
        }

        try
        {
            int years = _settings?.Current.RecheckYears ?? DegradationMonitor.DefaultRecheckYears;
            await Task.Run(() => _archive.SaveScan(pending.Drive, pending.Disc, pending.Scan, pending.Samples, years));
            _pendingSave = null;
            SaveFailed = false;
            SaveMessage = "Scansione salvata nello storico.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Saving the scan failed");
            SaveFailed = true;
            SaveMessage = $"Salvataggio della scansione non riuscito: {ex.Message} I campioni restano in memoria: usare \"Riprova salvataggio\".";
        }
        finally
        {
            RetrySaveCommand.NotifyCanExecuteChanged();
        }
    }
}
