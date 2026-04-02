namespace LocalSynapse.Core.Models;

public sealed class FolderInfo
{
    public required string Id { get; set; }
    public required string Path { get; set; }
    public required string DisplayName { get; set; }
    public required string AddedAt { get; set; }
    public string? LastScannedAt { get; set; }
    public int FileCount { get; set; }
}
