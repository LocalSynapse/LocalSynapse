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

    // 마지막 클릭 정보 (bounce 감지용)
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
        var normalizedQuery = query.ToLowerInvariant().Trim();
        var now = DateTime.UtcNow;

        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO search_clicks (query, file_path, click_count, last_clicked_at, position, is_bounce)
            VALUES ($query, $path, 1, $now, $pos, 0)
            ON CONFLICT(query, file_path) DO UPDATE SET
                click_count = click_count + 1,
                last_clicked_at = $now,
                position = $pos";
        cmd.Parameters.AddWithValue("$query", normalizedQuery);
        cmd.Parameters.AddWithValue("$path", filePath);
        cmd.Parameters.AddWithValue("$now", now.ToString("o"));
        cmd.Parameters.AddWithValue("$pos", position);
        cmd.ExecuteNonQuery();

        // 마지막 클릭 정보 저장 (bounce 감지용)
        _lastClickQuery = normalizedQuery;
        _lastClickFilePath = filePath;
        _lastClickTime = now;

        Debug.WriteLine($"[ClickBoost] Recorded: query=\"{normalizedQuery}\" path=\"{filePath}\" pos={position}");
    }

    /// <summary>
    /// 새 검색 시작 시 호출. 직전 클릭으로부터 10초 이내 재검색이면 bounce로 기록한다.
    /// </summary>
    public void OnNewSearch(string newQuery)
    {
        if (_lastClickQuery == null || _lastClickFilePath == null) return;

        var elapsed = DateTime.UtcNow - _lastClickTime;
        if (elapsed.TotalSeconds <= 10)
        {
            // 10초 이내 재검색 = bounce (클릭한 결과가 원하는 게 아니었음)
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

        // 리셋
        _lastClickQuery = null;
        _lastClickFilePath = null;
    }

    /// <summary>
    /// 주어진 경로 목록 각각에 대한 click boost 점수를 반환한다 (0.0 ~ 1.0).
    /// 단일 쿼리로 조회하여 N+1 문제를 회피한다.
    /// `virtual`로 선언하여 test double이 `override`로 호출 횟수를 감시할 수 있다.
    /// </summary>
    /// <returns>
    /// Dictionary&lt;path, boost&gt;. paths 중 click 기록이 없는 항목은 포함되지 않는다
    /// (호출자는 TryGetValue 사용).
    /// </returns>
    public virtual Dictionary<string, double> GetBoostBatch(string query, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return new Dictionary<string, double>();

        // W2 방어 가드: SQLite SQLITE_MAX_VARIABLE_NUMBER (기본 999). 이 메서드는
        // paths 각각에 $p{i} 1개 + $query 1개 = paths.Count + 1 변수를 사용하므로
        // 안전 한계는 paths.Count <= 998. 현재 Bm25SearchService는
        // LIMIT = TopK * ChunksPerFile * 3 = 최대 ~240 paths만 전달.
        // 향후 LIMIT 공식이 변경되거나 TopK가 크게 증가하면 이 가드가 명시적 실패로 안내.
        // 900으로 한 이유: 999에 정확히 맞추면 SQLite 컴파일 옵션 차이에 brittle. 여유 100.
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
        var normalizedQuery = query.ToLowerInvariant().Trim();

        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();

        var placeholders = string.Join(", ", paths.Select((_, i) => $"$p{i}"));
        cmd.CommandText = $@"
            SELECT file_path, click_count, position, is_bounce
            FROM search_clicks
            WHERE query = $query AND file_path IN ({placeholders})";
        cmd.Parameters.AddWithValue("$query", normalizedQuery);
        for (int i = 0; i < paths.Count; i++)
            cmd.Parameters.AddWithValue($"$p{i}", paths[i]);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var filePath = reader.GetString(0);
            var clickCount = reader.GetInt32(1);
            var position = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            var isBounce = !reader.IsDBNull(3) && reader.GetInt32(3) == 1;

            if (isBounce) { result[filePath] = 0.0; continue; }

            // position 가중치: 1위 클릭 = 약한 부스트, 8위 클릭 = 강한 부스트
            // base = count * 0.1 (max 1.0)
            // weight = log2(position + 2) → pos 0: 1.0, pos 1: 1.58, pos 7: 3.17
            var baseBoost = Math.Min(1.0, clickCount * 0.1);
            var positionWeight = Math.Log2(position + 2);
            var boost = Math.Min(1.0, baseBoost * positionWeight);
            result[filePath] = boost;
        }

        return result;
    }
}
