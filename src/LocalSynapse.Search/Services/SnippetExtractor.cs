using LocalSynapse.Search.Interfaces;

namespace LocalSynapse.Search.Services;

/// <summary>
/// 콘텐츠에서 쿼리 용어 주변 텍스트를 추출하여 스니펫을 생성한다.
///
/// [Porter stemmer 호환]
/// FTS5가 porter 스테밍으로 매칭하므로, 쿼리 "documents" → FTS5가 "document" 매칭.
/// 스니펫 추출 시 원본 텀으로 못 찾으면 stem 버전으로 재시도한다.
/// </summary>
public sealed class SnippetExtractor : ISnippetExtractor
{
    private const int ContextChars = 100;

    /// <summary>쿼리 용어 주변의 텍스트 스니펫을 추출한다.</summary>
    public string Extract(string content, IEnumerable<string> queryTerms, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(content)) return "";

        var terms = queryTerms.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (terms.Count == 0)
            return content.Length <= maxLength ? content : content[..maxLength] + "...";

        // Find first matching term (원본 → stem 순으로 시도)
        var (bestPos, bestLen) = FindBestMatch(content, terms);

        if (bestPos < 0)
            return content.Length <= maxLength ? content : content[..maxLength] + "...";

        // Center snippet around match
        var start = Math.Max(0, bestPos - ContextChars);
        var end = Math.Min(content.Length, bestPos + bestLen + ContextChars);

        // Limit to maxLength
        if (end - start > maxLength)
            end = start + maxLength;

        var snippet = content[start..end];
        var prefix = start > 0 ? "..." : "";
        var suffix = end < content.Length ? "..." : "";

        return prefix + snippet + suffix;
    }

    /// <summary>
    /// 콘텐츠에서 쿼리 용어의 위치를 찾는다.
    /// 1차: 원본 텀으로 검색
    /// 2차: Porter2 stem으로 검색 (원본으로 못 찾을 때)
    /// </summary>
    private static (int position, int length) FindBestMatch(string content, List<string> terms)
    {
        var bestPos = -1;
        var bestLen = 0;

        // 1차: 원본 텀으로 검색
        foreach (var term in terms)
        {
            var pos = content.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (pos >= 0 && (bestPos < 0 || pos < bestPos))
            {
                bestPos = pos;
                bestLen = term.Length;
            }
        }

        if (bestPos >= 0) return (bestPos, bestLen);

        // 2차: stem 변형으로 재시도
        foreach (var term in terms)
        {
            var stem = NaturalQueryParser.Stem(term);
            if (stem == term || string.IsNullOrEmpty(stem)) continue;

            // stem으로 시작하는 단어를 찾는다 (e.g., stem="document" → "documentation" 매칭)
            var searchFrom = 0;
            while (searchFrom < content.Length)
            {
                var pos = content.IndexOf(stem, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (pos < 0) break;

                // 단어 경계 확인: stem이 단어의 시작 부분인지 확인
                var isWordStart = pos == 0 || !char.IsLetterOrDigit(content[pos - 1]);
                if (isWordStart && (bestPos < 0 || pos < bestPos))
                {
                    // 매칭된 전체 단어의 끝을 찾는다
                    var wordEnd = pos + stem.Length;
                    while (wordEnd < content.Length && char.IsLetterOrDigit(content[wordEnd]))
                        wordEnd++;

                    bestPos = pos;
                    bestLen = wordEnd - pos;
                    break;
                }

                searchFrom = pos + 1;
            }

            if (bestPos >= 0) break;
        }

        return (bestPos, bestLen);
    }
}
