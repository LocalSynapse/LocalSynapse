using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.UI.Services;

/// <summary>
/// 1일 1회 GitHub Releases API로 업데이트 확인 + localsynapse.com에 통계 ping.
/// 양쪽 모두 실패해도 앱 기능에 영향 없음 (fire-and-forget).
/// </summary>
public sealed class UpdateCheckService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/LocalSynapse/LocalSynapse/releases/latest";
    private const string PingUrl = "https://localsynapse.com/api/ping";
    private const string DownloadPageUrl = "https://localsynapse.com/download";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly string _checkFilePath;

    /// <summary>업데이트 정보.</summary>
    public record UpdateInfo(
        string LatestVersion,
        string CurrentVersion,
        string ReleaseNotesUrl,
        string DownloadUrl,
        string Summary);

    /// <summary>update-check.json 스키마.</summary>
    private sealed class CheckState
    {
        public string? LastCheckAt { get; set; }
        public string? DismissedVersion { get; set; }
        public bool CheckEnabled { get; set; } = true;
    }

    /// <summary>UpdateCheckService 생성자.</summary>
    public UpdateCheckService(ISettingsStore settingsStore)
    {
        _checkFilePath = Path.Combine(settingsStore.GetDataFolder(), "update-check.json");
    }

    /// <summary>첫 실행 여부 (update-check.json 미존재).</summary>
    public bool IsFirstRun => !File.Exists(_checkFilePath);

    /// <summary>업데이트 체크 활성화 여부.</summary>
    public bool IsCheckEnabled => LoadState().CheckEnabled;

    /// <summary>업데이트 체크 활성화/비활성화.</summary>
    public void SetCheckEnabled(bool enabled)
    {
        var state = LoadState();
        state.CheckEnabled = enabled;
        SaveState(state);
    }

    /// <summary>첫 실행 동의 (update-check.json 생성, checkEnabled=true).</summary>
    public void AcceptFirstRun()
    {
        var state = new CheckState { CheckEnabled = true };
        SaveState(state);
    }

    /// <summary>첫 실행 비활성화 (update-check.json 생성, checkEnabled=false).</summary>
    public void DisableFromFirstRun()
    {
        var state = new CheckState { CheckEnabled = false };
        SaveState(state);
    }

    /// <summary>특정 버전을 dismiss.</summary>
    public void DismissVersion(string version)
    {
        var state = LoadState();
        state.DismissedVersion = version;
        SaveState(state);
    }

    /// <summary>1일 1회 업데이트 체크 + 통계 ping. 첫 실행 시 skip.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        // 첫 실행이면 네트워크 호출 0 — AcceptFirstRun() 이후 다음 시작 시 첫 체크
        if (IsFirstRun) return null;

        var state = LoadState();
        if (!state.CheckEnabled) return null;

        // 24시간 이내면 skip
        if (DateTime.TryParse(state.LastCheckAt, out var lastCheck)
            && DateTime.UtcNow - lastCheck < CheckInterval)
            return null;

        // 타임스탬프 즉시 갱신 (중복 호출 방지)
        state.LastCheckAt = DateTime.UtcNow.ToString("o");
        SaveState(state);

        UpdateInfo? result = null;

        // Task 1: GitHub API (업데이트 체크)
        try
        {
            result = await CheckGitHubReleaseAsync(state, ct);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateCheck] GitHub API error: {ex.Message}");
        }

        // Task 2: Statistics ping (fire-and-forget)
        _ = Task.Run(async () =>
        {
            try { await SendPingAsync(); }
            catch (Exception ex) { Debug.WriteLine($"[UpdateCheck] Ping error: {ex.Message}"); }
        });

        return result;
    }

    private async Task<UpdateInfo?> CheckGitHubReleaseAsync(CheckState state, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = HttpTimeout };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"LocalSynapse/{GetCurrentVersion()}");

        var json = await http.GetStringAsync(GitHubApiUrl, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.GetProperty("tag_name").GetString() ?? "";
        var versionStr = tagName.TrimStart('v');
        if (!Version.TryParse(versionStr, out var latest)) return null;

        var current = Assembly.GetEntryAssembly()?.GetName().Version;
        if (current == null || latest <= current) return null;

        // dismiss된 버전이면 skip
        if (state.DismissedVersion == versionStr) return null;

        var htmlUrl = root.GetProperty("html_url").GetString() ?? "";
        var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
        var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : null;

        var summary = ExtractSummary(name, tagName, body);

        return new UpdateInfo(versionStr, GetCurrentVersion(), htmlUrl, DownloadPageUrl, summary);
    }

    /// <summary>Release name 우선, bullet fallback.</summary>
    private static string ExtractSummary(string? name, string tagName, string? body)
    {
        // 1. name 필드 (tag_name과 다른 경우만)
        if (!string.IsNullOrWhiteSpace(name) && name != tagName && !name.StartsWith("v"))
            return name;

        // 2. body에서 첫 bullet 리스트
        if (!string.IsNullOrWhiteSpace(body))
        {
            var lines = body.Split('\n');
            var bullets = new List<string>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("- "))
                {
                    bullets.Add(trimmed[2..]);
                    if (bullets.Count >= 3) break;
                }
                else if (bullets.Count > 0)
                    break;
            }
            if (bullets.Count > 0)
                return string.Join("; ", bullets);
        }

        return "";
    }

    private async Task SendPingAsync()
    {
        using var http = new HttpClient { Timeout = HttpTimeout };
        var payload = JsonSerializer.Serialize(new
        {
            v = GetCurrentVersion(),
            os = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            locale = System.Globalization.CultureInfo.CurrentUICulture.Name
        });
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        await http.PostAsync(PingUrl, content);
    }

    private static string GetCurrentVersion()
    {
        var ver = Assembly.GetEntryAssembly()?.GetName().Version;
        return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "dev";
    }

    private CheckState LoadState()
    {
        if (!File.Exists(_checkFilePath)) return new CheckState();
        try
        {
            var json = File.ReadAllText(_checkFilePath);
            return JsonSerializer.Deserialize<CheckState>(json) ?? new CheckState();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateCheck] LoadState error: {ex.Message}");
            return new CheckState();
        }
    }

    private void SaveState(CheckState state)
    {
        try
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_checkFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateCheck] SaveState error: {ex.Message}");
        }
    }
}
