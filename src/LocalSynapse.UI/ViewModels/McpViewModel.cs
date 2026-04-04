using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSynapse.UI.Services;

namespace LocalSynapse.UI.ViewModels;

/// <summary>
/// MCP 설정 페이지 ViewModel.
/// Claude Desktop/Code 자동 연동 및 사용자 가이드를 제공한다.
/// </summary>
public partial class McpViewModel : ObservableObject
{
    private readonly McpConfigService _configService;

    // ── Claude Desktop ──
    [ObservableProperty] private bool _isClaudeDesktopInstalled;
    [ObservableProperty] private bool _isClaudeDesktopRegistered;
    [ObservableProperty] private string _claudeDesktopStatus = "";
    [ObservableProperty] private string _claudeDesktopMessage = "";

    // ── Claude Code ──
    [ObservableProperty] private string _claudeCodeAddCommand = "";
    [ObservableProperty] private string _claudeCodeRemoveCommand = "";
    [ObservableProperty] private string _claudeCodeCopyFeedback = "";

    // ── General ──
    [ObservableProperty] private string _exePath = "";
    [ObservableProperty] private string _configFilePath = "";

    /// <summary>MCP 도구 목록 (읽기 전용 표시용).</summary>
    public string[] AvailableTools { get; } =
    [
        "search_files — Search files by keyword or semantic query",
        "get_file_content — Read the content of an indexed file",
        "list_indexed_files — List all indexed files with filters",
        "get_pipeline_status — Check indexing pipeline status",
    ];

    public McpViewModel(McpConfigService configService)
    {
        _configService = configService;
        RefreshState();
    }

    /// <summary>상태를 새로고침한다.</summary>
    public void RefreshState()
    {
        ExePath = McpConfigService.ExePath;
        ConfigFilePath = McpConfigService.ClaudeDesktopConfigPath;
        IsClaudeDesktopInstalled = _configService.IsClaudeDesktopInstalled();
        IsClaudeDesktopRegistered = _configService.IsRegisteredInClaudeDesktop();
        ClaudeDesktopStatus = IsClaudeDesktopRegistered ? "Connected" : "Not connected";
        ClaudeDesktopMessage = "";
        ClaudeCodeAddCommand = _configService.GetClaudeCodeAddCommand();
        ClaudeCodeRemoveCommand = _configService.GetClaudeCodeRemoveCommand();
        ClaudeCodeCopyFeedback = "";
    }

    /// <summary>Claude Desktop에 MCP 서버를 등록한다.</summary>
    [RelayCommand]
    private void RegisterClaudeDesktop()
    {
        var result = _configService.RegisterClaudeDesktop();
        ClaudeDesktopMessage = result.Message;
        RefreshAfterAction();
    }

    /// <summary>Claude Desktop에서 MCP 서버를 제거한다.</summary>
    [RelayCommand]
    private void UnregisterClaudeDesktop()
    {
        var result = _configService.UnregisterClaudeDesktop();
        ClaudeDesktopMessage = result.Message;
        RefreshAfterAction();
    }

    /// <summary>Config 파일 위치를 탐색기로 연다.</summary>
    [RelayCommand]
    private void OpenConfigFolder()
    {
        var dir = System.IO.Path.GetDirectoryName(ConfigFilePath);
        if (dir != null && System.IO.Directory.Exists(dir))
        {
            PlatformHelper.OpenFolder(dir);
        }
    }

    private void RefreshAfterAction()
    {
        IsClaudeDesktopRegistered = _configService.IsRegisteredInClaudeDesktop();
        ClaudeDesktopStatus = IsClaudeDesktopRegistered ? "Connected" : "Not connected";
    }
}
