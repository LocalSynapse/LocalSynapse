using System;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.UI.Services;
using LocalSynapse.UI.Services.Localization;

namespace LocalSynapse.UI.ViewModels;

/// <summary>
/// 설정 페이지 ViewModel. 언어 설정 + About 카드 (버전 상태 + What's new + 업데이트).
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly ILocalizationService _loc;
    private readonly UpdateCheckService _updateCheck;

    // Language
    [ObservableProperty] private string _language = "en";
    [ObservableProperty] private bool _isEnglishSelected;
    [ObservableProperty] private bool _isKoreanSelected;
    [ObservableProperty] private bool _isFrenchSelected;
    [ObservableProperty] private bool _isGermanSelected;
    [ObservableProperty] private bool _isChineseSelected;

    // About — version
    [ObservableProperty] private string _appVersion = GetAssemblyVersion();
    [ObservableProperty] private string _dataFolder = "";
    [ObservableProperty] private string _versionDisplay = "";

    // About — status
    [ObservableProperty] private bool _isUpToDate;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _statusIcon = "";
    [ObservableProperty] private bool _showStatus;
    [ObservableProperty] private Avalonia.Media.SolidColorBrush _statusForegroundBrush
        = new(Avalonia.Media.Color.Parse("#065F46"));

    // About — What's new
    [ObservableProperty] private string _whatsNewTitle = "";
    [ObservableProperty] private List<string> _whatsNewItems = [];
    [ObservableProperty] private bool _showWhatsNew;

    // About — update buttons
    [ObservableProperty] private bool _hasUpdate;
    [ObservableProperty] private string _updateVersion = "";
    [ObservableProperty] private string _updateReleaseUrl = "";
    [ObservableProperty] private string _updateDownloadUrl = "";
    [ObservableProperty] private bool _showUpdateButtons;

    // Update toggle
    [ObservableProperty] private bool _isUpdateCheckEnabled = true;

    // First run notice
    [ObservableProperty] private bool _showFirstRunNotice;

    /// <summary>SettingsViewModel 생성자.</summary>
    public SettingsViewModel(
        ISettingsStore settings,
        ILocalizationService loc,
        UpdateCheckService updateCheck)
    {
        _settings = settings;
        _loc = loc;
        _updateCheck = updateCheck;

        Language = _loc.Current;
        UpdateSelectionFlags();
        DataFolder = settings.GetDataFolder();
        _loc.LanguageChanged += OnLanguageChanged;

        IsUpdateCheckEnabled = _updateCheck.IsCheckEnabled;
        ShowFirstRunNotice = _updateCheck.IsFirstRun;

        LoadVersionInfo();
    }

    /// <summary>csproj Version에서 자동으로 버전을 읽는다.</summary>
    private static string GetAssemblyVersion()
    {
        var ver = Assembly.GetEntryAssembly()?.GetName().Version;
        return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "dev";
    }

    /// <summary>버전 상태 + What's new + 업데이트 정보 로드.</summary>
    private void LoadVersionInfo()
    {
        var currentVersion = AppVersion;

        // 1. 항상: 번들 릴리즈 노트
        var locale = _loc.Current;
        var currentNotes = ReleaseNotesProvider.GetCurrentNotes(locale);
        WhatsNewTitle = _loc.Format(StringKeys.UpdateCheck.WhatsNewTitle, currentVersion);
        WhatsNewItems = currentNotes;
        ShowWhatsNew = currentNotes.Count > 0;

        // 2. 업데이트 체크 결과 반영
        if (_updateCheck.HasUpdateAvailable && _updateCheck.LastResult is { } info)
        {
            HasUpdate = true;
            UpdateVersion = info.LatestVersion;
            VersionDisplay = $"{currentVersion} → {info.LatestVersion}";
            StatusMessage = _loc[StringKeys.UpdateCheck.UpdateAvailable];
            StatusIcon = "↑";
            ShowStatus = true;
            ShowUpdateButtons = true;
            StatusForegroundBrush = new(Avalonia.Media.Color.Parse("#1E40AF"));
            UpdateReleaseUrl = info.ReleaseNotesUrl;
            UpdateDownloadUrl = info.DownloadUrl;

            if (info.ReleaseNotes.Count > 0)
            {
                WhatsNewTitle = _loc.Format(StringKeys.UpdateCheck.WhatsNewTitle, info.LatestVersion);
                WhatsNewItems = info.ReleaseNotes;
            }
        }
        else if (_updateCheck.HasChecked)
        {
            IsUpToDate = true;
            VersionDisplay = currentVersion;
            StatusMessage = _loc[StringKeys.UpdateCheck.UpToDate];
            StatusIcon = "✓";
            ShowStatus = true;
            StatusForegroundBrush = new(Avalonia.Media.Color.Parse("#065F46"));
        }
        else
        {
            VersionDisplay = currentVersion;
            ShowStatus = false;
        }
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
        IsFrenchSelected = _loc.Current == "fr";
        IsGermanSelected = _loc.Current == "de";
        IsChineseSelected = _loc.Current == "zh";
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Language = _loc.Current;
        UpdateSelectionFlags();
    }

    // ── Update Check ──

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
        ShowUpdateButtons = false;
        // Dismiss 후 상태를 "up to date"로 갱신
        IsUpToDate = true;
        StatusMessage = _loc[StringKeys.UpdateCheck.UpToDate];
        StatusIcon = "✓";
        StatusForegroundBrush = new(Avalonia.Media.Color.Parse("#065F46"));
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
