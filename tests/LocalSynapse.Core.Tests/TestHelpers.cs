using LocalSynapse.Core.Models;

namespace LocalSynapse.Core.Tests;

internal static class TestHelpers
{
    /// <summary>Create a minimal FileMetadata for testing UpsertFiles.</summary>
    public static FileMetadata CreateTestFile(string path, string? filename = null)
    {
        var fn = filename ?? System.IO.Path.GetFileName(path);
        return new FileMetadata
        {
            Id = "",  // UpsertFiles internally overwrites with GenerateFileId(path)
            Path = path,
            Filename = fn,
            Extension = System.IO.Path.GetExtension(path),
            SizeBytes = 1000,
            ModifiedAt = DateTime.UtcNow.ToString("o"),
            IndexedAt = DateTime.UtcNow.ToString("o"),  // UpsertFiles overwrites with batch value (W3)
            FolderPath = System.IO.Path.GetDirectoryName(path) ?? "",
            MtimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            IsDirectory = false,
            ExtractStatus = ExtractStatuses.Success,
        };
    }
}
