using Xunit;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Core.Repositories;
using LocalSynapse.Search;
using LocalSynapse.Search.Interfaces;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Tests;

public sealed class CascadeSearchStrategyTests : IDisposable
{
    private readonly SearchTestDb _db;
    private readonly Bm25SearchService _bm25;
    private readonly EmbeddingRepository _embeddingRepo;

    public CascadeSearchStrategyTests()
    {
        _db = SearchTestHelper.Create();
        _bm25 = new Bm25SearchService(_db.Factory, _db.ClickService);
        _embeddingRepo = new EmbeddingRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Mode_IsSmart()
    {
        var sut = MakeStrategy(new EmptyBridge());
        Assert.Equal(SearchMode.Smart, sut.Mode);
    }

    [Fact]
    public async Task SearchAsync_NoQueryEmbedding_FallsBackToBm25Ranking()
    {
        // EmptyBridge returns an empty vector — cascade falls back to BM25-only.
        // Response is labeled Fast because that's what actually ran (matches the
        // user-visible "Smart unavailable — using Fast" banner copy).
        // Output count must equal BM25 input count (no drop).
        var sut = MakeStrategy(new EmptyBridge());

        var direct = _bm25.Search("budget", new SearchOptions { TopK = 10 });
        var response = await sut.SearchAsync("budget", new SearchOptions { TopK = 10 });

        Assert.Equal(SearchMode.Fast, response.Mode);
        Assert.Equal(direct.Count, response.Items.Count);
    }

    [Fact]
    public async Task SearchAsync_EmptyBm25_ReturnsEmptyResponse()
    {
        var sut = MakeStrategy(new EmptyBridge());
        var response = await sut.SearchAsync("zzzzz_no_matches_zzzzz", new SearchOptions { TopK = 10 });

        Assert.Equal(SearchMode.Smart, response.Mode);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task SearchAsync_DoesNotCacheFallbackResponse()
    {
        // EmptyBridge takes the BM25-only fallback path. That path deliberately
        // skips caching so the next search retries the embedding call —
        // important so a transient model-load delay doesn't pin Fast results
        // for 30 seconds once the model is healthy. The bridge is therefore
        // called on every invocation, not just the first.
        var bridge = new EmptyBridge();
        var sut = MakeStrategy(bridge);

        await sut.SearchAsync("budget", new SearchOptions { TopK = 10 });
        var firstCallCount = bridge.CallCount;
        await sut.SearchAsync("budget", new SearchOptions { TopK = 10 });
        var secondCallCount = bridge.CallCount;

        Assert.Equal(1, firstCallCount);
        Assert.Equal(2, secondCallCount);
    }

    private CascadeSearchStrategy MakeStrategy(IEmbeddingBridge bridge)
    {
        // Stamp repo: report enough coverage that IsAvailable would pass, but
        // SearchAsync does not gate on IsAvailable — orchestrator does. Strategy
        // itself runs to completion regardless and falls through if no embedding.
        var stamp = new SmartReadyStampRepository();
        return new CascadeSearchStrategy(_bm25, bridge, _embeddingRepo, stamp);
    }

    private sealed class EmptyBridge : IEmbeddingBridge
    {
        public int CallCount;
        public bool IsReady => true;
        public string? ActiveModelId => "bge-m3";
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(Array.Empty<float>());
        }
    }

    private sealed class SmartReadyStampRepository : IPipelineStampRepository
    {
        public PipelineStamps GetCurrent() => new()
        {
            EmbeddableChunks = 100,
            EmbeddedChunks = 100,
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
