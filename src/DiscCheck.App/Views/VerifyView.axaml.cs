using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DiscCheck.App.ViewModels;

namespace DiscCheck.App.Views;

public partial class VerifyView : UserControl
{
    public VerifyView()
    {
        InitializeComponent();
        BrowseFolder.Click += async (_, _) => await PickFolderAsync();
        BrowseCue.Click += async (_, _) => await PickCueAsync();
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is VerifyViewModel vm)
            await vm.OnViewOpenedAsync();
    }

    private async Task PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top || DataContext is not VerifyViewModel vm) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Cartella della Sorgente", AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            vm.SourcePath = path;
    }

    private async Task PickCueAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top || DataContext is not VerifyViewModel vm) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "File CUE della Sorgente",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("CUE sheet") { Patterns = ["*.cue"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            vm.SourcePath = path;
    }
}
