using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
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
        try
        {
            using var doc = WordprocessingDocument.Open(filePath, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null)
                return ExtractionResult.Ok("");

            var sb = new StringBuilder();

            foreach (var element in body.ChildElements)
            {
                if (element is Paragraph para)
                {
                    var text = para.InnerText;
                    if (!string.IsNullOrWhiteSpace(text))
                        sb.AppendLine(text);
                }
                else if (element is Table table)
                {
                    foreach (var row in table.Descendants<TableRow>())
                    {
                        var cells = row.Descendants<TableCell>()
                            .Select(c => c.InnerText.Trim());
                        sb.AppendLine(string.Join("\t", cells));
                    }
                }
            }

            return ExtractionResult.Ok(sb.ToString());
        }
        catch (DocumentFormat.OpenXml.Packaging.OpenXmlPackageException ex)
            when (ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractionResult.Fail("ENCRYPTED", ex.Message);
        }
    }
}
