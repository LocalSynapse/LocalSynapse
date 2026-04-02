using Microsoft.Data.Sqlite;
using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.Core.Database;

/// <summary>
/// v1.2.0의 32개 마이그레이션을 단일 CREATE TABLE 세트로 통합한 스키마 초기화 서비스.
/// </summary>
public sealed class MigrationService : IMigrationService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    // ── FTS5 토크나이저 정의 (porter 스테밍 + unicode61 + 커스텀 separator) ──
    // porter: 영어 형태소 변형 자동 스테밍 (documents→document, running→run)
    //         한국어/CJK에는 영향 없음 (라틴 알파벳 기반 스테밍)
    // unicode61: 유니코드 정규화 + 토큰 분리
    // separators: 파일명에 흔한 구분자를 토큰 경계로 처리
    private const string FtsTokenizer = "porter unicode61 separators ''_-().[]''";

    /// <summary>MigrationService 생성자.</summary>
    public MigrationService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>데이터베이스 스키마를 최신 상태로 초기화한다.</summary>
    public void RunMigrations()
    {
        using var conn = _connectionFactory.CreateConnection();

        ExecuteNonQuery(conn, "PRAGMA foreign_keys = ON;");

        CreateCoreTables(conn);
        CreateFtsTables(conn);
        CreateEmbeddingTables(conn);
        CreateEmailTables(conn);
        CreatePipelineTables(conn);
        CreateSettingsTable(conn);

        // 기존 DB 업그레이드: FTS 토크나이저가 porter 미포함이면 리빌드
        UpgradeFtsTokenizerIfNeeded(conn);

        System.Diagnostics.Debug.WriteLine(
            $"[MigrationService] Schema initialized at {DateTime.UtcNow:o}");
    }

    private static void CreateCoreTables(SqliteConnection conn)
    {
        // ── files ──
        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS files (
                id                    TEXT PRIMARY KEY,
                path                  TEXT NOT NULL UNIQUE,
                filename              TEXT NOT NULL,
                extension             TEXT NOT NULL,
                size_bytes            INTEGER NOT NULL,
                modified_at           TEXT NOT NULL,
                indexed_at            TEXT NOT NULL,
                folder_path           TEXT NOT NULL,
                mtime_ms              INTEGER NOT NULL DEFAULT 0,
                content               TEXT,
                content_updated_at    TEXT,
                extract_status        TEXT DEFAULT 'PENDING',
                last_extract_error_code TEXT,
                chunk_count           INTEGER DEFAULT 0,
                last_chunked_at       TEXT,
                content_hash          TEXT,
                is_directory          INTEGER DEFAULT 0,
                file_ref_number       INTEGER
            );

            CREATE INDEX IF NOT EXISTS idx_files_filename       ON files(filename);
            CREATE INDEX IF NOT EXISTS idx_files_extension       ON files(extension);
            CREATE INDEX IF NOT EXISTS idx_files_folder_path     ON files(folder_path);
            CREATE INDEX IF NOT EXISTS idx_files_modified_at     ON files(modified_at);
            CREATE INDEX IF NOT EXISTS idx_files_frn             ON files(file_ref_number)
                WHERE file_ref_number IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_files_is_directory    ON files(is_directory);
            CREATE INDEX IF NOT EXISTS idx_files_ext_dir         ON files(extension, is_directory);
            CREATE INDEX IF NOT EXISTS idx_files_extract_status_dir
                ON files(extract_status, is_directory);
        ");

        // ── folders ──
        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS folders (
                id              TEXT PRIMARY KEY,
                path            TEXT NOT NULL UNIQUE,
                display_name    TEXT NOT NULL,
                added_at        TEXT NOT NULL,
                last_scanned_at TEXT,
                file_count      INTEGER DEFAULT 0
            );
        ");

        // ── file_chunks ──
        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS file_chunks (
                id              TEXT PRIMARY KEY,
                file_id         TEXT NOT NULL,
                chunk_index     INTEGER NOT NULL,
                text            TEXT NOT NULL,
                source_type     TEXT NOT NULL,
                origin_meta     TEXT,
                token_count     INTEGER,
                content_hash    TEXT NOT NULL,
                created_at      TEXT NOT NULL,
                start_offset    INTEGER,
                end_offset      INTEGER,
                FOREIGN KEY(file_id) REFERENCES files(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_file_chunks_file_id
                ON file_chunks(file_id);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_file_chunks_file_index
                ON file_chunks(file_id, chunk_index);
        ");

        // ── search_clicks ──
        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS search_clicks (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                query           TEXT NOT NULL,
                file_path       TEXT NOT NULL,
                click_count     INTEGER DEFAULT 1,
                last_clicked_at TEXT NOT NULL,
                UNIQUE(query, file_path)
            );
            CREATE INDEX IF NOT EXISTS idx_search_clicks_query ON search_clicks(query);
        ");

        // ── usn_bookmarks ──
        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS usn_bookmarks (
                drive_letter     TEXT PRIMARY KEY,
                journal_id       INTEGER NOT NULL,
                last_usn         INTEGER NOT NULL,
                last_scan_method TEXT NOT NULL DEFAULT 'usn',
                last_scan_at     TEXT NOT NULL,
                file_count       INTEGER DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_usn_bookmarks_method
                ON usn_bookmarks(drive_letter, last_scan_method);
        ");
    }

    private static void CreateFtsTables(SqliteConnection conn)
    {
        // ── files_fts ── (filename/path/extension 검색 전용, content 미포함)
        ExecuteNonQuery(conn, $@"
            CREATE VIRTUAL TABLE IF NOT EXISTS files_fts USING fts5(
                file_id UNINDEXED,
                filename, path, extension,
                tokenize = '{FtsTokenizer}'
            );
        ");

        // ── chunks_fts ── (본문 검색 + filename/folder_path BM25 가중치)
        ExecuteNonQuery(conn, $@"
            CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts USING fts5(
                chunk_id UNINDEXED,
                file_id UNINDEXED,
                text,
                filename,
                folder_path,
                tokenize = '{FtsTokenizer}'
            );
        ");
    }

    private static void CreateEmbeddingTables(SqliteConnection conn)
    {
        // ── embeddings ── UNIQUE(file_id, chunk_id, model_id)
        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS embeddings (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                file_id     TEXT NOT NULL,
                chunk_id    INTEGER NOT NULL,
                vector      BLOB NOT NULL,
                vector_dim  INTEGER NOT NULL,
                model_id    TEXT NOT NULL DEFAULT 'bge-m3',
                created_at  TEXT NOT NULL,
                precision   TEXT DEFAULT 'float32',
                UNIQUE(file_id, chunk_id, model_id)
            );
            CREATE INDEX IF NOT EXISTS idx_embeddings_file  ON embeddings(file_id);
            CREATE INDEX IF NOT EXISTS idx_embeddings_model ON embeddings(model_id);
        ");

        // ── embedding_models ──
        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS embedding_models (
                model_id    TEXT PRIMARY KEY,
                name        TEXT NOT NULL,
                dim         INTEGER NOT NULL,
                is_active   INTEGER NOT NULL DEFAULT 0,
                created_at  TEXT NOT NULL
            );

            INSERT OR IGNORE INTO embedding_models (model_id, name, dim, is_active, created_at)
            VALUES ('bge-m3', 'BAAI/bge-m3', 1024, 1, datetime('now'));
        ");

        // ── optional_embedding_state ──
        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS optional_embedding_state (
                model_id                TEXT PRIMARY KEY,
                backfill_status         TEXT NOT NULL DEFAULT 'idle',
                backfill_progress_done  INTEGER DEFAULT 0,
                backfill_progress_total INTEGER DEFAULT 0,
                last_backfill_at        TEXT,
                last_error              TEXT,
                last_processed_chunk_id INTEGER DEFAULT 0,
                paused_at               TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_optional_embedding_state_status
                ON optional_embedding_state(backfill_status);
        ");

        // ── model_install_state ──
        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS model_install_state (
                model_id             TEXT PRIMARY KEY,
                status               TEXT NOT NULL DEFAULT 'not_installed',
                download_bytes_done  INTEGER DEFAULT 0,
                download_bytes_total INTEGER DEFAULT 0,
                last_error           TEXT,
                installed_at         TEXT,
                loaded_at            TEXT,
                updated_at           TEXT DEFAULT CURRENT_TIMESTAMP
            );

            INSERT OR IGNORE INTO model_install_state (model_id, status)
            VALUES ('bge-m3', 'not_installed');
        ");
    }

    private static void CreateEmailTables(SqliteConnection conn)
    {
        // ── emails (SSOT) ──
        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS emails (
                email_id            TEXT PRIMARY KEY,
                source_type         TEXT NOT NULL CHECK (source_type IN ('file', 'graph')),
                file_id             TEXT REFERENCES files(id),
                graph_message_id    TEXT,
                thread_id           TEXT,
                conversation_id     TEXT,
                internet_message_id TEXT,
                in_reply_to         TEXT,
                sender_email        TEXT,
                sender_name         TEXT,
                recipients_json     TEXT,
                subject             TEXT,
                normalized_subject  TEXT,
                sent_at             TEXT,
                received_at         TEXT,
                has_attachments     INTEGER DEFAULT 0,
                web_link            TEXT,
                body_preview        TEXT,
                folder_name         TEXT,
                is_from_me          INTEGER DEFAULT 0,
                created_at          TEXT DEFAULT CURRENT_TIMESTAMP,
                updated_at          TEXT,
                CHECK (
                    (source_type = 'graph' AND graph_message_id IS NOT NULL AND file_id IS NULL)
                    OR
                    (source_type = 'file' AND file_id IS NOT NULL AND graph_message_id IS NULL)
                )
            );

            CREATE UNIQUE INDEX IF NOT EXISTS idx_emails_graph_id
                ON emails(graph_message_id) WHERE graph_message_id IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_emails_source    ON emails(source_type);
            CREATE INDEX IF NOT EXISTS idx_emails_thread    ON emails(thread_id);
            CREATE INDEX IF NOT EXISTS idx_emails_sender    ON emails(sender_email);
            CREATE INDEX IF NOT EXISTS idx_emails_sent_at   ON emails(sent_at);
            CREATE INDEX IF NOT EXISTS idx_emails_file_id   ON emails(file_id) WHERE file_id IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_emails_folder    ON emails(folder_name);
        ");

        // ── emails_fts ──
        ExecuteNonQuery(conn, $@"
            CREATE VIRTUAL TABLE IF NOT EXISTS emails_fts USING fts5(
                email_id UNINDEXED,
                subject,
                body_preview,
                sender_name,
                sender_email,
                recipients_json,
                tokenize = '{FtsTokenizer}'
            );
        ");

        // FTS 동기화 트리거
        ExecuteNonQuery(conn, @"
            CREATE TRIGGER IF NOT EXISTS emails_fts_insert AFTER INSERT ON emails BEGIN
                INSERT INTO emails_fts(email_id, subject, body_preview, sender_name, sender_email, recipients_json)
                VALUES (NEW.email_id, NEW.subject, NEW.body_preview, NEW.sender_name, NEW.sender_email, NEW.recipients_json);
            END;
        ");
        ExecuteNonQuery(conn, @"
            CREATE TRIGGER IF NOT EXISTS emails_fts_update AFTER UPDATE ON emails BEGIN
                DELETE FROM emails_fts WHERE email_id = OLD.email_id;
                INSERT INTO emails_fts(email_id, subject, body_preview, sender_name, sender_email, recipients_json)
                VALUES (NEW.email_id, NEW.subject, NEW.body_preview, NEW.sender_name, NEW.sender_email, NEW.recipients_json);
            END;
        ");
        ExecuteNonQuery(conn, @"
            CREATE TRIGGER IF NOT EXISTS emails_fts_delete AFTER DELETE ON emails BEGIN
                DELETE FROM emails_fts WHERE email_id = OLD.email_id;
            END;
        ");

        // ── graph_connections ──
        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS graph_connections (
                id                TEXT PRIMARY KEY DEFAULT 'default',
                user_email        TEXT,
                user_name         TEXT,
                status            TEXT NOT NULL DEFAULT 'disconnected',
                error_message     TEXT,
                consent_given_at  TEXT,
                consent_version   INTEGER DEFAULT 1,
                created_at        TEXT DEFAULT CURRENT_TIMESTAMP,
                updated_at        TEXT
            );
        ");

        // ── graph_delta_state ──
        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS graph_delta_state (
                folder        TEXT PRIMARY KEY,
                delta_link    TEXT,
                last_sync_at  TEXT,
                status        TEXT DEFAULT 'ready',
                error_message TEXT,
                synced_count  INTEGER DEFAULT 0
            );
        ");
    }

    private static void CreatePipelineTables(SqliteConnection conn)
    {
        // ── pipeline_stamps ──
        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS pipeline_stamps (
                id                       TEXT PRIMARY KEY DEFAULT 'current',
                total_files              INTEGER NOT NULL DEFAULT 0,
                total_folders            INTEGER NOT NULL DEFAULT 0,
                scan_completed_at        TEXT,
                content_searchable_files INTEGER NOT NULL DEFAULT 0,
                indexed_files            INTEGER NOT NULL DEFAULT 0,
                total_chunks             INTEGER NOT NULL DEFAULT 0,
                indexing_completed_at    TEXT,
                embeddable_chunks        INTEGER NOT NULL DEFAULT 0,
                embedded_chunks          INTEGER NOT NULL DEFAULT 0,
                embedding_completed_at   TEXT,
                indexing_user_confirmed   INTEGER NOT NULL DEFAULT 0,
                embedding_user_confirmed  INTEGER NOT NULL DEFAULT 0,
                last_auto_run_at         TEXT,
                auto_run_count           INTEGER NOT NULL DEFAULT 0,
                updated_at               TEXT DEFAULT (datetime('now'))
            );

            INSERT OR IGNORE INTO pipeline_stamps (id) VALUES ('current');
        ");
    }

    private static void CreateSettingsTable(SqliteConnection conn)
    {
        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
        ");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FTS 토크나이저 업그레이드 (unicode61 → porter unicode61)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 기존 FTS 테이블이 porter 토크나이저 없이 생성되었으면 리빌드한다.
    /// settings 테이블의 'fts_tokenizer_version' 키로 상태를 추적한다.
    /// </summary>
    private static void UpgradeFtsTokenizerIfNeeded(SqliteConnection conn)
    {
        const string versionKey = "fts_tokenizer_version";
        const string currentVersion = "porter_v1";

        // 현재 버전 확인
        using (var checkCmd = conn.CreateCommand())
        {
            checkCmd.CommandText = "SELECT value FROM settings WHERE key = $key";
            checkCmd.Parameters.AddWithValue("$key", versionKey);
            var result = checkCmd.ExecuteScalar();
            if (result is string val && val == currentVersion)
                return; // 이미 최신 — 스킵
        }

        System.Diagnostics.Debug.WriteLine(
            "[MigrationService] FTS tokenizer upgrade: rebuilding FTS tables with porter stemmer...");

        using var tx = conn.BeginTransaction();
        try
        {
            // 1) FTS 테이블 DROP
            ExecuteNonQuery(conn, "DROP TABLE IF EXISTS files_fts;");
            ExecuteNonQuery(conn, "DROP TABLE IF EXISTS chunks_fts;");
            ExecuteNonQuery(conn, "DROP TABLE IF EXISTS emails_fts;");

            // email FTS 트리거도 DROP (재생성 위해)
            ExecuteNonQuery(conn, "DROP TRIGGER IF EXISTS emails_fts_insert;");
            ExecuteNonQuery(conn, "DROP TRIGGER IF EXISTS emails_fts_update;");
            ExecuteNonQuery(conn, "DROP TRIGGER IF EXISTS emails_fts_delete;");

            // 2) porter 토크나이저로 FTS 테이블 재생성
            ExecuteNonQuery(conn, $@"
                CREATE VIRTUAL TABLE files_fts USING fts5(
                    file_id UNINDEXED,
                    filename, path, extension,
                    tokenize = '{FtsTokenizer}'
                );
            ");

            ExecuteNonQuery(conn, $@"
                CREATE VIRTUAL TABLE chunks_fts USING fts5(
                    chunk_id UNINDEXED,
                    file_id UNINDEXED,
                    text,
                    filename,
                    folder_path,
                    tokenize = '{FtsTokenizer}'
                );
            ");

            ExecuteNonQuery(conn, $@"
                CREATE VIRTUAL TABLE emails_fts USING fts5(
                    email_id UNINDEXED,
                    subject,
                    body_preview,
                    sender_name,
                    sender_email,
                    recipients_json,
                    tokenize = '{FtsTokenizer}'
                );
            ");

            // 3) email FTS 트리거 재생성
            ExecuteNonQuery(conn, @"
                CREATE TRIGGER emails_fts_insert AFTER INSERT ON emails BEGIN
                    INSERT INTO emails_fts(email_id, subject, body_preview, sender_name, sender_email, recipients_json)
                    VALUES (NEW.email_id, NEW.subject, NEW.body_preview, NEW.sender_name, NEW.sender_email, NEW.recipients_json);
                END;
            ");
            ExecuteNonQuery(conn, @"
                CREATE TRIGGER emails_fts_update AFTER UPDATE ON emails BEGIN
                    DELETE FROM emails_fts WHERE email_id = OLD.email_id;
                    INSERT INTO emails_fts(email_id, subject, body_preview, sender_name, sender_email, recipients_json)
                    VALUES (NEW.email_id, NEW.subject, NEW.body_preview, NEW.sender_name, NEW.sender_email, NEW.recipients_json);
                END;
            ");
            ExecuteNonQuery(conn, @"
                CREATE TRIGGER emails_fts_delete AFTER DELETE ON emails BEGIN
                    DELETE FROM emails_fts WHERE email_id = OLD.email_id;
                END;
            ");

            // 4) 소스 테이블에서 FTS 데이터 재적재
            ExecuteNonQuery(conn, @"
                INSERT INTO files_fts (file_id, filename, path, extension)
                SELECT id, filename, path, extension FROM files;
            ");

            ExecuteNonQuery(conn, @"
                INSERT INTO chunks_fts (chunk_id, file_id, text, filename, folder_path)
                SELECT c.id, c.file_id, c.text, f.filename, f.folder_path
                FROM file_chunks c
                JOIN files f ON c.file_id = f.id
                WHERE c.text IS NOT NULL AND LENGTH(c.text) > 0;
            ");

            ExecuteNonQuery(conn, @"
                INSERT INTO emails_fts (email_id, subject, body_preview, sender_name, sender_email, recipients_json)
                SELECT email_id, subject, body_preview, sender_name, sender_email, recipients_json
                FROM emails;
            ");

            // 5) 버전 스탬프 기록
            ExecuteNonQuery(conn, $@"
                INSERT INTO settings (key, value) VALUES ('{versionKey}', '{currentVersion}')
                ON CONFLICT(key) DO UPDATE SET value = '{currentVersion}';
            ");

            tx.Commit();

            System.Diagnostics.Debug.WriteLine(
                "[MigrationService] FTS tokenizer upgrade complete: porter stemmer enabled.");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            System.Diagnostics.Debug.WriteLine(
                $"[MigrationService] FTS tokenizer upgrade FAILED: {ex.Message}");
            throw;
        }
    }

    private static void ExecuteNonQuery(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
