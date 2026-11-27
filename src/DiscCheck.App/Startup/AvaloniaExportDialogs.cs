using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using DAS.Design.Charts;
using DiscCheck.Core;

namespace DiscCheck.App.Startup;

/// <summary>Save-file picker and overwrite confirmation on the main window (Requirements 21.5, 21.6).</summary>
public sealed class AvaloniaExportDialogs : IExportDialogs
{
    private static Window? Owner =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<string?> PickSavePathAsync(ExportKind kind, string suggestedFileName)
    {
        if (Owner is not { } owner) return null;
        var (ext, label) = kind switch
        {
            ExportKind.Csv => ("csv", "CSV"),
            ExportKind.Png => ("png", "Immagine PNG"),
            ExportKind.Svg => ("svg", "Immagine SVG"),
            _ => ("html", "Report HTML"),
        };
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = ext,
            ShowOverwritePrompt = false, // the overwrite confirmation is asked by the Exporter (Requirement 21.6)
            FileTypeChoices = [new FilePickerFileType(label) { Patterns = [$"*.{ext}"] }],
        });
        return file?.TryGetLocalPath();
    }

    public async Task<bool> ConfirmOverwriteAsync(string path)
    {
        if (Owner is not { } owner) return false;
        bool result = false;
        var yes = new Button { Content = "Sovrascrivi", IsDefault = true };
        var no = new Button { Content = "Annulla", IsCancel = true };
        var dialog = new Window
        {
            Title = "Il file esiste già",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = $"Il file esiste già:\n{path}\nSovrascriverlo?", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { yes, no } },
                },
            },
        };
        yes.Click += (_, _) => { result = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
        return result;
    }
}

/// <summary>Adapts the <see cref="DasChartView"/> of a view to the Core export interface.</summary>
public sealed class DasChartViewExportSource(DasChartView view) : IChartImageSource
{
    public void ExportPng(string path) => view.ExportPng(path);
    public void ExportSvg(string path) => view.ExportSvg(path);
}
