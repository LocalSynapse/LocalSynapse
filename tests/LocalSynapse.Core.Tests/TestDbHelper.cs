using LocalSynapse.Core.Database;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Repositories;

namespace LocalSynapse.Core.Tests;

/// <summary>
/// Creates a temp file-based SQLite DB for each test.
/// Each call gets a fresh DB with migrations applied.
/// </summary>
internal static class TestDbHelper
{
    public static TestDb Create()
    {
        var settings = new TempSettingsStore();
        var factory = new SqliteConnectionFactory(settings);

        var migration = new MigrationService(factory);
        migration.RunMigrations();

        return new TestDb
        {
            Settings = settings,
            Factory = factory,
            Migration = migration,
            FileRepo = new FileRepository(factory),
            ChunkRepo = new ChunkRepository(factory),
            EmbeddingRepo = new EmbeddingRepository(factory),
            StampRepo = new PipelineStampRepository(factory),
        };
    }
}

internal sealed class TestDb : IDisposable
{
    public required TempSettingsStore Settings { get; init; }
    public required SqliteConnectionFactory Factory { get; init; }
    public required MigrationService Migration { get; init; }
    public required FileRepository FileRepo { get; init; }
    public required ChunkRepository ChunkRepo { get; init; }
    public required EmbeddingRepository EmbeddingRepo { get; init; }
    public required PipelineStampRepository StampRepo { get; init; }

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
        _tempDir = Path.Combine(Path.GetTempPath(), "ls_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public string GetLanguage() => "en";
    public void SetLanguage(string cultureName) { }
    public string GetDataFolder() => _tempDir;
    public string GetLogFolder() => Path.Combine(_tempDir, "logs");
    public string GetModelFolder() => Path.Combine(_tempDir, "models");
    public string GetDatabasePath() => Path.Combine(_tempDir, "test.db");
    public string[]? GetScanRoots() => null;
    public void SetScanRoots(string[]? roots) { }
    public string GetPerformanceMode() => "Cruise";
    public void SetPerformanceMode(string mode) { }
    public (string?, string?) GetGpuDetectionCache() => (null, null);
    public void SetGpuDetectionCache(string? bestProvider, string? gpuName) { }
}
