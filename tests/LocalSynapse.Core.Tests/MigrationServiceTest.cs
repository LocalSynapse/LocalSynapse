using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalSynapse.Core.Tests;

public class MigrationServiceTest
{
    [Fact]
    public void RunMigrations_CreatesAllTables()
    {
        using var db = TestDbHelper.Create();
        using var conn = db.Factory.CreateConnection();

        var tables = GetTableNames(conn);

        Assert.Contains("files", tables);
        Assert.Contains("folders", tables);
        Assert.Contains("file_chunks", tables);
        Assert.Contains("embeddings", tables);
        Assert.Contains("pipeline_stamps", tables);
        Assert.Contains("settings", tables);
        Assert.Contains("search_clicks", tables);
        Assert.Contains("emails", tables);
        Assert.Contains("graph_connections", tables);
        Assert.Contains("graph_delta_state", tables);
        Assert.Contains("usn_bookmarks", tables);
        Assert.Contains("embedding_models", tables);
        Assert.Contains("model_install_state", tables);
        Assert.Contains("optional_embedding_state", tables);
    }

    [Fact]
    public void RunMigrations_CreatesFts5Tables()
    {
        using var db = TestDbHelper.Create();
        using var conn = db.Factory.CreateConnection();

        var tables = GetTableNames(conn);

        Assert.Contains("files_fts", tables);
        Assert.Contains("chunks_fts", tables);
        Assert.Contains("emails_fts", tables);
    }

    [Fact]
    public void RunMigrations_Idempotent()
    {
        using var db = TestDbHelper.Create();

        // Running migrations again should not throw
        db.Migration.RunMigrations();
        db.Migration.RunMigrations();

        using var conn = db.Factory.CreateConnection();
        var tables = GetTableNames(conn);
        Assert.Contains("files", tables);
    }

    [Fact]
    public void RunMigrations_FTS5TablesHaveCorrectTokenizer()
    {
        using var db = TestDbHelper.Create();
        using var conn = db.Factory.CreateConnection();

        // Check all FTS tables for porter + unicode61 tokenizer
        foreach (var tableName in new[] { "chunks_fts", "files_fts", "emails_fts" })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT sql FROM sqlite_master WHERE name=$name";
            cmd.Parameters.AddWithValue("$name", tableName);
            var sql = cmd.ExecuteScalar() as string;

            Assert.NotNull(sql);
            Assert.Contains("porter", sql,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("unicode61", sql);
            Assert.Contains("separators", sql);
        }
    }

    [Fact]
    public void RunMigrations_SetsTokenizerVersionStamp()
    {
        using var db = TestDbHelper.Create();
        using var conn = db.Factory.CreateConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "SELECT value FROM settings WHERE key = 'fts_tokenizer_version'";
        var version = cmd.ExecuteScalar() as string;

        Assert.Equal("porter_v2", version);
    }

    [Fact]
    public void RunMigrations_PipelineStampsHasInitialRecord()
    {
        using var db = TestDbHelper.Create();
        using var conn = db.Factory.CreateConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "SELECT COUNT(*) FROM pipeline_stamps WHERE id='current'";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(1, count);
    }

    [Fact]
    public void RunMigrations_UpgradesExistingFtsTokenizer()
    {
        using var db = TestDbHelper.Create();
        using var conn = db.Factory.CreateConnection();

        // 시뮬레이션: 버전 스탬프를 제거하여 "업그레이드 필요" 상태로 만든다
        using (var delCmd = conn.CreateCommand())
        {
            delCmd.CommandText = "DELETE FROM settings WHERE key = 'fts_tokenizer_version'";
            delCmd.ExecuteNonQuery();
        }

        // 다시 마이그레이션 실행 — FTS 리빌드가 발생해야 함
        db.Migration.RunMigrations();

        // porter 토크나이저 확인
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE name='chunks_fts'";
        var sql = cmd.ExecuteScalar() as string;
        Assert.NotNull(sql);
        Assert.Contains("porter", sql, StringComparison.OrdinalIgnoreCase);

        // 버전 스탬프 재설정 확인
        using var vCmd = conn.CreateCommand();
        vCmd.CommandText = "SELECT value FROM settings WHERE key = 'fts_tokenizer_version'";
        var version = vCmd.ExecuteScalar() as string;
        Assert.Equal("porter_v2", version);
    }

    [Fact]
    public void RunMigrations_FtsUpgradePreservesData()
    {
        using var db = TestDbHelper.Create();
        using var conn = db.Factory.CreateConnection();

        // 테스트 파일 삽입
        using (var insCmd = conn.CreateCommand())
        {
            insCmd.CommandText = @"
                INSERT INTO files (id, path, filename, extension, size_bytes, modified_at,
                                   indexed_at, folder_path, mtime_ms)
                VALUES ('f1', '/test/doc.txt', 'doc.txt', '.txt', 100,
                        '2024-01-01', '2024-01-01', '/test', 0)";
            insCmd.ExecuteNonQuery();
        }
        using (var insCmd = conn.CreateCommand())
        {
            insCmd.CommandText = @"
                INSERT INTO file_chunks (id, file_id, chunk_index, text, source_type, content_hash, created_at)
                VALUES ('c1', 'f1', 0, 'The documents contain important information about configuration',
                        'text', 'h1', '2024-01-01')";
            insCmd.ExecuteNonQuery();
        }

        // files_fts + chunks_fts에 데이터 삽입
        using (var insCmd = conn.CreateCommand())
        {
            insCmd.CommandText = @"
                INSERT INTO files_fts (file_id, filename, path, extension)
                VALUES ('f1', 'doc.txt', '/test/doc.txt', '.txt')";
            insCmd.ExecuteNonQuery();
        }
        using (var insCmd = conn.CreateCommand())
        {
            insCmd.CommandText = @"
                INSERT INTO chunks_fts (chunk_id, file_id, text, filename, folder_path)
                VALUES ('c1', 'f1', 'The documents contain important information about configuration',
                        'doc.txt', '/test')";
            insCmd.ExecuteNonQuery();
        }

        // 버전 스탬프 제거 → 업그레이드 트리거
        using (var delCmd = conn.CreateCommand())
        {
            delCmd.CommandText = "DELETE FROM settings WHERE key = 'fts_tokenizer_version'";
            delCmd.ExecuteNonQuery();
        }

        // 마이그레이션 재실행 → FTS 리빌드
        db.Migration.RunMigrations();

        // FTS 데이터가 보존되었는지 확인 (소스 테이블에서 재적재)
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM chunks_fts";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(1, count);

        // porter 스테밍으로 "document" 검색 시 "documents" 콘텐츠가 매칭되는지 확인
        using var matchCmd = conn.CreateCommand();
        matchCmd.CommandText = "SELECT COUNT(*) FROM chunks_fts WHERE chunks_fts MATCH 'document'";
        var matchCount = Convert.ToInt32(matchCmd.ExecuteScalar());
        Assert.True(matchCount > 0, "Porter stemmer should match 'document' to 'documents' content");
    }

    private static List<string> GetTableNames(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        var tables = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) tables.Add(r.GetString(0));
        return tables;
    }
}
