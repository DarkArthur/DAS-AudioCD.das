using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscCheck.Core;

namespace DiscCheck.App.ViewModels;

/// <summary>A TOC row: track number, start LBA and mm:ss duration (Requirement 5.2).</summary>
public sealed record TocRowViewModel(int TrackNo, int StartLba, string Duration, string Kind);

/// <summary>
/// Disc view: TOC, Media_ID and manufacturer of the disc in the selected drive. Depends only on Core interfaces.
/// Every new read or drive change first clears all previous disc data (Requirement 5.10).
/// </summary>
public sealed partial class DiscViewModel : ObservableObject
{
    private readonly IDiscReader _reader;
    private readonly DriveViewModel _drives;
    private readonly IDiscMediaAssociation? _association;
    private DiscReadResult? _current;

    public DiscViewModel(IDiscReader reader, DriveViewModel drives, IDiscMediaAssociation? association = null)
    {
        _reader = reader;
        _drives = drives;
        _association = association;
        _drives.PropertyChanged += OnDrivesChanged;
    }

    public ObservableCollection<TocRowViewModel> Tracks { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadDiscCommand))]
    [NotifyCanExecuteChangedFor(nameof(EjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(AssociateCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _totalSectors = string.Empty;

    [ObservableProperty]
    private string _mediaId = string.Empty;

    [ObservableProperty]
    private string _manufacturer = string.Empty;

    [ObservableProperty]
    private string? _mediaUnavailableReason;

    [ObservableProperty]
    private string? _associationMessage;

    [ObservableProperty]
    private bool _hasDisc;

    [ObservableProperty]
    private bool _hasMediaId;

    /// <summary>False when an unsupported disc (DVD/BD) is detected: scan and verify commands must stay disabled (Requirement 24.8).</summary>
    [ObservableProperty]
    private bool _scanAndVerifyAllowed = true;

    /// <summary>TOC of the disc read last; null when there is no readable audio disc.</summary>
    public Toc? CurrentToc => HasDisc ? _current?.Toc : null;

    /// <summary>Hash of the TOC of the disc read last; null when there is no readable audio disc.</summary>
    public string? CurrentTocHash => HasDisc ? _current?.TocHash : null;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);
    public bool HasMediaUnavailableReason => !string.IsNullOrEmpty(MediaUnavailableReason);
    public bool HasAssociationMessage => !string.IsNullOrEmpty(AssociationMessage);

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatusMessage));
    partial void OnMediaUnavailableReasonChanged(string? value) => OnPropertyChanged(nameof(HasMediaUnavailableReason));
    partial void OnAssociationMessageChanged(string? value) => OnPropertyChanged(nameof(HasAssociationMessage));

    private char? SelectedLetter => _drives.SelectedDrive is { IsIdentified: true } d ? d.DriveLetter : null;

    private void OnDrivesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DriveViewModel.SelectedDrive))
            return;

        Clear();
        StatusMessage = SelectedLetter is null ? "Selezionare un drive nella vista Drive." : null;
        ReadDiscCommand.NotifyCanExecuteChanged();
        EjectCommand.NotifyCanExecuteChanged();
    }

    private void Clear()
    {
        _current = null;
        Tracks.Clear();
        TotalSectors = string.Empty;
        MediaId = string.Empty;
        Manufacturer = string.Empty;
        MediaUnavailableReason = null;
        AssociationMessage = null;
        StatusMessage = null;
        HasDisc = false;
        HasMediaId = false;
        ScanAndVerifyAllowed = true;
        AssociateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRead))]
    public async Task ReadDiscAsync()
    {
        if (SelectedLetter is not { } letter)
            return;

        IsBusy = true;
        Clear();
        try
        {
            var result = await Task.Run(() => _reader.Read(letter));
            Apply(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRead() => SelectedLetter is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRead))]
    public async Task EjectAsync()
    {
        if (SelectedLetter is not { } letter)
            return;

        IsBusy = true;
        try
        {
            var error = await Task.Run(() => _reader.Eject(letter));
            Clear();
            StatusMessage = error;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Applies a read result; shows disc data only for <see cref="DiscStatus.Audio"/>.</summary>
    public void Apply(DiscReadResult result)
    {
        Clear();
        _current = result;
        ScanAndVerifyAllowed = result.Status != DiscStatus.UnsupportedMedium;

        if (result.Status != DiscStatus.Audio || result.Toc is not { } toc)
        {
            StatusMessage = result.Message;
            return;
        }

        for (int i = 0; i < toc.Tracks.Count; i++)
        {
            var t = toc.Tracks[i];
            Tracks.Add(new TocRowViewModel(t.TrackNo, t.StartLba, TocFormat.Duration(toc.TrackSectors(i)),
                t.IsAudio ? "audio" : "dati"));
        }

        TotalSectors = toc.TotalSectors.ToString(System.Globalization.CultureInfo.InvariantCulture);
        HasDisc = true;

        if (result.MediaId is { } id)
        {
            MediaId = id.ToString();
            Manufacturer = result.Manufacturer ?? AtipTable.UnknownManufacturer;
            HasMediaId = true;
        }
        else
        {
            MediaUnavailableReason = $"Media_ID e produttore non disponibili: {result.MediaUnavailableReason}.";
        }

        AssociateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Links the disc to its media row through <see cref="IDiscMediaAssociation"/> (persistence arrives in M4).</summary>
    [RelayCommand(CanExecute = nameof(CanAssociate))]
    public async Task AssociateAsync()
    {
        if (_association is null || _current is not { TocHash: { } hash, MediaId: { } id } r)
            return;

        IsBusy = true;
        try
        {
            await Task.Run(() => _association.Associate(hash, id, r.Manufacturer ?? AtipTable.UnknownManufacturer));
            AssociationMessage = "Disco associato al supporto.";
        }
        catch (Exception ex)
        {
            AssociationMessage = $"Associazione non riuscita: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAssociate() => _association is not null && HasMediaId && !IsBusy;
}
