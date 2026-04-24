using Xunit;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Search.Interfaces;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Tests;

public sealed class DenseSearchServiceTest
{
    private static readonly float[] QueryVector = MakeUnitVector(4, 0);
    private static readonly SearchOptions DefaultOptions = new() { TopK = 5 };

    // ── IsAvailable tests ──

    [Fact]
    public void IsAvailable_BridgeNotReady_ReturnsFalse()
    {
        var sut = CreateService(bridgeReady: false, embeddable: 100, embedded: 100);
        Assert.False(sut.IsAvailable);
    }

    [Fact]
    public void IsAvailable_LowCoverage_ReturnsFalse()
    {
        var sut = CreateService(bridgeReady: true, embeddable: 100, embedded: 20);
        Assert.False(sut.IsAvailable);
    }

    [Fact]
    public void IsAvailable_HighCoverage_ReturnsTrue()
    {
        var sut = CreateService(bridgeReady: true, embeddable: 100, embedded: 90);
        Assert.True(sut.IsAvailable);
    }

    // ── SearchAsync tests ──

    [Fact]
    public async Task SearchAsync_NoEmbeddings_ReturnsEmpty()
    {
        var sut = CreateService(bridgeReady: true, embeddable: 100, embedded: 100);
        var result = await sut.SearchAsync("test", DefaultOptions);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_ReturnsTopK_BySimilarity()
    {
        var embeddings = new List<EmbeddingRecord>
        {
            MakeRecord("f1", 0, MakeUnitVector(4, 0)),  // sim ≈ 1.0 (identical)
            MakeRecord("f2", 0, MakeUnitVector(4, 1)),  // sim ≈ 0.5
            MakeRecord("f3", 0, MakeUnitVector(4, 2)),  // sim ≈ 0.5
            MakeRecord("f4", 0, MakeOppositeVector(4)), // sim ≈ -0.5 (below threshold)
        };

        var sut = CreateService(
            bridgeReady: true, embeddable: 100, embedded: 100,
            embeddings: embeddings);

        var result = await sut.SearchAsync("test", new SearchOptions { TopK = 3 });

        Assert.True(result.Count >= 1);
        Assert.Equal("f1", result[0].FileId); // highest similarity
        Assert.True(result[0].Score > 0.9);
    }

    [Fact]
    public async Task SearchAsync_FiltersLowSimilarity()
    {
        var embeddings = new List<EmbeddingRecord>
        {
            MakeRecord("f1", 0, MakeOrthogonalVector(4)), // sim ≈ 0.0
        };

        var sut = CreateService(
            bridgeReady: true, embeddable: 100, embedded: 100,
            embeddings: embeddings);

        var result = await sut.SearchAsync("test", DefaultOptions);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_DuplicateScores_NoCrash()
    {
        // PriorityQueue handles duplicate priorities safely
        var embeddings = new List<EmbeddingRecord>
        {
            MakeRecord("f1", 0, MakeUnitVector(4, 0)),
            MakeRecord("f2", 0, MakeUnitVector(4, 0)), // identical vector → same score
            MakeRecord("f3", 0, MakeUnitVector(4, 0)), // identical vector → same score
        };

        var sut = CreateService(
            bridgeReady: true, embeddable: 100, embedded: 100,
            embeddings: embeddings);

        var result = await sut.SearchAsync("test", DefaultOptions);
        Assert.Equal(3, result.Count);
    }

    // ── Helpers ──

    private static DenseSearchService CreateService(
        bool bridgeReady, int embeddable, int embedded,
        List<EmbeddingRecord>? embeddings = null)
    {
        return new DenseSearchService(
            new FakeEmbeddingBridge(bridgeReady),
            new FakeEmbeddingRepository(embeddings ?? []),
            new FakeFileRepository(),
            new FakeChunkRepository(),
            new FakeStampRepository(embeddable, embedded));
    }

    private static EmbeddingRecord MakeRecord(string fileId, int chunkId, float[] vector)
        => new() { FileId = fileId, ChunkId = chunkId, Vector = vector };

    private static float[] MakeUnitVector(int dim, int hotIndex)
    {
        var v = new float[dim];
        v[hotIndex % dim] = 1f;
        return v;
    }

    private static float[] MakeOppositeVector(int dim)
    {
        var v = new float[dim];
        v[0] = -1f;
        return v;
    }

    private static float[] MakeOrthogonalVector(int dim)
    {
        // All zeros → cosine similarity = 0
        return new float[dim];
    }

    // ── Fakes ──

    private sealed class FakeEmbeddingBridge : IEmbeddingBridge
    {
        public FakeEmbeddingBridge(bool isReady) => IsReady = isReady;
        public bool IsReady { get; }
        public string? ActiveModelId => "bge-m3";
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
            => Task.FromResult(QueryVector);
    }

    private sealed class FakeEmbeddingRepository : IEmbeddingRepository
    {
        private readonly List<EmbeddingRecord> _records;
        public FakeEmbeddingRepository(List<EmbeddingRecord> records) => _records = records;

        public async IAsyncEnumerable<EmbeddingRecord> EnumerateAllEmbeddingsAsync(
            string modelId, int batchSize = 500,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var r in _records)
            {
                await Task.CompletedTask;
                yield return r;
            }
        }

        public Task<int> GetEmbeddingCountAsync(string modelId, CancellationToken ct = default)
            => Task.FromResult(_records.Count);
        public async IAsyncEnumerable<ChunkForEmbedding> EnumerateChunksMissingEmbeddingAsync(
            string modelId, int batchSize,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public Task UpsertEmbeddingAsync(string fileId, int chunkId, string modelId, float[] vector,
            CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<EmbeddingWithChunk>> GetEmbeddingsByFileIdsAsync(
            string[] fileIds, string modelId, CancellationToken ct = default)
            => Task.FromResult(new List<EmbeddingWithChunk>());
        public Task DeleteAllEmbeddingsAsync(string modelId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeFileRepository : IFileRepository
    {
        public FileMetadata? GetById(string id) => new()
        {
            Id = id, Path = $"/test/{id}.txt", Filename = $"{id}.txt",
            Extension = ".txt", ModifiedAt = "2026-01-01", IndexedAt = "2026-01-01",
            FolderPath = "/test"
        };

        public FileMetadata UpsertFile(FileMetadata file) => file;
        public int UpsertFiles(IEnumerable<FileMetadata> files) => 0;
        public FileMetadata? GetByPath(string path) => null;
        public IEnumerable<string> ListPathsUnderFolder(string folderPath) => [];
        public int DeleteByPaths(IEnumerable<string> paths) => 0;
        public void UpdateExtractStatus(string fileId, string status, string? errorCode = null) { }
        public void BatchUpdateExtractStatus(IEnumerable<(string fileId, string status)> updates) { }
        public IEnumerable<FileMetadata> GetFilesPendingExtraction(int limit = 1000) => [];
        public int CountPendingExtraction() => 0;
        public int CountIndexedContentSearchableFiles() => 0;
        public (int files, int folders, int contentSearchable) CountScanStampTotals() => (0, 0, 0);
        public (int cloud, int tooLarge, int encrypted, int parseError) CountSkippedByCategory() => (0, 0, 0, 0);
        public IEnumerable<FileMetadata> SearchByFilename(string query, int limit = 20) => [];
        public Task<string?> GetFilePathByFrnAsync(long frn, string drivePrefix) => Task.FromResult<string?>(null);
        public Task UpdateMetadataAsync(string filePath, long fileSize, DateTime modifiedAt) => Task.CompletedTask;
        public Task DeleteByPathAsync(string filePath) => Task.CompletedTask;
        public Task<bool> ExistsByPathAsync(string filePath) => Task.FromResult(false);
        public IReadOnlyList<FileMetadata> ListFilesUnderFolder(string? folder, string? extension, int limit) => [];
        public Dictionary<string, long> GetAllFileMtimes() => new();
    }

    private sealed class FakeChunkRepository : IChunkRepository
    {
        public int UpsertChunks(IEnumerable<FileChunk> chunks) => 0;
        public IEnumerable<FileChunk> GetChunksForFile(string fileId) =>
        [
            new FileChunk
            {
                Id = "c1", FileId = fileId, ChunkIndex = 0,
                Text = "Test chunk text", SourceType = "text",
                ContentHash = "abc", CreatedAt = "2026-01-01"
            }
        ];
        public int DeleteChunksForFile(string fileId) => 0;
        public int GetTotalCount() => 0;
    }

    private sealed class FakeStampRepository : IPipelineStampRepository
    {
        private readonly int _embeddable;
        private readonly int _embedded;
        public FakeStampRepository(int embeddable, int embedded)
        {
            _embeddable = embeddable;
            _embedded = embedded;
        }

        public PipelineStamps GetCurrent() => new()
        {
            EmbeddableChunks = _embeddable,
            EmbeddedChunks = _embedded
        };

        public void StampScanComplete(int totalFiles, int totalFolders, int contentSearchableFiles) { }
        public void UpdateIndexingProgress(int indexedFiles, int totalChunks) { }
        public void StampIndexingComplete(int indexedFiles, int totalChunks) { }
        public void UpdateEmbeddableChunks(int embeddableChunks) { }
        public void UpdateEmbeddingProgress(int embeddedChunks) { }
        public void StampEmbeddingComplete(int embeddableChunks, int embeddedChunks) { }
        public void StampAutoRun() { }
    }
}
