using Xunit;
using LocalSynapse.Pipeline.Parsing;

namespace LocalSynapse.Pipeline.Tests;

public class ContentExtractorTest : IDisposable
{
    private readonly ContentExtractor _extractor = new();
    private readonly string _tempDir;

    public ContentExtractorTest()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ls_ce_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task ExtractAsync_PlainText_ReturnsContent()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "Hello plain text content");

        var result = await _extractor.ExtractAsync(filePath, ".txt");

        Assert.True(result.Success);
        Assert.Contains("Hello plain text content", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_Html_StripsTagsReturnsText()
    {
        var filePath = Path.Combine(_tempDir, "test.html");
        await File.WriteAllTextAsync(filePath, "<html><body><h1>Title</h1><p>Body text</p></body></html>");

        var result = await _extractor.ExtractAsync(filePath, ".html");

        Assert.True(result.Success);
        Assert.Contains("Title", result.Text);
        Assert.Contains("Body text", result.Text);
        Assert.DoesNotContain("<h1>", result.Text);
        Assert.DoesNotContain("<p>", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_UnsupportedExtension_ReturnsFail()
    {
        var filePath = Path.Combine(_tempDir, "test.xyz");
        await File.WriteAllTextAsync(filePath, "some data");

        var result = await _extractor.ExtractAsync(filePath, ".xyz");

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    [InlineData(".txt")]
    public void IsSupported_ReturnsTrueForKnownExtensions(string ext)
    {
        Assert.True(_extractor.IsSupported(ext));
    }

    [Theory]
    [InlineData(".xyz")]
    [InlineData(".exe")]
    public void IsSupported_ReturnsFalseForUnknown(string ext)
    {
        Assert.False(_extractor.IsSupported(ext));
    }
}
