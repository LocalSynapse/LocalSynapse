using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System.Runtime.InteropServices;

namespace LocalSynapse.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Global shortcut: Ctrl+K (Windows/Linux) or ⌘K (macOS) focuses the search box.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.K)
        {
            var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            var modifierPressed = isMac
                ? e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                : e.KeyModifiers.HasFlag(KeyModifiers.Control);

            if (modifierPressed)
            {
                // Navigate to Search tab
                if (DataContext is ViewModels.MainViewModel mainVm)
                    mainVm.NavigateToCommand.Execute(ViewModels.PageType.Search);

                // Find the SearchBox TextBox in the visual tree
                var searchBox = this.GetVisualDescendants()
                    .OfType<TextBox>()
                    .FirstOrDefault(t => t.Name == "SearchBox");
                if (searchBox != null)
                {
                    searchBox.Focus();
                    searchBox.SelectAll();
                }

                e.Handled = true;
            }
        }
        base.OnKeyDown(e);
    }
}
