using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LocalSynapse.UI.ViewModels;

/// <summary>
/// 메인 네비게이션 ViewModel. 페이지 전환을 관리한다.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private PageType _currentPage = PageType.Search;

    [ObservableProperty]
    private object? _currentPageViewModel;

    private readonly IServiceProvider _services;

    /// <summary>MainViewModel 생성자.</summary>
    public MainViewModel(IServiceProvider services)
    {
        _services = services;
        NavigateTo(PageType.Search);
    }

    /// <summary>페이지 전환 커맨드.</summary>
    [RelayCommand]
    private void NavigateTo(PageType page)
    {
        CurrentPage = page;
        CurrentPageViewModel = page switch
        {
            PageType.Search => _services.GetService(typeof(SearchViewModel)),
            PageType.DataSetup => _services.GetService(typeof(DataSetupViewModel)),
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
    /// <summary>보안 페이지.</summary>
    Security,
    /// <summary>설정 페이지.</summary>
    Settings,
}
