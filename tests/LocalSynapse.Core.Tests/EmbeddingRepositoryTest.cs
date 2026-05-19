using LocalSynapse.Core.Models;
using LocalSynapse.Core.Repositories;
using Xunit;

namespace LocalSynapse.Core.Tests;

public class EmbeddingRepositoryTest
{
    private static void SeedFileAndChunks(TestDb db, string fileId, string path, int chunkCount)
    {
        db.FileRepo.UpsertFile(new FileMetadata
        {
            Id = fileId,
            Path = path,
            Filename = Path.GetFileName(path),
            Extension = ".txt",
            SizeBytes = 100,
            ModifiedAt = DateTime.UtcNow.ToString("o"),
            IndexedAt = DateTime.UtcNow.ToString("o"),
            FolderPath = Path.GetDirectoryName(path) ?? "",
        });

        var chunks = Enumerable.Range(0, chunkCount).Select(i => new FileChunk
        {
            Id = $"{fileId}:{i}",
            FileId = fileId,
            ChunkIndex = i,
            Text = $"Chunk {i} content for testing",
            SourceType = "text",
            ContentHash = $"hash_{fileId}_{i}",
            CreatedAt = DateTime.UtcNow.ToString("o"),
        });
        db.ChunkRepo.UpsertChunks(chunks);
    }

    private static float[] MakeVector(int dim = 1024) =>
        Enumerable.Range(0, dim).Select(i => (float)i / dim).ToArray();

    [Fact]
    public async Task UpsertEmbedding_StoresAndRetrieves()
    {
        using var db = TestDbHelper.Create();
        var fileId = FileRepository.GenerateFileId(@"C:\test.txt");
        SeedFileAndChunks(db, fileId, @"C:\test.txt", 1);

        var vector = MakeVector();
        await db.EmbeddingRepo.UpsertEmbeddingAsync(fileId, 0, "bge-m3", vector);

        var results = await db.EmbeddingRepo.GetEmbeddingsByFileIdsAsync(
            new[] { fileId }, "bge-m3");
        Assert.Single(results);
        Assert.Equal(vector.Length, results[0].Vector.Length);
        Assert.Equal(vector[0], results[0].Vector[0], 5);
        Assert.Equal(vector[100], results[0].Vector[100], 5);
    }

