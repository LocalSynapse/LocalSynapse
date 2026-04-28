using System.Runtime.CompilerServices;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Pipeline.Interfaces;
using LocalSynapse.Pipeline.Orchestration;
using Xunit;

namespace LocalSynapse.Pipeline.Tests;

/// <summary>
/// Gate 4 TDD tests for v2.5.2 Pause/Resume/ScanNow state machine fixes (P1-1, P1-2, P1-3).
/// </summary>
public class PipelineOrchestratorStateTest
{
    private static PipelineOrchestrator CreateOrchestrator()
    {
        return new PipelineOrchestrator(
            new StubFileScanner(),
            new StubContentExtractor(),
            new StubTextChunker(),
            new StubEmbeddingService(),
            new StubFileRepository(),
            new StubChunkRepository(),
            new StubEmbeddingRepository(),
            new StubPipelineStampRepository());
    }

    [Fact]
    public void Resume_ClearsIsPaused_AndSetsIdle()
    {
        var orch = CreateOrchestrator();
        orch.Pause();
        Assert.True(orch.IsPaused);
        Assert.Equal(PipelinePhase.Paused, orch.CurrentPhase);

        orch.Resume();
        Assert.False(orch.IsPaused);
        Assert.Equal(PipelinePhase.Idle, orch.CurrentPhase);
    }

    [Fact]
    public async Task Pause_ThenRunCycle_PreservesPausedPhase()
    {
        var orch = CreateOrchestrator();
        orch.Pause();

        await orch.RunCycleAsync(CancellationToken.None);

        // P1-3: after cycle with paused state, phase must remain Paused (not reset to Idle)
        Assert.Equal(PipelinePhase.Paused, orch.CurrentPhase);
        Assert.True(orch.IsPaused);
    }

    [Fact]
    public async Task Resume_ThenRunCycle_CompletesNormally()
    {
        var orch = CreateOrchestrator();
        orch.Pause();
        orch.Resume();

        await orch.RunCycleAsync(CancellationToken.None);

        // After resume, cycle should run to completion (stubs return empty data)
        Assert.False(orch.IsPaused);
        Assert.NotEqual(PipelinePhase.Paused, orch.CurrentPhase);
    }

    // ── Minimal stubs — all interface members return empty/default ──

    private sealed class StubFileScanner : IFileScanner
    {
        public Task<ScanResult> ScanAllDrivesAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new ScanResult());
    }

    private sealed class StubContentExtractor : IContentExtractor
    {
        public Task<ExtractionResult> ExtractAsync(string filePath, string extension, CancellationToken ct = default)
            => Task.FromResult(ExtractionResult.Ok(""));
        public bool IsSupported(string extension) => false;
    }

    private sealed class StubTextChunker : ITextChunker
    {
        public IReadOnlyList<TextChunk> Chunk(string text, string sourceType = "text", string? originMeta = null)
            => Array.Empty<TextChunk>();
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public bool IsReady => false;
        public string? ActiveModelId => null;
        public int VectorDimension => 0;
        public Task InitializeAsync(string modelId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<float>());
        public Task<float[][]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<float[]>());
        public void Unload() { }
    }

    private sealed class StubFileRepository : IFileRepository
    {
        public FileMetadata UpsertFile(FileMetadata file) => file;
        public int UpsertFiles(IEnumerable<FileMetadata> files) => 0;
        public FileMetadata? GetById(string id) => null;
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
        public Dictionary<string, long> GetAllFileMtimes() => new();
        public HashSet<string> GetCloudSkippedPaths() => new();
    }

    private sealed class StubChunkRepository : IChunkRepository
    {
        public int UpsertChunks(IEnumerable<FileChunk> chunks) => 0;
        public IEnumerable<FileChunk> GetChunksForFile(string fileId) => [];
        public int DeleteChunksForFile(string fileId) => 0;
        public int GetTotalCount() => 0;
    }

    private sealed class StubEmbeddingRepository : IEmbeddingRepository
    {
        public Task<int> GetEmbeddingCountAsync(string modelId, CancellationToken ct = default)
            => Task.FromResult(0);
        public async IAsyncEnumerable<ChunkForEmbedding> EnumerateChunksMissingEmbeddingAsync(
            string modelId, int batchSize, [EnumeratorCancellation] CancellationToken ct = default)
        { await Task.CompletedTask; yield break; }
        public Task UpsertEmbeddingAsync(string fileId, int chunkId, string modelId, float[] vector, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<List<EmbeddingWithChunk>> GetEmbeddingsByFileIdsAsync(string[] fileIds, string modelId, CancellationToken ct = default)
            => Task.FromResult(new List<EmbeddingWithChunk>());
        public Task DeleteAllEmbeddingsAsync(string modelId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubPipelineStampRepository : IPipelineStampRepository
    {
        public PipelineStamps GetCurrent() => new();
        public void StampScanComplete(int totalFiles, int totalFolders, int contentSearchableFiles) { }
        public void UpdateIndexingProgress(int indexedFiles, int totalChunks) { }
        public void StampIndexingComplete(int indexedFiles, int totalChunks) { }
        public void UpdateEmbeddableChunks(int embeddableChunks) { }
        public void UpdateEmbeddingProgress(int embeddedChunks) { }
        public void StampEmbeddingComplete(int embeddableChunks, int embeddedChunks) { }
        public void StampAutoRun() { }
    }
}
