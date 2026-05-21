using System.Text.Json;
using LocalSynapse.Mcp.Tests.Fakes;
using LocalSynapse.Mcp.Tools;

namespace LocalSynapse.Mcp.Tests.Contract;

public class SearchFilesContractTest
{
    [Fact]
    [Trait("Category", "Contract")]
    public async Task SearchFiles_OutputHasRequiredKeys()
    {
        var fakeSearch = new FakeHybridSearch();
        var json = await LocalSynapseTools.SearchFiles(
            fakeSearch,
            query: "test",
            topK: 5,
            extensions: null,
            cancellationToken: CancellationToken.None);

        SchemaSnapshot.AssertContainsAllKeys(json, new Dictionary<string, JsonValueKind>
        {
            ["query"] = JsonValueKind.String,
            ["mode"] = JsonValueKind.String,
            ["count"] = JsonValueKind.Number,
            ["items"] = JsonValueKind.Array,
            ["stats"] = JsonValueKind.Object,
        });

        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        var firstItem = items[0];
        foreach (var key in new[] { "fileId", "filename", "path", "extension", "score", "matchSnippet", "matchSource", "modifiedAt" })
        {
            Assert.True(firstItem.TryGetProperty(key, out _), $"items[0] missing required key: {key}");
        }

        var stats = doc.RootElement.GetProperty("stats");
        foreach (var key in new[] { "bm25Count", "denseCount", "totalCandidates", "finalCount", "durationMs" })
        {
            Assert.True(stats.TryGetProperty(key, out _), $"stats missing required key: {key}");
        }
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task SearchFiles_EmptyQuery_ReturnsErrorWithKey()
    {
        var fakeSearch = new FakeHybridSearch();
        var json = await LocalSynapseTools.SearchFiles(
            fakeSearch,
            query: "",
            topK: 5,
            extensions: null,
            cancellationToken: CancellationToken.None);

        SchemaSnapshot.AssertContainsAllKeys(json, new Dictionary<string, JsonValueKind> { ["error"] = JsonValueKind.String });
    }
}
