using System.Text.RegularExpressions;
using LocalSynapse.Core.Models;
using LocalSynapse.Search.Interfaces;

namespace LocalSynapse.Search.Services;

/// <summary>
/// 검색 결과를 폴더+파일명 패턴 기반으로 DocumentFamily 그룹으로 묶는다.
/// </summary>
public sealed partial class DocumentFamilyService : IDocumentFamilyService
{
    /// <summary>HybridHit 목록을 문서 계열로 그룹핑한다.</summary>
    public IReadOnlyList<DocumentFamily> GroupResults(IEnumerable<HybridHit> hits)
    {
        var groups = new Dictionary<string, DocumentFamily>();

        foreach (var hit in hits)
        {
            var key = GenerateFamilyKey(hit.Filename, hit.FolderPath ?? "");
            if (!groups.TryGetValue(key, out var family))
            {
                family = new DocumentFamily
                {
                    FamilyKey = key,
                    Label = Path.GetFileNameWithoutExtension(hit.Filename),
                    FolderPath = hit.FolderPath ?? "",
                    PrimaryType = hit.Extension,
                    LatestDate = hit.ModifiedAt ?? "",
                };
                groups[key] = family;
            }

            family.Files.Add(hit);
            if (string.Compare(hit.ModifiedAt ?? "", family.LatestDate, StringComparison.Ordinal) > 0)
                family.LatestDate = hit.ModifiedAt ?? "";
        }

        // Sort families by best score, select representative
        foreach (var family in groups.Values)
        {
            family.Files.Sort((a, b) => b.HybridScore.CompareTo(a.HybridScore));
            if (family.Files.Count > 0)
            {
                family.Label = Path.GetFileNameWithoutExtension(family.Files[0].Filename);
                family.MatchSnippet = family.Files[0].MatchSnippet ?? "";
            }
        }

        return groups.Values
            .OrderByDescending(f => f.Files.Max(h => h.HybridScore))
            .ToList();
    }

    private static string GenerateFamilyKey(string filename, string folderPath)
    {
        var normalized = NormalizeFilename(filename);
        return $"{folderPath}|{normalized}".ToLowerInvariant();
    }

    private static string NormalizeFilename(string filename)
    {
        var name = filename;
        // Remove extension
        name = ExtensionRegex().Replace(name, "");
        // Remove special prefixes
        name = SpecialPrefixRegex().Replace(name, "");
        // Remove copy prefixes
        name = CopyPrefixRegex().Replace(name, "");
        // Remove dates (8-digit YYYYMMDD)
        name = Date8Regex().Replace(name, "");
        // Remove dates (6-digit YYMMDD)
        name = Date6Regex().Replace(name, "");
        // Remove version/status
        name = VersionRegex().Replace(name, "");
        // Remove sequence numbers
        name = SequenceRegex().Replace(name, "");
        // Replace delimiters with spaces
        name = DelimiterRegex().Replace(name, " ");
        // Collapse whitespace
        name = WhitespaceRegex().Replace(name, " ").Trim();

        return name;
    }

    [GeneratedRegex(@"\.[a-zA-Z0-9]{1,5}$")]
    private static partial Regex ExtensionRegex();

    [GeneratedRegex(@"^[★※◆■●▶▷◇□△▲♦♠♣♥♡☆◎○◈►]+\s*")]
    private static partial Regex SpecialPrefixRegex();

    [GeneratedRegex(@"^\s*(복사본|사본|Copy\s+of|복사\s*[-–—])\s*", RegexOptions.IgnoreCase)]
    private static partial Regex CopyPrefixRegex();

    [GeneratedRegex(@"(?<!\d)20\d{2}[-./]?\d{2}[-./]?\d{2}(?!\d)")]
    private static partial Regex Date8Regex();

    [GeneratedRegex(@"(?<!\d)[012]\d(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01])(?!\d)")]
    private static partial Regex Date6Regex();

    [GeneratedRegex(@"(?:^|(?<=[\s_\-]))(?:v\d+|vDraft|final|approved|signed|executed|rev\d+|ver\d+|최종|수정본|수정|완료|개정)(?=[\s_\-.]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"[\(\[\{]\s*\d+\s*[\)\]\}]|#\d+|_\d{1,2}(?=[_\s.]|$)|\d{1,2}차")]
    private static partial Regex SequenceRegex();

    [GeneratedRegex(@"[\[\]\(\)\{\}「」【】_\-–—.,;:~]+")]
    private static partial Regex DelimiterRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceRegex();
}
