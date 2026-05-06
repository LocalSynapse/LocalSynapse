using Xunit;
using LocalSynapse.Pipeline.Chunking;

namespace LocalSynapse.Pipeline.Tests;

public class TextChunkerTest
{
    private readonly TextChunker _chunker = new();

    [Fact]
    public void Chunk_EmptyText_ReturnsEmpty()
    {
        var result = _chunker.Chunk("");
        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        var result = _chunker.Chunk("Hello, world!");
        Assert.Single(result);
        Assert.Equal("Hello, world!", result[0].Text);
    }

    [Fact]
    public void Chunk_LongText_SplitsCorrectly()
    {
        // Build text with multiple paragraphs totaling ~2000 chars
        var paragraphs = Enumerable.Range(0, 10)
            .Select(i => new string('A', 200))
            .ToArray();
        var text = string.Join("\n\n", paragraphs);

        var result = _chunker.Chunk(text);
        Assert.True(result.Count >= 2, $"Expected 2+ chunks but got {result.Count}");
    }

    [Fact]
    public void Chunk_ExceedsMaxChunks_CapsAt500()
    {
        // 600 paragraphs, each short enough to be its own chunk
        // but we need them to not merge. Make each ~990 chars so each is one chunk.
        var paragraphs = Enumerable.Range(0, 600)
            .Select(i => new string('X', 990))
            .ToArray();
        var text = string.Join("\n\n", paragraphs);

        var result = _chunker.Chunk(text);
        Assert.True(result.Count <= 500, $"Expected max 500 chunks but got {result.Count}");
    }

    [Fact]
    public void Chunk_SingleHugeParagraph_ForceSplits()
    {
        // Single paragraph of 3000 chars (no paragraph breaks)
        var text = new string('Z', 3000);

        var result = _chunker.Chunk(text);
        Assert.True(result.Count >= 3, $"Expected 3+ chunks for 3000 chars but got {result.Count}");
    }

    [Fact]
    public void Chunk_ContentHashIsDeterministic()
    {
        var text = "Deterministic hash test content.";
        var result1 = _chunker.Chunk(text);
        var result2 = _chunker.Chunk(text);

        Assert.Equal(result1[0].ContentHash, result2[0].ContentHash);
    }
}
