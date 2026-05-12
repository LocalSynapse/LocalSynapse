using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.UI.Interfaces;
using LocalSynapse.UI.Services.Localization;

namespace LocalSynapse.UI.Services;

/// <summary>
/// Orchestrates global hotkey registration with platform-specific providers.
/// Handles fallback logic, combo changes, and window toggle behavior.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private readonly RuntimeMode _mode;
    private readonly ISettingsStore _settings;
    private readonly ILocalizationService _loc;
    private readonly IGlobalHotkeyProvider _provider;
    private Window? _mainWindow;
    private string? _registeredCombo;

    /// <summary>Predefined safe hotkey combos for Windows.</summary>
    public static readonly (string display, string combo)[] WindowsPresets =
    [
        ("Alt + Space", "Alt+Space"),
        ("Ctrl + Shift + Space", "Ctrl+Shift+Space"),
        ("Ctrl + Alt + S", "Ctrl+Alt+S"),
        ("Ctrl + Shift + K", "Ctrl+Shift+K"),
        ("Win + Shift + S", "Win+Shift+S"),
        ("Ctrl + `", "Ctrl+OemTilde"),
    ];

    /// <summary>Predefined safe hotkey combos for macOS.</summary>
    public static readonly (string display, string combo)[] MacOsPresets =
    [
        ("\u2318\u21e7Space", "Cmd+Shift+Space"),
        ("\u2303\u21e7Space", "Ctrl+Shift+Space"),
        ("\u2318\u2303S", "Cmd+Ctrl+S"),
        ("\u2318\u21e7K", "Cmd+Shift+K"),
        ("\u2318\u21e7F", "Cmd+Shift+F"),
        ("\u2318`", "Cmd+OemTilde"),
    ];

    /// <summary>Returns the platform-appropriate presets.</summary>
    public static (string display, string combo)[] GetPresets()
        => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? MacOsPresets : WindowsPresets;

    /// <summary>Returns the platform default combo string.</summary>
    public static string GetPlatformDefault()
        => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Cmd+Shift+Space" : "Alt+Space";

    /// <summary>Returns the platform fallback combo string.</summary>
    public static string GetPlatformFallback() => "Ctrl+Shift+Space";

    /// <summary>The currently registered combo, or null if none.</summary>
    public string? RegisteredCombo => _registeredCombo;

    /// <summary>Creates a new GlobalHotkeyService.</summary>
    public GlobalHotkeyService(
        RuntimeMode mode,
        ISettingsStore settings,
        ILocalizationService loc,
        IGlobalHotkeyProvider provider)
    {
        _mode = mode;
        _settings = settings;
        _loc = loc;
        _provider = provider;
    }

    /// <summary>Initialize hotkey registration. No-op in non-UI modes or if disabled.</summary>
    public void Initialize(Window mainWindow)
    {
        if (_mode != RuntimeMode.Ui) return;
        _mainWindow = mainWindow;

        if (!_settings.GetHotkeyEnabled()) return;

        _provider.HotkeyPressed += OnHotkeyPressed;

        var combo = _settings.GetHotkeyCombo() ?? GetPlatformDefault();
        if (_provider.TryRegister(combo))
        {
            _registeredCombo = combo;
            _settings.SetHotkeyCombo(combo);
            Debug.WriteLine($"[Hotkey] Registered: {combo}");
        }
        else
        {
            // Silent fallback
            var fallback = GetPlatformFallback();
            if (_provider.TryRegister(fallback))
            {
                _registeredCombo = fallback;
                _settings.SetHotkeyCombo(fallback);
                Debug.WriteLine($"[Hotkey] Fallback registered: {fallback}");
            }
            else
            {
                Debug.WriteLine("[Hotkey] All registration attempts failed");
            }
        }
    }

    /// <summary>
    /// Change the hotkey combo. Returns null on success, or an error message on failure.
    /// </summary>
    public string? ChangeCombo(string newCombo)
    {
        _provider.Unregister();
        if (_provider.TryRegister(newCombo))
        {
            _registeredCombo = newCombo;
            _settings.SetHotkeyCombo(newCombo);
            Debug.WriteLine($"[Hotkey] Changed to: {newCombo}");
            return null;
        }

        // Revert to previous
        if (_registeredCombo != null)
            _provider.TryRegister(_registeredCombo);

        return _loc[StringKeys.AlwaysOn.HotkeyError];
    }

    /// <summary>Enable hotkey registration.</summary>
    public void Enable()
    {
        if (_mainWindow == null) return;
        var combo = _settings.GetHotkeyCombo() ?? GetPlatformDefault();
        if (_provider.TryRegister(combo))
        {
            _registeredCombo = combo;
        }
    }

    /// <summary>Disable (unregister) the hotkey.</summary>
    public void Disable()
    {
        _provider.Unregister();
        _registeredCombo = null;
    }

    private void OnHotkeyPressed()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow == null) return;

            if (_mainWindow.IsVisible && _mainWindow.IsActive)
            {
                // Toggle: hide
                _mainWindow.Hide();
            }
            else
            {
                // Show + focus search
                _mainWindow.Show();
                _mainWindow.Activate();

                if (_mainWindow.DataContext is ViewModels.MainViewModel mainVm)
                    mainVm.NavigateToCommand.Execute(ViewModels.PageType.Search);

                // Focus search box
                var searchBox = _mainWindow.GetVisualDescendants()
                    .OfType<Avalonia.Controls.TextBox>()
                    .FirstOrDefault(t => t.Name == "SearchBox");
                searchBox?.Focus();
                searchBox?.SelectAll();
            }
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.HotkeyPressed -= OnHotkeyPressed;
        _provider.Dispose();
    }
}
