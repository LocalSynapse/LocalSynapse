using LocalSynapse.Core.Database;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Core.Utils;
using Microsoft.Data.Sqlite;

namespace LocalSynapse.Core.Repositories;

/// <summary>
/// 파일 청크 CRUD 및 chunks_fts 동기화를 담당하는 Repository 구현체.
/// </summary>
public sealed class ChunkRepository : IChunkRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    /// <summary>ChunkRepository 생성자.</summary>
    public ChunkRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>복수 청크를 일괄 Upsert하고, chunks_fts를 동기화한다.</summary>
    public int UpsertChunks(IEnumerable<FileChunk> chunks)
    {
        var chunkList = chunks.ToList();
        if (chunkList.Count == 0) return 0;

        using var conn = _connectionFactory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var count = 0;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO file_chunks (id, file_id, chunk_index, text, source_type, origin_meta,
                                     token_count, content_hash, created_at, start_offset, end_offset)
            VALUES ($id, $file_id, $chunk_index, $text, $source_type, $origin_meta,
                    $token_count, $content_hash, $created_at, $start_offset, $end_offset)
            ON CONFLICT(file_id, chunk_index) DO UPDATE SET
                text         = excluded.text,
                source_type  = excluded.source_type,
                origin_meta  = excluded.origin_meta,
                token_count  = excluded.token_count,
                content_hash = excluded.content_hash,
                start_offset = excluded.start_offset,
                end_offset   = excluded.end_offset";

        var pId         = cmd.Parameters.Add("$id", SqliteType.Text);
        var pFileId     = cmd.Parameters.Add("$file_id", SqliteType.Text);
        var pChunkIndex = cmd.Parameters.Add("$chunk_index", SqliteType.Integer);
        var pText       = cmd.Parameters.Add("$text", SqliteType.Text);
        var pSourceType = cmd.Parameters.Add("$source_type", SqliteType.Text);
        var pOriginMeta = cmd.Parameters.Add("$origin_meta", SqliteType.Text);
        var pTokenCount = cmd.Parameters.Add("$token_count", SqliteType.Integer);
        var pHash       = cmd.Parameters.Add("$content_hash", SqliteType.Text);
        var pCreatedAt  = cmd.Parameters.Add("$created_at", SqliteType.Text);
        var pStart      = cmd.Parameters.Add("$start_offset", SqliteType.Integer);
        var pEnd        = cmd.Parameters.Add("$end_offset", SqliteType.Integer);

        foreach (var c in chunkList)
        {
            pId.Value         = c.Id;
            pFileId.Value     = c.FileId;
            pChunkIndex.Value = c.ChunkIndex;
            pText.Value       = c.Text;
            pSourceType.Value = c.SourceType;
            pOriginMeta.Value = (object?)c.OriginMeta ?? DBNull.Value;
            pTokenCount.Value = (object?)c.TokenCount ?? DBNull.Value;
            pHash.Value       = c.ContentHash;
            pCreatedAt.Value  = c.CreatedAt;
            pStart.Value      = (object?)c.StartOffset ?? DBNull.Value;
            pEnd.Value        = (object?)c.EndOffset ?? DBNull.Value;

            cmd.ExecuteNonQuery();
            count++;
        }

        // Bulk FTS sync: file별 DELETE + INSERT
        var fileIds = chunkList.Select(c => c.FileId).Distinct().ToList();

        // ── Stale 임베딩 삭제 (file별 배치) ──
        // 청크 텍스트가 변경되었으므로 기존 벡터는 무효.
        // 다음 파이프라인 사이클에서 EnumerateChunksMissingEmbeddingAsync가 재생성.
        foreach (var fid in fileIds)
        {
            using var delEmbCmd = conn.CreateCommand();
            delEmbCmd.CommandText = "DELETE FROM embeddings WHERE file_id = $fid";
            delEmbCmd.Parameters.AddWithValue("$fid", fid);
            delEmbCmd.ExecuteNonQuery();
        }

        foreach (var fileId in fileIds)
        {
            using (var delCmd = conn.CreateCommand())
            {
                delCmd.CommandText = @"
                    DELETE FROM chunks_fts
                    WHERE chunk_id IN (SELECT id FROM file_chunks WHERE file_id = $file_id)";
                delCmd.Parameters.AddWithValue("$file_id", fileId);
                delCmd.ExecuteNonQuery();
            }

            using (var readCmd = conn.CreateCommand())
            {
                readCmd.CommandText = @"
                    SELECT c.id, c.text, f.filename, f.folder_path
                    FROM file_chunks c
                    JOIN files f ON c.file_id = f.id
                    WHERE c.file_id = $file_id AND c.text IS NOT NULL AND LENGTH(c.text) > 0";
                readCmd.Parameters.AddWithValue("$file_id", fileId);

                using var insCmd = conn.CreateCommand();
                insCmd.CommandText = @"
                    INSERT INTO chunks_fts (chunk_id, file_id, text, filename, folder_path)
                    VALUES ($cid, $fid, $txt, $fn, $fp)";
                var pCid = insCmd.Parameters.Add("$cid", SqliteType.Text);
                var pFid2 = insCmd.Parameters.Add("$fid", SqliteType.Text);
                var pTxt = insCmd.Parameters.Add("$txt", SqliteType.Text);
                var pFn = insCmd.Parameters.Add("$fn", SqliteType.Text);
                var pFp = insCmd.Parameters.Add("$fp", SqliteType.Text);

                using var reader = readCmd.ExecuteReader();
                while (reader.Read())
                {
                    pCid.Value = reader.GetString(0);
                    pFid2.Value = fileId;
                    pTxt.Value = CjkTextUtils.ApplyBigramSplit(reader.GetString(1));
                    pFn.Value = CjkTextUtils.ApplyBigramSplit(reader.GetString(2));
                    pFp.Value = CjkTextUtils.ApplyBigramSplit(reader.GetString(3));
                    insCmd.ExecuteNonQuery();
                }
            }
        }

        tx.Commit();
        return count;
    }

    /// <summary>파일 ID에 해당하는 모든 청크를 chunk_index 순으로 반환한다.</summary>
    public IEnumerable<FileChunk> GetChunksForFile(string fileId)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, file_id, chunk_index, text, source_type, origin_meta,
                   token_count, content_hash, created_at, start_offset, end_offset
            FROM file_chunks
            WHERE file_id = $file_id
            ORDER BY chunk_index";
        cmd.Parameters.AddWithValue("$file_id", fileId);

        var chunks = new List<FileChunk>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) chunks.Add(ReadRow(r));
        return chunks;
    }

    /// <summary>파일 ID에 해당하는 모든 청크와 FTS 항목을 삭제한다.</summary>
    public int DeleteChunksForFile(string fileId)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var tx = conn.BeginTransaction();

        using (var ftsCmd = conn.CreateCommand())
        {
            ftsCmd.CommandText = @"
                DELETE FROM chunks_fts
                WHERE chunk_id IN (SELECT id FROM file_chunks WHERE file_id = $file_id)";
            ftsCmd.Parameters.AddWithValue("$file_id", fileId);
            ftsCmd.ExecuteNonQuery();
        }

        int count;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM file_chunks WHERE file_id = $file_id";
            cmd.Parameters.AddWithValue("$file_id", fileId);
            count = cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return count;
    }

    /// <summary>전체 청크 수를 반환한다.</summary>
    public int GetTotalCount()
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM file_chunks";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static FileChunk ReadRow(SqliteDataReader r)
    {
        return new FileChunk
        {
            Id          = r.GetString(0),
            FileId      = r.GetString(1),
            ChunkIndex  = r.GetInt32(2),
            Text        = r.IsDBNull(3) ? "" : r.GetString(3),
            SourceType  = r.GetString(4),
            OriginMeta  = r.IsDBNull(5) ? null : r.GetString(5),
            TokenCount  = r.IsDBNull(6) ? null : r.GetInt32(6),
            ContentHash = r.GetString(7),
            CreatedAt   = r.GetString(8),
            StartOffset = r.IsDBNull(9) ? null : r.GetInt32(9),
            EndOffset   = r.IsDBNull(10) ? null : r.GetInt32(10),
        };
    }
}
