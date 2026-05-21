using System.Text.Json;
using LocalSynapse.Core.Models;
using LocalSynapse.Mcp.Tests.Fakes;
using LocalSynapse.Mcp.Tools;

namespace LocalSynapse.Mcp.Tests.Contract;

public class GetPipelineStatusContractTest
{
    [Fact]
    [Trait("Category", "Contract")]
    public async Task GetPipelineStatus_Available_HasRequiredKeys()
    {
        var stamp = new PipelineStamps
        {
            TotalFiles = 10,
            IndexedFiles = 8,
            TotalChunks = 32,
            EmbeddedChunks = 24,
            ScanCompletedAt = "2026-05-21T00:00:00Z",
            IndexingCompletedAt = "2026-05-21T00:01:00Z",
            EmbeddingCompletedAt = "2026-05-21T00:02:00Z",
        };
        var json = await LocalSynapseTools.GetPipelineStatus(
            new FakePipelineStampRepository(stamp),
            cancellationToken: CancellationToken.None);

        SchemaSnapshot.AssertContainsAllKeys(json, new Dictionary<string, JsonValueKind>
        {
            ["totalFiles"] = JsonValueKind.Number,
            ["indexedFiles"] = JsonValueKind.Number,
            ["totalChunks"] = JsonValueKind.Number,
            ["embeddedChunks"] = JsonValueKind.Number,
            ["lastScanAt"] = JsonValueKind.String,
            ["lastIndexAt"] = JsonValueKind.String,
        });

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("scanComplete", out var sc));
        Assert.True(sc.ValueKind == JsonValueKind.True || sc.ValueKind == JsonValueKind.False);
        Assert.True(doc.RootElement.TryGetProperty("embeddingComplete", out var ec));
        Assert.True(ec.ValueKind == JsonValueKind.True || ec.ValueKind == JsonValueKind.False);
    }
}
