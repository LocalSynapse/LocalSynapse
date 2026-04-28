using System.Diagnostics;
using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Pipeline.Interfaces;

namespace LocalSynapse.Pipeline.Scanning;

/// <summary>
/// Scans fixed drives recursively to collect file metadata into DB.
/// All disk I/O runs on a background thread via Task.Run.
/// Cloud-only files are detected via FileSystemEntry.Attributes (zero I/O, no cloud recall).
/// Unchanged files (same mtime) are skipped for fast re-scans.
/// </summary>
public sealed class FileScanner : IFileScanner
{
    private readonly IFileRepository _fileRepo;
    private readonly IPipelineStampRepository _stampRepo;
    private readonly ISettingsStore _settingsStore;
    private readonly string[]? _scanRoots;
    private const int BatchSize = 500;

    /// <summary>Production constructor.</summary>
    public FileScanner(IFileRepository fileRepo, IPipelineStampRepository stampRepo, ISettingsStore settingsStore)
    {
        _fileRepo = fileRepo;
        _stampRepo = stampRepo;
        _settingsStore = settingsStore;
    }

    /// <summary>Test constructor with explicit scan roots.</summary>
    public FileScanner(IFileRepository fileRepo, IPipelineStampRepository stampRepo, string[] scanRoots)
        : this(fileRepo, stampRepo, new NullSettingsStoreForTest())
    {
        _scanRoots = scanRoots;
    }

    /// <summary>Minimal ISettingsStore for test constructor (returns defaults).</summary>
    private sealed class NullSettingsStoreForTest : ISettingsStore
    {
        public string GetLanguage() => "en";
        public void SetLanguage(string cultureName) { }
        public string GetDataFolder() => "";
        public string GetLogFolder() => "";
        public string GetModelFolder() => "";
        public string GetDatabasePath() => "";
        public string[]? GetScanRoots() => null;
        public void SetScanRoots(string[]? roots) { }
    }

