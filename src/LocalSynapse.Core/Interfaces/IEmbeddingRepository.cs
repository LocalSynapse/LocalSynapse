namespace LocalSynapse.Core.Interfaces;

public interface IEmbeddingRepository
{
    Task<int> GetEmbeddingCountAsync(string modelId, CancellationToken ct = default);
    IAsyncEnumerable<ChunkForEmbedding> EnumerateChunksMissingEmbeddingAsync(
        string modelId, int batchSize, CancellationToken ct = default);
    Task UpsertEmbeddingAsync(string fileId, int chunkId, string modelId, float[] vector,
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
