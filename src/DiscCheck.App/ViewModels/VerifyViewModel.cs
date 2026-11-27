using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscCheck.App.Startup;
using DiscCheck.Core;
using DiscCheck.Verify;
using Serilog;

namespace DiscCheck.App.ViewModels;

/// <summary>State of the Verifica view.</summary>
public enum VerifyState { Idle, Running }

/// <summary>A row of the results table (Requirements 14.8, 14.9, 14.13, 15.9).</summary>
public sealed class VerifyTrackRowViewModel
{
    public VerifyTrackRowViewModel(TrackResult t)
    {
        TrackNo = t.TrackNo;
        Outcome = t.Identical ? "identica" : "diversa";
        DifferentSamples = t.DifferentSamples.ToString(CultureInfo.CurrentCulture);
        FirstDifference = t.FirstDiffIndex is long i ? i.ToString(CultureInfo.CurrentCulture) : "—";
        ExcludedSamples = t.ExcludedEdgeSamples.ToString(CultureInfo.CurrentCulture);
        Hashes = $"CRC32 letto {t.CrcRead:X8}, sorgente {t.CrcSource:X8}; " +
                 $"SHA-256 letto {t.Sha256Read}, sorgente {t.Sha256Source}";
        Unreliable = t.UnreliableLbas.Count == 0
            ? string.Empty
            : $"Lettura non affidabile ai blocchi LBA: {string.Join(", ", t.UnreliableLbas)}";
    }

    public int TrackNo { get; }
    public string Outcome { get; }
    public string DifferentSamples { get; }
    public string FirstDifference { get; }
    public string ExcludedSamples { get; }
    public string Hashes { get; }
    public string Unreliable { get; }
    public bool HasUnreliable => Unreliable.Length > 0;
}

/// <summary>
/// Verifica view: Disco and Sorgente selection, bit-perfect run with per-track progress and results table, offset
/// detection and manual entry, cache detection and saving. Orchestrates only: the run is <see cref="Verifier"/>, the
/// offset is <see cref="OffsetService"/> / <see cref="OffsetAutoDetector"/>, the cache is <see cref="CacheDetectorRunner"/>.
/// No colours and no chart code (Requirements 14, 15, 12, 35.4).
/// </summary>
public sealed partial class VerifyViewModel : ObservableObject
{
    public const string IdenticalMessage = "Disco identico alla sorgente";
    public const string NoDriveMessage = "Selezionare un drive nella vista Drive.";
    public const string NoDiscMessage = "Inserire un disco audio nel drive selezionato.";
    public const string NoSourceMessage = "Selezionare la Sorgente (cartella o file CUE).";
    public const string NoOffsetMessage = "Offset non determinato: rilevarlo automaticamente o inserirlo manualmente.";
    public const string StatusRunning = "in corso";
    public const string StatusCompleted = "completata";
    public const string StatusCancelled = "annullata";
    public const string StatusError = "errore";
    public const string StatusPreCheck = "non avviata";
    public const int MinRereads = 1;
    public const int MaxRereads = 10;
    public const int DefaultRereads = 3;

    private readonly DriveViewModel _drives;
    private readonly DiscViewModel _disc;
    private readonly Verifier _verifier;
    private readonly OffsetService _offsets;
    private readonly OffsetAutoDetector _offsetDetector;
    private readonly CacheDetectorRunner _cache;
    private readonly bool _enabled;
    private readonly IArchive? _archive;

    private CancellationTokenSource? _cts;
    private DriveRecord? _stored;
    private int? _offset;
    private int _registeredVersion;
    private PendingSave? _pendingSave;

