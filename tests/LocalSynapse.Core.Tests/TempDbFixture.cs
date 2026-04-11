using System.Diagnostics;
using LocalSynapse.Core.Database;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Repositories;

namespace LocalSynapse.Core.Tests;

/// <summary>
/// Per-test temporary file-based SQLite fixture.
/// Creates a fresh DB with migrations applied, cleans up on dispose.
/// Uses file DB (not :memory:) because WAL mode behavior differs.
/// </summary>
public sealed class TempDbFixture : IDisposable
{
    public string DataFolder { get; }
    public string DbPath { get; }
    public ISettingsStore Settings { get; }
    public SqliteConnectionFactory Factory { get; }

    public TempDbFixture()
    {
        DataFolder = Path.Combine(
            Path.GetTempPath(),
            $"localsynapse-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DataFolder);

        // Step 2 이후: SettingsStore가 JSON 기반. Step 0~1 동안은 legacy SQLite 기반.
        // 어느 경우든 dataFolder 주입 방식은 동일.
        Settings = new SettingsStore(DataFolder);
        Factory = new SqliteConnectionFactory(Settings);

        DbPath = Settings.GetDatabasePath();

        var migration = new MigrationService(Factory);
        migration.RunMigrations();
    }

    public void Dispose()
    {
        // Step 1 이후 SqliteConnectionFactory는 IDisposable이 아니므로 Factory.Dispose() 호출 안 함.
        // Migration 중 열린 connection은 MigrationService의 using 패턴으로 이미 close 상태.
        // Step 0 시점에서는 fixture 사용 테스트가 0개이므로 미dispose가 실질 문제 없음.
        try
        {
            Directory.Delete(DataFolder, recursive: true);
        }
        catch (Exception ex)
        {
            // 테스트 정리 실패는 비치명적 — temp 폴더 누적은 OS가 cleanup.
            // SQLite WAL/SHM 파일이 OS-level lock으로 즉시 삭제 안 될 수 있음.
            // CLAUDE.md 규칙: 모든 catch는 로깅 필수 (빈 catch 금지).
            Debug.WriteLine($"[TempDbFixture] Failed to delete {DataFolder}: {ex.Message}");
        }
    }
}
