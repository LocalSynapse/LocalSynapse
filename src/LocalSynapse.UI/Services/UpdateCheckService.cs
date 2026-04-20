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
/// HasUpdateAvailable 상태를 직접 관리하여 ViewModel 간 역참조를 제거한다.
/// </summary>
public sealed class UpdateCheckService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/LocalSynapse/LocalSynapse/releases/latest";
    private const string PingUrl = "https://localsynapse.com/api/ping";
    private const string DownloadPageUrl = "https://localsynapse.com/download";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly string _checkFilePath;
    private bool _isFirstRunCached;
    private bool _isFirstRunChecked;

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

    /// <summary>첫 실행 여부 (캐시됨, 디스크 hit 최소화).</summary>
    public bool IsFirstRun
    {
        get
        {
            if (!_isFirstRunChecked)
            {
                _isFirstRunCached = !File.Exists(_checkFilePath);
                _isFirstRunChecked = true;
            }
            return _isFirstRunCached;
        }
    }

    /// <summary>업데이트 가능 여부. MainViewModel/SettingsViewModel 양쪽에서 참조.</summary>
    public bool HasUpdateAvailable { get; set; }

    /// <summary>마지막 체크 결과 캐시.</summary>
    public UpdateInfo? LastResult { get; private set; }

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
        SaveState(new CheckState { CheckEnabled = true });
        _isFirstRunCached = false;
        _isFirstRunChecked = true;
    }

    /// <summary>첫 실행 비활성화 (update-check.json 생성, checkEnabled=false).</summary>
    public void DisableFromFirstRun()
    {
        SaveState(new CheckState { CheckEnabled = false });
        _isFirstRunCached = false;
        _isFirstRunChecked = true;
    }

    /// <summary>특정 버전을 dismiss.</summary>
    public void DismissVersion(string version)
    {
        var state = LoadState();
        state.DismissedVersion = version;
        SaveState(state);
        HasUpdateAvailable = false;
        LastResult = null;
    }

    /// <summary>1일 1회 업데이트 체크 + 통계 ping. 첫 실행 시 skip.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (IsFirstRun) return null;

        var state = LoadState();
        if (!state.CheckEnabled) return null;

        if (DateTime.TryParse(state.LastCheckAt, out var lastCheck)
            && DateTime.UtcNow - lastCheck < CheckInterval)
            return null;

        state.LastCheckAt = DateTime.UtcNow.ToString("o");
        SaveState(state);

        UpdateInfo? result = null;

        // Task 1: GitHub API (업데이트 체크)
        try
        {
            result = await CheckGitHubReleaseAsync(state, ct);
            LastResult = result;
            HasUpdateAvailable = result != null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateCheck] GitHub API error: {ex.Message}");
        }

        // Task 2: Statistics ping (fire-and-forget, CancellationToken 전달)
        _ = Task.Run(async () =>
        {
            try { await SendPingAsync(ct); }
            catch (Exception ex) { Debug.WriteLine($"[UpdateCheck] Ping error: {ex.Message}"); }
        }, ct);

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

        // TryGetProperty로 안전 파싱
        if (!root.TryGetProperty("tag_name", out var tagProp)) return null;
        var tagName = tagProp.GetString() ?? "";
        var versionStr = tagName.TrimStart('v');
        if (!Version.TryParse(versionStr, out var latest)) return null;

        var current = Assembly.GetEntryAssembly()?.GetName().Version;
        if (current == null || latest <= current) return null;

        if (state.DismissedVersion == versionStr) return null;

        var htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? "" : "";
        var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
        var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : null;

        var summary = ExtractSummary(name, tagName, body);

        return new UpdateInfo(versionStr, GetCurrentVersion(), htmlUrl, DownloadPageUrl, summary);
    }

    /// <summary>Release name 우선, bullet fallback.</summary>
    private static string ExtractSummary(string? name, string tagName, string? body)
    {
        // 1. name 필드 (tag_name과 다르고, tag_name 그 자체가 아닌 경우)
        if (!string.IsNullOrWhiteSpace(name) && name != tagName
            && !name.Equals(tagName.TrimStart('v'), StringComparison.OrdinalIgnoreCase))
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

    private async Task SendPingAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = HttpTimeout };
        var payload = JsonSerializer.Serialize(new
        {
            v = GetCurrentVersion(),
            os = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            locale = System.Globalization.CultureInfo.CurrentUICulture.Name
        });
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        await http.PostAsync(PingUrl, content, ct);
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
