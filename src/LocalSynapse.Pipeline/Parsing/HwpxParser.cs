using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using LocalSynapse.Pipeline.Interfaces;

namespace LocalSynapse.Pipeline.Parsing;

/// <summary>
/// HWPX 파서. ZIP 내 Contents/*.xml에서 Hancom 문단 텍스트 노드를 추출한다.
/// </summary>
internal static class HwpxParser
{
    private static readonly XNamespace HpNs = "http://www.hancom.co.kr/hwpml/2011/paragraph";

    /// <summary>HWPX 파일에서 텍스트를 추출한다.</summary>
    public static async Task<ExtractionResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var sb = new StringBuilder();

        // Contents/sectionN.xml 파일 정렬 처리
        var sectionEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("Contents/section", StringComparison.OrdinalIgnoreCase)
                     && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName)
            .ToList();

        foreach (var entry in sectionEntries)
        {
            ct.ThrowIfCancellationRequested();

            using var stream = entry.Open();
            var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);

            // 3-tier fallback for text elements
            var texts = doc.Descendants(HpNs + "t").Select(e => e.Value).ToList();

            if (texts.Count == 0)
            {
                texts = doc.Descendants()
                    .Where(e => e.Name.LocalName == "t"
                             && (e.Name.NamespaceName?.Contains("hancom") ?? false))
                    .Select(e => e.Value)
                    .ToList();
            }

            if (texts.Count == 0)
            {
                texts = doc.Descendants()
                    .Where(e => e.Name.LocalName == "t")
                    .Select(e => e.Value)
                    .ToList();
            }

            foreach (var text in texts)
            {
                var trimmed = text.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    sb.AppendLine(trimmed);
            }
        }

        return ExtractionResult.Ok(sb.ToString());
    }
}
