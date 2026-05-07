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
    public const string DownloadPageUrl = "https://localsynapse.com/download";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly string _checkFilePath;
    private readonly TelemetryCounterService _telemetry;
    private bool _isFirstRunCached;
    private bool _isFirstRunChecked;

    /// <summary>업데이트 정보.</summary>
    public record UpdateInfo(
        string LatestVersion,
        string CurrentVersion,
        string ReleaseNotesUrl,
        string DownloadUrl,
        string Summary,
        List<string> ReleaseNotes);

    /// <summary>Asset URLs/sizes parsed from the GitHub release. Nested inside UpdateCheckService
    /// (alongside UpdateInfo and CheckState) for namespace cohesion. UpdateInfo stays the
    /// lightweight "is there a newer version?" record; asset metadata is only needed at install
    /// click time (SPEC-IU-1 §4.3 / §8).</summary>
    public sealed record ReleaseAssets(
        string WindowsAssetUrl,
        long WindowsAssetSize,
        string Sha256SumsUrl);

    /// <summary>체크 완료 여부.</summary>
    public bool HasChecked { get; private set; }

    /// <summary>update-check.json 스키마.</summary>
    private sealed class CheckState
    {
        public string? LastCheckAt { get; set; }
        public string? DismissedVersion { get; set; }
        public bool CheckEnabled { get; set; } = true;
        public string? Iid { get; set; }
        public string? LastPayload { get; set; }
        public string? LastPayloadAt { get; set; }
        public string? ReleaseNotesUrl { get; set; }
    }

    /// <summary>UpdateCheckService 생성자.</summary>
    public UpdateCheckService(ISettingsStore settingsStore, TelemetryCounterService telemetry)
    {
        _checkFilePath = Path.Combine(settingsStore.GetDataFolder(), "update-check.json");
        _telemetry = telemetry;
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

    /// <summary>Last parsed Windows asset URLs (null if asset/SUMS missing in latest release).</summary>
    public ReleaseAssets? LatestAssets { get; private set; }

    /// <summary>업데이트 체크 활성화 여부.</summary>
    public bool IsCheckEnabled => LoadState().CheckEnabled;

    /// <summary>최신 버전 문자열.</summary>
    public string? LatestVersion => LastResult?.LatestVersion;

    /// <summary>릴리스 노트 URL.</summary>
    public string? ReleaseNotesUrl => LoadState().ReleaseNotesUrl;

    /// <summary>마지막 전송 payload와 timestamp를 반환한다.</summary>
    public (string? Payload, string? SentAt) GetLastPayload()
    {
        var state = LoadState();
        return (state.LastPayload, state.LastPayloadAt);
    }

    /// <summary>업데이트 체크 활성화/비활성화.</summary>
    public void SetCheckEnabled(bool enabled)
    {
        var state = LoadState();
        state.CheckEnabled = enabled;
        SaveState(state);
    }

    /// <summary>첫 실행 동의 (update-check.json 생성, checkEnabled=true). 기존 Iid 보존.</summary>
    public void AcceptFirstRun()
    {
        var state = LoadState();
        state.CheckEnabled = true;
        SaveState(state);
        _isFirstRunCached = false;
        _isFirstRunChecked = true;
    }

    /// <summary>첫 실행 비활성화 (update-check.json 생성, checkEnabled=false). 기존 Iid 보존.</summary>
    public void DisableFromFirstRun()
    {
        var state = LoadState();
        state.CheckEnabled = false;
        SaveState(state);
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
    }

    /// <summary>1일 1회 업데이트 체크 + 통계 ping.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        // WO-SEC0: first-run gate removed — runs from first launch.
        // If update-check.json doesn't exist yet, create it with defaults (CheckEnabled=true).
        var state = LoadState();
        if (!state.CheckEnabled) return null;

        if (DateTime.TryParse(state.LastCheckAt, out var lastCheck)
            && DateTime.UtcNow - lastCheck < CheckInterval)
            return null;

        // iid 인라인 생성 (race 방지 — 동일 state 객체 사용)
        if (string.IsNullOrEmpty(state.Iid))
            state.Iid = Guid.NewGuid().ToString();

        state.LastCheckAt = DateTime.UtcNow.ToString("o");
        SaveState(state);

        var iid = state.Iid;
        UpdateInfo? result = null;

        // Task 1: GitHub API (업데이트 체크)
        try
        {
            result = await CheckGitHubReleaseAsync(state, ct);
            LastResult = result;
            HasUpdateAvailable = result != null;
            if (result != null)
            {
                state.ReleaseNotesUrl = result.ReleaseNotesUrl;
                SaveState(state);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateCheck] GitHub API error: {ex.Message}");
        }
        HasChecked = true;

        // Task 2: Statistics ping (fire-and-forget, CancellationToken 전달)
        var snapshot = _telemetry.Snapshot();
        _ = Task.Run(async () =>
        {
            try
            {
                await SendPingAsync(iid, snapshot, ct);
                _telemetry.ResetCounters(snapshot);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateCheck] Ping failed, counters retained: {ex.Message}");
            }
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
        var releaseNotes = ParseReleaseNotes(body);

        // IU-1a: parse Windows installer asset + SHA256SUMS.txt asset.
        // If either is missing, LatestAssets stays null and the Install button does not appear (SPEC-IU-1 §4.2).
        LatestAssets = ParseWindowsAssets(root, tagName);

        return new UpdateInfo(versionStr, GetCurrentVersion(), htmlUrl, DownloadPageUrl, summary, releaseNotes);
    }

    /// <summary>Parse Windows installer + SHA256SUMS.txt URLs from GitHub release assets[].
    /// Returns null if either asset is missing (SPEC-IU-1 §4.2 gate).</summary>
    private static ReleaseAssets? ParseWindowsAssets(JsonElement root, string tagName)
    {
        if (!root.TryGetProperty("assets", out var assetsProp)
            || assetsProp.ValueKind != JsonValueKind.Array)
            return null;

        var expectedInstaller = $"LocalSynapse-{tagName}-Windows-Setup.exe";
        const string expectedSums = "SHA256SUMS.txt";

        string? installerUrl = null;
        long installerSize = 0;
        string? sumsUrl = null;

        foreach (var asset in assetsProp.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameEl)) continue;
            var assetName = nameEl.GetString();
            if (string.IsNullOrEmpty(assetName)) continue;

            if (string.Equals(assetName, expectedInstaller, StringComparison.Ordinal))
            {
                if (asset.TryGetProperty("browser_download_url", out var urlEl))
                    installerUrl = urlEl.GetString();
                if (asset.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var size))
                    installerSize = size;
            }
            else if (string.Equals(assetName, expectedSums, StringComparison.Ordinal))
            {
                if (asset.TryGetProperty("browser_download_url", out var urlEl))
                    sumsUrl = urlEl.GetString();
            }
        }

        if (string.IsNullOrEmpty(installerUrl) || string.IsNullOrEmpty(sumsUrl))
            return null;

        return new ReleaseAssets(installerUrl, installerSize, sumsUrl);
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

    /// <summary>Release body에서 사용자 대상 bullet 리스트를 추출한다.</summary>
    private static List<string> ParseReleaseNotes(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];

        var lines = body.Split('\n');
        var notes = new List<string>();
        bool inUserSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("### For users") ||
                (trimmed.StartsWith("### v") && trimmed.Contains("새로운")))
            {
                inUserSection = true;
                continue;
            }
            if (inUserSection && trimmed.StartsWith("###"))
                break;
            if (inUserSection && (trimmed.StartsWith("- ") || trimmed.StartsWith("· ")))
                notes.Add(trimmed.TrimStart('-', '·', ' '));
        }

        if (notes.Count == 0)
        {
            notes = lines
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("- ") || l.StartsWith("· "))
                .Select(l => l.TrimStart('-', '·', ' '))
                .Take(5)
                .ToList();
        }

        return notes;
    }

    private async Task SendPingAsync(string iid, TelemetrySnapshot? stats, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = HttpTimeout };
        var payloadObj = new Dictionary<string, object?>
        {
            ["iid"] = iid,
            ["v"] = GetCurrentVersion(),
            ["os"] = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            ["locale"] = System.Globalization.CultureInfo.CurrentUICulture.Name,
        };
        if (stats != null)
        {
            payloadObj["search_count"] = stats.SearchCount;
            payloadObj["empty_result_count"] = stats.EmptyResultCount;
            payloadObj["avg_response_ms"] = stats.AvgResponseMs;
            payloadObj["modality_bm25"] = stats.ModalityBm25;
            payloadObj["modality_dense"] = stats.ModalityDense;
            payloadObj["modality_hybrid"] = stats.ModalityHybrid;
            payloadObj["top_result_click_count"] = stats.TopResultClickCount;
            payloadObj["indexed_doc_count_bucket"] = stats.IndexedDocCountBucket;
        }
        var payload = JsonSerializer.Serialize(payloadObj, new JsonSerializerOptions { WriteIndented = false });
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        await http.PostAsync(PingUrl, content, ct);

        // Persist for [View last sent] transparency
        var state = LoadState();
        state.LastPayload = payload;
        state.LastPayloadAt = DateTime.UtcNow.ToString("o");
        SaveState(state);
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
            var tempPath = _checkFilePath + ".tmp";
            var backupPath = _checkFilePath + ".bak";

            File.WriteAllText(tempPath, json);

            if (File.Exists(_checkFilePath))
            {
                File.Replace(tempPath, _checkFilePath, backupPath);
                try { File.Delete(backupPath); }
                catch (Exception delEx) { Debug.WriteLine($"[UpdateCheck] Backup cleanup: {delEx.Message}"); }
            }
            else
            {
                File.Move(tempPath, _checkFilePath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateCheck] SaveState error: {ex.Message}");
        }
    }
}
