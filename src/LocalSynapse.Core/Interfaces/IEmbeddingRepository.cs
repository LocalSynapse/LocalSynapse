namespace LocalSynapse.Core.Interfaces;

public interface IEmbeddingRepository
{
    Task<int> GetEmbeddingCountAsync(string modelId, CancellationToken ct = default);
    IAsyncEnumerable<ChunkForEmbedding> EnumerateChunksMissingEmbeddingAsync(
        string modelId, int batchSize, CancellationToken ct = default);
    Task UpsertEmbeddingAsync(string fileId, int chunkId, string modelId, float[] vector,
        CancellationToken ct = default);

    /// <summary>임베딩 목록을 단일 트랜잭션으로 벌크 Upsert한다.</summary>
    Task<int> BulkUpsertEmbeddingsAsync(
        IReadOnlyList<(string fileId, int chunkId, string modelId, float[] vector)> items,
        CancellationToken ct = default);
    Task<List<EmbeddingWithChunk>> GetEmbeddingsByFileIdsAsync(
        string[] fileIds, string modelId, CancellationToken ct = default);
    Task DeleteAllEmbeddingsAsync(string modelId, CancellationToken ct = default);

    /// <summary>
    /// 지정 모델의 전체 임베딩을 배치 단위로 스트리밍 반환한다.
    /// 메모리 폭발 방지를 위해 batchSize 단위로 DB에서 읽는다.
    /// </summary>
    IAsyncEnumerable<EmbeddingRecord> EnumerateAllEmbeddingsAsync(
        string modelId, int batchSize = 500, CancellationToken ct = default);

    // ── v2.11.0 ──
    // ⚠️ CLAUDE.md Interfaces/ 수정 예외 승인 (2026-05-19): additive-only.

    /// <summary>
    /// (file_id, chunk_id) 튜플 집합에 해당하는 임베딩을 일괄 조회한다.
    /// 후보 청크 단위 재순위(cascade rerank)에 사용한다. 누락된 튜플은 결과에서 빠진다.
    /// </summary>
    Task<List<EmbeddingRecord>> GetEmbeddingsByChunkIdsAsync(
        (string fileId, int chunkId)[] keys, string modelId, CancellationToken ct = default);
}

public sealed class ChunkForEmbedding
{
    public required string ChunkId { get; set; }
    public required string FileId { get; set; }
    public int ChunkIndex { get; set; }
    public required string Text { get; set; }
}

public sealed class EmbeddingWithChunk
{
    public required string FileId { get; set; }
    public required string FilePath { get; set; }
    public int ChunkId { get; set; }
    public required string ChunkText { get; set; }
    public required float[] Vector { get; set; }
}

/// <summary>Dense search 벡터 스캔용 경량 레코드. 파일 메타데이터 미포함.</summary>
public sealed class EmbeddingRecord
{
    public required string FileId { get; set; }
    public int ChunkId { get; set; }
    public required float[] Vector { get; set; }
}
