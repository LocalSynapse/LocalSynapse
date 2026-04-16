using System.Diagnostics;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using LocalSynapse.Core.Diagnostics;
using LocalSynapse.Pipeline.Interfaces;
using A = DocumentFormat.OpenXml.Drawing;

namespace LocalSynapse.Pipeline.Parsing;

/// <summary>
/// PPTX 파서. 슬라이드별 텍스트를 추출하고 OriginMeta에 슬라이드 번호를 기록한다.
/// </summary>
internal static class PptxParser
{
    /// <summary>PPTX 파일에서 텍스트를 추출한다.</summary>
    public static ExtractionResult Parse(string filePath)
    {
        long sizeBytes = -1;
        try { sizeBytes = new FileInfo(filePath).Length; }
        catch (Exception sEx) { Debug.WriteLine($"[PptxParser] Size probe: {sEx.Message}"); }

        var openSw = Stopwatch.StartNew();
        using var doc = PresentationDocument.Open(filePath, false);
        var presentationPart = doc.PresentationPart;
        if (presentationPart == null)
            return ExtractionResult.Ok("");

        var slideIds = presentationPart.Presentation.SlideIdList?.Elements<SlideId>().ToList();
        openSw.Stop();
        SpeedDiagLog.Log("PARSE_DETAIL",
            "ext", ".pptx", "stage", "open",
            "time_ms", openSw.ElapsedMilliseconds,
            "slide_count", slideIds?.Count ?? 0, "size_bytes", sizeBytes);
        if (slideIds == null || slideIds.Count == 0)
            return ExtractionResult.Ok("");

        var slidesSw = Stopwatch.StartNew();
        var sb = new StringBuilder();
        var slideNum = 0;

        foreach (var slideId in slideIds)
        {
            slideNum++;
            var slidePart = presentationPart.GetPartById(slideId.RelationshipId!) as SlidePart;
            if (slidePart == null) continue;

            var texts = slidePart.Slide.Descendants<A.Text>()
                .Select(t => t.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t));

            var slideText = string.Join(" ", texts);
            if (!string.IsNullOrEmpty(slideText))
            {
                sb.AppendLine($"[Slide {slideNum}]");
                sb.AppendLine(slideText);
            }
        }
        slidesSw.Stop();
        SpeedDiagLog.Log("PARSE_DETAIL",
            "ext", ".pptx", "stage", "slides",
            "time_ms", slidesSw.ElapsedMilliseconds);

        return ExtractionResult.Ok(sb.ToString(), "slide", null);
    }
}
