using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscCheck.Core;
using DiscCheck.Verify;
using Serilog;

namespace DiscCheck.App.ViewModels;

public enum C2TestUiState { Idle, Running, Completed, Cancelled, Error }

/// <summary>
/// C2_Reliability_Test panel of the Drive view (Requirements 13.1, 13.3, 13.4, 13.6-13.8). The test starts only after the
/// user has declared a disc with a known defect and explicitly confirmed; the Drive must declare C2 support.
/// </summary>
public sealed partial class C2ReliabilityViewModel : ObservableObject
{
    private readonly DriveViewModel _drives;
    private readonly DiscViewModel _disc;
    private readonly C2ReliabilityTestRunner _runner;
    private readonly bool _enabled;
    private CancellationTokenSource? _cts;

    public C2ReliabilityViewModel(DriveViewModel drives, DiscViewModel disc, C2ReliabilityTestRunner runner, bool testsEnabled = true)
    {
        _drives = drives;
        _disc = disc;
        _runner = runner;
        _enabled = testsEnabled;
        drives.PropertyChanged += OnSourceChanged;
        disc.PropertyChanged += OnSourceChanged;
        Refresh();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _knownDefectDeclared;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _confirmed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private C2TestUiState _state = C2TestUiState.Idle;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _resultMessage;

    [ObservableProperty]
    private string? _availabilityMessage;

    public bool IsRunning => State == C2TestUiState.Running;
    public bool HasResultMessage => !string.IsNullOrEmpty(ResultMessage);
    public bool HasAvailabilityMessage => !string.IsNullOrEmpty(AvailabilityMessage);

    partial void OnStateChanged(C2TestUiState value) => OnPropertyChanged(nameof(IsRunning));
    partial void OnResultMessageChanged(string? value) => OnPropertyChanged(nameof(HasResultMessage));
    partial void OnAvailabilityMessageChanged(string? value) => OnPropertyChanged(nameof(HasAvailabilityMessage));

    private DriveInfoEx? SelectedInfo => _drives.SelectedDrive?.Item.Info;

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (State == C2TestUiState.Running)
            return;
        Refresh();
    }

    private void Refresh()
    {
        AvailabilityMessage = !_enabled
            ? "Il test richiede privilegi di amministratore."
            : SelectedInfo is null ? "Selezionare un Drive identificato nell'elenco."
            : !SelectedInfo.C2Supported ? C2ReliabilityTestRunner.UnsupportedMessage
            : !_disc.HasDisc || _disc.CurrentToc is null ? "Leggere nella vista Disco un disco audio con un difetto noto."
            : null;
        StartCommand.NotifyCanExecuteChanged();
    }

    private bool CanStart() =>
        State != C2TestUiState.Running && AvailabilityMessage is null && KnownDefectDeclared && Confirmed;

    [RelayCommand(CanExecute = nameof(CanStart))]
    public async Task StartAsync()
    {
        if (SelectedInfo is not { } info || _disc.CurrentToc is not { } toc)
            return;

        var drive = new DriveRecord(0, info.Vendor.Trim(), info.Model.Trim(), info.Firmware.Trim(),
                                    info.C2Supported, info.ReadOffset, info.CachesAudio, info.CacheKb);
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        State = C2TestUiState.Running;
        Progress = 0;
        ResultMessage = "Test in corso…";
        var progress = new Progress<double>(p => Progress = p);
        try
        {
            var result = await Task.Run(() => _runner.Run(info.Letter, drive, toc, p => ((IProgress<double>)progress).Report(p), ct), CancellationToken.None);
            ResultMessage = result.Message + (result.CacheDefeatLimited
                ? " Il Cache_Defeat non è stato applicabile a tutte le letture." : string.Empty);
            State = result.Status switch
            {
                C2TestStatus.Cancelled => C2TestUiState.Cancelled,
                C2TestStatus.Completed => C2TestUiState.Completed,
                _ => C2TestUiState.Error,
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "C2_Reliability_Test failed");
            ResultMessage = $"Test non riuscito: {ex.Message}. L'affidabilità C2 del Drive non è stata modificata.";
            State = C2TestUiState.Error;
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            Confirmed = false;
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    public void Cancel() => _cts?.Cancel();
}
