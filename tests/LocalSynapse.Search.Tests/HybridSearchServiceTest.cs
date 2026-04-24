using Xunit;
using LocalSynapse.Core.Models;
using LocalSynapse.Search.Interfaces;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Tests;

public sealed class HybridSearchServiceTest : IDisposable
{
    private readonly SearchTestDb _db;
    private readonly HybridSearchService _sut;

    public HybridSearchServiceTest()
    {
        _db = SearchTestHelper.Create();
        var bm25 = new Bm25SearchService(_db.Factory, _db.ClickService);
        _sut = new HybridSearchService(bm25, new FakeDenseSearch());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SearchAsync_FtsOnlyMode_WhenDenseUnavailable()
    {
        var options = new SearchOptions { TopK = 10 };
        var response = await _sut.SearchAsync("budget", options);

        Assert.Equal(SearchMode.FtsOnly, response.Mode);
    }

    [Fact]
    public async Task SearchAsync_ReturnsSearchResponse()
    {
        var options = new SearchOptions { TopK = 10 };
        var response = await _sut.SearchAsync("budget", options);

        Assert.Equal("budget", response.Query);
        Assert.NotNull(response.Items);
        Assert.True(response.Items.Count > 0);
        Assert.NotNull(response.Stats);
        Assert.True(response.Stats.Bm25Count > 0);
    }

    [Fact]
    public async Task QuickSearchAsync_ReturnsResults()
    {
        var response = await _sut.QuickSearchAsync("contract");

        Assert.True(response.Items.Count > 0);
        Assert.Contains(response.Items, h =>
            h.FileId == SearchTestHelper.File4Id || h.FileId == SearchTestHelper.File5Id);
    }

    private sealed class FakeDenseSearch : IDenseSearch
    {
        public bool IsAvailable => false;

        public Task<IReadOnlyList<DenseHit>> SearchAsync(
            string query, SearchOptions options, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<DenseHit>>([]);
        }
    }
}
