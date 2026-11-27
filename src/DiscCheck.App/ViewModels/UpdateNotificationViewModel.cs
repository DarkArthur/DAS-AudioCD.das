using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscCheck.App.Startup;
using DiscCheck.Core;
using Serilog;

namespace DiscCheck.App.ViewModels;

/// <summary>
/// "New version available" banner of the main window. Runs one check per launch in the background; failures are only
/// logged (no network or no configured source must not disturb the user). It never downloads or installs anything.
/// </summary>
public sealed partial class UpdateNotificationViewModel(
    UpdateChecker checker, SettingsStore settings, string currentVersion, IUrlOpener opener) : ObservableObject
{
    private bool _checked;

    [ObservableProperty] private bool _hasUpdate;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private string? _url;

    public bool CanOpen => !string.IsNullOrEmpty(Url);
    partial void OnUrlChanged(string? value)
    {
        OnPropertyChanged(nameof(CanOpen));
        OpenCommand.NotifyCanExecuteChanged();
    }

    public string CurrentVersion => currentVersion;

    /// <summary>Checks once per launch; later calls do nothing.</summary>
    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (_checked) return;
        _checked = true;
        UpdateCheckResult result;
        try
        {
            result = await checker.CheckAsync(settings.Current.UpdateSource, currentVersion, ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed");
            return;
        }

        switch (result.Status)
        {
            case UpdateStatus.UpdateAvailable:
                Message = $"È disponibile DiscCheck {result.LatestVersion} (versione installata: {currentVersion}).";
                if (!string.IsNullOrWhiteSpace(result.Notes)) Message += " " + result.Notes!.Trim();
                Url = result.Url;
                HasUpdate = true;
                Log.Information("Update available: {Latest} (installed {Current})", result.LatestVersion, currentVersion);
                break;
            case UpdateStatus.Failed:
                Log.Warning("Update check failed: {Reason}", result.Message);
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    public void Open()
    {
        if (Url is { } url) opener.Open(url);
    }

    [RelayCommand]
    public void Dismiss() => HasUpdate = false;
}
