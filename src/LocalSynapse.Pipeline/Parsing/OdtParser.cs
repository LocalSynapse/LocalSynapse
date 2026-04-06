using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using LocalSynapse.Pipeline.Interfaces;

namespace LocalSynapse.Pipeline.Parsing;

/// <summary>
/// ODF 파일(.odt, .ods, .odp)에서 텍스트를 추출한다.
/// ODF는 ZIP 아카이브 안에 content.xml을 포함하는 구조이다.
/// </summary>
public static partial class OdtParser
{
    /// <summary>ODF 파일을 파싱하여 텍스트를 추출한다.</summary>
    public static async Task<ExtractionResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            using var zip = ZipFile.OpenRead(filePath);
            var contentEntry = zip.GetEntry("content.xml");
            if (contentEntry == null)
                return ExtractionResult.Fail("INVALID_ODF", "No content.xml found in ODF archive");

            using var stream = contentEntry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var xml = await reader.ReadToEndAsync(ct);

            var text = ExtractTextFromXml(xml);
            if (string.IsNullOrWhiteSpace(text))
                return ExtractionResult.Fail("EMPTY", "No text extracted from ODF");

            return new ExtractionResult { Text = text.Trim(), Success = true };
        }
        catch (InvalidDataException ex)
        {
            Debug.WriteLine($"[OdtParser] Invalid ZIP: {ex.Message}");
            return ExtractionResult.Fail("INVALID_ZIP", ex.Message);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OdtParser] Error: {ex.Message}");
            return ExtractionResult.Fail("PARSE_ERROR", ex.Message);
        }
    }

    private static string ExtractTextFromXml(string xml)
    {
        var sb = new StringBuilder();

        // Replace paragraph/heading end tags with newlines
        var withBreaks = ParagraphEndRegex().Replace(xml, "\n");
        // Replace tab elements
        withBreaks = withBreaks.Replace("<text:tab/>", "\t").Replace("<text:tab />", "\t");
        // Replace line breaks
        withBreaks = withBreaks.Replace("<text:line-break/>", "\n").Replace("<text:line-break />", "\n");

        // Strip all remaining XML tags
        var text = XmlTagRegex().Replace(withBreaks, "");

        // Decode common XML entities
        text = text.Replace("&amp;", "&")
                   .Replace("&lt;", "<")
                   .Replace("&gt;", ">")
                   .Replace("&quot;", "\"")
                   .Replace("&apos;", "'");

        // Collapse excessive blank lines
        text = MultiNewlineRegex().Replace(text, "\n\n");

        return text.Trim();
    }

    [GeneratedRegex(@"</text:(p|h)>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphEndRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex XmlTagRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiNewlineRegex();
}
