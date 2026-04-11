using LocalSynapse.Core.Repositories;
using Microsoft.Data.Sqlite;

namespace LocalSynapse.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void SettingsStore_RoundTripsLanguage()
    {
        using var temp = new TempDbFixture();

        // TempDbFixture 생성자가 이미 `new SettingsStore(DataFolder)`를 호출하여
        // Step 2 이후에는 settings.json이 생성된 상태일 수 있다. T1은 "기본값 'en'"을
        // 검증하려 하므로 기존 JSON을 삭제하여 깨끗한 시작점을 확보한다.
        // (Step 2의 SettingsStore는 JSON 없으면 TryMigrateFromSqlite → 빈 settings 테이블 → 'en')
        var jsonPath = System.IO.Path.Combine(temp.DataFolder, "settings.json");
        if (File.Exists(jsonPath)) File.Delete(jsonPath);

        var store = new SettingsStore(temp.DataFolder);
        Assert.Equal("en", store.GetLanguage()); // default — legacy SQLite가 비어있음

        store.SetLanguage("ko-KR");
        Assert.Equal("ko-KR", store.GetLanguage());

        // 영속성 확인: 새 instance로 로드 → JSON 파일에서 읽음
        var store2 = new SettingsStore(temp.DataFolder);
        Assert.Equal("ko-KR", store2.GetLanguage());
    }

    [Fact]
    public void SettingsStore_MigratesFromLegacySqlite_OnFirstRun()
    {
        using var temp = new TempDbFixture();
        // temp fixture의 migration이 settings 테이블을 생성했음

        // 직접 legacy 키 삽입
        using (var conn = new SqliteConnection($"Data Source={temp.DbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT OR REPLACE INTO settings (key, value) VALUES ('language', 'ko-KR')";
            cmd.ExecuteNonQuery();
        }

        // settings.json 제거 (fixture의 SettingsStore가 미리 생성했을 가능성)
        var jsonPath = System.IO.Path.Combine(temp.DataFolder, "settings.json");
        if (File.Exists(jsonPath)) File.Delete(jsonPath);

        // 새 SettingsStore 생성 → legacy 마이그레이션 발동
        var store = new SettingsStore(temp.DataFolder);
        Assert.Equal("ko-KR", store.GetLanguage());

        // JSON 파일 생성 확인
        Assert.True(File.Exists(jsonPath));

        // SQLite 테이블은 삭제되지 않음 확인 (롤백 안전망)
        using var conn2 = new SqliteConnection($"Data Source={temp.DbPath}");
        conn2.Open();
        using var cmd2 = conn2.CreateCommand();
        cmd2.CommandText = "SELECT value FROM settings WHERE key = 'language'";
        Assert.Equal("ko-KR", cmd2.ExecuteScalar());
    }

    [Fact]
    public void SettingsStore_CorruptJson_IsBackedUp_AndMigrationRerun()
    {
        using var temp = new TempDbFixture();
        var jsonPath = System.IO.Path.Combine(temp.DataFolder, "settings.json");

        // 손상된 JSON 파일 작성 (fixture가 생성한 기존 JSON을 덮어씀)
        File.WriteAllText(jsonPath, "{ invalid json");

        // Legacy SQLite에 값 삽입 (백업 후 migration이 이 값을 복구해야 함)
        using (var conn = new SqliteConnection($"Data Source={temp.DbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT OR REPLACE INTO settings (key, value) VALUES ('language', 'de-DE')";
            cmd.ExecuteNonQuery();
        }

        var store = new SettingsStore(temp.DataFolder);
        Assert.Equal("de-DE", store.GetLanguage());

        // 손상된 파일이 백업됨 확인
        var corruptFiles = Directory.GetFiles(temp.DataFolder, "settings.json.corrupt.*");
        Assert.Single(corruptFiles);

        // 새 settings.json 생성됨 확인
        Assert.True(File.Exists(jsonPath));
    }
}
