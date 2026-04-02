using System.Net;
using System.Text.RegularExpressions;
using LocalSynapse.Pipeline.Interfaces;

namespace LocalSynapse.Pipeline.Parsing;

/// <summary>
/// HTML 파서. 정규식으로 태그를 제거하고 HTML 엔티티를 디코딩한다.
/// </summary>
internal static partial class HtmlParser
{
    private const long MaxSizeBytes = 10 * 1024 * 1024; // 10MB

    /// <summary>HTML 파일에서 텍스트를 추출한다.</summary>
    public static async Task<ExtractionResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MaxSizeBytes)
            return ExtractionResult.Fail("TOO_LARGE", $"File size {fileInfo.Length} exceeds 10MB limit");

        var html = await File.ReadAllTextAsync(filePath, ct);

        // Remove <script> and <style> blocks
        html = ScriptRegex().Replace(html, " ");
        html = StyleRegex().Replace(html, " ");

        // Remove HTML tags
        html = TagRegex().Replace(html, " ");

        // Decode HTML entities
        html = WebUtility.HtmlDecode(html);

        // Collapse whitespace
        html = WhitespaceRegex().Replace(html, " ").Trim();

        return ExtractionResult.Ok(html);
    }

    [GeneratedRegex(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
