using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.UI.Services;

namespace LocalSynapse.UI.ViewModels;

/// <summary>
/// ViewModel for the Always-On onboarding dialog (fresh install and upgrade variants).
/// </summary>
public partial class AlwaysOnOnboardingViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly AutoStartService _autoStartService;

    /// <summary>True if this is an upgrade flow, false for fresh install.</summary>
    public bool IsUpgrade { get; }

    /// <summary>Display string for the currently registered hotkey.</summary>
    public string HotkeyDisplay { get; }

    /// <summary>Whether auto-start checkbox is checked.</summary>
    [ObservableProperty] private bool _isAutoStartChecked = true;

    /// <summary>Set to true when the user confirms the dialog.</summary>
    public bool IsConfirmed { get; private set; }

    /// <summary>Creates the onboarding ViewModel.</summary>
    public AlwaysOnOnboardingViewModel(
        ISettingsStore settings,
        GlobalHotkeyService hotkeyService,
        AutoStartService autoStartService,
        bool isUpgrade)
    {
        _settings = settings;
        _hotkeyService = hotkeyService;
        _autoStartService = autoStartService;
        IsUpgrade = isUpgrade;

        // Show the actual registered combo
        var presets = GlobalHotkeyService.GetPresets();
        var combo = hotkeyService.RegisteredCombo ?? GlobalHotkeyService.GetPlatformDefault();
        var match = Array.Find(presets, p => p.combo == combo);
        HotkeyDisplay = match.display ?? combo;
    }

    /// <summary>User confirmed the dialog.</summary>
    [RelayCommand]
    private void Confirm()
    {
        IsConfirmed = true;

        // Apply auto-start preference
        if (IsAutoStartChecked)
            _autoStartService.Enable();
        else
            _autoStartService.Disable();

        // Mark version as seen
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        _settings.SetLastSeenVersion(version != null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "2.13.0");
    }
}
