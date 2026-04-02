namespace LocalSynapse.Pipeline.Interfaces;

public interface IContentExtractor
{
    Task<ExtractionResult> ExtractAsync(string filePath, string extension, CancellationToken ct = default);
    bool IsSupported(string extension);
}

public sealed class ExtractionResult
{
    public bool Success { get; set; }
    public string? Text { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorDetail { get; set; }
    public string SourceType { get; set; } = "text";
    public string? OriginMeta { get; set; }

    public static ExtractionResult Ok(string text, string sourceType = "text", string? meta = null)
        => new() { Success = true, Text = text, SourceType = sourceType, OriginMeta = meta };
    public static ExtractionResult Fail(string code, string? detail = null)
        => new() { Success = false, ErrorCode = code, ErrorDetail = detail };
}
