using System.Diagnostics;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LocalSynapse.Core.Diagnostics;
using LocalSynapse.Pipeline.Interfaces;

namespace LocalSynapse.Pipeline.Parsing;

/// <summary>
/// DOCX 파서. DocumentFormat.OpenXml을 사용하여 문단/테이블 텍스트 추출.
/// </summary>
internal static class DocxParser
{
    /// <summary>DOCX 파일에서 텍스트를 추출한다.</summary>
    public static ExtractionResult Parse(string filePath)
    {
        long sizeBytes = -1;
        try { sizeBytes = new FileInfo(filePath).Length; }
        catch (Exception sEx) { Debug.WriteLine($"[DocxParser] Size probe: {sEx.Message}"); }
        try
        {
            var openSw = Stopwatch.StartNew();
            using var doc = WordprocessingDocument.Open(filePath, false);
            openSw.Stop();
            SpeedDiagLog.Log("PARSE_DETAIL",
                "ext", ".docx", "stage", "open",
                "time_ms", openSw.ElapsedMilliseconds, "size_bytes", sizeBytes);

            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null)
                return ExtractionResult.Ok("");

            var bodySw = Stopwatch.StartNew();
            var sb = new StringBuilder();

            foreach (var element in body.ChildElements)
            {
                if (element is Paragraph para)
                {
                    var text = string.Concat(para.Descendants<Run>().Select(r => r.InnerText));
                    if (!string.IsNullOrWhiteSpace(text))
                        sb.AppendLine(text);
                }
                else if (element is Table table)
                {
                    foreach (var row in table.Descendants<TableRow>())
                    {
                        var cells = row.Descendants<TableCell>()
                            .Select(c => string.Concat(c.Descendants<Run>().Select(r => r.InnerText)).Trim());
                        sb.AppendLine(string.Join("\t", cells));
                    }
                }
            }
            bodySw.Stop();
            SpeedDiagLog.Log("PARSE_DETAIL",
                "ext", ".docx", "stage", "body",
                "time_ms", bodySw.ElapsedMilliseconds);

            return ExtractionResult.Ok(sb.ToString());
        }
        catch (DocumentFormat.OpenXml.Packaging.OpenXmlPackageException ex)
            when (ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractionResult.Fail("ENCRYPTED", ex.Message);
        }
    }
}
