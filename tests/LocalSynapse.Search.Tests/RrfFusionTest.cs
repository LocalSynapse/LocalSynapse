using Xunit;
using LocalSynapse.Core.Models;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Tests;

public sealed class RrfFusionTest
{
    [Fact]
    public void Combine_MergesResults()
    {
        var bm25 = new List<Bm25Hit>
        {
            MakeBm25("f1", "file1.txt", 3.0),
            MakeBm25("f2", "file2.txt", 2.0),
            MakeBm25("f3", "file3.txt", 1.0),
        };
        var dense = new List<DenseHit>
        {
            MakeDense("f4", "file4.txt", 0.9),
            MakeDense("f5", "file5.txt", 0.8),
            MakeDense("f6", "file6.txt", 0.7),
        };
        var options = new SearchOptions { TopK = 20 };

        var result = RrfFusion.Combine(bm25, dense, options);

        Assert.Equal(6, result.Count);
    }

    [Fact]
    public void Combine_BothListsContainSameFile_HigherScore()
    {
        var bm25 = new List<Bm25Hit>
        {
            MakeBm25("shared", "shared.txt", 3.0),
            MakeBm25("bm25only", "bm25only.txt", 2.0),
        };
        var dense = new List<DenseHit>
        {
            MakeDense("shared", "shared.txt", 0.9),
            MakeDense("denseonly", "denseonly.txt", 0.8),
        };
        var options = new SearchOptions { TopK = 20 };

        var result = RrfFusion.Combine(bm25, dense, options);

        var sharedHit = result.First(h => h.FileId == "shared");
        var bm25OnlyHit = result.First(h => h.FileId == "bm25only");

        // Shared file gets RRF score from both lists, so it should be higher
        Assert.True(sharedHit.HybridScore > bm25OnlyHit.HybridScore);
    }

    [Fact]
    public void Combine_EmptyDense_ReturnsBm25Only()
    {
        var bm25 = new List<Bm25Hit>
        {
            MakeBm25("f1", "file1.txt", 3.0),
            MakeBm25("f2", "file2.txt", 2.0),
        };
        var dense = new List<DenseHit>();
        var options = new SearchOptions { TopK = 20 };

        var result = RrfFusion.Combine(bm25, dense, options);

        Assert.Equal(2, result.Count);
        Assert.All(result, h => Assert.Equal(0, h.DenseScore));
    }

    [Fact]
    public void Combine_RespectsTopK()
    {
        var bm25 = new List<Bm25Hit>
        {
            MakeBm25("f1", "file1.txt", 3.0),
            MakeBm25("f2", "file2.txt", 2.0),
            MakeBm25("f3", "file3.txt", 1.0),
        };
        var dense = new List<DenseHit>
        {
            MakeDense("f4", "file4.txt", 0.9),
            MakeDense("f5", "file5.txt", 0.8),
        };
        var options = new SearchOptions { TopK = 3 };

        var result = RrfFusion.Combine(bm25, dense, options);

        Assert.True(result.Count <= 3);
    }

    private static Bm25Hit MakeBm25(string fileId, string filename, double score) => new()
    {
        FileId = fileId,
        Filename = filename,
        Path = $"/test/{filename}",
        Extension = ".txt",
        FolderPath = "/test",
        Score = score,
        ModifiedAt = DateTime.UtcNow.ToString("o"),
    };

    private static DenseHit MakeDense(string fileId, string filename, double score) => new()
    {
        FileId = fileId,
        Content = "test content",
        Score = score,
        Path = $"/test/{filename}",
        Filename = filename,
        Extension = ".txt",
        ModifiedAt = DateTime.UtcNow.ToString("o"),
    };
}
