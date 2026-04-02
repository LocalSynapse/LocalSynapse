namespace LocalSynapse.Core.Models;

public sealed class FileMetadata
{
    public required string Id { get; set; }
    public required string Path { get; set; }
    public required string Filename { get; set; }
    public required string Extension { get; set; }
    public long SizeBytes { get; set; }
    public required string ModifiedAt { get; set; }
    public required string IndexedAt { get; set; }
    public required string FolderPath { get; set; }
    public long MtimeMs { get; set; }
    public string? Content { get; set; }
    public string? ContentUpdatedAt { get; set; }
    public string ExtractStatus { get; set; } = ExtractStatuses.Pending;
    public string? LastExtractErrorCode { get; set; }
    public int ChunkCount { get; set; }
    public string? LastChunkedAt { get; set; }
    public string? ContentHash { get; set; }
    public bool IsDirectory { get; set; }
    public long FileRefNumber { get; set; }
}

public static class ExtractStatuses
{
    public const string Pending = "PENDING";
    public const string Success = "SUCCESS";
    public const string Skipped = "SKIPPED";
    public const string Error = "ERROR";
}