    /// <summary>Scan all fixed drives and upsert file metadata to DB.</summary>
    public Task<ScanResult> ScanAllDrivesAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() => ScanAllDrivesCore(progress, ct), ct);
    }

    private ScanResult ScanAllDrivesCore(IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = new ScanResult();

        var rootPaths = _scanRoots != null
            ? _scanRoots.ToList()
            : (_settingsStore.GetScanRoots()?.ToList() ?? GetDefaultScanRoots());

        Debug.WriteLine($"[Scan] Starting scan on roots: {string.Join(", ", rootPaths)}");

        // Load existing mtime_ms for unchanged file detection (performance critical)
        var existingMtimes = _fileRepo.GetAllFileMtimes();
        var cloudSkippedPaths = _fileRepo.GetCloudSkippedPaths();
        Debug.WriteLine($"[Scan] Loaded {existingMtimes.Count} existing file mtimes for skip detection");

        var batch = new List<FileMetadata>(BatchSize);
        var totalFiles = 0;
        var unchangedSkipped = 0;
        var cloudSkipped = 0;
        var filteredSkipped = 0;
        var totalFolders = 0;
        var lastProgressReport = Stopwatch.StartNew();

        foreach (var rootPath in rootPaths)
        {
            ct.ThrowIfCancellationRequested();
            ReportProgress(progress, rootPath, totalFiles, totalFolders);

            var stack = new Stack<string>();
            stack.Push(rootPath);

            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var currentDir = stack.Pop();

                // Enumerate subdirectories
                foreach (var subDir in EnumerateDirectoriesSafe(currentDir, ct))
                {
                    if (ScanFilterHelper.ShouldSkipDirectory(subDir.Name, subDir.Attributes))
                        continue;

                    stack.Push(subDir.FullPath);
                    totalFolders++;

                    batch.Add(new FileMetadata
                    {
                        Id = GenerateFileId(subDir.FullPath),
                        Path = subDir.FullPath,
                        Filename = subDir.Name,
                        Extension = "",
                        SizeBytes = 0,
                        ModifiedAt = DateTime.UtcNow.ToString("o"),
                        IndexedAt = DateTime.UtcNow.ToString("o"),
                        FolderPath = Path.GetDirectoryName(subDir.FullPath) ?? "",
                        IsDirectory = true,
                        ExtractStatus = ExtractStatuses.Skipped
                    });
                }

                // Enumerate files
                foreach (var file in EnumerateFilesSafe(currentDir, ct))
                {
                    totalFiles++;

                    // --- Filter 1: System / ReparsePoint / size ---
                    if (ScanFilterHelper.ShouldSkipFile(file.Attributes, file.Length))
                    {
                        filteredSkipped++;
                        continue;
                    }

                    // --- Filter 2: Unchanged file (same mtime = no modification) ---
                    if (existingMtimes.TryGetValue(file.FullPath, out var existingMtime)
                        && existingMtime == file.MtimeMs
                        && !cloudSkippedPaths.Contains(file.FullPath))
                    {
                        unchangedSkipped++;

                        if (lastProgressReport.ElapsedMilliseconds > 200)
                        {
                            ReportProgress(progress, rootPath, totalFiles, totalFolders);
                            lastProgressReport.Restart();
                        }
                        continue;
                    }

                    // --- Filter 3: Cloud placeholder (add to DB but skip content) ---
                    var isCloud = ScanFilterHelper.IsCloudPlaceholder(file.Attributes);
                    if (isCloud) cloudSkipped++;

                    batch.Add(new FileMetadata
                    {
                        Id = GenerateFileId(file.FullPath),
                        Path = file.FullPath,
                        Filename = file.FileName,
                        Extension = file.Extension,
                        SizeBytes = file.Length,
                        ModifiedAt = file.LastWriteUtc.ToString("o"),
                        IndexedAt = DateTime.UtcNow.ToString("o"),
                        FolderPath = Path.GetDirectoryName(file.FullPath) ?? "",
                        MtimeMs = file.MtimeMs,
                        IsDirectory = false,
                        ExtractStatus = isCloud ? ExtractStatuses.Skipped : ExtractStatuses.Pending,
                        LastExtractErrorCode = isCloud ? "CLOUD_FILE" : null,
                    });

                    if (batch.Count >= BatchSize)
                    {
                        result.FilesUpserted += _fileRepo.UpsertFiles(batch);
                        batch.Clear();
                    }

                    if (lastProgressReport.ElapsedMilliseconds > 200)
                    {
                        ReportProgress(progress, rootPath, totalFiles, totalFolders);
                        lastProgressReport.Restart();
                    }
                }

                if (totalFiles % 5000 == 0)
                {
                    Debug.WriteLine($"[Scan] Progress: {totalFiles:N0} total, " +
                        $"{unchangedSkipped:N0} unchanged, {cloudSkipped:N0} cloud, " +
                        $"{filteredSkipped:N0} filtered");
                }
            }
        }

        // Flush remaining
        if (batch.Count > 0)
        {
            result.FilesUpserted += _fileRepo.UpsertFiles(batch);
            batch.Clear();
        }

        // Stamp scan complete
        var (stampFiles, stampFolders, stampCS) = _fileRepo.CountScanStampTotals();
        _stampRepo.StampScanComplete(stampFiles, stampFolders, stampCS);

        result.FilesDiscovered = totalFiles;
        result.FoldersDiscovered = totalFolders;
        result.Duration = sw.Elapsed;

        Debug.WriteLine($"[Scan] Complete in {sw.Elapsed.TotalSeconds:F1}s: " +
            $"{totalFiles:N0} files, {result.FilesUpserted:N0} upserted, " +
            $"{unchangedSkipped:N0} unchanged, {cloudSkipped:N0} cloud, " +
            $"{filteredSkipped:N0} filtered");

        return result;
    }

    /// <summary>OS별 기본 스캔 루트를 반환한다.</summary>
    private static List<string> GetDefaultScanRoots()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX))
        {
            // macOS: 사용자 홈 디렉토리만 스캔 (시스템 볼륨 제외)
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home) && Directory.Exists(home))
                return [home];

            // fallback
            return ["/Users"];
        }

        // Windows: 기존 로직 — 모든 고정 드라이브
        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => d.RootDirectory.FullName)
            .ToList();
    }

    private static void ReportProgress(IProgress<ScanProgress>? progress, string drive, int files, int folders)
    {
        progress?.Report(new ScanProgress
        {
            CurrentDrive = drive,
            FilesFound = files,
            FoldersFound = folders
        });
    }

    /// <summary>
    /// Enumerate files safely. Returns tuples with FileAttributes captured
    /// directly from FileSystemEntry (WIN32_FIND_DATA) — no separate I/O call,
    /// so cloud recall is physically impossible.
    /// </summary>
    private static List<ScannedFile> EnumerateFilesSafe(string path, CancellationToken ct)
    {
        var results = new List<ScannedFile>();
        try
        {
            var enumerable = new FileSystemEnumerable<ScannedFile>(
                path,
                (ref FileSystemEntry entry) =>
                {
                    var name = entry.FileName.ToString();
                    return new ScannedFile
                    {
                        FullPath = entry.ToFullPath(),
                        FileName = name,
                        Extension = Path.GetExtension(name).ToLowerInvariant(),
                        Length = entry.Length,
                        MtimeMs = new DateTimeOffset(entry.LastWriteTimeUtc.UtcDateTime).ToUnixTimeMilliseconds(),
                        LastWriteUtc = entry.LastWriteTimeUtc.UtcDateTime,
                        Attributes = entry.Attributes,
                    };
                },
                new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = true,
                    AttributesToSkip = 0,  // we filter ourselves
                    ReturnSpecialDirectories = false,
                })
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory
            };

            foreach (var item in enumerable)
            {
                if (ct.IsCancellationRequested) break;
                results.Add(item);
            }
        }
        catch (UnauthorizedAccessException) { Debug.WriteLine($"[Scan] EnumerateFiles access denied: {path}"); }
        catch (IOException ex) { Debug.WriteLine($"[Scan] EnumerateFiles I/O error: {path}, {ex.Message}"); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Scan] EnumerateFiles failed: {path}, {ex.Message}");
        }
        return results;
    }

    private static List<(string FullPath, string Name, FileAttributes Attributes)>
        EnumerateDirectoriesSafe(string path, CancellationToken ct)
    {
        var results = new List<(string, string, FileAttributes)>();
        try
        {
            var enumerable = new FileSystemEnumerable<(string, string, FileAttributes)>(
                path,
                (ref FileSystemEntry entry) => (
                    entry.ToFullPath(),
                    entry.FileName.ToString(),
                    entry.Attributes
                ),
                new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = true,
                    AttributesToSkip = 0,
                    ReturnSpecialDirectories = false,
                })
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) => entry.IsDirectory
            };

            foreach (var item in enumerable)
            {
                if (ct.IsCancellationRequested) break;
                results.Add(item);
            }
        }
        catch (UnauthorizedAccessException) { Debug.WriteLine($"[Scan] EnumerateDirectories access denied: {path}"); }
        catch (IOException ex) { Debug.WriteLine($"[Scan] EnumerateDirectories I/O error: {path}, {ex.Message}"); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Scan] EnumerateDirectories failed: {path}, {ex.Message}");
        }
        return results;
    }

    private static string GenerateFileId(string filePath)
    {
        var normalized = filePath.Replace('\\', '/').ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    /// <summary>Internal struct for scanned file data, captured from FileSystemEntry.</summary>
    private readonly struct ScannedFile
    {
        public required string FullPath { get; init; }
        public required string FileName { get; init; }
        public required string Extension { get; init; }
        public required long Length { get; init; }
        public required long MtimeMs { get; init; }
        public required DateTime LastWriteUtc { get; init; }
        public required FileAttributes Attributes { get; init; }
    }
}
