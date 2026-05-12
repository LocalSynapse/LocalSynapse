using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;

namespace LocalSynapse.UI.Views;

public partial class MainWindow : Window
{
    /// <summary>When true, the next close will actually shut down the app (not minimize to tray).</summary>
    internal bool IsRealQuit { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnWindowClosing;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (IsRealQuit) return;

        var settings = App.Services?.GetService<ISettingsStore>();
        if (settings == null || !settings.GetMinimizeToTrayOnClose()) return;

        e.Cancel = true;

        // First-close notification
        if (settings.GetShowFirstCloseToast())
        {
            settings.SetShowFirstCloseToast(false);
            var tray = App.Services?.GetService<TrayIconService>();
            if (tray != null)
            {
                tray.ShowBalloonOrModal(this);
                // macOS: modal handles hide after "Got it"
                // Windows: hide immediately (tooltip notification is non-blocking)
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    this.Hide();
            }
            else
            {
                this.Hide();
            }
        }
        else
        {
            this.Hide();
        }
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
