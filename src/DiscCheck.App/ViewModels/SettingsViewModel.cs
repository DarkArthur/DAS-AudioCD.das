using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscCheck.App.Startup;
using DiscCheck.Core;
using Serilog;

namespace DiscCheck.App.ViewModels;

/// <summary>
/// Impostazioni view (Requirement 22): edits a copy of the stored settings and applies them on save. Orchestrates only:
/// validation is <see cref="SettingsValidator"/>, persistence is <see cref="SettingsStore"/> (the file is untouched when
/// anything fails), the Database switch is <see cref="IArchiveSwitcher"/>, the theme <see cref="IThemeApplier"/> and
/// the Verbose log level <see cref="LogLevelController"/>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const string SavedMessage = "Impostazioni salvate.";
    public const string ConfirmDatabaseMessage =
        "Cambiare il percorso del Database? L'accesso ai dati sul percorso precedente verrà chiuso e il Database " +
        "sarà aperto sul nuovo percorso (creato se non esiste).";

    private readonly SettingsStore _store;
    private readonly LogLevelController? _log;
    private readonly IThemeApplier? _themeApplier;
    private readonly IArchiveSwitcher? _archive;
    private Settings? _pending;

    public SettingsViewModel(SettingsStore store, LogLevelController? log = null, IThemeApplier? theme = null,
                             IArchiveSwitcher? archive = null)
    {
        _store = store;
        _log = log;
        _themeApplier = theme;
        _archive = archive;
        Load(store.Current);
    }

    public static IReadOnlyList<LiteOnMethod> LiteOnMethods { get; } = Enum.GetValues<LiteOnMethod>();
    public static IReadOnlyList<SettingsTheme> Themes { get; } = Enum.GetValues<SettingsTheme>();

    /// <summary>Violated constraints, one line per field, shown next to the form (Requirement 22.9).</summary>
    public ObservableCollection<string> Errors { get; } = [];

    public bool HasErrors => Errors.Count > 0;

    // ---- editable fields (numbers are text so that a wrong entry is reported instead of silently coerced) ----

    [ObservableProperty] private string _c1MeanWarnText = string.Empty;
    [ObservableProperty] private string _c1MaxWarnText = string.Empty;
    [ObservableProperty] private string _c1MaxKoText = string.Empty;
    [ObservableProperty] private string _defaultSpeedText = string.Empty;
    [ObservableProperty] private LiteOnMethod _liteOnMethod;
    [ObservableProperty] private string _databasePath = string.Empty;
    [ObservableProperty] private bool _debugMode;
    [ObservableProperty] private SettingsTheme _theme;
    [ObservableProperty] private string _rereadAttemptsText = string.Empty;
    [ObservableProperty] private string _scsiTimeoutText = string.Empty;
    [ObservableProperty] private string _recheckYearsText = string.Empty;

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _awaitingDatabaseConfirmation;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);
    public string ConfirmDatabaseText => ConfirmDatabaseMessage;

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatusMessage));

    private void Load(Settings s)
    {
        C1MeanWarnText = Format(s.Thresholds.C1MeanWarn);
        C1MaxWarnText = Format(s.Thresholds.C1MaxWarn);
        C1MaxKoText = Format(s.Thresholds.C1MaxKo);
        DefaultSpeedText = s.DefaultSpeedX.ToString(CultureInfo.CurrentCulture);
        LiteOnMethod = s.LiteOnMethod;
        DatabasePath = s.DatabasePath;
        DebugMode = s.DebugMode;
        Theme = s.Theme;
        RereadAttemptsText = s.RereadAttempts.ToString(CultureInfo.CurrentCulture);
        ScsiTimeoutText = s.ScsiTimeoutSeconds.ToString(CultureInfo.CurrentCulture);
        RecheckYearsText = s.RecheckYears.ToString(CultureInfo.CurrentCulture);
    }

    private static string Format(double v) => v.ToString("R", CultureInfo.CurrentCulture);

    private void SetErrors(IEnumerable<string> lines)
    {
        Errors.Clear();
        foreach (var l in lines) Errors.Add(l);
        OnPropertyChanged(nameof(HasErrors));
    }

    private static string Line(SettingsError e) => $"{e.Field}: {e.Message}";

    // ---- save ---------------------------------------------------------------------------------------------

    [RelayCommand]
    public void Save()
    {
        StatusMessage = null;
        AwaitingDatabaseConfirmation = false;
        _pending = null;

        if (!TryBuild(out var settings, out var errors))
        {
            SetErrors(errors);
            return;
        }

        SetErrors([]);
        bool pathChanged = _archive is not null && !string.Equals(
            DatabasePathOf(settings), DatabasePathOf(_store.Current), StringComparison.OrdinalIgnoreCase);
        if (pathChanged)
        {
            _pending = settings;
            AwaitingDatabaseConfirmation = true; // nothing changes until the user confirms (Requirement 22.6)
            return;
        }

        Commit(settings);
    }

    [RelayCommand]
    public void ConfirmDatabaseChange()
    {
        if (_pending is not { } settings || _archive is null) return;
        _pending = null;
        AwaitingDatabaseConfirmation = false;

        string previousPath = _archive.CurrentPath;
        if (_archive.TryReopen(DatabasePathOf(settings)) is { } error)
        {
            // Previous path and data access are kept, settings.json is not touched (Requirement 22.11).
            DatabasePath = _store.Current.DatabasePath;
            SetErrors([error]);
            return;
        }

        if (!Commit(settings))
        {
            // The file could not be written: go back to the Database in use before, so that file and runtime agree.
            if (_archive.TryReopen(previousPath) is { } rollback)
                Log.Error("Cannot return to the previous Database: {Error}", rollback);
            DatabasePath = _store.Current.DatabasePath;
        }
    }

    [RelayCommand]
    public void CancelDatabaseChange()
    {
        _pending = null;
        AwaitingDatabaseConfirmation = false;
        DatabasePath = _store.Current.DatabasePath; // the previous path is kept (Requirement 22.6)
        StatusMessage = null;
    }

    private bool Commit(Settings settings)
    {
        var result = _store.Save(settings);
        if (!result.Success)
        {
            SetErrors(result.Errors.Select(Line));
            return false;
        }

        SetErrors([]);
        _log?.SetDebugMode(settings.DebugMode);
        _themeApplier?.Apply(settings.Theme);
        StatusMessage = SavedMessage;
        return true;
    }

    /// <summary>Environment variables of the path (e.g. <c>%LOCALAPPDATA%</c>) are expanded before it is used.</summary>
    public static string DatabasePathOf(Settings s) => ExpandPath(s.DatabasePath);

    public static string ExpandPath(string path) => Environment.ExpandEnvironmentVariables(path.Trim());

    private bool TryBuild(out Settings settings, out List<string> errors)
    {
        errors = [];
        double mean = ParseDouble(C1MeanWarnText, "thresholds.c1MeanWarn", errors);
        double warn = ParseDouble(C1MaxWarnText, "thresholds.c1MaxWarn", errors);
        double ko = ParseDouble(C1MaxKoText, "thresholds.c1MaxKo", errors);
        int speed = ParseInt(DefaultSpeedText, "defaultSpeedX", "La velocità deve essere un intero tra 1 e 52.", errors);
        int rereads = ParseInt(RereadAttemptsText, "rereadAttempts", "Il numero di tentativi di rilettura deve essere un intero tra 1 e 10.", errors);
        int timeout = ParseInt(ScsiTimeoutText, "scsiTimeoutSeconds", "Il timeout SCSI deve essere un intero tra 1 e 300 secondi.", errors);
        int years = ParseInt(RecheckYearsText, "recheckYears", "L'intervallo di ricontrollo deve essere un intero tra 5 e 10 anni.", errors);

        settings = new Settings
        {
            Thresholds = new Thresholds(mean, warn, ko),
            DefaultSpeedX = speed,
            LiteOnMethod = LiteOnMethod,
            DatabasePath = DatabasePath?.Trim() ?? string.Empty,
            DebugMode = DebugMode,
            Theme = Theme,
            RereadAttempts = rereads,
            ScsiTimeoutSeconds = timeout,
            RecheckYears = years,
        };

        // Range checks only on fields that parsed, to avoid reporting the same field twice.
        var parseFailed = errors.Select(e => e[..e.IndexOf(':')]).ToHashSet();
        errors.AddRange(SettingsValidator.Validate(settings)
            .Where(e => !parseFailed.Contains(e.Field)).Select(Line));
        return errors.Count == 0;
    }

    private static double ParseDouble(string text, string field, List<string> errors)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) && double.IsFinite(v)) return v;
        errors.Add($"{field}: La soglia deve essere un valore numerico maggiore o uguale a 0.");
        return double.NaN;
    }

    private static int ParseInt(string text, string field, string message, List<string> errors)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var v)) return v;
        errors.Add($"{field}: {message}");
        return 0;
    }
}
