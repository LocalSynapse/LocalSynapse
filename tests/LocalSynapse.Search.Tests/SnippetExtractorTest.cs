using Xunit;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Tests;

public sealed class SnippetExtractorTest
{
    private readonly SnippetExtractor _sut = new();

    [Fact]
    public void Extract_FindsQueryTermWindow()
    {
        var longText = new string('x', 300) + " budget allocation details " + new string('y', 300);

        var snippet = _sut.Extract(longText, ["budget"]);

        Assert.Contains("budget", snippet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_ShortContent_ReturnsAll()
    {
        var shortText = "Short text about budgets.";

        var snippet = _sut.Extract(shortText, ["budget"]);

        Assert.Equal(shortText, snippet);
    }

    [Fact]
    public void Extract_RespectsMaxLength()
    {
        var longText = new string('a', 200) + " budget " + new string('b', 200);
        var maxLength = 50;

        var snippet = _sut.Extract(longText, ["budget"], maxLength);

        // The snippet content (excluding ellipsis markers) should not exceed maxLength
        // The method adds "..." prefix/suffix, so we check total is bounded reasonably
        var contentWithoutEllipsis = snippet.Replace("...", "");
        Assert.True(contentWithoutEllipsis.Length <= maxLength,
            $"Snippet content length {contentWithoutEllipsis.Length} exceeds maxLength {maxLength}");
    }
}
