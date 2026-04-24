using LocalSynapse.Core.Database;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Core.Repositories;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Tests;

/// <summary>
/// Shared test data setup for Search tests.
/// Creates a temp file-based SQLite DB with migrations applied and seeds test data.
/// </summary>
internal static class SearchTestHelper
{
    /// <summary>File IDs computed by FileRepository.GenerateFileId for the seeded paths.</summary>
    public static string File1Id => FileRepository.GenerateFileId("/Documents/budget_report_2024.xlsx");
    public static string File2Id => FileRepository.GenerateFileId("/Documents/meeting_notes_Q4.docx");
    public static string File3Id => FileRepository.GenerateFileId("/Projects/README.md");
    public static string File4Id => FileRepository.GenerateFileId("/Legal/contract_final.pdf");
    public static string File5Id => FileRepository.GenerateFileId("/Legal/contract_v2_draft.pdf");

    /// <summary>Creates a test DB with migrations applied and seeds files + chunks.</summary>
    public static SearchTestDb Create()
    {
        var settings = new TempSettingsStore();
        var factory = new SqliteConnectionFactory(settings);

        var migration = new MigrationService(factory);
        migration.RunMigrations();

        var fileRepo = new FileRepository(factory);
        var chunkRepo = new ChunkRepository(factory);

        SeedData(fileRepo, chunkRepo);

        return new SearchTestDb
        {
            Settings = settings,
            Factory = factory,
            FileRepo = fileRepo,
            ChunkRepo = chunkRepo,
            ClickService = new SearchClickService(factory),
        };
    }

    private static void SeedData(FileRepository fileRepo, ChunkRepository chunkRepo)
    {
        var now = DateTime.UtcNow.ToString("o");

        var files = new List<FileMetadata>
        {
            new()
            {
                Id = "", Path = "/Documents/budget_report_2024.xlsx",
                Filename = "budget_report_2024.xlsx", Extension = ".xlsx",
                ModifiedAt = now, IndexedAt = now, FolderPath = "/Documents",
                SizeBytes = 1024, ExtractStatus = ExtractStatuses.Success,
            },
            new()
            {
                Id = "", Path = "/Documents/meeting_notes_Q4.docx",
                Filename = "meeting_notes_Q4.docx", Extension = ".docx",
                ModifiedAt = now, IndexedAt = now, FolderPath = "/Documents",
                SizeBytes = 2048, ExtractStatus = ExtractStatuses.Success,
            },
            new()
            {
                Id = "", Path = "/Projects/README.md",
                Filename = "README.md", Extension = ".md",
                ModifiedAt = now, IndexedAt = now, FolderPath = "/Projects",
                SizeBytes = 512, ExtractStatus = ExtractStatuses.Success,
            },
            new()
            {
                Id = "", Path = "/Legal/contract_final.pdf",
                Filename = "contract_final.pdf", Extension = ".pdf",
                ModifiedAt = now, IndexedAt = now, FolderPath = "/Legal",
                SizeBytes = 4096, ExtractStatus = ExtractStatuses.Success,
            },
            new()
            {
                Id = "", Path = "/Legal/contract_v2_draft.pdf",
                Filename = "contract_v2_draft.pdf", Extension = ".pdf",
                ModifiedAt = now, IndexedAt = now, FolderPath = "/Legal",
                SizeBytes = 3072, ExtractStatus = ExtractStatuses.Success,
            },
        };

        fileRepo.UpsertFiles(files);

        var chunks = new List<FileChunk>
        {
            new()
            {
                Id = "chunk1", FileId = File1Id, ChunkIndex = 0,
                Text = "Total project budget for 2024 fiscal year is 500M including operational costs and capital expenditures",
                SourceType = ChunkSourceTypes.Text, ContentHash = "h1", CreatedAt = now,
            },
            new()
            {
                Id = "chunk2", FileId = File2Id, ChunkIndex = 0,
                Text = "Key agenda items discussed in Q4 strategy meeting including revenue targets and market expansion plans",
                SourceType = ChunkSourceTypes.Text, ContentHash = "h2", CreatedAt = now,
            },
            new()
            {
                Id = "chunk3", FileId = File3Id, ChunkIndex = 0,
                Text = "This project implements a local file search engine with full text indexing and semantic search capabilities",
                SourceType = ChunkSourceTypes.Text, ContentHash = "h3", CreatedAt = now,
            },
            new()
            {
                Id = "chunk4", FileId = File4Id, ChunkIndex = 0,
                Text = "Terms and conditions between party A and party B regarding the supply agreement effective January 2024",
                SourceType = ChunkSourceTypes.Text, ContentHash = "h4", CreatedAt = now,
            },
            new()
            {
                Id = "chunk5", FileId = File5Id, ChunkIndex = 0,
                Text = "Draft contract review shows sections needing revision including liability clauses and payment terms",
                SourceType = ChunkSourceTypes.Text, ContentHash = "h5", CreatedAt = now,
            },
        };

        chunkRepo.UpsertChunks(chunks);
    }
}

internal sealed class SearchTestDb : IDisposable
{
    public required TempSettingsStore Settings { get; init; }
    public required SqliteConnectionFactory Factory { get; init; }
    public required FileRepository FileRepo { get; init; }
    public required ChunkRepository ChunkRepo { get; init; }
    public required SearchClickService ClickService { get; init; }

    public void Dispose()
    {
        try { Directory.Delete(Settings.GetDataFolder(), recursive: true); }
        catch { /* cleanup best-effort */ }
    }
}

internal sealed class TempSettingsStore : ISettingsStore
{
    private readonly string _tempDir;

    public TempSettingsStore()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ls_search_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public string GetLanguage() => "en";
    public void SetLanguage(string cultureName) { }
    public string GetDataFolder() => _tempDir;
    public string GetLogFolder() => Path.Combine(_tempDir, "logs");
    public string GetModelFolder() => Path.Combine(_tempDir, "models");
    public string GetDatabasePath() => Path.Combine(_tempDir, "test.db");
}
