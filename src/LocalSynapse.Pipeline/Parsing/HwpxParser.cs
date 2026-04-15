using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using LocalSynapse.Core.Diagnostics;
using LocalSynapse.Pipeline.Interfaces;

namespace LocalSynapse.Pipeline.Parsing;

/// <summary>
/// HWPX 파서. ZIP 내 Contents/*.xml에서 Hancom 문단 텍스트 노드를 추출한다.
/// </summary>
internal static class HwpxParser
{
    private const long MaxEntrySize = 50_000_000; // 50MB
    private static readonly XmlReaderSettings SafeXmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        MaxCharactersInDocument = 100_000_000, // 100M chars — layered defense with byte cap
        Async = true
    };

    private static readonly XNamespace HpNs = "http://www.hancom.co.kr/hwpml/2011/paragraph";

    /// <summary>HWPX 파일에서 텍스트를 추출한다.</summary>
    public static async Task<ExtractionResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        long sizeBytes = -1;
        try { sizeBytes = new FileInfo(filePath).Length; }
        catch (Exception sEx) { Debug.WriteLine($"[HwpxParser] Size probe: {sEx.Message}"); }

        var zipSw = Stopwatch.StartNew();
        using var archive = ZipFile.OpenRead(filePath);
        var sb = new StringBuilder();

        // Contents/sectionN.xml 파일 정렬 처리
        var sectionEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("Contents/section", StringComparison.OrdinalIgnoreCase)
                     && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName)
            .ToList();
        zipSw.Stop();
        SpeedDiagLog.Log("PARSE_DETAIL",
            "ext", ".hwpx", "stage", "zip_open",
            "time_ms", zipSw.ElapsedMilliseconds,
            "section_count", sectionEntries.Count, "size_bytes", sizeBytes);

        var xmlSw = Stopwatch.StartNew();
        foreach (var entry in sectionEntries)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.Length > MaxEntrySize)
            {
                Debug.WriteLine($"[HwpxParser] Skipping oversized entry: {entry.FullName} ({entry.Length} bytes)");
                continue;
            }

            using var stream = entry.Open();
            using var xmlReader = XmlReader.Create(stream, SafeXmlSettings);
            var doc = await XDocument.LoadAsync(xmlReader, LoadOptions.None, ct);

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
        xmlSw.Stop();
        SpeedDiagLog.Log("PARSE_DETAIL",
            "ext", ".hwpx", "stage", "xml_parse",
            "time_ms", xmlSw.ElapsedMilliseconds);

        return ExtractionResult.Ok(sb.ToString());
    }
}