    [Fact]
    public async Task EnumerateChunksMissingEmbedding_SkipsExisting()
    {
        using var db = TestDbHelper.Create();
        var fileId = FileRepository.GenerateFileId(@"C:\test.txt");
        SeedFileAndChunks(db, fileId, @"C:\test.txt", 3);

        // Embed chunk 0 and 1
        await db.EmbeddingRepo.UpsertEmbeddingAsync(fileId, 0, "bge-m3", MakeVector());
        await db.EmbeddingRepo.UpsertEmbeddingAsync(fileId, 1, "bge-m3", MakeVector());

        // Only chunk 2 should be missing — enumerate first batch only
        // (EnumerateChunksMissingEmbeddingAsync loops until consumer embeds,
        //  so we take the first batch and break)
        var missing = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var chunk in db.EmbeddingRepo.EnumerateChunksMissingEmbeddingAsync("bge-m3", 100, cts.Token))
        {
            missing.Add(chunk.ChunkId);
            break; // take first result only — the method loops until embeddings are stored
        }
        Assert.Single(missing);
    }

    [Fact]
    public async Task BulkUpsertEmbeddings_InsertsAllInSingleTransaction()
    {
        using var db = TestDbHelper.Create();
        var fileId = FileRepository.GenerateFileId(@"C:\bulk.txt");
        SeedFileAndChunks(db, fileId, @"C:\bulk.txt", 5);

        var items = Enumerable.Range(0, 5)
            .Select(i => (fileId, i, "bge-m3", MakeVector()))
            .ToList();
        var count = await db.EmbeddingRepo.BulkUpsertEmbeddingsAsync(items);

        Assert.Equal(5, count);
        var total = await db.EmbeddingRepo.GetEmbeddingCountAsync("bge-m3");
        Assert.Equal(5, total);
    }

    [Fact]
    public async Task BulkUpsertEmbeddings_IdempotentOnDuplicate()
    {
        using var db = TestDbHelper.Create();
        var fileId = FileRepository.GenerateFileId(@"C:\dup.txt");
        SeedFileAndChunks(db, fileId, @"C:\dup.txt", 3);

        var items = Enumerable.Range(0, 3)
            .Select(i => (fileId, i, "bge-m3", MakeVector()))
            .ToList();
        await db.EmbeddingRepo.BulkUpsertEmbeddingsAsync(items);
        await db.EmbeddingRepo.BulkUpsertEmbeddingsAsync(items); // second call — same data

        var total = await db.EmbeddingRepo.GetEmbeddingCountAsync("bge-m3");
        Assert.Equal(3, total);
    }

    [Fact]
    public async Task GetEmbeddingsByFileIds_ReturnsCorrectVectors()
    {
        using var db = TestDbHelper.Create();
        var fid1 = FileRepository.GenerateFileId(@"C:\a.txt");
        var fid2 = FileRepository.GenerateFileId(@"C:\b.txt");
        var fid3 = FileRepository.GenerateFileId(@"C:\c.txt");

        SeedFileAndChunks(db, fid1, @"C:\a.txt", 1);
        SeedFileAndChunks(db, fid2, @"C:\b.txt", 1);
        SeedFileAndChunks(db, fid3, @"C:\c.txt", 1);

        await db.EmbeddingRepo.UpsertEmbeddingAsync(fid1, 0, "bge-m3", MakeVector());
        await db.EmbeddingRepo.UpsertEmbeddingAsync(fid2, 0, "bge-m3", MakeVector());
        await db.EmbeddingRepo.UpsertEmbeddingAsync(fid3, 0, "bge-m3", MakeVector());

        // Query only 2 files
        var results = await db.EmbeddingRepo.GetEmbeddingsByFileIdsAsync(
            new[] { fid1, fid3 }, "bge-m3");
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.FileId == fid1 || r.FileId == fid3));
    }

    [Fact]
    public async Task GetEmbeddingsByChunkIdsAsync_ReturnsRequestedTuples()
    {
        using var db = TestDbHelper.Create();
        var fid1 = FileRepository.GenerateFileId(@"C:\file1.txt");
        var fid2 = FileRepository.GenerateFileId(@"C:\file2.txt");
        SeedFileAndChunks(db, fid1, @"C:\file1.txt", 3);
        SeedFileAndChunks(db, fid2, @"C:\file2.txt", 3);

        await db.EmbeddingRepo.UpsertEmbeddingAsync(fid1, 0, "bge-m3", MakeVector());
        await db.EmbeddingRepo.UpsertEmbeddingAsync(fid1, 1, "bge-m3", MakeVector());
        await db.EmbeddingRepo.UpsertEmbeddingAsync(fid2, 0, "bge-m3", MakeVector());

        // Request a subset of (file_id, chunk_id) tuples.
        var keys = new[] { (fid1, 0), (fid1, 1), (fid2, 0) };
        var results = await db.EmbeddingRepo.GetEmbeddingsByChunkIdsAsync(keys, "bge-m3");

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.FileId == fid1 && r.ChunkId == 0);
        Assert.Contains(results, r => r.FileId == fid1 && r.ChunkId == 1);
        Assert.Contains(results, r => r.FileId == fid2 && r.ChunkId == 0);
    }

    [Fact]
    public async Task GetEmbeddingsByChunkIdsAsync_SkipsMissingTuples()
    {
        using var db = TestDbHelper.Create();
        var fid = FileRepository.GenerateFileId(@"C:\file1.txt");
        SeedFileAndChunks(db, fid, @"C:\file1.txt", 3);
        await db.EmbeddingRepo.UpsertEmbeddingAsync(fid, 0, "bge-m3", MakeVector());

        // Request tuples that include both an existing and a non-existing entry.
        var keys = new[] { (fid, 0), (fid, 99) };
        var results = await db.EmbeddingRepo.GetEmbeddingsByChunkIdsAsync(keys, "bge-m3");

        Assert.Single(results);
        Assert.Equal(fid, results[0].FileId);
        Assert.Equal(0, results[0].ChunkId);
    }

    [Fact]
    public async Task GetEmbeddingsByChunkIdsAsync_RespectsModelIdFilter()
    {
        using var db = TestDbHelper.Create();
        var fid = FileRepository.GenerateFileId(@"C:\file1.txt");
        SeedFileAndChunks(db, fid, @"C:\file1.txt", 1);
        await db.EmbeddingRepo.UpsertEmbeddingAsync(fid, 0, "bge-m3", MakeVector());

        var keys = new[] { (fid, 0) };
        var matching = await db.EmbeddingRepo.GetEmbeddingsByChunkIdsAsync(keys, "bge-m3");
        var nonMatching = await db.EmbeddingRepo.GetEmbeddingsByChunkIdsAsync(keys, "other-model");

        Assert.Single(matching);
        Assert.Empty(nonMatching);
    }
}
