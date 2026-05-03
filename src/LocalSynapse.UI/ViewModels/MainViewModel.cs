using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.UI.Services;

namespace LocalSynapse.UI.ViewModels;

/// <summary>
/// 메인 네비게이션 ViewModel. 페이지 전환 + 업데이트 알림 dot 관리.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private PageType _currentPage = PageType.Search;

    [ObservableProperty]
    private object? _currentPageViewModel;

    [ObservableProperty]
    private bool _hasUpdateAvailable;

    [ObservableProperty]
    private bool _showUpdateBanner;

    [ObservableProperty]
    private bool _isWelcomeCompleted;

    private readonly IServiceProvider _services;
    private readonly UpdateCheckService _updateCheck;

    /// <summary>MainViewModel 생성자.</summary>
    /// <summary>Whether the nav rail should be visible (hidden on Welcome).</summary>
    public bool ShowNavRail => CurrentPage != PageType.Welcome;

    public MainViewModel(IServiceProvider services, UpdateCheckService updateCheck,
        IPipelineStampRepository stampRepo)
    {
        _services = services;
        _updateCheck = updateCheck;

        WeakReferenceMessenger.Default.Register<NavigateMessage>(this, (r, m) =>
            ((MainViewModel)r).NavigateTo(m.Page));

        // First-run detection: no scan has ever completed
        var stamps = stampRepo.GetCurrent();
        var isFirstRun = !stamps.ScanComplete && stamps.TotalFiles == 0;
        IsWelcomeCompleted = !isFirstRun;
        NavigateTo(isFirstRun ? PageType.Welcome : PageType.Search);

        // fire-and-forget 업데이트 체크 (첫 실행이면 skip — C3 해결)
        _ = Task.Run(async () =>
        {
            try
            {
                await _updateCheck.CheckAsync();
                if (_updateCheck.HasUpdateAvailable)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        HasUpdateAvailable = true;
                        if (IsWelcomeCompleted)
                            ShowUpdateBanner = true;
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainVM] Update check error: {ex.Message}");
            }
        });
    }

    /// <summary>페이지 전환 커맨드.</summary>
    [RelayCommand]
    private void NavigateTo(PageType page)
    {
        CurrentPage = page;
        OnPropertyChanged(nameof(ShowNavRail));
        if (page != PageType.Welcome)
            IsWelcomeCompleted = true;
        HasUpdateAvailable = _updateCheck.HasUpdateAvailable;
        ShowUpdateBanner = HasUpdateAvailable && IsWelcomeCompleted;
        CurrentPageViewModel = page switch
        {
            PageType.Search => _services.GetService(typeof(SearchViewModel)),
            PageType.DataSetup => _services.GetService(typeof(DataSetupViewModel)),
            PageType.McpSetup => _services.GetService(typeof(McpViewModel)),
            PageType.Security => _services.GetService(typeof(SecurityViewModel)),
            PageType.Settings => _services.GetService(typeof(SettingsViewModel)),
            PageType.Welcome => _services.GetService(typeof(WelcomeViewModel)),
            _ => null
        };
    }

    /// <summary>배너 닫기. DismissedVersion 저장.</summary>
    [RelayCommand]
    private void DismissBanner()
    {
        ShowUpdateBanner = false;
        HasUpdateAvailable = false;
        var version = _updateCheck.LatestVersion;
        if (!string.IsNullOrEmpty(version))
            _updateCheck.DismissVersion(version);
    }

    /// <summary>릴리스 노트를 기본 브라우저에서 열기.</summary>
    [RelayCommand]
    private void ViewReleaseNotes()
    {
        var url = _updateCheck.ReleaseNotesUrl;
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainVM] Open release notes error: {ex.Message}");
            }
        }
    }
}

/// <summary>페이지 타입 열거.</summary>
public enum PageType
{
    /// <summary>검색 페이지.</summary>
    Search,
    /// <summary>데이터 준비 페이지.</summary>
    DataSetup,
    /// <summary>MCP 설정 페이지.</summary>
    McpSetup,
    /// <summary>보안 페이지.</summary>
    Security,
    /// <summary>설정 페이지.</summary>
    Settings,
    /// <summary>첫 실행 Welcome 페이지.</summary>
    Welcome,
}

/// <summary>Cross-VM navigation request message.</summary>
public sealed record NavigateMessage(PageType Page);
