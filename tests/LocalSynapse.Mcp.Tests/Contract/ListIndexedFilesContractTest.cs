using System.Text.Json;
using LocalSynapse.Core.Models;
using LocalSynapse.Mcp.Tests.Fakes;
using LocalSynapse.Mcp.Tools;

namespace LocalSynapse.Mcp.Tests.Contract;

public class ListIndexedFilesContractTest
{
    private static FileMetadata MakeFile(string id, string folder, string ext) => new()
    {
        Id = id,
        Path = $"{folder}/{id}{ext}",
        Filename = $"{id}{ext}",
        Extension = ext,
        FolderPath = folder,
        SizeBytes = 1024,
        ModifiedAt = "2026-05-21T00:00:00Z",
        IndexedAt = "2026-05-21T00:00:00Z",
        MtimeMs = 0,
        IsDirectory = false,
        ExtractStatus = ExtractStatuses.Success,
        ChunkCount = 3,
    };

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ListIndexedFiles_OutputHasRequiredKeys()
    {
        var files = new[]
        {
            MakeFile("a", "/test", ".docx"),
            MakeFile("b", "/test", ".pdf"),
        };
        var json = await LocalSynapseTools.ListIndexedFiles(
            new FakeFileRepository(files),
            folder: null,
            extension: null,
            limit: 10,
            cancellationToken: CancellationToken.None);

        SchemaSnapshot.AssertContainsAllKeys(json, new Dictionary<string, JsonValueKind>
        {
            ["count"] = JsonValueKind.Number,
            ["files"] = JsonValueKind.Array,
        });

        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("files").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        var first = items[0];
        foreach (var key in new[] { "id", "filename", "path", "extension", "sizeBytes", "modifiedAt", "extractStatus", "chunkCount" })
        {
            Assert.True(first.TryGetProperty(key, out _), $"files[0] missing required key: {key}");
        }
    }
}
