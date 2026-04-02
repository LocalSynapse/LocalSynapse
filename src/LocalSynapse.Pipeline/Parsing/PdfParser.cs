using System.Diagnostics;
using System.Text;
using LocalSynapse.Pipeline.Interfaces;
using UglyToad.PdfPig;

namespace LocalSynapse.Pipeline.Parsing;

/// <summary>
/// PDF 파서. UglyToad.PdfPig를 사용하여 페이지별 텍스트 추출.
/// </summary>
internal static class PdfParser
{
    /// <summary>PDF 파일에서 텍스트를 추출한다.</summary>
    public static Task<ExtractionResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var document = PdfDocument.Open(filePath);
                var sb = new StringBuilder();

                foreach (var page in document.GetPages())
                {
                    ct.ThrowIfCancellationRequested();
                    var pageText = page.Text;
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        sb.AppendLine(pageText);
                    }
                }

                return ExtractionResult.Ok(sb.ToString());
            }
            catch (Exception ex) when (ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase)
                                    || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"[PdfParser] Encrypted PDF: {filePath}");
                return ExtractionResult.Fail("ENCRYPTED", ex.Message);
            }
        }, ct);
    }
}
