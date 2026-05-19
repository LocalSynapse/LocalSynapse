using Xunit;
using LocalSynapse.Core.Models;
using LocalSynapse.Search;
using LocalSynapse.Search.Interfaces;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Tests;

public sealed class Bm25SearchStrategyTests : IDisposable
{
    private readonly SearchTestDb _db;
    private readonly Bm25SearchStrategy _sut;

    public Bm25SearchStrategyTests()
    {
        _db = SearchTestHelper.Create();
        var bm25 = new Bm25SearchService(_db.Factory, _db.ClickService);
        _sut = new Bm25SearchStrategy(bm25);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Mode_IsFast()
    {
        Assert.Equal(SearchMode.Fast, _sut.Mode);
    }

    [Fact]
    public async Task SearchAsync_ReturnsFastModeResponse()
    {
        var response = await _sut.SearchAsync("budget", new SearchOptions { TopK = 10 });

        Assert.Equal(SearchMode.Fast, response.Mode);
        Assert.Equal("budget", response.Query);
        Assert.True(response.Items.Count > 0);
        Assert.Equal(response.Items.Count, response.Stats.Bm25Count);
        Assert.Equal(0, response.Stats.DenseCount);
    }

    [Fact]
    public async Task SearchAsync_PreservesBm25Ranking()
    {
        // Strategy is a thin wrapper — output count and ordering should match
        // the BM25 service it wraps when given identical input.
        var bm25Direct = new Bm25SearchService(_db.Factory, _db.ClickService);
        var direct = bm25Direct.Search("budget", new SearchOptions { TopK = 10 });
        var response = await _sut.SearchAsync("budget", new SearchOptions { TopK = 10 });

        Assert.Equal(direct.Count, response.Items.Count);
        for (int i = 0; i < direct.Count; i++)
        {
            Assert.Equal(direct[i].FileId, response.Items[i].FileId);
            // Float tolerance: two BM25 calls compute the same score but accumulate
            // boost multipliers in slightly different orders depending on instance state.
            Assert.Equal(direct[i].Score, response.Items[i].Bm25Score, precision: 4);
        }
    }
}
