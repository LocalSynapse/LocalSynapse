using System.Diagnostics;
using LocalSynapse.Core.Database;
using Microsoft.Data.Sqlite;

namespace LocalSynapse.Search.Services;

/// <summary>
/// 검색 클릭 기록 및 부스트 점수 계산.
/// 클릭 위치(position)에 따른 차등 가중치와 재검색 실패(bounce) 감지를 지원한다.
/// </summary>
public class SearchClickService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    // 마지막 클릭 정보 (bounce 감지용) — lock으로 보호
    private readonly object _clickLock = new();
    private string? _lastClickQuery;
    private string? _lastClickFilePath;
    private DateTime _lastClickTime;

    /// <summary>SearchClickService 생성자.</summary>
    public SearchClickService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>검색 클릭을 기록한다. position은 결과 목록에서의 순위 (0-based).</summary>
    public void RecordClick(string query, string filePath, int position)
    {
        var normalizedQuery = NaturalQueryParser.RemoveStopwords(query).ToLowerInvariant().Trim();
        var normalizedPath = NormalizePath(filePath);
        var now = DateTime.UtcNow;

        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO search_clicks (query, file_path, click_count, last_clicked_at, position, is_bounce)
            VALUES ($query, $path, 1, $now, $pos, 0)
            ON CONFLICT(query, file_path) DO UPDATE SET
                click_count = click_count + 1,
                last_clicked_at = $now,
                position = MIN(COALESCE(position, $pos), $pos)";
        cmd.Parameters.AddWithValue("$query", normalizedQuery);
        cmd.Parameters.AddWithValue("$path", normalizedPath);
        cmd.Parameters.AddWithValue("$now", now.ToString("o"));
        cmd.Parameters.AddWithValue("$pos", position);
        cmd.ExecuteNonQuery();

        // known race: DB write와 필드 갱신 사이에 OnNewSearch가 끼어들 수 있음 (무시 가능)
        lock (_clickLock)
        {
            _lastClickQuery = normalizedQuery;
            _lastClickFilePath = normalizedPath;
            _lastClickTime = now;
        }

        Debug.WriteLine($"[ClickBoost] Recorded: query=\"{normalizedQuery}\" path=\"{normalizedPath}\" pos={position}");
    }

    /// <summary>
    /// 새 검색 시작 시 호출. 직전 클릭으로부터 10초 이내 재검색이면 bounce로 기록한다.
    /// Type-ahead prefix 연속은 bounce로 보지 않는다.
    /// </summary>
    public void OnNewSearch(string newQuery)
    {
        var normalizedNewQuery = NaturalQueryParser.RemoveStopwords(newQuery).ToLowerInvariant().Trim();

        lock (_clickLock)
        {
            if (_lastClickQuery == null || _lastClickFilePath == null) return;

            var elapsed = DateTime.UtcNow - _lastClickTime;

            // A-32: type-ahead prefix continuations are not bounces
            var isTypeAheadContinuation =
                !string.IsNullOrEmpty(_lastClickQuery) &&
                (normalizedNewQuery.StartsWith(_lastClickQuery, StringComparison.Ordinal) ||
                 _lastClickQuery.StartsWith(normalizedNewQuery, StringComparison.Ordinal));

            if (!isTypeAheadContinuation && elapsed.TotalSeconds <= 10)
            {
                try
                {
                    using var conn = _connectionFactory.CreateConnection();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        UPDATE search_clicks SET is_bounce = 1
                        WHERE query = $query AND file_path = $path";
                    cmd.Parameters.AddWithValue("$query", _lastClickQuery);
                    cmd.Parameters.AddWithValue("$path", _lastClickFilePath);
                    cmd.ExecuteNonQuery();

                    Debug.WriteLine($"[ClickBoost] Bounce detected: query=\"{_lastClickQuery}\" path=\"{_lastClickFilePath}\" ({elapsed.TotalSeconds:F1}s)");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ClickBoost] Bounce update failed: {ex.Message}");
                }
            }

            _lastClickQuery = null;
            _lastClickFilePath = null;
        }
    }

    /// <summary>
    /// 주어진 경로 목록 각각에 대한 click boost 점수를 반환한다 (0.0 ~ 1.0).
    /// 단일 쿼리로 조회하여 N+1 문제를 회피한다.
    /// `virtual`로 선언하여 test double이 `override`로 호출 횟수를 감시할 수 있다.
    /// </summary>
    public virtual Dictionary<string, double> GetBoostBatch(string query, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return new Dictionary<string, double>();

        const int MaxPathsPerCall = 900;
        if (paths.Count > MaxPathsPerCall)
        {
            throw new ArgumentException(
                $"GetBoostBatch supports at most {MaxPathsPerCall} paths per call " +
                $"(SQLite SQLITE_MAX_VARIABLE_NUMBER limit). Got {paths.Count}. " +
                $"Caller must chunk the input or extend this method to handle chunking internally.",
                nameof(paths));
        }

        var result = new Dictionary<string, double>(paths.Count);
        var normalizedQuery = NaturalQueryParser.RemoveStopwords(query).ToLowerInvariant().Trim();
        var normalizedPaths = paths.Select(NormalizePath).ToList();

        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();

        var placeholders = string.Join(", ", normalizedPaths.Select((_, i) => $"$p{i}"));
        cmd.CommandText = $@"
            SELECT file_path, click_count, position, is_bounce
            FROM search_clicks
            WHERE query = $query AND file_path IN ({placeholders})";
        cmd.Parameters.AddWithValue("$query", normalizedQuery);
        for (int i = 0; i < normalizedPaths.Count; i++)
            cmd.Parameters.AddWithValue($"$p{i}", normalizedPaths[i]);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var filePath = reader.GetString(0);
            var clickCount = reader.GetInt32(1);
            var position = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            var isBounce = !reader.IsDBNull(3) && reader.GetInt32(3) == 1;

            if (isBounce) { result[NormalizePath(filePath)] = 0.0; continue; }

            var baseBoost = Math.Min(1.0, clickCount * 0.1);
            var positionWeight = Math.Log2(position + 2);
            var boost = Math.Min(1.0, baseBoost * positionWeight);
            result[NormalizePath(filePath)] = boost;
        }

        return result;
    }

    /// <summary>검색 결과 중 이전에 클릭한 파일 경로와 마지막 열람일+총 클릭 수를 반환한다.</summary>
    public Dictionary<string, (DateTime lastOpened, int totalClicks)> GetRecentlyOpenedPaths(
        IReadOnlyList<string> candidatePaths, int limit = 5)
    {
        if (candidatePaths.Count == 0) return new();

        // SQLite 변수 상한 방어 — 900개씩 청크 처리
        const int chunkSize = 900;
        var result = new Dictionary<string, (DateTime, int)>(StringComparer.OrdinalIgnoreCase);

        for (int offset = 0; offset < candidatePaths.Count; offset += chunkSize)
        {
            var chunk = candidatePaths.Skip(offset).Take(chunkSize).ToList();
            using var conn = _connectionFactory.CreateConnection();
            using var cmd = conn.CreateCommand();

            var placeholders = new List<string>();
            for (int i = 0; i < chunk.Count; i++)
            {
                placeholders.Add($"$p{i}");
                cmd.Parameters.AddWithValue($"$p{i}", NormalizePath(chunk[i]));
            }

            cmd.CommandText = $@"
                SELECT file_path, MAX(last_clicked_at) as last_opened, SUM(click_count) as total_clicks
                FROM search_clicks
                WHERE is_bounce = 0 AND file_path IN ({string.Join(",", placeholders)})
                GROUP BY file_path
                ORDER BY last_opened DESC
                LIMIT $limit";
            cmd.Parameters.AddWithValue("$limit", limit);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (DateTime.TryParse(r.GetString(1), out var dt))
                    result[r.GetString(0)] = (dt, r.GetInt32(2));
            }
        }

        return result;
    }

    /// <summary>Normalize file path for consistent key matching.</summary>
    private static string NormalizePath(string path)
        => path.ToLowerInvariant().TrimEnd('\\', '/');
}
