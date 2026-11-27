using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscCheck.App.Startup;
using DiscCheck.Core;
using DiscCheck.Data;

namespace DiscCheck.App.ViewModels;

/// <summary>Main window state. Only startup warnings for now; functional content arrives in later tasks.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    /// <summary>Position of the Archivio/Degrado tab in the main TabControl.</summary>
    public const int DegradationTabIndex = 6;

    public MainWindowViewModel(StartupInfo startup, DriveViewModel drives, DiscViewModel disc, ScanViewModel scan,
                               VerifyViewModel verify, HistoryViewModel history, ArchiveOpenResult archive,
                               SettingsViewModel? settings = null, SettingsLoadResult? settingsLoad = null,
                               ComparisonViewModel? comparison = null, DegradationViewModel? degradation = null,
                               C2ReliabilityViewModel? c2Reliability = null)
    {
        C2Reliability = c2Reliability;
        Settings = settings;
        Degradation = degradation;
        Comparison = comparison;
        Drives = drives;
        Disc = disc;
        Scan = scan;
        Verify = verify;
        History = history;
        var warnings = new List<string>();

        if (!startup.IsAdministrator)
        {
            warnings.Add(
                "Sono richiesti privilegi di amministratore. Chiudere DiscCheck e riavviarlo come amministratore " +
                "(clic destro sull'applicazione, quindi \"Esegui come amministratore\"). " +
                "Scansioni e verifica sono disabilitate finché i privilegi non sono disponibili.");
        }

        // Database could not be created or opened: path and cause (Requirements 16.9, 16.10, 16.15).
        if (archive.Error is { } dbError)
            warnings.Add(dbError);

        // settings.json unreadable or invalid: defaults in use, the file is left as it is (Requirement 22.10).
        if (settingsLoad is { HasWarnings: true } load)
        {
            warnings.Add("settings.json non è valido o non è leggibile: sono usati i valori predefiniti e il file non è stato modificato. " +
                         string.Join(" ", load.Errors.Select(e => $"{e.Field}: {e.Message}")));
        }

        if (!startup.FileLogAvailable)
        {
            warnings.Add("Il log su file non è disponibile: i messaggi di log sono scritti solo su console.");
        }

        Warnings = warnings;
        ScansEnabled = startup.ScansEnabled;
    }

    public string AppName => "DiscCheck";

    public DriveViewModel Drives { get; }

    public C2ReliabilityViewModel? C2Reliability { get; }

    public DiscViewModel Disc { get; }


    public ScanViewModel Scan { get; }

    public VerifyViewModel Verify { get; }

    public HistoryViewModel History { get; }

    public SettingsViewModel? Settings { get; }

    public ComparisonViewModel? Comparison { get; }

    public DegradationViewModel? Degradation { get; }

    [ObservableProperty] private int _selectedTabIndex;

    /// <summary>Opens the Archivio/Degrado tab from the startup reminder and dismisses the reminder (Requirement 20.6).</summary>
    [RelayCommand]
    public void OpenDegradation()
    {
        SelectedTabIndex = DegradationTabIndex;
        Degradation?.DismissReminder();
    }

    /// <summary>Runs the once-per-launch recheck reminder; called when the main window is shown.</summary>
    public Task CheckReminderAsync() => Degradation?.CheckReminderAsync() ?? Task.CompletedTask;

    public IReadOnlyList<string> Warnings { get; }

    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>False when scans and verification must stay disabled (Requirement 26.7).</summary>
    public bool ScansEnabled { get; }
}
