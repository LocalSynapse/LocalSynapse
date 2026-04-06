using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using LocalSynapse.Pipeline.Interfaces;

namespace LocalSynapse.Pipeline.Parsing;

/// <summary>
/// RTF 파일에서 텍스트를 추출한다.
/// RTF 제어어를 제거하고 순수 텍스트만 반환한다.
/// </summary>
public static partial class RtfParser
{
    /// <summary>RTF 파일을 파싱하여 텍스트를 추출한다.</summary>
    public static async Task<ExtractionResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var raw = await File.ReadAllTextAsync(filePath, Encoding.Default, ct);
            if (string.IsNullOrWhiteSpace(raw))
                return ExtractionResult.Fail("EMPTY", "RTF file is empty");

            var text = StripRtf(raw);
            if (string.IsNullOrWhiteSpace(text))
                return ExtractionResult.Fail("EMPTY", "No text extracted from RTF");

            return new ExtractionResult { Text = text.Trim(), Success = true };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RtfParser] Error: {ex.Message}");
            return ExtractionResult.Fail("PARSE_ERROR", ex.Message);
        }
    }

    private static string StripRtf(string rtf)
    {
        // Remove {\*\...} groups (optional destinations)
        var result = OptionalDestRegex().Replace(rtf, "");

        // Remove \' hex-encoded characters → replace with actual char
        result = HexCharRegex().Replace(result, m =>
        {
            var hex = m.Groups[1].Value;
            return ((char)Convert.ToByte(hex, 16)).ToString();
        });

        // Remove all remaining control words (\keyword[N])
        result = ControlWordRegex().Replace(result, m =>
        {
            var word = m.Groups[1].Value;
            // \par and \line → newline
            if (word is "par" or "line") return "\n";
            // \tab → tab
            if (word == "tab") return "\t";
            return "";
        });

        // Remove braces
        result = result.Replace("{", "").Replace("}", "");

        // Collapse multiple blank lines
        result = MultiNewlineRegex().Replace(result, "\n\n");

        return result.Trim();
    }

    [GeneratedRegex(@"\{\\\*\\[^{}]*\}", RegexOptions.Singleline)]
    private static partial Regex OptionalDestRegex();

    [GeneratedRegex(@"\\'([0-9a-fA-F]{2})")]
    private static partial Regex HexCharRegex();

    [GeneratedRegex(@"\\([a-z]+)-?\d*\s?", RegexOptions.IgnoreCase)]
    private static partial Regex ControlWordRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiNewlineRegex();
}
