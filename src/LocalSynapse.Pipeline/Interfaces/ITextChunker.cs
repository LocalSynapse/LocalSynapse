namespace LocalSynapse.Pipeline.Interfaces;

public interface ITextChunker
{
    IReadOnlyList<TextChunk> Chunk(string text, string sourceType = "text", string? originMeta = null);
}

public sealed class TextChunk
{
    public required string Text { get; set; }
    public required string ContentHash { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public required string SourceType { get; set; }
    public string? OriginMeta { get; set; }
}
