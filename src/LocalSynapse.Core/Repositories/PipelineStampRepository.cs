using LocalSynapse.Core.Database;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using Microsoft.Data.Sqlite;

namespace LocalSynapse.Core.Repositories;

/// <summary>
/// 파이프라인 진행 상태(stamp)를 관리하는 Repository 구현체.
/// UI 숫자는 이 테이블에서만 읽어야 한다 (실시간 COUNT(*) 쿼리 금지).
/// </summary>
public sealed class PipelineStampRepository : IPipelineStampRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    /// <summary>PipelineStampRepository 생성자.</summary>
    public PipelineStampRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>현재 파이프라인 stamp 상태를 반환한다.</summary>
    public PipelineStamps GetCurrent()
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM pipeline_stamps WHERE id = 'current'";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new PipelineStamps();

        return new PipelineStamps
        {
            TotalFiles            = r.GetInt32(r.GetOrdinal("total_files")),
            TotalFolders          = r.GetInt32(r.GetOrdinal("total_folders")),
            ScanCompletedAt       = GetNullableString(r, "scan_completed_at"),
            ContentSearchableFiles= r.GetInt32(r.GetOrdinal("content_searchable_files")),
            IndexedFiles          = r.GetInt32(r.GetOrdinal("indexed_files")),
            TotalChunks           = r.GetInt32(r.GetOrdinal("total_chunks")),
            IndexingCompletedAt   = GetNullableString(r, "indexing_completed_at"),
            EmbeddableChunks      = r.GetInt32(r.GetOrdinal("embeddable_chunks")),
            EmbeddedChunks        = r.GetInt32(r.GetOrdinal("embedded_chunks")),
            EmbeddingCompletedAt  = GetNullableString(r, "embedding_completed_at"),
            LastAutoRunAt         = GetNullableString(r, "last_auto_run_at"),
            AutoRunCount          = r.GetInt32(r.GetOrdinal("auto_run_count")),
        };
    }

    /// <summary>스캔 완료 시 stamp를 기록한다.</summary>
    public void StampScanComplete(int totalFiles, int totalFolders, int contentSearchableFiles)
    {
        ExecuteUpdate(@"
            UPDATE pipeline_stamps SET
                total_files              = $p0,
                total_folders            = $p1,
                content_searchable_files = $p2,
                scan_completed_at        = datetime('now'),
                updated_at               = datetime('now')
            WHERE id = 'current'",
            ("$p0", totalFiles), ("$p1", totalFolders), ("$p2", contentSearchableFiles));
    }

    /// <summary>인덱싱 진행 중 stamp를 갱신한다.</summary>
    public void UpdateIndexingProgress(int indexedFiles, int totalChunks)
    {
        ExecuteUpdate(@"
            UPDATE pipeline_stamps SET
                indexed_files         = $p0,
                total_chunks          = $p1,
                indexing_completed_at = NULL,
                updated_at            = datetime('now')
            WHERE id = 'current'",
            ("$p0", indexedFiles), ("$p1", totalChunks));
    }

    /// <summary>인덱싱 완료 시 stamp를 기록한다.</summary>
    public void StampIndexingComplete(int indexedFiles, int totalChunks)
    {
        ExecuteUpdate(@"
            UPDATE pipeline_stamps SET
                indexed_files         = $p0,
                total_chunks          = $p1,
                indexing_completed_at = datetime('now'),
                updated_at            = datetime('now')
            WHERE id = 'current'",
            ("$p0", indexedFiles), ("$p1", totalChunks));
    }

    /// <summary>임베딩 가능 청크 수를 갱신한다.</summary>
    public void UpdateEmbeddableChunks(int embeddableChunks)
    {
        ExecuteUpdate(@"
            UPDATE pipeline_stamps SET
                embeddable_chunks = $p0,
                updated_at        = datetime('now')
            WHERE id = 'current'",
            ("$p0", embeddableChunks));
    }

    /// <summary>임베딩 진행 중 stamp를 갱신한다.</summary>
    public void UpdateEmbeddingProgress(int embeddedChunks)
    {
        ExecuteUpdate(@"
            UPDATE pipeline_stamps SET
                embedded_chunks        = $p0,
                embedding_completed_at = NULL,
                updated_at             = datetime('now')
            WHERE id = 'current'",
            ("$p0", embeddedChunks));
    }

    /// <summary>임베딩 완료 시 stamp를 기록한다.</summary>
    public void StampEmbeddingComplete(int embeddableChunks, int embeddedChunks)
    {
        ExecuteUpdate(@"
            UPDATE pipeline_stamps SET
                embeddable_chunks      = $p0,
                embedded_chunks        = $p1,
                embedding_completed_at = datetime('now'),
                updated_at             = datetime('now')
            WHERE id = 'current'",
            ("$p0", embeddableChunks), ("$p1", embeddedChunks));
    }

    /// <summary>자동 실행 시각과 횟수를 기록한다.</summary>
    public void StampAutoRun()
    {
        ExecuteUpdate(@"
            UPDATE pipeline_stamps SET
                last_auto_run_at = datetime('now'),
                auto_run_count   = auto_run_count + 1,
                updated_at       = datetime('now')
            WHERE id = 'current'");
    }

    // ─────────────────────────── private ───────────────────────────

    private void ExecuteUpdate(string sql, params (string name, object value)[] parameters)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private static string? GetNullableString(SqliteDataReader r, string column)
    {
        var ordinal = r.GetOrdinal(column);
        return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }
}
