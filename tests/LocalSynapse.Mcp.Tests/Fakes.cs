using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Search;
using LocalSynapse.Search.Interfaces;

namespace LocalSynapse.Mcp.Tests.Fakes;

/// <summary>
/// IHybridSearch 3 멤버: CurrentMode, SearchAsync, QuickSearchAsync.
/// 결정적 SearchResponse — Mode=Smart 고정 (BuildBm25OnlyResponse 의도 정합).
/// </summary>
internal sealed class FakeHybridSearch : IHybridSearch
{
    public SearchMode CurrentMode => SearchMode.Smart;

    public Task<SearchResponse> SearchAsync(string query, SearchOptions options, CancellationToken ct = default)
        => Task.FromResult(BuildDeterministicResponse(query));

    public Task<SearchResponse> QuickSearchAsync(string query, int limit = 20, CancellationToken ct = default)
        => Task.FromResult(BuildDeterministicResponse(query));

    private static SearchResponse BuildDeterministicResponse(string query) => new()
    {
        Query = query,
        Mode = SearchMode.Smart,
        Items = new List<HybridHit>
        {
            new()
            {
                FileId = "test-file-1",
                Filename = "report.docx",
                Path = "/test/report.docx",
                Extension = ".docx",
                FolderPath = "/test",
                HybridScore = 0.85,
                Bm25Score = 0.7,
                DenseScore = 0.6,
                MatchedTerms = new List<string> { "query" },
                ModifiedAt = "2026-05-21T00:00:00Z",
                IsDirectory = false,
                MatchSource = MatchSource.Content,
                MatchSnippet = "matching ...query... text",
            },
        },
        Stats = new SearchStats
        {
            Bm25Count = 1,
            DenseCount = 1,
            TotalCandidates = 1,
            FinalCount = 1,
            DurationMs = 5,
        },
    };
}

/// <summary>
/// IFileRepository (21 멤버) fake. Contract test가 호출하는 GetById / ListFilesUnderFolder만 의미 있는 구현.
/// 나머지 19개는 NotSupportedException — 호출 시 즉시 빨강으로 누락 검증 가능.
/// </summary>
internal sealed class FakeFileRepository : IFileRepository
{
    private readonly Dictionary<string, FileMetadata> _files;

    public FakeFileRepository(params FileMetadata[] seed)
        => _files = seed.ToDictionary(f => f.Id, StringComparer.Ordinal);

    public FileMetadata? GetById(string id) => _files.TryGetValue(id, out var f) ? f : null;

    public IReadOnlyList<FileMetadata> ListFilesUnderFolder(string? folder, string? extension, int limit)
        => _files.Values
            .Where(f => folder is null || f.FolderPath == folder)
            .Where(f => extension is null || f.Extension == extension)
            .Take(limit)
            .ToList();

    // 나머지 19 멤버 — IFileRepository.cs 실측 기반
    public FileMetadata UpsertFile(FileMetadata file) => throw new NotSupportedException();
    public int UpsertFiles(IEnumerable<FileMetadata> files) => throw new NotSupportedException();
    public FileMetadata? GetByPath(string path) => throw new NotSupportedException();
    public IEnumerable<string> ListPathsUnderFolder(string folderPath) => throw new NotSupportedException();
    public int DeleteByPaths(IEnumerable<string> paths) => throw new NotSupportedException();
    public void UpdateExtractStatus(string fileId, string status, string? errorCode = null) => throw new NotSupportedException();
    public void BatchUpdateExtractStatus(IEnumerable<(string fileId, string status)> updates) => throw new NotSupportedException();
    public IEnumerable<FileMetadata> GetFilesPendingExtraction(int limit = 1000) => throw new NotSupportedException();
    public int CountPendingExtraction() => throw new NotSupportedException();
    public int CountIndexedContentSearchableFiles() => throw new NotSupportedException();
    public (int files, int folders, int contentSearchable) CountScanStampTotals() => throw new NotSupportedException();
    public (int cloud, int tooLarge, int encrypted, int parseError) CountSkippedByCategory() => throw new NotSupportedException();
    public IEnumerable<FileMetadata> SearchByFilename(string query, int limit = 20) => throw new NotSupportedException();
    public Task<string?> GetFilePathByFrnAsync(long frn, string drivePrefix) => throw new NotSupportedException();
    public Task UpdateMetadataAsync(string filePath, long fileSize, DateTime modifiedAt) => throw new NotSupportedException();
    public Task DeleteByPathAsync(string filePath) => throw new NotSupportedException();
    public Task<bool> ExistsByPathAsync(string filePath) => throw new NotSupportedException();
    public Dictionary<string, long> GetAllFileMtimes() => throw new NotSupportedException();
    public HashSet<string> GetCloudSkippedPaths() => throw new NotSupportedException();
}

/// <summary>IChunkRepository (4 멤버) fake. Contract test는 GetChunksForFile만 사용.</summary>
internal sealed class FakeChunkRepository : IChunkRepository
{
    private readonly Dictionary<string, List<FileChunk>> _byFileId;

    public FakeChunkRepository(Dictionary<string, List<FileChunk>>? seed = null)
        => _byFileId = seed ?? new Dictionary<string, List<FileChunk>>(StringComparer.Ordinal);

    public IEnumerable<FileChunk> GetChunksForFile(string fileId)
        => _byFileId.TryGetValue(fileId, out var c) ? c : Array.Empty<FileChunk>();

    public int UpsertChunks(IEnumerable<FileChunk> chunks) => throw new NotSupportedException();
    public int DeleteChunksForFile(string fileId) => throw new NotSupportedException();
    public int GetTotalCount() => throw new NotSupportedException();
}

/// <summary>
/// IPipelineStampRepository (8 멤버) fake.
/// GetCurrent() 반환은 non-nullable PipelineStamps. Empty stamp는 new PipelineStamps() (모든 필드 default).
/// </summary>
internal sealed class FakePipelineStampRepository : IPipelineStampRepository
{
    private readonly PipelineStamps _stamp;

    public FakePipelineStampRepository(PipelineStamps? stamp = null)
        => _stamp = stamp ?? new PipelineStamps();

    public PipelineStamps GetCurrent() => _stamp;

    public void StampScanComplete(int totalFiles, int totalFolders, int contentSearchableFiles) => throw new NotSupportedException();
    public void UpdateIndexingProgress(int indexedFiles, int totalChunks) => throw new NotSupportedException();
    public void StampIndexingComplete(int indexedFiles, int totalChunks) => throw new NotSupportedException();
    public void UpdateEmbeddableChunks(int embeddableChunks) => throw new NotSupportedException();
    public void UpdateEmbeddingProgress(int embeddedChunks) => throw new NotSupportedException();
    public void StampEmbeddingComplete(int embeddableChunks, int embeddedChunks) => throw new NotSupportedException();
    public void StampAutoRun() => throw new NotSupportedException();
}
