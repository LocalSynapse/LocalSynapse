using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
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
        using var doc = PresentationDocument.Open(filePath, false);
        var presentationPart = doc.PresentationPart;
        if (presentationPart == null)
            return ExtractionResult.Ok("");

        var slideIds = presentationPart.Presentation.SlideIdList?.Elements<SlideId>().ToList();
        if (slideIds == null || slideIds.Count == 0)
            return ExtractionResult.Ok("");

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

        return ExtractionResult.Ok(sb.ToString(), "slide", null);
    }
}
