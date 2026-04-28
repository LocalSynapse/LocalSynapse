using LocalSynapse.Core.Models;

namespace LocalSynapse.Core.Interfaces;

public interface IFileRepository
{
    FileMetadata UpsertFile(FileMetadata file);
    int UpsertFiles(IEnumerable<FileMetadata> files);
    FileMetadata? GetById(string id);
    FileMetadata? GetByPath(string path);
    IEnumerable<string> ListPathsUnderFolder(string folderPath);
    int DeleteByPaths(IEnumerable<string> paths);
    void UpdateExtractStatus(string fileId, string status, string? errorCode = null);
    void BatchUpdateExtractStatus(IEnumerable<(string fileId, string status)> updates);
    IEnumerable<FileMetadata> GetFilesPendingExtraction(int limit = 1000);
    int CountPendingExtraction();
    int CountIndexedContentSearchableFiles();
    (int files, int folders, int contentSearchable) CountScanStampTotals();
    (int cloud, int tooLarge, int encrypted, int parseError) CountSkippedByCategory();
    IEnumerable<FileMetadata> SearchByFilename(string query, int limit = 20);
    Task<string?> GetFilePathByFrnAsync(long frn, string drivePrefix);
    Task UpdateMetadataAsync(string filePath, long fileSize, DateTime modifiedAt);
    Task DeleteByPathAsync(string filePath);
    Task<bool> ExistsByPathAsync(string filePath);

    /// <summary>
    /// 지정 폴더 하위의 파일 목록을 단일 쿼리로 반환한다.
    /// folder가 null이면 전체 파일 대상.
    /// </summary>
    IReadOnlyList<FileMetadata> ListFilesUnderFolder(string? folder, string? extension, int limit);

    /// <summary>
    /// Get path → mtime_ms map for all non-directory files.
    /// Used by FileScanner for unchanged file detection (skip files with same mtime).
    /// Single query, loaded once before each scan cycle.
    /// </summary>
    Dictionary<string, long> GetAllFileMtimes();

    /// <summary>Returns paths of files previously skipped as cloud files (for re-evaluation).</summary>
    HashSet<string> GetCloudSkippedPaths();
}
