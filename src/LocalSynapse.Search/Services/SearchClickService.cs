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
    /// 이전 클릭 기반 부스트 점수를 반환한다 (0.0 ~ 1.0).
    /// position이 높을수록(=하위 결과) 강한 부스트, bounce 클릭은 부스트 감산.
    /// </summary>
    public double GetBoost(string query, string filePath)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT click_count, position, is_bounce FROM search_clicks
            WHERE query = $query AND file_path = $path";
        cmd.Parameters.AddWithValue("$query", query.ToLowerInvariant().Trim());
        cmd.Parameters.AddWithValue("$path", filePath);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return 0.0;

        var clickCount = reader.GetInt32(0);
        var position = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
        var isBounce = !reader.IsDBNull(2) && reader.GetInt32(2) == 1;

        if (isBounce) return 0.0; // bounce 클릭은 부스트 없음

        // position 가중치: 1위 클릭 = 약한 부스트, 8위 클릭 = 강한 부스트
        // base = count * 0.1 (max 1.0)
        // weight = 1.0 / log2(position + 2) → pos 0: 1.0, pos 1: 0.63, pos 7: 0.33
        // 최종 = base / log2(position + 2) 역수 = base * log2(position + 2)
        var baseBoost = Math.Min(1.0, clickCount * 0.1);
        var positionWeight = Math.Log2(position + 2); // pos 0→1.0, pos 1→1.58, pos 7→3.17
        var boost = Math.Min(1.0, baseBoost * positionWeight);

        return boost;
    }
}
