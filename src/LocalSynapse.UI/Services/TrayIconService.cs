using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Pipeline.Interfaces;
using LocalSynapse.Pipeline.Orchestration;
using LocalSynapse.UI.Services.Localization;

namespace LocalSynapse.UI.Services;

/// <summary>
/// Manages the system tray icon lifecycle, context menu, and related behaviors.
/// Skipped entirely in MCP/Dump modes.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly RuntimeMode _mode;
    private readonly IPipelineOrchestrator _orchestrator;
    private readonly ISettingsStore _settings;
    private readonly ILocalizationService _loc;
    private TrayIcon? _trayIcon;
    private Window? _mainWindow;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private bool _disposed;

    /// <summary>Creates a new TrayIconService.</summary>
    public TrayIconService(
        RuntimeMode mode,
        IPipelineOrchestrator orchestrator,
        ISettingsStore settings,
        ILocalizationService loc)
    {
        _mode = mode;
        _orchestrator = orchestrator;
        _settings = settings;
        _loc = loc;
    }

    /// <summary>Initialize tray icon and context menu. No-op in non-UI modes.</summary>
    public void Initialize(Window mainWindow, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_mode != RuntimeMode.Ui) return;

        _mainWindow = mainWindow;
        _desktop = desktop;

        _trayIcon = new TrayIcon
        {
            ToolTipText = "LocalSynapse",
            IsVisible = true,
        };

        // Load icon from embedded resource
        try
        {
            var uri = new Uri("avares://LocalSynapse.UI/Assets/app-icon-256.png");
            var assets = Avalonia.Platform.AssetLoader.Open(uri);
            _trayIcon.Icon = new WindowIcon(assets);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Tray] Failed to load icon: {ex.Message}");
        }

        _trayIcon.Menu = BuildContextMenu();
        _trayIcon.Clicked += OnTrayClicked;

        // Subscribe to pipeline state for dynamic status
        _orchestrator.ProgressChanged += OnProgressChanged;

        Debug.WriteLine("[Tray] TrayIcon initialized");
    }

    /// <summary>Show first-close notification. Windows: balloon tip. macOS: in-window modal.</summary>
    public async void ShowBalloonOrModal(Window parentWindow)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: in-window modal
            var dialog = new Window
            {
                Title = _loc[StringKeys.AlwaysOn.FirstCloseTitle],
                Width = 380,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
            };

            var panel = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 16,
            };
            panel.Children.Add(new TextBlock
            {
                Text = _loc[StringKeys.AlwaysOn.MacFirstCloseBody],
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
            var button = new Button
            {
                Content = _loc[StringKeys.AlwaysOn.MacFirstCloseConfirm],
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            button.Click += (_, _) => dialog.Close();
            panel.Children.Add(button);
            dialog.Content = panel;

            await dialog.ShowDialog(parentWindow);
            parentWindow.Hide();
        }
        else
        {
            // Windows: use tooltip-style notification (Avalonia TrayIcon doesn't have ShowBalloonTip)
            // Fallback: update tooltip text briefly
            if (_trayIcon != null)
            {
                _trayIcon.ToolTipText = _loc[StringKeys.AlwaysOn.FirstCloseBody];
                // Reset tooltip after 5 seconds
                _ = Task.Delay(5000).ContinueWith(_ =>
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_trayIcon != null)
                            _trayIcon.ToolTipText = "LocalSynapse";
                    }));
            }
        }
    }

    /// <summary>Perform a real application quit (not minimize).</summary>
    public void RequestQuit()
    {
        if (_mainWindow is Views.MainWindow mw)
            mw.IsRealQuit = true;
        _desktop?.Shutdown();
    }

    /// <summary>Toggle main window visibility.</summary>
    public void ToggleMainWindow()
    {
        if (_mainWindow == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow.IsVisible)
            {
                _mainWindow.Hide();
            }
            else
            {
                _mainWindow.Show();
                _mainWindow.Activate();
            }
        });
    }

    private NativeMenu BuildContextMenu()
    {
        var menu = new NativeMenu();

        // Quick Search
        var searchItem = new NativeMenuItem(_loc[StringKeys.AlwaysOn.TrayQuickSearch]);
        searchItem.Click += (_, _) => ToggleMainWindow();
        menu.Items.Add(searchItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        // Indexing status (disabled info item)
        var statusItem = new NativeMenuItem(_loc[StringKeys.AlwaysOn.TrayIndexingIdle])
        {
            IsEnabled = false,
        };
        menu.Items.Add(statusItem);

        // Pause/Resume
        var pauseItem = new NativeMenuItem(_loc[StringKeys.AlwaysOn.TrayPause]);
        pauseItem.Click += (_, _) =>
        {
            if (_orchestrator.IsPaused)
            {
                _orchestrator.Resume();
                pauseItem.Header = _loc[StringKeys.AlwaysOn.TrayPause];
                statusItem.Header = _loc[StringKeys.AlwaysOn.TrayIndexingRunning];
            }
            else
            {
                _orchestrator.Pause();
                pauseItem.Header = _loc[StringKeys.AlwaysOn.TrayResume];
                statusItem.Header = _loc[StringKeys.AlwaysOn.TrayIndexingIdle];
            }
        };
        menu.Items.Add(pauseItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        // Settings
        var settingsItem = new NativeMenuItem(_loc[StringKeys.AlwaysOn.TraySettings]);
        settingsItem.Click += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _mainWindow?.Show();
                _mainWindow?.Activate();
                if (_mainWindow?.DataContext is ViewModels.MainViewModel mainVm)
                    mainVm.NavigateToCommand.Execute(ViewModels.PageType.Settings);
            });
        };
        menu.Items.Add(settingsItem);

        // Quit
        var quitItem = new NativeMenuItem(_loc[StringKeys.AlwaysOn.TrayQuit]);
        quitItem.Click += (_, _) => RequestQuit();
        menu.Items.Add(quitItem);

        return menu;
    }

    private void OnTrayClicked(object? sender, EventArgs e)
    {
        ToggleMainWindow();
    }

    private void OnProgressChanged(PipelineProgress progress)
    {
        // Update tooltip with current phase
        Dispatcher.UIThread.Post(() =>
        {
            if (_trayIcon == null) return;
            _trayIcon.ToolTipText = _orchestrator.IsRunning
                ? $"LocalSynapse — {progress.Phase}"
                : "LocalSynapse";
        });
    }

    /// <summary>Dispose tray icon resources.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _orchestrator.ProgressChanged -= OnProgressChanged;
        if (_trayIcon != null)
        {
            _trayIcon.Clicked -= OnTrayClicked;
            _trayIcon.IsVisible = false;
            _trayIcon = null;
        }
    }
}
