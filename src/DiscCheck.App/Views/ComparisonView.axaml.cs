using Avalonia.Controls;
using Avalonia.Interactivity;
using DiscCheck.App.ViewModels;

namespace DiscCheck.App.Views;

public partial class ComparisonView : UserControl
{
    private bool _trimming;

    public ComparisonView()
    {
        InitializeComponent();
        this.FindControl<ListBox>("ScanList")!.SelectionChanged += OnSelectionChanged;
    }

    /// <summary>Reads the Database each time the view is opened.</summary>
    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is ComparisonViewModel vm)
            await vm.LoadAsync();
    }

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_trimming || DataContext is not ComparisonViewModel vm || sender is not ListBox list) return;
        var selected = list.SelectedItems?.OfType<ScanRowViewModel>().ToList() ?? [];
        int kept = await vm.SetSelectionAsync(selected);

        // More than 8: the first 8 stay selected (Requirement 19.2).
        if (selected.Count > kept && list.SelectedItems is { } items)
        {
            _trimming = true;
            try
            {
                foreach (var extra in selected.Skip(kept))
                    items.Remove(extra);
            }
            finally { _trimming = false; }
        }
    }
}