    public VerifyViewModel(DriveViewModel drives, DiscViewModel disc, Verifier verifier, OffsetService offsets,
                           OffsetAutoDetector offsetDetector, CacheDetectorRunner cache, StartupInfo startup,
                           IArchive? archive = null, SettingsStore? settings = null)
    {
        _drives = drives;
        _disc = disc;
        _verifier = verifier;
        _offsets = offsets;
        _offsetDetector = offsetDetector;
        _cache = cache;
        _enabled = startup.ScansEnabled;
        _archive = archive;

        // N of the re-reads comes from the settings (Requirement 22.4); the field stays editable per run.
        if (settings is not null)
        {
            MaxRereadCount = settings.Current.RereadAttempts;
            settings.Changed += s => MaxRereadCount = s.RereadAttempts;
        }

        _offset = SelectedInfo?.ReadOffset;
        ShowOffset();
        ShowCache(SelectedInfo?.CachesAudio, SelectedInfo?.CacheKb);
        drives.PropertyChanged += OnDrivesChanged;
        disc.PropertyChanged += OnDiscChanged;
        RefreshAvailability();
    }

    public ObservableCollection<VerifyTrackRowViewModel> Rows { get; } = [];

    // ---- selection ---------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetectOffsetCommand))]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private int _maxRereadCount = DefaultRereads;

    partial void OnMaxRereadCountChanged(int value)
    {
        int clamped = Math.Clamp(value, MinRereads, MaxRereads);
        if (clamped != value) MaxRereadCount = clamped;
    }

    // ---- state -------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetectOffsetCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetManualOffsetCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetectCacheCommand))]
    private VerifyState _state = VerifyState.Idle;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _availabilityMessage;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressText = string.Empty;

    // ---- result ------------------------------------------------------------------------------------------

    [ObservableProperty]
    private bool _isDiscIdentical;

    [ObservableProperty]
    private string? _reportNote;

    [ObservableProperty]
    private string? _cacheDefeatNote;

    [ObservableProperty]
    private string? _saveMessage;

    [ObservableProperty]
    private bool _saveFailed;

    // ---- offset and cache --------------------------------------------------------------------------------

    [ObservableProperty]
    private string _offsetText = "non determinato";

    [ObservableProperty]
    private string _manualOffsetText = string.Empty;

    [ObservableProperty]
    private string? _offsetMessage;

    [ObservableProperty]
    private bool _offsetIsError;

    [ObservableProperty]
    private string _cacheText = "non eseguito";

    [ObservableProperty]
    private string? _cacheMessage;

    public bool IsRunning => State == VerifyState.Running;
    public bool IsIdle => State == VerifyState.Idle;
    public bool HasStatusText => !string.IsNullOrEmpty(StatusText);
    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasAvailabilityMessage => !string.IsNullOrEmpty(AvailabilityMessage);
    public bool HasReportNote => !string.IsNullOrEmpty(ReportNote);
    public bool HasCacheDefeatNote => !string.IsNullOrEmpty(CacheDefeatNote);
    public bool HasSaveMessage => !string.IsNullOrEmpty(SaveMessage);
    public bool HasOffsetMessage => !string.IsNullOrEmpty(OffsetMessage);
    public bool HasCacheMessage => !string.IsNullOrEmpty(CacheMessage);
    public bool HasRows => Rows.Count > 0;
    public string IdenticalText => IdenticalMessage;

    /// <summary>Disc line shown in the selection area.</summary>
    public string DiscSummary => _disc.CurrentToc is { } toc
        ? $"Disco: {toc.Tracks.Count(t => t.IsAudio)} tracce audio, {toc.TotalSectors.ToString(CultureInfo.CurrentCulture)} settori"
        : "Disco: nessuno";

