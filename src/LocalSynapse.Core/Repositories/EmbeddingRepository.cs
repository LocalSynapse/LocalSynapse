using System.Runtime.CompilerServices;
using LocalSynapse.Core.Database;
using LocalSynapse.Core.Interfaces;
using Microsoft.Data.Sqlite;

namespace LocalSynapse.Core.Repositories;

/// <summary>
/// Dense vector 임베딩 CRUD를 담당하는 Repository 구현체.
/// </summary>
public sealed class EmbeddingRepository : IEmbeddingRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    /// <summary>EmbeddingRepository 생성자.</summary>
    public EmbeddingRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>지정 모델의 임베딩 총 수를 반환한다.</summary>
    public Task<int> GetEmbeddingCountAsync(string modelId, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var conn = _connectionFactory.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM embeddings WHERE model_id = $modelId";
            cmd.Parameters.AddWithValue("$modelId", modelId);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }, ct);
    }

    /// <summary>지정 모델의 임베딩이 없는 청크를 배치 단위로 열거한다.</summary>
    public async IAsyncEnumerable<ChunkForEmbedding> EnumerateChunksMissingEmbeddingAsync(
        string modelId, int batchSize, [EnumeratorCancellation] CancellationToken ct = default)
    {
        const string sql = @"
            SELECT fc.id, fc.file_id, fc.chunk_index, fc.text
            FROM file_chunks fc
            LEFT JOIN embeddings e ON fc.file_id = e.file_id
                AND fc.chunk_index = e.chunk_id
                AND e.model_id = $modelId
            WHERE e.id IS NULL
                AND fc.text IS NOT NULL
                AND LENGTH(fc.text) > 0
            LIMIT $limit";

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var batch = await Task.Run(() =>
            {
                var results = new List<ChunkForEmbedding>();
                using var conn = _connectionFactory.CreateConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$modelId", modelId);
                cmd.Parameters.AddWithValue("$limit", batchSize);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    results.Add(new ChunkForEmbedding
                    {
                        ChunkId    = r.GetString(0),
                        FileId     = r.GetString(1),
                        ChunkIndex = r.GetInt32(2),
                        Text       = r.GetString(3)
                    });
                }
                return results;
            }, ct);

            if (batch.Count == 0) yield break;

            foreach (var chunk in batch)
                yield return chunk;
        }
    }

    /// <summary>임베딩을 Upsert한다.</summary>
    public Task UpsertEmbeddingAsync(string fileId, int chunkId, string modelId, float[] vector,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var conn = _connectionFactory.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO embeddings (file_id, chunk_id, vector, vector_dim, model_id, created_at)
                VALUES ($fileId, $chunkId, $vector, $vectorDim, $modelId, $createdAt)
                ON CONFLICT(file_id, chunk_id, model_id) DO UPDATE SET
                    vector     = excluded.vector,
                    vector_dim = excluded.vector_dim,
                    created_at = excluded.created_at";
            cmd.Parameters.AddWithValue("$fileId", fileId);
            cmd.Parameters.AddWithValue("$chunkId", chunkId);
            cmd.Parameters.AddWithValue("$vector", VectorToBlob(vector));
            cmd.Parameters.AddWithValue("$vectorDim", vector.Length);
            cmd.Parameters.AddWithValue("$modelId", modelId);
            cmd.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }, ct);
    }

    /// <summary>임베딩 목록을 단일 트랜잭션으로 벌크 Upsert한다.</summary>
    public Task<int> BulkUpsertEmbeddingsAsync(
        IReadOnlyList<(string fileId, int chunkId, string modelId, float[] vector)> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0) return Task.FromResult(0);

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var conn = _connectionFactory.CreateConnection();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO embeddings (file_id, chunk_id, vector, vector_dim, model_id, created_at)
                VALUES ($fileId, $chunkId, $vector, $vectorDim, $modelId, $createdAt)
                ON CONFLICT(file_id, chunk_id, model_id) DO UPDATE SET
                    vector     = excluded.vector,
                    vector_dim = excluded.vector_dim,
                    created_at = excluded.created_at";

            var pFileId = cmd.Parameters.Add("$fileId", SqliteType.Text);
            var pChunkId = cmd.Parameters.Add("$chunkId", SqliteType.Integer);
            var pVector = cmd.Parameters.Add("$vector", SqliteType.Blob);
            var pVectorDim = cmd.Parameters.Add("$vectorDim", SqliteType.Integer);
            var pModelId = cmd.Parameters.Add("$modelId", SqliteType.Text);
            var pCreatedAt = cmd.Parameters.Add("$createdAt", SqliteType.Text);
            cmd.Prepare();

            var now = DateTime.UtcNow.ToString("o");
            var count = 0;

            foreach (var (fileId, chunkId, modelId, vector) in items)
            {
                ct.ThrowIfCancellationRequested();
                pFileId.Value = fileId;
                pChunkId.Value = chunkId;
                pVector.Value = VectorToBlob(vector);
                pVectorDim.Value = vector.Length;
                pModelId.Value = modelId;
                pCreatedAt.Value = now;
                cmd.ExecuteNonQuery();
                count++;
            }

            tx.Commit();
            return count;
        }, ct);
    }

    /// <summary>파일 ID 목록에 해당하는 임베딩과 청크 텍스트를 반환한다.</summary>
    public Task<List<EmbeddingWithChunk>> GetEmbeddingsByFileIdsAsync(
        string[] fileIds, string modelId, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var results = new List<EmbeddingWithChunk>();
            if (fileIds.Length == 0) return results;

            using var conn = _connectionFactory.CreateConnection();
            using var cmd = conn.CreateCommand();

            var placeholders = string.Join(", ", fileIds.Select((_, i) => $"$fid{i}"));
            cmd.CommandText = $@"
                SELECT e.file_id, f.path, e.chunk_id, fc.text, e.vector
                FROM embeddings e
                INNER JOIN files f ON e.file_id = f.id
                INNER JOIN file_chunks fc ON e.file_id = fc.file_id AND e.chunk_id = fc.chunk_index
                WHERE e.file_id IN ({placeholders})
                    AND e.model_id = $modelId";

            for (int i = 0; i < fileIds.Length; i++)
                cmd.Parameters.AddWithValue($"$fid{i}", fileIds[i]);
            cmd.Parameters.AddWithValue("$modelId", modelId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                results.Add(new EmbeddingWithChunk
                {
                    FileId    = r.GetString(0),
                    FilePath  = r.GetString(1),
                    ChunkId   = r.GetInt32(2),
                    ChunkText = r.IsDBNull(3) ? "" : r.GetString(3),
                    Vector    = BlobToVector((byte[])r.GetValue(4))
                });
            }
            return results;
        }, ct);
    }

    /// <summary>지정 모델의 모든 임베딩을 삭제한다.</summary>
    public Task DeleteAllEmbeddingsAsync(string modelId, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var conn = _connectionFactory.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM embeddings WHERE model_id = $modelId";
            cmd.Parameters.AddWithValue("$modelId", modelId);
            cmd.ExecuteNonQuery();
        }, ct);
    }

    /// <summary>지정 모델의 전체 임베딩을 배치 단위로 스트리밍 반환한다.</summary>
    public async IAsyncEnumerable<EmbeddingRecord> EnumerateAllEmbeddingsAsync(
        string modelId, int batchSize = 500,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var offset = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await Task.Run(() =>
            {
                var results = new List<EmbeddingRecord>();
                using var conn = _connectionFactory.CreateConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT file_id, chunk_id, vector
                    FROM embeddings
                    WHERE model_id = $modelId
                    ORDER BY id
                    LIMIT $limit OFFSET $offset";
                cmd.Parameters.AddWithValue("$modelId", modelId);
                cmd.Parameters.AddWithValue("$limit", batchSize);
                cmd.Parameters.AddWithValue("$offset", offset);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    results.Add(new EmbeddingRecord
                    {
                        FileId = r.GetString(0),
                        ChunkId = r.GetInt32(1),
                        Vector = BlobToVector((byte[])r.GetValue(2))
                    });
                }
                return results;
            }, ct);

            if (batch.Count == 0) break;

            foreach (var record in batch)
                yield return record;

            if (batch.Count < batchSize) break;
            offset += batchSize;
        }
    }

    private static byte[] VectorToBlob(float[] vector)
    {
        var bytes = new byte[vector.Length * 4];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BlobToVector(byte[] blob)
    {
        var vector = new float[blob.Length / 4];
        Buffer.BlockCopy(blob, 0, vector, 0, blob.Length);
        return vector;
    }
}
