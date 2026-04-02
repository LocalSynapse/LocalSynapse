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
