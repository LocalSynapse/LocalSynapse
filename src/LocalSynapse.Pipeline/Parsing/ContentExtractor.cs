using System.Diagnostics;
using LocalSynapse.Core.Constants;
using LocalSynapse.Pipeline.Interfaces;
using LocalSynapse.Pipeline.Scanning;

namespace LocalSynapse.Pipeline.Parsing;

/// <summary>
/// 확장자 기반 파서 라우터. 파일 확장자에 따라 적절한 파서를 호출한다.
/// </summary>
public sealed class ContentExtractor : IContentExtractor
{
    /// <summary>지원 확장자인지 확인한다.</summary>
    public bool IsSupported(string extension)
        => FileExtensions.IsContentSearchable(extension);

    /// <summary>파일에서 텍스트를 추출한다.</summary>
    public async Task<ExtractionResult> ExtractAsync(string filePath, string extension, CancellationToken ct = default)
    {
        var ext = extension.ToLowerInvariant();
        if (!ext.StartsWith('.')) ext = "." + ext;

        try
        {
            // Defensive: never open cloud placeholder or cloud sync path files
            if (ScanFilterHelper.IsCloudSyncPath(filePath))
            {
                Debug.WriteLine($"[Extract] Blocked cloud file access: {filePath}");
                return ExtractionResult.Fail("CLOUD_FILE");
            }
            return ext switch
            {
                ".txt" or ".md" or ".csv" or ".json" or ".log" or ".xml"
                    => await PlainTextParser.ParseAsync(filePath, ct),
                ".pdf"
                    => await PdfParser.ParseAsync(filePath, ct),
                ".docx"
                    => await Task.Run(() => DocxParser.Parse(filePath), ct),
                ".xlsx"
                    => await Task.Run(() => XlsxParser.Parse(filePath), ct),
                ".pptx"
                    => await Task.Run(() => PptxParser.Parse(filePath), ct),
                ".hwp"
                    => await Task.Run(() => HwpParser.Parse(filePath), ct),
                ".hwpx"
                    => await HwpxParser.ParseAsync(filePath, ct),
                ".html" or ".htm"
                    => await HtmlParser.ParseAsync(filePath, ct),
                ".rtf"
                    => await PlainTextParser.ParseAsync(filePath, ct),
                ".odt" or ".ods" or ".odp"
                    => await PlainTextParser.ParseAsync(filePath, ct),
                ".eml" or ".msg"
                    => ExtractionResult.Fail("UNSUPPORTED_IN_PIPELINE",
                        "Email parsing is handled by the Email agent"),
                _   => ExtractionResult.Fail("UNSUPPORTED", $"Extension not supported: {ext}")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[ContentExtractor] Access denied: {filePath} - {ex.Message}");
            return ExtractionResult.Fail("ACCESS_DENIED", ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            Debug.WriteLine($"[ContentExtractor] File not found: {filePath} - {ex.Message}");
            return ExtractionResult.Fail("FILE_NOT_FOUND", ex.Message);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ContentExtractor] Extraction failed: {filePath} - {ex.Message}");
            return ExtractionResult.Fail("PARSE_ERROR", ex.Message);
        }
    }
}
