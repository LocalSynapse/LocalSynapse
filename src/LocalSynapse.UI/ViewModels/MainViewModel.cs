using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Pipeline.Interfaces;
using LocalSynapse.UI.Services;
using LocalSynapse.UI.Services.Localization;

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

    // Install-update state (IU-1a)
    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private string _installButtonCaption = "";

    [ObservableProperty]
    private bool _installButtonIsPrimary = true;

    [ObservableProperty]
    private bool _installButtonEnabled = true;

    [ObservableProperty]
    private string _installSubText = "";

    [ObservableProperty]
    private bool _installSubTextVisible;

    /// <summary>True only on Windows in IU-1a; IU-1b extends to macOS.</summary>
    public bool IsInstallSupported { get; } = PlatformHelper.IsWindows;

    /// <summary>True iff Install button should appear: platform supports it AND release exposes
    /// the Windows installer + SHA256SUMS assets (SPEC-IU-1 §4.2 gate). Re-raised whenever
    /// LatestAssets transitions from null → non-null after the update check completes.</summary>
    public bool ShowInstallButton => IsInstallSupported && _updateCheck.LatestAssets != null;

    private readonly IServiceProvider _services;
    private readonly UpdateCheckService _updateCheck;
    private readonly UpdateInstallerService _installer;
    private readonly ILocalizationService _loc;
    private CancellationTokenSource? _installCts;
    private DateTime _lastProgressUpdate = DateTime.MinValue;
    private int _lastProgressPct = -1;

    /// <summary>MainViewModel 생성자.</summary>
    /// <summary>Whether the nav rail should be visible (hidden on Welcome).</summary>
    public bool ShowNavRail => CurrentPage != PageType.Welcome;

    public MainViewModel(IServiceProvider services, UpdateCheckService updateCheck,
        UpdateInstallerService installer, ILocalizationService loc,
        IPipelineStampRepository stampRepo)
    {
        _services = services;
        _updateCheck = updateCheck;
        _installer = installer;
        _loc = loc;

        // Install button starts in Idle state
        SetInstallStateIdle();

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
                        // ShowInstallButton depends on _updateCheck.LatestAssets which may have
                        // transitioned null → non-null during CheckAsync; raise change notification
                        // so the XAML IsVisible binding re-evaluates.
                        OnPropertyChanged(nameof(ShowInstallButton));
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

    /// <summary>Install/Cancel/Open-download toggle command per SPEC-IU-1 §4.5 + §4.1 permanent-failure morph.</summary>
    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (IsInstalling)
        {
            // Cancel mode
            _installCts?.Cancel();
            return;
        }

        // Permanent-failure morph: button caption became "Open download page" after a SHA256
        // failure (SPEC-IU-1 §6 C2 / C2'). Clicking it opens the marketing download page instead
        // of re-attempting the failed install. PlatformHelper.OpenFile uses
        // ProcessStartInfo + UseShellExecute=true which the OS shell resolves as a URL on both
        // Windows and macOS — semantically `OpenUrl` would be a clearer name (filed for IU-1b
        // refactor; see DIFF-IU-1a Round-2 W-NEW-2). For IU-1a the existing helper is reused as-is.
        if (InstallButtonCaption == _loc[StringKeys.Banner.InstallOpenDownload])
        {
            PlatformHelper.OpenFile(UpdateCheckService.DownloadPageUrl);
            return;
        }

        if (_updateCheck.LastResult is not { } info || _updateCheck.LatestAssets is not { } assets)
        {
            Debug.WriteLine("[MainVM] InstallUpdate: no update info / assets — banner state inconsistent");
            return;
        }

        IsInstalling = true;
        _installCts = new CancellationTokenSource();
        ClearInstallSubText();
        bool launched = false;  // Tracks successful Launch() — finally must NOT reset IsInstalling after launch (SPEC-IU-1 §4.4)

        var progress = new Progress<DownloadProgress>(p =>
        {
            // Throttle: 10 Hz / 5% delta whichever sparser (SPEC-IU-1 §4.5)
            var now = DateTime.UtcNow;
            var pct = (int)p.Percent;
            if ((now - _lastProgressUpdate).TotalMilliseconds < 100
                && Math.Abs(pct - _lastProgressPct) < 5)
                return;
            _lastProgressUpdate = now;
            _lastProgressPct = pct;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                InstallButtonCaption = _loc.Format(StringKeys.Banner.InstallProgress, pct);
                InstallButtonIsPrimary = false;
                InstallButtonEnabled = true;  // Cancel is the action
            });
        });

        try
        {
            // Download
            var artifact = await _installer.DownloadAsync(assets, progress, _installCts.Token);

            // Verifying state (verification is inside DownloadAsync; if we got here it's verified —
            // briefly flash this caption for user feedback)
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                InstallButtonCaption = _loc[StringKeys.Banner.InstallVerifying];
                InstallButtonIsPrimary = false;
                InstallButtonEnabled = false;
            });

            // Launching state
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                InstallButtonCaption = _loc[StringKeys.Banner.InstallLaunching];
                InstallButtonEnabled = false;
            });

            _installer.Launch(artifact);
            launched = true;

            // Stay in Launching state — the Inno Setup taskkill (LocalSynapse.iss:222)
            // will terminate this process within a few seconds. Do NOT reset to Idle.
            // The `launched` flag prevents the `finally` block below from undoing this.
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[MainVM] Install cancelled by user");
            Avalonia.Threading.Dispatcher.UIThread.Post(SetInstallStateIdle);
        }
        catch (System.IO.IOException ioEx) when ((ioEx.HResult & 0xFFFF) == 0x70)
        {
            // ERROR_DISK_FULL = 0x70 (112). C4.
            Debug.WriteLine($"[MainVM] Install: disk full: {ioEx.Message}");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                SetInstallStateFailed(transient: true,
                    captionKey: StringKeys.Banner.InstallRetry,
                    subTextKey: StringKeys.Banner.InstallError.Disk));
        }
        catch (System.IO.FileNotFoundException fnfEx)
        {
            // C5: AV quarantine
            Debug.WriteLine($"[MainVM] Install: file vanished: {fnfEx.Message}");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                SetInstallStateFailed(transient: true,
                    captionKey: StringKeys.Banner.InstallRetry,
                    subTextKey: StringKeys.Banner.InstallError.Generic,
                    subTextArg: "File was removed by another process"));
        }
        catch (System.Net.Http.HttpRequestException httpEx)
        {
            // C1, C2''
            Debug.WriteLine($"[MainVM] Install: network error: {httpEx.Message}");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                SetInstallStateFailed(transient: true,
                    captionKey: StringKeys.Banner.InstallRetry,
                    subTextKey: StringKeys.Banner.InstallError.Network));
        }
        catch (System.IO.InvalidDataException invEx)
        {
            // C2 / C2': SHA256 mismatch / SHA256SUMS missing line
            Debug.WriteLine($"[MainVM] Install: integrity failure: {invEx.Message}");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                SetInstallStateFailed(transient: false,
                    captionKey: StringKeys.Banner.InstallOpenDownload,
                    subTextKey: StringKeys.Banner.InstallError.Checksum));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainVM] Install: unexpected error: {ex}");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                SetInstallStateFailed(transient: true,
                    captionKey: StringKeys.Banner.InstallRetry,
                    subTextKey: StringKeys.Banner.InstallError.Generic,
                    subTextArg: ex.Message));
        }
        finally
        {
            // Cleanup always runs — but state reset SKIPPED on successful Launch (SPEC-IU-1 §4.4):
            // GUI must stay in "Launching..." until Inno Setup's taskkill terminates the process.
            _installCts?.Dispose();
            _installCts = null;
            _lastProgressUpdate = DateTime.MinValue;
            _lastProgressPct = -1;
            if (!launched)
            {
                IsInstalling = false;
            }
        }
    }

    private void SetInstallStateIdle()
    {
        InstallButtonCaption = _loc[StringKeys.Banner.InstallUpdate];
        InstallButtonIsPrimary = true;
        InstallButtonEnabled = true;
        ClearInstallSubText();
    }

    private void SetInstallStateFailed(bool transient, string captionKey, string subTextKey,
        string? subTextArg = null)
    {
        InstallButtonCaption = _loc[captionKey];
        InstallButtonIsPrimary = transient;  // Retry = primary; Open download = secondary
        InstallButtonEnabled = true;
        InstallSubText = subTextArg is null ? _loc[subTextKey] : _loc.Format(subTextKey, subTextArg);
        InstallSubTextVisible = true;
    }

    private void ClearInstallSubText()
    {
        InstallSubText = "";
        InstallSubTextVisible = false;
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
