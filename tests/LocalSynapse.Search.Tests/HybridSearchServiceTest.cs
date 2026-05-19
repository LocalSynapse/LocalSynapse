using Xunit;
using LocalSynapse.Core.Models;
using LocalSynapse.Search;
using LocalSynapse.Search.Interfaces;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Tests;

public sealed class HybridSearchServiceTest : IDisposable
{
    private readonly SearchTestDb _db;
    private readonly Bm25SearchService _bm25;

    public HybridSearchServiceTest()
    {
        _db = SearchTestHelper.Create();
        _bm25 = new Bm25SearchService(_db.Factory, _db.ClickService);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SearchAsync_DispatchesToFastStrategy_WhenSettingsSayFast()
    {
        _db.Settings.SetSearchMode("fast");
        var fast = new RecordingStrategy(SearchMode.Fast);
        var smart = new RecordingStrategy(SearchMode.Smart);
        var sut = new HybridSearchService(new ISearchStrategy[] { fast, smart }, _bm25, _db.Settings);

        var response = await sut.SearchAsync("budget", new SearchOptions { TopK = 10 });

        Assert.Equal(1, fast.CallCount);
        Assert.Equal(0, smart.CallCount);
        Assert.Equal(SearchMode.Fast, response.Mode);
    }

    [Fact]
    public async Task SearchAsync_DispatchesToSmartStrategy_WhenSettingsSaySmart()
    {
        _db.Settings.SetSearchMode("smart");
        var fast = new RecordingStrategy(SearchMode.Fast);
        var smart = new RecordingStrategy(SearchMode.Smart);
        var sut = new HybridSearchService(new ISearchStrategy[] { fast, smart }, _bm25, _db.Settings);

        var response = await sut.SearchAsync("budget", new SearchOptions { TopK = 10 });

        Assert.Equal(0, fast.CallCount);
        Assert.Equal(1, smart.CallCount);
        Assert.Equal(SearchMode.Smart, response.Mode);
    }

    [Fact]
    public async Task SearchAsync_FallsBackToFast_WhenRequestedModeNotRegistered()
    {
        _db.Settings.SetSearchMode("smart");
        var fast = new RecordingStrategy(SearchMode.Fast);
        // Only Fast registered — Smart not present.
        var sut = new HybridSearchService(new ISearchStrategy[] { fast }, _bm25, _db.Settings);

        var response = await sut.SearchAsync("budget", new SearchOptions { TopK = 10 });

        Assert.Equal(1, fast.CallCount);
        Assert.Equal(SearchMode.Fast, response.Mode);
    }

    [Fact]
    public async Task QuickSearchAsync_BypassesStrategyDispatch()
    {
        _db.Settings.SetSearchMode("smart");
        var fast = new RecordingStrategy(SearchMode.Fast);
        var smart = new RecordingStrategy(SearchMode.Smart);
        var sut = new HybridSearchService(new ISearchStrategy[] { fast, smart }, _bm25, _db.Settings);

        var response = await sut.QuickSearchAsync("contract");

        Assert.Equal(0, fast.CallCount);
        Assert.Equal(0, smart.CallCount);
        Assert.True(response.Items.Count > 0);
        Assert.Contains(response.Items, h =>
            h.FileId == SearchTestHelper.File4Id || h.FileId == SearchTestHelper.File5Id);
    }

    [Fact]
    public async Task CurrentMode_ReflectsMostRecentlyDispatchedStrategy()
    {
        _db.Settings.SetSearchMode("smart");
        var fast = new RecordingStrategy(SearchMode.Fast);
        var smart = new RecordingStrategy(SearchMode.Smart);
        var sut = new HybridSearchService(new ISearchStrategy[] { fast, smart }, _bm25, _db.Settings);

        await sut.SearchAsync("x", new SearchOptions { TopK = 5 });
        Assert.Equal(SearchMode.Smart, sut.CurrentMode);

        _db.Settings.SetSearchMode("fast");
        await sut.SearchAsync("y", new SearchOptions { TopK = 5 });
        Assert.Equal(SearchMode.Fast, sut.CurrentMode);
    }

    /// <summary>Records invocation count and returns a Mode-matching empty response.</summary>
    private sealed class RecordingStrategy : ISearchStrategy
    {
        public SearchMode Mode { get; }
        public int CallCount;

        public RecordingStrategy(SearchMode mode) { Mode = mode; }

        public Task<SearchResponse> SearchAsync(string query, SearchOptions options, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(new SearchResponse
            {
                Query = query,
                Mode  = Mode,
                Items = new List<HybridHit>(),
                Stats = new SearchStats(),
            });
        }
    }
}
