using System.Text.RegularExpressions;

namespace LocalSynapse.Search.Services;

/// <summary>
/// 쿼리 용어에 &lt;mark&gt; 태그를 삽입하여 하이라이트한다.
///
/// [Porter stemmer 호환]
/// FTS5가 porter 스테밍으로 매칭하므로, 쿼리 "documents"로 검색 시
/// 콘텐츠의 "document", "documentation" 등도 하이라이트해야 한다.
/// 원본 텀 매칭 후, 매칭이 없으면 stem 기반으로 재시도한다.
/// </summary>
public static class TextHighlighter
{
    /// <summary>텍스트에서 쿼리 용어를 &lt;mark&gt; 태그로 감싼다.</summary>
    public static string Highlight(string text, IEnumerable<string> queryTerms)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var result = text;
        foreach (var term in queryTerms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;

            // 1차: 원본 텀으로 하이라이트
            var escaped = Regex.Escape(term);
            var original = result;
            result = Regex.Replace(result, escaped, m => $"<mark>{m.Value}</mark>", RegexOptions.IgnoreCase);

            // 원본 텀으로 매칭된 게 있으면 다음 텀으로
            if (result != original) continue;

            // 2차: stem 기반으로 하이라이트 (원본에서 매칭 안 됐을 때만)
            var stem = NaturalQueryParser.Stem(term);
            if (!string.IsNullOrEmpty(stem) && stem != term)
            {
                // stem으로 시작하는 단어를 매칭 (\b = word boundary)
                var stemPattern = Regex.Escape(stem) + @"\w*";
                result = Regex.Replace(result, stemPattern,
                    m => $"<mark>{m.Value}</mark>", RegexOptions.IgnoreCase);
            }
        }

        return result;
    }
}
