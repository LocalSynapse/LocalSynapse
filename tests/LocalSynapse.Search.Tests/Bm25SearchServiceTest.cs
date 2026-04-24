using Xunit;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Tests;

public sealed class Bm25SearchServiceTest : IDisposable
{
    private readonly SearchTestDb _db;
    private readonly Bm25SearchService _sut;

    public Bm25SearchServiceTest()
    {
        _db = SearchTestHelper.Create();
        _sut = new Bm25SearchService(_db.Factory, _db.ClickService);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Search_FindsRelevantFile()
    {
        var options = new SearchOptions { TopK = 10 };
        var results = _sut.Search("budget", options);

        Assert.Contains(results, r => r.FileId == SearchTestHelper.File1Id);
    }

    [Fact]
    public void Search_English_FindsRelevantFile()
    {
        var options = new SearchOptions { TopK = 10 };
        var results = _sut.Search("search engine", options);

        Assert.Contains(results, r => r.FileId == SearchTestHelper.File3Id);
    }

    [Fact]
    public void Search_ReturnsScoreDescending()
    {
        var options = new SearchOptions { TopK = 10 };
        var results = _sut.Search("contract", options);

        Assert.True(results.Count >= 2);
        for (int i = 1; i < results.Count; i++)
        {
            Assert.True(results[i - 1].Score >= results[i].Score,
                $"Score at index {i - 1} ({results[i - 1].Score}) should be >= score at index {i} ({results[i].Score})");
        }
    }

    [Fact]
    public void Search_RespectsTopK()
    {
        var options = new SearchOptions { TopK = 2 };
        var results = _sut.Search("contract", options);

        Assert.True(results.Count <= 2);
    }

    [Fact]
    public void QuickSearch_MatchesFilename()
    {
        var results = _sut.QuickSearch("contract");

        Assert.True(results.Count >= 1);
        Assert.Contains(results, r =>
            r.FileId == SearchTestHelper.File4Id || r.FileId == SearchTestHelper.File5Id);
    }

    [Fact]
    public void QuickSearch_LimitWorks()
    {
        var results = _sut.QuickSearch("contract", limit: 1);

        Assert.Single(results);
    }
}
