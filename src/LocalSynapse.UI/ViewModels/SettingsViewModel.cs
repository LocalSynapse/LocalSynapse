using System;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.UI.Services.Localization;

namespace LocalSynapse.UI.ViewModels;

/// <summary>
/// 설정 페이지 ViewModel.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly ILocalizationService _loc;

    [ObservableProperty] private string _language = "en";
    [ObservableProperty] private bool _isEnglishSelected;
    [ObservableProperty] private bool _isKoreanSelected;
    [ObservableProperty] private string _appVersion = GetAssemblyVersion();
    [ObservableProperty] private string _dataFolder = "";

    /// <summary>SettingsViewModel 생성자.</summary>
    public SettingsViewModel(ISettingsStore settings, ILocalizationService loc)
    {
        _settings = settings;
        _loc = loc;
        Language = _loc.Current;
        UpdateSelectionFlags();
        DataFolder = settings.GetDataFolder();
        _loc.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>csproj Version에서 자동으로 버전을 읽는다.</summary>
    private static string GetAssemblyVersion()
    {
        var ver = Assembly.GetEntryAssembly()?.GetName().Version;
        return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "dev";
    }

    /// <summary>언어 변경 커맨드.</summary>
    [RelayCommand]
    private void ChangeLanguage(string cultureName)
    {
        _loc.SetLanguage(cultureName);
        Language = _loc.Current;
        UpdateSelectionFlags();
    }

    private void UpdateSelectionFlags()
    {
        IsEnglishSelected = _loc.Current == "en";
        IsKoreanSelected = _loc.Current == "ko";
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Language = _loc.Current;
        UpdateSelectionFlags();
    }
}
