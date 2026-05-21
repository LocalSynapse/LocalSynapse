using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.Search.Tests.Fakes;

/// <summary>
/// IEmbeddingRepository fake. Cascade rerank only needs
/// GetEmbeddingsByChunkIdsAsync; the rest throw to surface unintended use.
/// </summary>
internal sealed class FakeEmbeddingRepository : IEmbeddingRepository
{
    private readonly Dictionary<(string fileId, int chunkId), float[]> _vectors;

    public FakeEmbeddingRepository(Dictionary<(string fileId, int chunkId), float[]>? seed = null)
        => _vectors = seed ?? new();

    public Task<List<EmbeddingRecord>> GetEmbeddingsByChunkIdsAsync(
        (string fileId, int chunkId)[] keys, string modelId, CancellationToken ct = default)
    {
        var result = new List<EmbeddingRecord>();
        foreach (var key in keys)
        {
            if (_vectors.TryGetValue(key, out var v))
            {
                result.Add(new EmbeddingRecord
                {
                    FileId = key.fileId,
                    ChunkId = key.chunkId,
                    Vector = v,
                });
            }
        }
        return Task.FromResult(result);
    }

    public Task<int> GetEmbeddingCountAsync(string modelId, CancellationToken ct = default)
        => throw new NotSupportedException();
    public IAsyncEnumerable<ChunkForEmbedding> EnumerateChunksMissingEmbeddingAsync(
        string modelId, int batchSize, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task UpsertEmbeddingAsync(string fileId, int chunkId, string modelId, float[] vector,
        CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<int> BulkUpsertEmbeddingsAsync(
        IReadOnlyList<(string fileId, int chunkId, string modelId, float[] vector)> items,
        CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<List<EmbeddingWithChunk>> GetEmbeddingsByFileIdsAsync(
        string[] fileIds, string modelId, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task DeleteAllEmbeddingsAsync(string modelId, CancellationToken ct = default)
        => throw new NotSupportedException();
    public IAsyncEnumerable<EmbeddingRecord> EnumerateAllEmbeddingsAsync(
        string modelId, int batchSize = 500, CancellationToken ct = default)
        => throw new NotSupportedException();
}
