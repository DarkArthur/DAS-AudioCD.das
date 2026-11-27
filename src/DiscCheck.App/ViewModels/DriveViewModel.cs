using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscCheck.Core;

namespace DiscCheck.App.ViewModels;

/// <summary>A row of the Drive view, with the display texts of Requirement 4.3.</summary>
public sealed class DriveItemViewModel
{
    public const string Undetermined = "non determinato";
    public const string NotRun = "non eseguito";

    public DriveItemViewModel(DriveListItem item)
    {
        Item = item;
        Letter = $"{item.Letter}:";
        var i = item.Info;
        if (i is null)
        {
            Title = $"{Letter} non identificabile";
            Detail = item.UnidentifiableReason ?? string.Empty;
            return;
        }

        Title = $"{Letter} {i.Vendor} {i.Model}".TrimEnd();
        Vendor = i.Vendor;
        Model = i.Model;
        Firmware = i.Firmware;
        C2Support = i.C2Supported ? "supportati" : "non supportati";
        ReadOffset = i.ReadOffset is { } o ? $"{o} campioni" : Undetermined;
        Cache = i.CachesAudio switch
        {
            true => i.CacheKb is { } kb ? $"rilevata ({kb} kB)" : "rilevata",
            false => "non rilevata",
            null => NotRun,
        };
        C2Reliability = i.C2Reliable switch { true => "affidabile", false => "non affidabile", null => "non testata" };
        Detail = string.Empty;
    }

    public DriveListItem Item { get; }
    public char DriveLetter => Item.Letter;
    public bool IsIdentified => Item.IsIdentified;
    public string Letter { get; }
    public string Title { get; }
    public string Detail { get; }
    public string Vendor { get; } = string.Empty;
    public string Model { get; } = string.Empty;
    public string Firmware { get; } = string.Empty;
    public string C2Support { get; } = string.Empty;
    public string ReadOffset { get; } = string.Empty;
    public string Cache { get; } = string.Empty;
    public string C2Reliability { get; } = string.Empty;
}

/// <summary>Drive view: enumeration and "Reset drive". Depends only on Core interfaces.</summary>
public sealed partial class DriveViewModel : ObservableObject
{
    private readonly IDriveEnumerator _enumerator;
    private readonly IDriveResetService _reset;

    public DriveViewModel(IDriveEnumerator enumerator, IDriveResetService reset)
    {
        _enumerator = enumerator;
        _reset = reset;
    }

    public ObservableCollection<DriveItemViewModel> Drives { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetDriveCommand))]
    private DriveItemViewModel? _selectedDrive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetDriveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _emptyMessage;

    [ObservableProperty]
    private string? _resetMessage;

    [ObservableProperty]
    private bool _resetSucceeded;

    public bool HasEmptyMessage => !string.IsNullOrEmpty(EmptyMessage);
    public bool HasResetMessage => !string.IsNullOrEmpty(ResetMessage);

    partial void OnEmptyMessageChanged(string? value) => OnPropertyChanged(nameof(HasEmptyMessage));
    partial void OnResetMessageChanged(string? value) => OnPropertyChanged(nameof(HasResetMessage));

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var result = await Task.Run(_enumerator.Enumerate);
            Drives.Clear();
            foreach (var item in result.Items)
                Drives.Add(new DriveItemViewModel(item));
            EmptyMessage = result.Message;
            ResetMessage = null;
            SelectedDrive = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRefresh() => !IsBusy;

    /// <summary>Enabled once a drive is selected (Requirement 4.8); stays enabled after a failure (Requirement 26.6).</summary>
    [RelayCommand(CanExecute = nameof(CanReset))]
    public async Task ResetDriveAsync()
    {
        if (SelectedDrive is not { } drive)
            return;

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _reset.Reset(drive.DriveLetter));
            ResetSucceeded = result.Success;
            ResetMessage = result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanReset() => SelectedDrive is not null && !IsBusy;
}
