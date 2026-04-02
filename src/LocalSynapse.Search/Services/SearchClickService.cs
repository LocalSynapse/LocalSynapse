using LocalSynapse.Core.Database;
using Microsoft.Data.Sqlite;

namespace LocalSynapse.Search.Services;

/// <summary>
/// 검색 클릭 기록 및 부스트 점수 계산.
/// </summary>
public sealed class SearchClickService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    /// <summary>SearchClickService 생성자.</summary>
    public SearchClickService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>검색 클릭을 기록한다.</summary>
    public void RecordClick(string query, string filePath)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO search_clicks (query, file_path, click_count, last_clicked_at)
            VALUES ($query, $path, 1, $now)
            ON CONFLICT(query, file_path) DO UPDATE SET
                click_count = click_count + 1,
                last_clicked_at = $now";
        cmd.Parameters.AddWithValue("$query", query.ToLowerInvariant().Trim());
        cmd.Parameters.AddWithValue("$path", filePath);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>이전 클릭 기반 부스트 점수를 반환한다 (0.0 ~ 1.0).</summary>
    public double GetBoost(string query, string filePath)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT click_count FROM search_clicks
            WHERE query = $query AND file_path = $path";
        cmd.Parameters.AddWithValue("$query", query.ToLowerInvariant().Trim());
        cmd.Parameters.AddWithValue("$path", filePath);

        var result = cmd.ExecuteScalar();
        if (result is long count && count > 0)
            return Math.Min(1.0, count * 0.1); // 10 clicks = max 1.0 boost
        return 0.0;
    }
}