    partial void OnStateChanged(VerifyState value)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsIdle));
    }

    partial void OnStatusTextChanged(string? value) => OnPropertyChanged(nameof(HasStatusText));
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasErrorMessage));
    partial void OnAvailabilityMessageChanged(string? value) => OnPropertyChanged(nameof(HasAvailabilityMessage));
    partial void OnReportNoteChanged(string? value) => OnPropertyChanged(nameof(HasReportNote));
    partial void OnCacheDefeatNoteChanged(string? value) => OnPropertyChanged(nameof(HasCacheDefeatNote));
    partial void OnSaveMessageChanged(string? value) => OnPropertyChanged(nameof(HasSaveMessage));
    partial void OnOffsetMessageChanged(string? value) => OnPropertyChanged(nameof(HasOffsetMessage));
    partial void OnCacheMessageChanged(string? value) => OnPropertyChanged(nameof(HasCacheMessage));

    // ---- drive / disc tracking ---------------------------------------------------------------------------

    private DriveInfoEx? SelectedInfo => _drives.SelectedDrive?.Item.Info;

    private void OnDrivesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DriveViewModel.SelectedDrive)) return;
        _stored = null;
        _offset = SelectedInfo?.ReadOffset;
        Interlocked.Increment(ref _registeredVersion);
        ShowOffset();
        ShowCache(SelectedInfo?.CachesAudio, SelectedInfo?.CacheKb);
        OffsetMessage = null;
        CacheMessage = null;
        RefreshAvailability();
    }

    private void OnDiscChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DiscViewModel.HasDisc) or nameof(DiscViewModel.ScanAndVerifyAllowed)
            or nameof(DiscViewModel.StatusMessage))
        {
            OnPropertyChanged(nameof(DiscSummary));
            RefreshAvailability();
        }
    }

    private void RefreshAvailability()
    {
        AvailabilityMessage = (_drives.SelectedDrive, _disc) switch
        {
            _ when !_enabled => "La verifica è disabilitata: servono i privilegi di amministratore.",
            (not { IsIdentified: true }, _) => NoDriveMessage,
            (_, { ScanAndVerifyAllowed: false }) => "Il supporto inserito non è un CD: verifica non disponibile.",
            (_, { HasDisc: false } d) => string.IsNullOrEmpty(d.StatusMessage) ? NoDiscMessage : $"{NoDiscMessage} {d.StatusMessage}",
            _ when string.IsNullOrWhiteSpace(SourcePath) => NoSourceMessage,
            _ when _offset is null => NoOffsetMessage,
            _ => null,
        };
        StartCommand.NotifyCanExecuteChanged();
        DetectOffsetCommand.NotifyCanExecuteChanged();
        SetManualOffsetCommand.NotifyCanExecuteChanged();
        DetectCacheCommand.NotifyCanExecuteChanged();
    }

    partial void OnSourcePathChanged(string value) => RefreshAvailability();

    private static bool SameDrive(DriveRecord a, DriveRecord b) =>
        a.Vendor == b.Vendor && a.Model == b.Model && a.Firmware == b.Firmware;

    private DriveRecord? CurrentDriveRecord()
    {
        if (SelectedInfo is not { } i) return null;
        var s = _stored;
        return new DriveRecord(s?.Id ?? 0, i.Vendor, i.Model, i.Firmware, i.C2Supported, _offset,
                               s?.CachesAudio ?? i.CachesAudio, s?.CacheKb ?? i.CacheKb, s?.C2Reliable ?? i.C2Reliable);
    }

    /// <summary>
    /// Called when the view opens. A Drive not yet in the <c>drives</c> table is added and the Cache_Detector runs
    /// (Requirement 12.1); a known Drive only reloads its stored offset and cache values.
    /// </summary>
    public async Task OnViewOpenedAsync()
    {
        if (!_disc.HasDisc && _drives.SelectedDrive is { IsIdentified: true } && _disc.ReadDiscCommand.CanExecute(null))
            await _disc.ReadDiscAsync();
        OnPropertyChanged(nameof(DiscSummary));

        if (_archive is null || CurrentDriveRecord() is not { } record || State == VerifyState.Running)
        {
            RefreshAvailability();
            return;
        }

        int version = Volatile.Read(ref _registeredVersion);
        bool isNew;
        try
        {
            (isNew, _stored) = await Task.Run(() => _archive.Execute<(bool, DriveRecord?)>(r =>
            {
                var existing = r.Drives.List().FirstOrDefault(d => SameDrive(d, record));
                if (existing is not null) return (false, existing);
                long id = r.Drives.AddIfMissing(record);
                return (true, record with { Id = id });
            }));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Registering the drive failed");
            RefreshAvailability();
            return;
        }

        if (version != Volatile.Read(ref _registeredVersion)) return; // another drive was selected meanwhile

        if (_stored is { } s)
        {
            _offset ??= s.ReadOffset;
            ShowOffset();
            ShowCache(s.CachesAudio, s.CacheKb);
        }
        RefreshAvailability();

        if (isNew && CanDetectCache())
            await DetectCacheAsync();
    }

    // ---- cache detection (Requirements 12.1-12.5) --------------------------------------------------------

    private bool CanDetectCache() =>
        State == VerifyState.Idle && _enabled && _drives.SelectedDrive is { IsIdentified: true }
        && _disc.HasDisc && _disc.ScanAndVerifyAllowed && _disc.CurrentToc is not null;

    [RelayCommand(CanExecute = nameof(CanDetectCache))]
    public async Task DetectCacheAsync()
    {
        if (CurrentDriveRecord() is not { } record || SelectedInfo is not { } info || _disc.CurrentToc is not { } toc)
            return;

        State = VerifyState.Running;
        CacheMessage = "Rilevamento della cache in corso…";
        try
        {
            var result = await Task.Run(() => _cache.Run(info.Letter, record, toc));
            if (!result.Completed)
            {
                CacheMessage = $"Rilevamento della cache interrotto: {result.AbortReason}";
                return;
            }

            _stored = record with { CachesAudio = result.CachesAudio, CacheKb = result.CacheKb };
            ShowCache(result.CachesAudio, result.CacheKb);
            CacheMessage = result.Warnings.Count > 0 ? string.Join(" ", result.Warnings) : "Rilevamento della cache completato.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cache detection failed");
            CacheMessage = $"Rilevamento della cache non riuscito: {ex.Message}";
        }
        finally
        {
            State = VerifyState.Idle;
            RefreshAvailability();
        }
    }

    private void ShowCache(bool? caches, int? kb) =>
        CacheText = caches switch
        {
            true => kb is int k ? $"rilevata ({k} kB)" : "rilevata",
            false => "non rilevata",
            null => "non eseguito",
        };

    // ---- offset (Requirements 15.1-15.7) -----------------------------------------------------------------

    private void ShowOffset() =>
        OffsetText = _offset is int o ? $"{o.ToString(CultureInfo.CurrentCulture)} campioni" : "non determinato";

    private bool CanDetectOffset() =>
        State == VerifyState.Idle && _enabled && !string.IsNullOrWhiteSpace(SourcePath)
        && _drives.SelectedDrive is { IsIdentified: true } && _disc.HasDisc && _disc.ScanAndVerifyAllowed
        && _disc.CurrentToc is not null;

    /// <summary>Automatic detection on the first track; on failure the stored offset is untouched and manual entry is requested.</summary>
    [RelayCommand(CanExecute = nameof(CanDetectOffset))]
    public async Task DetectOffsetAsync()
    {
        if (CurrentDriveRecord() is not { } record || SelectedInfo is not { } info || _disc.CurrentToc is not { } toc)
            return;

        State = VerifyState.Running;
        OffsetIsError = false;
        OffsetMessage = "Rilevamento dell'offset in corso…";
        ProgressPercent = 0;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var progress = new Progress<double>(f => ProgressPercent = Math.Min(100, f * 100));
        string source = SourcePath;
        try
        {
            var result = await Task.Run(() =>
                _offsetDetector.Run(info.Letter, record, toc, source, f => ((IProgress<double>)progress).Report(f), ct));
            if (result.IsFound)
            {
                _offset = result.Offset;
                _stored = record with { ReadOffset = _offset };
                ShowOffset();
                OffsetMessage = $"Offset rilevato: {_offset} campioni (applicato a tutte le tracce).";
            }
            else
            {
                OffsetIsError = true;
                OffsetMessage = result.Message; // offset unchanged; manual entry requested (Requirements 15.6, 15.7)
            }
        }
        catch (OperationCanceledException)
        {
            OffsetMessage = "Rilevamento dell'offset annullato.";
        }
        catch (Exception ex) when (ex is SourceFileException or ReadCdException)
        {
            OffsetIsError = true;
            OffsetMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Offset detection failed");
            OffsetIsError = true;
            OffsetMessage = $"Rilevamento dell'offset non riuscito: {ex.Message}";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            State = VerifyState.Idle;
            RefreshAvailability();
        }
    }

    private bool CanSetManual() => State == VerifyState.Idle && _drives.SelectedDrive is { IsIdentified: true };

    /// <summary>Validates -5000..+5000, saves and uses the value; a rejected value keeps the previous offset (Requirements 15.2, 15.3).</summary>
    [RelayCommand(CanExecute = nameof(CanSetManual))]
    public async Task SetManualOffsetAsync()
    {
        if (CurrentDriveRecord() is not { } record) return;

        var check = OffsetService.ValidateManual(ManualOffsetText);
        if (!check.Accepted)
        {
            OffsetIsError = true;
            OffsetMessage = check.Error;
            return;
        }

        try
        {
            await Task.Run(() => _offsets.SetManual(record, ManualOffsetText));
            _offset = check.Offset;
            _stored = record with { ReadOffset = _offset };
            ShowOffset();
            OffsetIsError = false;
            OffsetMessage = $"Offset impostato: {_offset} campioni.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Saving the manual offset failed");
            OffsetIsError = true;
            OffsetMessage = $"Salvataggio dell'offset non riuscito: {ex.Message}";
        }
        finally
        {
            RefreshAvailability();
        }
    }

    // ---- start / cancel ----------------------------------------------------------------------------------

    private bool CanStart() =>
        State == VerifyState.Idle && _enabled && !string.IsNullOrWhiteSpace(SourcePath) && _offset is not null
        && _drives.SelectedDrive is { IsIdentified: true } && _disc.HasDisc && _disc.ScanAndVerifyAllowed
        && _disc.CurrentToc is not null;

    [RelayCommand(CanExecute = nameof(CanStart))]
    public async Task StartAsync()
    {
        if (CurrentDriveRecord() is not { } record || SelectedInfo is not { } info || _disc.CurrentToc is not { } toc
            || _offset is not int offset)
            return;

        ResetRun();
        State = VerifyState.Running;
        StatusText = StatusRunning;
        RefreshAvailability();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var progress = new Progress<VerifyProgress>(OnProgress);
        var options = new VerifyOptions(info.Letter, record, toc, SourcePath.Trim(), offset, MaxRereadCount);

        try
        {
            var report = await Task.Run(() => _verifier.Run(options, progress, ct), CancellationToken.None);
            ApplyReport(report, record, toc);
            if (report.Result is { } result) // cancelled or rejected runs have nothing to save (Requirement 14.12)
            {
                _pendingSave = BuildPendingSave(report, result, record, toc, options.SourcePath);
                await SaveAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Verification failed unexpectedly");
            ErrorMessage = $"Verifica non riuscita: {ex.Message}";
            StatusText = StatusError;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            State = VerifyState.Idle;
            RefreshAvailability();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel()
    {
        _cts?.Cancel();
        StatusText = "annullamento in corso";
    }

    private bool CanCancel() => State == VerifyState.Running;

    private void ResetRun()
    {
        Rows.Clear();
        OnPropertyChanged(nameof(HasRows));
        ErrorMessage = null;
        StatusText = null;
        ReportNote = null;
        CacheDefeatNote = null;
        IsDiscIdentical = false;
        _pendingSave = null;
        SaveMessage = null;
        SaveFailed = false;
        RetrySaveCommand.NotifyCanExecuteChanged();
        ProgressPercent = 0;
        ProgressText = string.Empty;
    }

    private void OnProgress(VerifyProgress p)
    {
        ProgressPercent = p.Percent;
        ProgressText = $"Traccia {p.TrackNo} ({p.TrackIndex + 1}/{p.TrackCount}): {p.Percent.ToString("0", CultureInfo.CurrentCulture)}%";
        if (p.Completed is { } t)
        {
            Rows.Add(new VerifyTrackRowViewModel(t));
            OnPropertyChanged(nameof(HasRows));
        }
    }

    /// <summary>Shows the final state; "Disco identico alla sorgente" only for a completed all-identical run (Requirements 14.10, 14.11).</summary>
    private void ApplyReport(VerifyReport report, DriveRecord drive, Toc toc)
    {
        // The table holds every compared track, including those not reported through progress (final sync).
        if (Rows.Count != report.Tracks.Count)
        {
            Rows.Clear();
            foreach (var t in report.Tracks) Rows.Add(new VerifyTrackRowViewModel(t));
            OnPropertyChanged(nameof(HasRows));
        }

        switch (report.Status)
        {
            case VerifyStatus.Completed:
                StatusText = StatusCompleted;
                ProgressPercent = 100;
                break;
            case VerifyStatus.Cancelled:
                StatusText = StatusCancelled;
                break;
            case VerifyStatus.PreCheckFailed:
                StatusText = StatusPreCheck;
                ErrorMessage = report.Message;
                break;
            default:
                StatusText = StatusError;
                ErrorMessage = report.Message;
                break;
        }

        IsDiscIdentical = report.IsDiscIdentical;

        if (report.Tracks.Count > 0)
        {
            ReportNote = $"Offset usato: {report.OffsetUsed} campioni. Campioni di bordo esclusi per traccia: " +
                         string.Join(", ", report.Tracks.Select(t => $"{t.TrackNo}: {t.ExcludedEdgeSamples}")) + ".";
        }

        if (report.CacheDefeatLimited)
        {
            var lbas = toc.Tracks.Select((t, i) => (t, i)).Where(x => x.t.IsAudio)
                .SelectMany(x => new long[] { x.t.StartLba, x.t.StartLba + Math.Max(0, toc.TrackSectors(x.i) - 1) });
            long missing = CacheDefeatPlanner.MaxMissingSectors(lbas, toc.LeadOutLba, drive.CacheKb);
            CacheDefeatNote = "Alcune riletture sono state eseguite senza Cache_Defeat: il disco non contiene un'area lontana " +
                              $"di dimensione sufficiente (settori mancanti, al massimo {missing.ToString(CultureInfo.CurrentCulture)}).";
        }
    }

    // ---- saving (Requirements 14.12, 14.17, 15.10) -------------------------------------------------------

    private sealed record PendingSave(DriveRecord Drive, DiscRecord Disc, VerificationRecord Verification,
                                      IReadOnlyList<VerificationTrackRecord> Tracks);

    private PendingSave BuildPendingSave(VerifyReport report, VerificationResult result, DriveRecord drive, Toc toc, string source)
    {
        var disc = new DiscRecord(0, null, null, null, null, null, null, _disc.CurrentTocHash ?? TocHasher.Hash(toc),
                                  toc.Tracks.Count, toc.TotalSectors, null, null);
        var verification = new VerificationRecord(0, 0, 0, DateTimeOffset.Now, source, report.OffsetUsed, result);
        return new PendingSave(drive, disc, verification, report.ToTrackRecords());
    }

    private bool CanRetrySave() => _pendingSave is not null && State == VerifyState.Idle;

    /// <summary>Saves the verification; on failure the results stay in memory for a new attempt.</summary>
    [RelayCommand(CanExecute = nameof(CanRetrySave))]
    public async Task RetrySaveAsync() => await SaveAsync();

    private async Task SaveAsync()
    {
        if (_pendingSave is not { } pending) return;
        if (_archive is null)
        {
            SaveMessage = "Archivio non disponibile: la verifica non è stata salvata.";
            return;
        }

        try
        {
            await Task.Run(() => _archive.SaveVerification(pending.Drive, pending.Disc, pending.Verification, pending.Tracks));
            _pendingSave = null;
            SaveFailed = false;
            SaveMessage = "Verifica salvata nello storico.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Saving the verification failed");
            SaveFailed = true;
            SaveMessage = $"Salvataggio della verifica non riuscito: {ex.Message} I risultati restano in memoria: usare \"Riprova salvataggio\".";
        }
        finally
        {
            RetrySaveCommand.NotifyCanExecuteChanged();
        }
    }
}
