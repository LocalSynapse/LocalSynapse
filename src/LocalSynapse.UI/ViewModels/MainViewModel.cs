using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
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

    private readonly IServiceProvider _services;
    private readonly UpdateCheckService _updateCheck;

    /// <summary>MainViewModel 생성자.</summary>
    public MainViewModel(IServiceProvider services, UpdateCheckService updateCheck)
    {
        _services = services;
        _updateCheck = updateCheck;

        WeakReferenceMessenger.Default.Register<NavigateMessage>(this, (r, m) =>
            ((MainViewModel)r).NavigateTo(m.Page));

        NavigateTo(PageType.Search);

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
        // Sync dot state from service (reflects dismiss)
        HasUpdateAvailable = _updateCheck.HasUpdateAvailable;
        CurrentPageViewModel = page switch
        {
            PageType.Search => _services.GetService(typeof(SearchViewModel)),
            PageType.DataSetup => _services.GetService(typeof(DataSetupViewModel)),
            PageType.McpSetup => _services.GetService(typeof(McpViewModel)),
            PageType.Security => _services.GetService(typeof(SecurityViewModel)),
            PageType.Settings => _services.GetService(typeof(SettingsViewModel)),
            _ => null
        };
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
}

/// <summary>Cross-VM navigation request message.</summary>
public sealed record NavigateMessage(PageType Page);
