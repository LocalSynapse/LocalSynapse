using System;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.UI.Services;
using LocalSynapse.UI.Services.Localization;

namespace LocalSynapse.UI.ViewModels;

/// <summary>
/// 설정 페이지 ViewModel. 언어 설정 + 업데이트 체크 UI.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly ILocalizationService _loc;
    private readonly UpdateCheckService _updateCheck;
    private readonly MainViewModel _mainVm;

    // Language
    [ObservableProperty] private string _language = "en";
    [ObservableProperty] private bool _isEnglishSelected;
    [ObservableProperty] private bool _isKoreanSelected;

    // About
    [ObservableProperty] private string _appVersion = GetAssemblyVersion();
    [ObservableProperty] private string _dataFolder = "";

    // Update card
    [ObservableProperty] private bool _hasUpdate;
    [ObservableProperty] private string _updateVersion = "";
    [ObservableProperty] private string _updateSummary = "";
    [ObservableProperty] private string _updateReleaseUrl = "";
    [ObservableProperty] private string _updateDownloadUrl = "";

    // Update toggle
    [ObservableProperty] private bool _isUpdateCheckEnabled = true;

    // First run notice
    [ObservableProperty] private bool _showFirstRunNotice;

    /// <summary>SettingsViewModel 생성자.</summary>
    public SettingsViewModel(
        ISettingsStore settings,
        ILocalizationService loc,
        UpdateCheckService updateCheck,
        MainViewModel mainVm)
    {
        _settings = settings;
        _loc = loc;
        _updateCheck = updateCheck;
        _mainVm = mainVm;

        Language = _loc.Current;
        UpdateSelectionFlags();
        DataFolder = settings.GetDataFolder();
        _loc.LanguageChanged += OnLanguageChanged;

        // Update check state
        IsUpdateCheckEnabled = _updateCheck.IsCheckEnabled;
        ShowFirstRunNotice = _updateCheck.IsFirstRun;

        // Load update info if available
        if (_mainVm.HasUpdateAvailable)
            LoadUpdateInfo();
    }

    /// <summary>csproj Version에서 자동으로 버전을 읽는다.</summary>
    private static string GetAssemblyVersion()
    {
        var ver = Assembly.GetEntryAssembly()?.GetName().Version;
        return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "dev";
    }

    // ── Language ──

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

    // ── Update Check ──

    /// <summary>업데이트 정보 로드 (MainVM에서 체크 완료 후).</summary>
    private async void LoadUpdateInfo()
    {
        try
        {
            var info = await _updateCheck.CheckAsync();
            if (info != null)
            {
                HasUpdate = true;
                UpdateVersion = info.LatestVersion;
                UpdateSummary = info.Summary;
                UpdateReleaseUrl = info.ReleaseNotesUrl;
                UpdateDownloadUrl = info.DownloadUrl;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsVM] LoadUpdateInfo error: {ex.Message}");
        }
    }

    /// <summary>다운로드 페이지 열기.</summary>
    [RelayCommand]
    private void OpenDownload()
    {
        if (!string.IsNullOrEmpty(UpdateDownloadUrl))
            PlatformHelper.OpenFile(UpdateDownloadUrl);
    }

    /// <summary>릴리즈 노트 페이지 열기.</summary>
    [RelayCommand]
    private void OpenReleaseNotes()
    {
        if (!string.IsNullOrEmpty(UpdateReleaseUrl))
            PlatformHelper.OpenFile(UpdateReleaseUrl);
    }

    /// <summary>업데이트 알림 dismiss.</summary>
    [RelayCommand]
    private void DismissUpdate()
    {
        if (!string.IsNullOrEmpty(UpdateVersion))
            _updateCheck.DismissVersion(UpdateVersion);
        HasUpdate = false;
        _mainVm.HasUpdateAvailable = false;
    }

    /// <summary>업데이트 체크 토글.</summary>
    partial void OnIsUpdateCheckEnabledChanged(bool value)
    {
        _updateCheck.SetCheckEnabled(value);
    }

    /// <summary>첫 실행 알림 OK — 체크 활성화.</summary>
    [RelayCommand]
    private void AcceptFirstRun()
    {
        _updateCheck.AcceptFirstRun();
        ShowFirstRunNotice = false;
        IsUpdateCheckEnabled = true;
    }

    /// <summary>첫 실행 알림 Disable — 체크 비활성화.</summary>
    [RelayCommand]
    private void DisableFromFirstRun()
    {
        _updateCheck.DisableFromFirstRun();
        ShowFirstRunNotice = false;
        IsUpdateCheckEnabled = false;
    }
}
