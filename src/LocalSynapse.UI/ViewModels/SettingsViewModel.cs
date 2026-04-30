using System;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Pipeline.Interfaces;
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

    // Performance mode
    private readonly IPipelineOrchestrator _orchestrator;
    private readonly Pipeline.Embedding.GpuDetectionService _gpuDetection;
    [ObservableProperty] private bool _isStealthSelected;
    [ObservableProperty] private bool _isCruiseSelected;
    [ObservableProperty] private bool _isOverdriveSelected;
    [ObservableProperty] private bool _isMadMaxSelected;
    [ObservableProperty] private bool _isMadMaxEnabled;
    [ObservableProperty] private string _madMaxSubText = "";
    [ObservableProperty] private string _performanceModeTech = "";
    [ObservableProperty] private string _performanceModeDesc = "";

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

    // (Update toggle moved to Security tab in M2-F)

    // First run notice removed (WO-SEC0)

    /// <summary>SettingsViewModel 생성자.</summary>
    public SettingsViewModel(
        ISettingsStore settings,
        ILocalizationService loc,
        UpdateCheckService updateCheck,
        IPipelineOrchestrator orchestrator,
        Pipeline.Embedding.GpuDetectionService gpuDetection)
    {
        _settings = settings;
        _loc = loc;
        _updateCheck = updateCheck;
        _orchestrator = orchestrator;
        _gpuDetection = gpuDetection;

        Language = _loc.Current;
        UpdateSelectionFlags();
        UpdatePerformanceModeFlags();
        UpdateMadMaxState();
        DataFolder = settings.GetDataFolder();
        _loc.LanguageChanged += OnLanguageChanged;



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
        UpdatePerformanceModeText();
    }

    // ── Performance Mode ──

    /// <summary>성능 모드 변경 커맨드.</summary>
    [RelayCommand]
    private void ChangePerformanceMode(string mode)
    {
        if (mode == "MadMax" && !IsMadMaxEnabled) return;
        _settings.SetPerformanceMode(mode);
        _orchestrator.RequestImmediateCycle();
        UpdatePerformanceModeFlags();
    }

    private void UpdatePerformanceModeFlags()
    {
        var mode = _settings.GetPerformanceMode();
        IsStealthSelected = mode == "Stealth";
        IsCruiseSelected = mode == "Cruise";
        IsOverdriveSelected = mode == "Overdrive";
        IsMadMaxSelected = mode == "MadMax";
        UpdatePerformanceModeText();
    }

    private void UpdatePerformanceModeText()
    {
        var mode = _settings.GetPerformanceMode();
        (PerformanceModeTech, PerformanceModeDesc) = mode switch
        {
            "Stealth" => (_loc[StringKeys.Settings.Performance.StealthTech],
                          _loc[StringKeys.Settings.Performance.StealthDesc]),
            "Overdrive" => (_loc[StringKeys.Settings.Performance.OverdriveTech],
                            _loc[StringKeys.Settings.Performance.OverdriveDesc]),
            "MadMax" => (_loc[StringKeys.Settings.Performance.MadMaxTech],
                         _loc[StringKeys.Settings.Performance.MadMaxDesc]),
            _ => (_loc[StringKeys.Settings.Performance.CruiseTech],
                  _loc[StringKeys.Settings.Performance.CruiseDesc]),
        };
    }

    private void UpdateMadMaxState()
    {
        var result = _gpuDetection.CachedResult;
        IsMadMaxEnabled = result?.BestProvider != null;
        MadMaxSubText = result?.BestProvider != null
            ? _loc.Format(StringKeys.Settings.Performance.MadMaxDetected, result.GpuName ?? "", result.BestProvider)
            : _loc[StringKeys.Settings.Performance.MadMaxUnavailable];

        // Auto-downgrade: if MadMax is stored but GPU is now unavailable, fall back to Overdrive
        if (_settings.GetPerformanceMode() == "MadMax" && !IsMadMaxEnabled)
        {
            _settings.SetPerformanceMode("Overdrive");
            System.Diagnostics.Debug.WriteLine("[SettingsVM] MadMax stored but GPU unavailable — auto-downgraded to Overdrive");
            UpdatePerformanceModeFlags();
        }
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

    // GotIt / ManageInSecurity commands removed (WO-SEC0)

}
