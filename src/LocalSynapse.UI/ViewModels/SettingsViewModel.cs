using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.UI.ViewModels;

/// <summary>
/// 설정 페이지 ViewModel.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;

    [ObservableProperty] private string _language = "en";
    [ObservableProperty] private string _appVersion = GetAssemblyVersion();
    [ObservableProperty] private string _dataFolder = "";

    /// <summary>SettingsViewModel 생성자.</summary>
    public SettingsViewModel(ISettingsStore settings)
    {
        _settings = settings;
        Language = settings.GetLanguage();
        DataFolder = settings.GetDataFolder();
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
        _settings.SetLanguage(cultureName);
        Language = cultureName;
    }
}
