using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.UI.ViewModels;

/// <summary>
/// 보안 페이지 ViewModel.
/// </summary>
public partial class SecurityViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;

    [ObservableProperty] private string _storageLocation = "";
    [ObservableProperty] private string _storageSize = "";

    /// <summary>SecurityViewModel 생성자.</summary>
    public SecurityViewModel(ISettingsStore settings)
    {
        _settings = settings;
        StorageLocation = settings.GetDataFolder();
        RefreshStorageSize();
    }

    /// <summary>데이터 폴더 열기.</summary>
    [RelayCommand]
    private void OpenDataFolder()
    {
        var folder = _settings.GetDataFolder();
        if (Directory.Exists(folder))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
    }

    private void RefreshStorageSize()
    {
        var folder = _settings.GetDataFolder();
        if (Directory.Exists(folder))
        {
            var size = new DirectoryInfo(folder)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
            StorageSize = FormatBytes(size);
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:F1} {units[unit]}";
    }
}
