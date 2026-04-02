using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LocalSynapse.UI.ViewModels;

namespace LocalSynapse.UI.Views;

/// <summary>
/// Search page code-behind. Handles keyboard, selection, sort, and clipboard.
/// </summary>
public partial class SearchPage : UserControl
{
    public SearchPage()
    {
        InitializeComponent();
    }

    /// <summary>Enter key triggers search.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SearchViewModel vm)
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    /// <summary>Filter tab click handler.</summary>
    private void FilterTab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && DataContext is SearchViewModel vm)
        {
            if (Enum.TryParse<SearchFilter>(tag, out var filter))
                vm.ActiveFilter = filter;
        }
    }

    /// <summary>Sort selection changed.</summary>
    private void Sort_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item
            && item.Tag is string tag && DataContext is SearchViewModel vm)
        {
            if (Enum.TryParse<SortOption>(tag, out var sort))
                vm.ActiveSort = sort;
        }
    }

    /// <summary>Folder row click — select folder.</summary>
    private void FolderRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is SearchResultFolder folder
            && DataContext is SearchViewModel vm)
        {
            vm.SelectedItem = folder;
        }
    }

    /// <summary>File row click — select file.</summary>
    private void FileRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is SearchResultFile file
            && DataContext is SearchViewModel vm)
        {
            vm.SelectedItem = file;
        }
    }

    /// <summary>File row double-click — open file.</summary>
    private void FileRow_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SearchViewModel vm)
        {
            vm.OpenFileCommand.Execute(null);
        }
    }

    /// <summary>Detail path click — open parent folder.</summary>
    private void DetailPath_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SearchViewModel vm)
        {
            vm.OpenDetailPathCommand.Execute(null);
        }
    }

    /// <summary>Copy path to clipboard with feedback.</summary>
    private async void CopyPath_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SearchViewModel vm && TopLevel.GetTopLevel(this) is { Clipboard: { } clipboard })
        {
            await clipboard.SetTextAsync(vm.DetailPath);

            // Brief visual feedback on the button
            if (sender is Button btn)
            {
                var original = btn.Content;
                btn.Content = "Copied!";
                await Task.Delay(1500);
                btn.Content = original;
            }
        }
    }
}
