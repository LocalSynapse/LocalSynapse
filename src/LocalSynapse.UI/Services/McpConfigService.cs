using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalSynapse.UI.Services;

/// <summary>
/// Claude Desktop / Claude Code의 MCP 설정 파일을 자동 등록/해제한다.
/// </summary>
public sealed class McpConfigService
{
    private const string ServerName = "localsynapse";

    /// <summary>Claude Desktop config 파일 경로 (%APPDATA%\Claude\claude_desktop_config.json).</summary>
    public static string ClaudeDesktopConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Claude", "claude_desktop_config.json");

    /// <summary>MCP Stdio 서버 바이너리 경로. GUI와 같은 디렉토리에 위치.</summary>
    public static string McpExePath
    {
        get
        {
            var baseDir = AppContext.BaseDirectory;
            var exeName = OperatingSystem.IsWindows() ? "localsynapse-mcp.exe" : "localsynapse-mcp";
            return Path.Combine(baseDir, exeName);
        }
    }

    /// <summary>Claude Desktop이 설치되어 있는지 확인.</summary>
    public bool IsClaudeDesktopInstalled()
    {
        var claudeDir = Path.GetDirectoryName(ClaudeDesktopConfigPath);
        return claudeDir != null && Directory.Exists(claudeDir);
    }

    /// <summary>Claude Desktop config에 LocalSynapse MCP가 등록되어 있는지 확인.</summary>
    public bool IsRegisteredInClaudeDesktop()
    {
        try
        {
            if (!File.Exists(ClaudeDesktopConfigPath)) return false;
            var json = File.ReadAllText(ClaudeDesktopConfigPath);
            var root = JsonNode.Parse(json);
            return root?["mcpServers"]?[ServerName] != null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[McpConfig] Claude Desktop registration check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Claude Desktop config에 LocalSynapse MCP 서버를 등록한다.</summary>
    public McpConfigResult RegisterClaudeDesktop()
    {
        try
        {
            var configDir = Path.GetDirectoryName(ClaudeDesktopConfigPath)!;
            Directory.CreateDirectory(configDir);

            JsonNode root;
            if (File.Exists(ClaudeDesktopConfigPath))
            {
                var existing = File.ReadAllText(ClaudeDesktopConfigPath);
                root = JsonNode.Parse(existing) ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            // mcpServers 섹션이 없으면 생성
            if (root["mcpServers"] is not JsonObject servers)
            {
                servers = new JsonObject();
                root["mcpServers"] = servers;
            }

            // localsynapse 엔트리 추가/덮어쓰기
            var entry = new JsonObject
            {
                ["command"] = McpExePath,
                ["args"] = new JsonArray()
            };
            servers[ServerName] = entry;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ClaudeDesktopConfigPath, root.ToJsonString(options));

            return new McpConfigResult(true, "Registered successfully.");
        }
        catch (Exception ex)
        {
            return new McpConfigResult(false, $"Failed: {ex.Message}");
        }
    }

    /// <summary>Claude Desktop config에서 LocalSynapse MCP 서버를 제거한다.</summary>
    public McpConfigResult UnregisterClaudeDesktop()
    {
        try
        {
            if (!File.Exists(ClaudeDesktopConfigPath))
                return new McpConfigResult(true, "Config file not found — nothing to remove.");

            var json = File.ReadAllText(ClaudeDesktopConfigPath);
            var root = JsonNode.Parse(json);
            if (root?["mcpServers"] is JsonObject servers)
            {
                servers.Remove(ServerName);
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(ClaudeDesktopConfigPath, root.ToJsonString(options));
            }

            return new McpConfigResult(true, "Unregistered successfully.");
        }
        catch (Exception ex)
        {
            return new McpConfigResult(false, $"Failed: {ex.Message}");
        }
    }

    /// <summary>Claude Code CLI 등록 명령어를 생성한다.</summary>
    public string GetClaudeCodeAddCommand()
    {
        var path = McpExePath;
        // Windows: 백슬래시 이스케이프, macOS: 그대로
        if (PlatformHelper.IsWindows)
            path = path.Replace("\\", "\\\\");
        return $"claude mcp add {ServerName} -- \"{path}\"";
    }

    /// <summary>Claude Code CLI 제거 명령어를 생성한다.</summary>
    public string GetClaudeCodeRemoveCommand()
    {
        return $"claude mcp remove {ServerName}";
    }
}

/// <summary>Config 작업 결과.</summary>
public sealed record McpConfigResult(bool Success, string Message);
