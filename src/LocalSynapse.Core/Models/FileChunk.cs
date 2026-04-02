namespace LocalSynapse.Core.Models;

public sealed class FileChunk
{
    public required string Id { get; set; }
    public required string FileId { get; set; }
    public int ChunkIndex { get; set; }
    public required string Text { get; set; }
    public required string SourceType { get; set; }
    public string? OriginMeta { get; set; }
    public int? TokenCount { get; set; }
    public required string ContentHash { get; set; }
    public required string CreatedAt { get; set; }
    public int? StartOffset { get; set; }
    public int? EndOffset { get; set; }
}

public static class ChunkSourceTypes
{
    public const string Text = "text";
    public const string Table = "table";
    public const string Slide = "slide";
    public const string Sheet = "sheet";
    public const string EmailBody = "email_body";
}
