using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.UI.Services;
using LocalSynapse.UI.Views;

namespace LocalSynapse.UI.ViewModels;

/// <summary>
/// 보안 페이지 ViewModel. 외부 통신 공개 + 토글 관리.
/// </summary>
public partial class SecurityViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly UpdateCheckService _updateCheck;

    [ObservableProperty] private string _storageLocation = "";
    [ObservableProperty] private string _storageSize = "";

    // External communication toggle
    [ObservableProperty] private bool _isExternalCommunicationEnabled;
    [ObservableProperty] private bool _isPingExpanded;
    [ObservableProperty] private bool _showTurnOffConfirmation;
    private bool _suppressToggleHandler;

    /// <summary>SecurityViewModel 생성자.</summary>
    public SecurityViewModel(ISettingsStore settings, UpdateCheckService updateCheck)
    {
        _settings = settings;
        _updateCheck = updateCheck;
        StorageLocation = settings.GetDataFolder();
        RefreshStorageSize();
        IsExternalCommunicationEnabled = _updateCheck.IsCheckEnabled;
    }

    partial void OnIsExternalCommunicationEnabledChanged(bool value)
    {
        if (_suppressToggleHandler) return;

        if (!value)
        {
            // User toggled OFF → revert and show confirmation first
            _suppressToggleHandler = true;
            IsExternalCommunicationEnabled = true;
            _suppressToggleHandler = false;
            ShowTurnOffConfirmation = true;
        }
        else
        {
            // User toggled ON → apply immediately
            _updateCheck.SetCheckEnabled(true);
        }
    }

    /// <summary>Confirm turning off external communication.</summary>
    [RelayCommand]
    private void ConfirmTurnOff()
    {
        _suppressToggleHandler = true;
        _updateCheck.SetCheckEnabled(false);
        IsExternalCommunicationEnabled = false;
        _suppressToggleHandler = false;
        ShowTurnOffConfirmation = false;
    }

    /// <summary>Cancel the turn-off confirmation.</summary>
    [RelayCommand]
    private void CancelTurnOff()
    {
        ShowTurnOffConfirmation = false;
    }

    /// <summary>Toggle ping detail expansion.</summary>
    [RelayCommand]
    private void TogglePingExpanded()
    {
        IsPingExpanded = !IsPingExpanded;
    }

    /// <summary>마지막 전송 데이터 다이얼로그 표시.</summary>
    [RelayCommand]
    private async Task ViewLastSentAsync()
    {
        var (payload, sentAt) = _updateCheck.GetLastPayload();
        var dialog = new LastPingDialog(payload, sentAt);
        if (Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } owner)
        {
            await dialog.ShowDialog(owner);
        }
    }

    /// <summary>데이터 폴더 열기.</summary>
    [RelayCommand]
    private void OpenDataFolder()
    {
        var folder = _settings.GetDataFolder();
        if (Directory.Exists(folder))
        {
            PlatformHelper.OpenFolder(folder);
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
