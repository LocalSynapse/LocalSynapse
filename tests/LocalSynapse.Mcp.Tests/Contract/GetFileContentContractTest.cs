using System.Text.Json;
using LocalSynapse.Core.Models;
using LocalSynapse.Mcp.Tests.Fakes;
using LocalSynapse.Mcp.Tools;

namespace LocalSynapse.Mcp.Tests.Contract;

public class GetFileContentContractTest
{
    private static FileMetadata MakeFile(string id) => new()
    {
        Id = id,
        Path = "/test/sample.docx",
        Filename = "sample.docx",
        Extension = ".docx",
        FolderPath = "/test",
        SizeBytes = 1024,
        ModifiedAt = "2026-05-21T00:00:00Z",
        IndexedAt = "2026-05-21T00:00:00Z",
        MtimeMs = 0,
        IsDirectory = false,
        ExtractStatus = ExtractStatuses.Success,
    };

    [Fact]
    [Trait("Category", "Contract")]
    public async Task GetFileContent_Happy_HasRequiredKeys()
    {
        var file = MakeFile("file-1");
        var chunks = new Dictionary<string, List<FileChunk>>
        {
            ["file-1"] = new()
            {
                new() { Id = "c1", FileId = "file-1", ChunkIndex = 0, Text = "hello content", SourceType = ChunkSourceTypes.Text, ContentHash = "h", CreatedAt = "2026-05-21T00:00:00Z" },
            },
        };
        var json = await LocalSynapseTools.GetFileContent(
            new FakeFileRepository(file),
            new FakeChunkRepository(chunks),
            fileId: "file-1",
            cancellationToken: CancellationToken.None);

        SchemaSnapshot.AssertContainsAllKeys(json, new Dictionary<string, JsonValueKind>
        {
            ["content"] = JsonValueKind.String,
            ["path"] = JsonValueKind.String,
            ["filename"] = JsonValueKind.String,
            ["extension"] = JsonValueKind.String,
            ["chunksAvailable"] = JsonValueKind.Number,
        });
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("truncated", out var truncated));
        Assert.True(truncated.ValueKind == JsonValueKind.True || truncated.ValueKind == JsonValueKind.False);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task GetFileContent_ChunksEmpty_HasRequiredKeys()
    {
        var file = MakeFile("file-empty");
        var json = await LocalSynapseTools.GetFileContent(
            new FakeFileRepository(file),
            new FakeChunkRepository(),
            fileId: "file-empty",
            cancellationToken: CancellationToken.None);

        SchemaSnapshot.AssertContainsAllKeys(json, new Dictionary<string, JsonValueKind>
        {
            ["content"] = JsonValueKind.String,
            ["path"] = JsonValueKind.String,
            ["filename"] = JsonValueKind.String,
            ["chunksAvailable"] = JsonValueKind.Number,
        });
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task GetFileContent_NotFound_ReturnsErrorKey()
    {
        var json = await LocalSynapseTools.GetFileContent(
            new FakeFileRepository(),
            new FakeChunkRepository(),
            fileId: "missing",
            cancellationToken: CancellationToken.None);

        SchemaSnapshot.AssertContainsAllKeys(json, new Dictionary<string, JsonValueKind> { ["error"] = JsonValueKind.String });
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task GetFileContent_EmptyFileId_ReturnsErrorKey()
    {
        var json = await LocalSynapseTools.GetFileContent(
            new FakeFileRepository(),
            new FakeChunkRepository(),
            fileId: "",
            cancellationToken: CancellationToken.None);

        SchemaSnapshot.AssertContainsAllKeys(json, new Dictionary<string, JsonValueKind> { ["error"] = JsonValueKind.String });
    }
}
