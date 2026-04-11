using System.Security.Cryptography;
using System.Text;
using LocalSynapse.Core.Constants;
using LocalSynapse.Core.Database;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using Microsoft.Data.Sqlite;

namespace LocalSynapse.Core.Repositories;

/// <summary>
/// 파일 메타데이터 CRUD 및 FTS5 동기화를 담당하는 Repository 구현체.
/// </summary>
public sealed class FileRepository : IFileRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    /// <summary>FileRepository 생성자.</summary>
    public FileRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>단일 파일 메타데이터를 Upsert한다.</summary>
    public FileMetadata UpsertFile(FileMetadata file)
    {
        using var conn = _connectionFactory.CreateConnection();

        file.Id = GenerateFileId(file.Path);
        file.IndexedAt = DateTime.UtcNow.ToString("o");
        file.FolderPath = Path.GetDirectoryName(file.Path) ?? "";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO files (id, path, filename, extension, size_bytes, modified_at,
                               indexed_at, folder_path, mtime_ms, is_directory, file_ref_number,
                               extract_status)
            VALUES ($id, $path, $filename, $ext, $size, $modified_at,
                    $indexed_at, $folder_path, $mtime_ms, $is_dir, $frn, $extract_status)
            ON CONFLICT(id) DO UPDATE SET
                filename       = excluded.filename,
                extension      = excluded.extension,
                size_bytes     = excluded.size_bytes,
                modified_at    = excluded.modified_at,
                indexed_at     = excluded.indexed_at,
                mtime_ms       = excluded.mtime_ms,
                is_directory   = excluded.is_directory,
                file_ref_number= excluded.file_ref_number,
                extract_status = excluded.extract_status";
        BindFileParams(cmd, file);
        cmd.ExecuteNonQuery();

        return file;
    }

    // R8 sub-batch size. 기존 단일 tx는 caller가 500개를 전달하면 그 500개 전체를
    // 한 transaction으로 묶어 lock hold time이 길었다. 75개씩 분할하면 lock hold
    // time이 ~1/7로 감소하고, 다른 writer (scan, click recording, MCP)의 대기 시간도
    // 비례 감소한다. 75는 50~100 범위의 중간값으로 spec §6.2에서 확정.
    private const int UpsertSubBatchSize = 75;

    /// <summary>복수 파일 메타데이터를 일괄 Upsert하고, files_fts도 동기화한다.
    /// 내부적으로 75개 단위 sub-batch로 분할하여 lock hold time을 줄인다.</summary>
    public int UpsertFiles(IEnumerable<FileMetadata> files)
    {
        // 1회 materialize (lazy enumerable 이중 열거 방지)
        var fileList = files as IReadOnlyList<FileMetadata> ?? files.ToList();

        // W3 회귀 방지: indexedAt을 메서드 진입 시점에 1회 계산.
        // 원본 코드는 단일 tx 내부에서 한 번 계산하여 모든 파일이 동일한 값을 공유했다.
        // Sub-batch마다 재계산하면 같은 "batch"로 scan된 파일들이 millisecond 단위로
        // 다른 timestamp를 갖게 되어 recency ranking 경계 케이스에서 회귀.
        // T9 regression guard (UpsertFiles_AssignsSameIndexedAtToAllFilesInBatch)가
        // 이를 검증한다.
        var indexedAt = DateTime.UtcNow.ToString("o");
        var totalCount = 0;

        for (int offset = 0; offset < fileList.Count; offset += UpsertSubBatchSize)
        {
            var take = Math.Min(UpsertSubBatchSize, fileList.Count - offset);
            var subBatch = new List<FileMetadata>(take);
            for (int i = 0; i < take; i++)
                subBatch.Add(fileList[offset + i]);

            totalCount += UpsertFilesSingleTransaction(subBatch, indexedAt);
        }

        return totalCount;
    }

    /// <summary>단일 transaction 내에서 주어진 file 목록을 upsert + files_fts 동기화.
    /// indexedAt은 outer UpsertFiles에서 1회 계산 후 파라미터로 전달받는다 (W3).</summary>
    private int UpsertFilesSingleTransaction(IReadOnlyList<FileMetadata> files, string indexedAt)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var count = 0;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO files (id, path, filename, extension, size_bytes, modified_at,
                               indexed_at, folder_path, mtime_ms, is_directory, file_ref_number,
                               extract_status)
            VALUES ($id, $path, $filename, $ext, $size, $modified_at,
                    $indexed_at, $folder_path, $mtime_ms, $is_dir, $frn, $extract_status)
            ON CONFLICT(id) DO UPDATE SET
                filename       = excluded.filename,
                extension      = excluded.extension,
                size_bytes     = excluded.size_bytes,
                modified_at    = excluded.modified_at,
                indexed_at     = excluded.indexed_at,
                mtime_ms       = excluded.mtime_ms,
                is_directory   = excluded.is_directory,
                file_ref_number= excluded.file_ref_number,
                extract_status = excluded.extract_status";

        var pId = cmd.Parameters.Add("$id", SqliteType.Text);
        var pPath = cmd.Parameters.Add("$path", SqliteType.Text);
        var pFilename = cmd.Parameters.Add("$filename", SqliteType.Text);
        var pExt = cmd.Parameters.Add("$ext", SqliteType.Text);
        var pSize = cmd.Parameters.Add("$size", SqliteType.Integer);
        var pModifiedAt = cmd.Parameters.Add("$modified_at", SqliteType.Text);
        var pIndexedAt = cmd.Parameters.Add("$indexed_at", SqliteType.Text);
        var pFolderPath = cmd.Parameters.Add("$folder_path", SqliteType.Text);
        var pMtimeMs = cmd.Parameters.Add("$mtime_ms", SqliteType.Integer);
        var pIsDir = cmd.Parameters.Add("$is_dir", SqliteType.Integer);
        var pFrn = cmd.Parameters.Add("$frn", SqliteType.Integer);
        var pStatus = cmd.Parameters.Add("$extract_status", SqliteType.Text);

        using var ftsDelCmd = conn.CreateCommand();
        ftsDelCmd.CommandText = "DELETE FROM files_fts WHERE file_id = $fts_del_id";
        ftsDelCmd.Parameters.Add("$fts_del_id", SqliteType.Text);

        using var ftsInsCmd = conn.CreateCommand();
        ftsInsCmd.CommandText = @"
            INSERT INTO files_fts (file_id, filename, path, extension)
            VALUES ($fts_id, $fts_filename, $fts_path, $fts_ext)";
        ftsInsCmd.Parameters.Add("$fts_id", SqliteType.Text);
        ftsInsCmd.Parameters.Add("$fts_filename", SqliteType.Text);
        ftsInsCmd.Parameters.Add("$fts_path", SqliteType.Text);
        ftsInsCmd.Parameters.Add("$fts_ext", SqliteType.Text);

        foreach (var file in files)
        {
            var id = GenerateFileId(file.Path);
            var folderPath = Path.GetDirectoryName(file.Path) ?? "";

            pId.Value = id;
            pPath.Value = file.Path;
            pFilename.Value = file.Filename;
            pExt.Value = file.Extension;
            pSize.Value = file.SizeBytes;
            pModifiedAt.Value = file.ModifiedAt;
            pIndexedAt.Value = indexedAt;
            pFolderPath.Value = folderPath;
            pMtimeMs.Value = file.MtimeMs;
            pIsDir.Value = file.IsDirectory ? 1 : 0;
            pFrn.Value = file.FileRefNumber == 0 ? DBNull.Value : file.FileRefNumber;
            pStatus.Value = file.ExtractStatus;

            cmd.ExecuteNonQuery();

            try
            {
                ftsDelCmd.Parameters["$fts_del_id"].Value = id;
                ftsDelCmd.ExecuteNonQuery();
                ftsInsCmd.Parameters["$fts_id"].Value = id;
                ftsInsCmd.Parameters["$fts_filename"].Value = file.Filename;
                ftsInsCmd.Parameters["$fts_path"].Value = file.Path;
                ftsInsCmd.Parameters["$fts_ext"].Value = file.Extension;
                ftsInsCmd.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileRepository] FTS insert failed: {ex.Message}");
            }

            count++;
        }

        tx.Commit();
        return count;
    }

    /// <summary>ID로 파일 메타데이터를 조회한다.</summary>
    public FileMetadata? GetById(string id)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectColumns + " FROM files WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);

        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadRow(r) : null;
    }

    /// <summary>경로로 파일 메타데이터를 조회한다.</summary>
    public FileMetadata? GetByPath(string path)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectColumns + " FROM files WHERE path = $path";
        cmd.Parameters.AddWithValue("$path", path);

        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadRow(r) : null;
    }

    /// <summary>지정 폴더 하위의 모든 파일 경로를 반환한다.</summary>
    public IEnumerable<string> ListPathsUnderFolder(string folderPath)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        var normalized = folderPath;
        if (OperatingSystem.IsWindows())
            normalized = folderPath.Replace('/', '\\');
        cmd.CommandText = "SELECT path FROM files WHERE path LIKE $pattern";
        cmd.Parameters.AddWithValue("$pattern", normalized + "%");

        var paths = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) paths.Add(r.GetString(0));
        return paths;
    }

    /// <summary>경로 목록에 해당하는 파일을 삭제한다. FTS/embeddings 정리 포함.</summary>
    public int DeleteByPaths(IEnumerable<string> paths)
    {
        var pathList = paths.ToList();
        if (pathList.Count == 0) return 0;

        using var conn = _connectionFactory.CreateConnection();
        using var tx = conn.BeginTransaction();

        using (var cleanCmd = conn.CreateCommand())
        {
            cleanCmd.CommandText = @"
                DELETE FROM chunks_fts WHERE chunk_id IN
                    (SELECT id FROM file_chunks WHERE file_id IN
                        (SELECT id FROM files WHERE path = $path));
                DELETE FROM embeddings WHERE file_id IN
                    (SELECT id FROM files WHERE path = $path);
                DELETE FROM files_fts WHERE file_id IN
                    (SELECT id FROM files WHERE path = $path);";
            var pClean = cleanCmd.Parameters.Add("$path", SqliteType.Text);
            foreach (var path in pathList)
            {
                pClean.Value = path;
                cleanCmd.ExecuteNonQuery();
            }
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM files WHERE path = $path";
        var pPath = cmd.Parameters.Add("$path", SqliteType.Text);

        var count = 0;
        foreach (var path in pathList)
        {
            pPath.Value = path;
            count += cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return count;
    }

    /// <summary>파일의 추출 상태를 갱신한다.</summary>
    public void UpdateExtractStatus(string fileId, string status, string? errorCode = null)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE files SET extract_status = $status, last_extract_error_code = $error
            WHERE id = $id";
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$error", (object?)errorCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", fileId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>복수 파일의 추출 상태를 일괄 갱신한다.</summary>
    public void BatchUpdateExtractStatus(IEnumerable<(string fileId, string status)> updates)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE files SET extract_status = $s WHERE id = $id";
        var pId = cmd.Parameters.Add("$id", SqliteType.Text);
        var pSt = cmd.Parameters.Add("$s", SqliteType.Text);

        foreach (var (id, st) in updates)
        {
            pId.Value = id;
            pSt.Value = st;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>추출 대기(PENDING) 상태인 content-searchable 파일 목록을 반환한다.</summary>
    public IEnumerable<FileMetadata> GetFilesPendingExtraction(int limit = 1000)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectColumns + $@"
            FROM files
            WHERE extract_status = 'PENDING'
              AND is_directory = 0
              AND extension IN ({FileExtensions.ToSqlInClause()})
            ORDER BY path
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        var files = new List<FileMetadata>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) files.Add(ReadRow(r));
        return files;
    }

    /// <summary>추출 대기(PENDING) 상태인 content-searchable 파일 수를 반환한다.</summary>
    public int CountPendingExtraction()
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT COUNT(*) FROM files
            WHERE extract_status = 'PENDING' AND is_directory = 0
              AND extension IN ({FileExtensions.ToSqlInClause()})";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>추출 성공(SUCCESS)인 content-searchable 파일 수를 반환한다.</summary>
    public int CountIndexedContentSearchableFiles()
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT COUNT(*) FROM files
            WHERE is_directory = 0
              AND extension IN ({FileExtensions.ToSqlInClause()})
              AND extract_status = 'SUCCESS'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>스캔 stamp용: 파일 수, 폴더 수, content-searchable 파일 수를 단일 쿼리로 반환한다.</summary>
    public (int files, int folders, int contentSearchable) CountScanStampTotals()
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        // ContentSearchableFiles excludes SKIPPED files (cloud, intentionally excluded)
        // so that IndexingPercent reflects actual indexable files only
        cmd.CommandText = $@"
            SELECT
                SUM(CASE WHEN is_directory = 0 THEN 1 ELSE 0 END),
                SUM(CASE WHEN is_directory = 1 THEN 1 ELSE 0 END),
                SUM(CASE WHEN is_directory = 0
                          AND extension IN ({FileExtensions.ToSqlInClause()})
                          AND extract_status != 'SKIPPED'
                     THEN 1 ELSE 0 END)
            FROM files";
        using var r = cmd.ExecuteReader();
        if (r.Read())
            return (
                r.IsDBNull(0) ? 0 : r.GetInt32(0),
                r.IsDBNull(1) ? 0 : r.GetInt32(1),
                r.IsDBNull(2) ? 0 : r.GetInt32(2));
        return (0, 0, 0);
    }

    /// <summary>content-searchable 파일 중 skip 카테고리별 건수를 반환한다.</summary>
    public (int cloud, int tooLarge, int encrypted, int parseError) CountSkippedByCategory()
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        // Cloud files use extract_status='SKIPPED' (set by FileScanner)
        cmd.CommandText = @"
            SELECT
                SUM(CASE WHEN extract_status = 'SKIPPED' THEN 1 ELSE 0 END),
                SUM(CASE WHEN extract_status = 'FAILED_TOO_LARGE' THEN 1 ELSE 0 END),
                SUM(CASE WHEN extract_status = 'ENCRYPTED' THEN 1 ELSE 0 END),
                SUM(CASE WHEN extract_status NOT IN ('SUCCESS','PENDING','SKIPPED',
                    'FAILED_TOO_LARGE','ENCRYPTED')
                    AND extract_status IS NOT NULL THEN 1 ELSE 0 END)
            FROM files
            WHERE is_directory = 0";
        using var r = cmd.ExecuteReader();
        if (r.Read())
            return (
                r.IsDBNull(0) ? 0 : r.GetInt32(0),
                r.IsDBNull(1) ? 0 : r.GetInt32(1),
                r.IsDBNull(2) ? 0 : r.GetInt32(2),
                r.IsDBNull(3) ? 0 : r.GetInt32(3));
        return (0, 0, 0, 0);
    }

    /// <summary>FTS5/LIKE를 이용한 파일명 검색.</summary>
    public IEnumerable<FileMetadata> SearchByFilename(string query, int limit = 20)
    {
        var tokens = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return [];

        bool useFts = tokens.Length == 1 && (IsKorean(tokens[0]) || tokens[0].Length >= 4);

        if (useFts)
            return SearchByFilenameFts5(tokens[0], limit);

        return SearchByFilenameLike(tokens, limit);
    }

    /// <summary>FRN으로 파일 경로를 조회한다.</summary>
    public async Task<string?> GetFilePathByFrnAsync(long frn, string drivePrefix)
    {
        return await Task.Run(() =>
        {
            using var conn = _connectionFactory.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT path FROM files
                WHERE file_ref_number = $frn AND path LIKE $prefix || '%'
                LIMIT 1";
            cmd.Parameters.AddWithValue("$frn", frn);
            cmd.Parameters.AddWithValue("$prefix", drivePrefix);
            return cmd.ExecuteScalar() as string;
        });
    }

    /// <summary>파일 메타데이터(크기, 수정일)를 갱신한다.</summary>
    public async Task UpdateMetadataAsync(string filePath, long fileSize, DateTime modifiedAt)
    {
        await Task.Run(() =>
        {
            using var conn = _connectionFactory.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE files SET
                    size_bytes  = $size,
                    modified_at = $mtime,
                    mtime_ms    = $mtime_ms,
                    indexed_at  = $indexed_at
                WHERE path = $path";
            cmd.Parameters.AddWithValue("$size", fileSize);
            cmd.Parameters.AddWithValue("$mtime", modifiedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$mtime_ms", new DateTimeOffset(modifiedAt).ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$indexed_at", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$path", filePath);
            cmd.ExecuteNonQuery();
        });
    }

    /// <summary>경로로 파일을 삭제한다 (FTS/chunks/embeddings 정리 포함).</summary>
    public async Task DeleteByPathAsync(string filePath)
    {
        await Task.Run(() =>
        {
            using var conn = _connectionFactory.CreateConnection();
            using var tx = conn.BeginTransaction();

            using (var cleanCmd = conn.CreateCommand())
            {
                cleanCmd.CommandText = @"
                    DELETE FROM chunks_fts WHERE chunk_id IN
                        (SELECT id FROM file_chunks WHERE file_id =
                            (SELECT id FROM files WHERE path = $path));
                    DELETE FROM embeddings WHERE file_id =
                        (SELECT id FROM files WHERE path = $path);
                    DELETE FROM files_fts WHERE file_id IN
                        (SELECT id FROM files WHERE path = $path);";
                cleanCmd.Parameters.AddWithValue("$path", filePath);
                cleanCmd.ExecuteNonQuery();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM files WHERE path = $path";
            cmd.Parameters.AddWithValue("$path", filePath);
            cmd.ExecuteNonQuery();

            tx.Commit();
        });
    }

    /// <summary>경로에 파일이 존재하는지 확인한다.</summary>
    public async Task<bool> ExistsByPathAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var conn = _connectionFactory.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM files WHERE path = $path LIMIT 1";
            cmd.Parameters.AddWithValue("$path", filePath);
            return cmd.ExecuteScalar() != null;
        });
    }

    // ─────────────────────────── private ───────────────────────────

    private const string SelectColumns = @"
        SELECT id, path, filename, extension, size_bytes, modified_at, indexed_at,
               folder_path, mtime_ms, content, content_updated_at, extract_status,
               last_extract_error_code, chunk_count, last_chunked_at, content_hash,
               is_directory, file_ref_number";

    private static FileMetadata ReadRow(SqliteDataReader r)
    {
        return new FileMetadata
        {
            Id                  = r.GetString(0),
            Path                = r.GetString(1),
            Filename            = r.GetString(2),
            Extension           = r.GetString(3),
            SizeBytes           = r.GetInt64(4),
            ModifiedAt          = r.GetString(5),
            IndexedAt           = r.GetString(6),
            FolderPath          = r.GetString(7),
            MtimeMs             = r.GetInt64(8),
            Content             = r.IsDBNull(9) ? null : r.GetString(9),
            ContentUpdatedAt    = r.IsDBNull(10) ? null : r.GetString(10),
            ExtractStatus       = r.IsDBNull(11) ? ExtractStatuses.Pending : r.GetString(11),
            LastExtractErrorCode= r.IsDBNull(12) ? null : r.GetString(12),
            ChunkCount          = r.IsDBNull(13) ? 0 : r.GetInt32(13),
            LastChunkedAt       = r.IsDBNull(14) ? null : r.GetString(14),
            ContentHash         = r.IsDBNull(15) ? null : r.GetString(15),
            IsDirectory         = !r.IsDBNull(16) && r.GetInt32(16) == 1,
            FileRefNumber       = r.IsDBNull(17) ? 0 : r.GetInt64(17),
        };
    }

    private static void BindFileParams(SqliteCommand cmd, FileMetadata f)
    {
        cmd.Parameters.AddWithValue("$id", f.Id);
        cmd.Parameters.AddWithValue("$path", f.Path);
        cmd.Parameters.AddWithValue("$filename", f.Filename);
        cmd.Parameters.AddWithValue("$ext", f.Extension);
        cmd.Parameters.AddWithValue("$size", f.SizeBytes);
        cmd.Parameters.AddWithValue("$modified_at", f.ModifiedAt);
        cmd.Parameters.AddWithValue("$indexed_at", f.IndexedAt);
        cmd.Parameters.AddWithValue("$folder_path", f.FolderPath);
        cmd.Parameters.AddWithValue("$mtime_ms", f.MtimeMs);
        cmd.Parameters.AddWithValue("$is_dir", f.IsDirectory ? 1 : 0);
        cmd.Parameters.AddWithValue("$frn", f.FileRefNumber == 0 ? DBNull.Value : f.FileRefNumber);
        cmd.Parameters.AddWithValue("$extract_status", f.ExtractStatus);
    }

    /// <summary>경로에서 SHA-256 기반 16자 file ID를 생성한다.</summary>
    public static string GenerateFileId(string filePath)
    {
        var normalized = filePath.Replace('\\', '/').ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    private IEnumerable<FileMetadata> SearchByFilenameFts5(string token, int limit)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();

        var escaped = token.Replace("\"", "\"\"");
        var ftsQuery = $"\"{escaped}\"*";

        cmd.CommandText = SelectColumns + @"
            FROM files
            WHERE id IN (SELECT file_id FROM files_fts WHERE files_fts MATCH $fts)
            ORDER BY modified_at DESC
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$fts", ftsQuery);
        cmd.Parameters.AddWithValue("$limit", limit);

        var files = new List<FileMetadata>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) files.Add(ReadRow(r));

        if (files.Count == 0)
            return SearchByFilenameLike(new[] { token }, limit);

        return files;
    }

    private IEnumerable<FileMetadata> SearchByFilenameLike(string[] tokens, int limit)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();

        var conditions = new List<string>();
        for (int i = 0; i < tokens.Length; i++)
        {
            conditions.Add($"(filename LIKE $t{i} COLLATE NOCASE OR path LIKE $t{i} COLLATE NOCASE)");
            cmd.Parameters.AddWithValue($"$t{i}", $"%{tokens[i]}%");
        }

        cmd.CommandText = SelectColumns + $@"
            FROM files
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY modified_at DESC
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        var files = new List<FileMetadata>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) files.Add(ReadRow(r));
        return files;
    }

    private static bool IsKorean(string text)
    {
        return text.Any(c => (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0x3131 && c <= 0x318E));
    }

    /// <summary>Get path → mtime_ms for all non-directory files.</summary>
    public Dictionary<string, long> GetAllFileMtimes()
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT path, mtime_ms FROM files WHERE is_directory = 0";
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            result[reader.GetString(0)] = reader.GetInt64(1);
        }
        return result;
    }
}
