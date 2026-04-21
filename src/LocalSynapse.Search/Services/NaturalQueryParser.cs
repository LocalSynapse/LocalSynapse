using System.Text.RegularExpressions;
using LocalSynapse.Core.Utils;
using Porter2StemmerStandard;

namespace LocalSynapse.Search.Services;

/// <summary>
/// 자연어 쿼리를 FTS5 MATCH 표현식으로 변환한다.
/// 한국어 서브토큰 분해 및 Porter2 스테밍을 적용한다.
///
/// [영어 검색 최적화]
/// - Porter stemmer: FTS5 토크나이저 레벨에서 처리 (MigrationService 참조)
///   → 인덱싱 시점 + 쿼리 시점 양쪽 모두 자동 스테밍
///   → "documents" 검색 → "document" 매칭, "running" → "run" 매칭
/// - Stop words: ToFts5Query()에서 필터링
/// - 하이픈 확장: "email" ↔ "e-mail" 양방향 매칭
/// </summary>
public static partial class NaturalQueryParser
{
    private static readonly EnglishPorter2Stemmer Stemmer = new();

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // 한국어 조사/어미
        "은", "는", "이", "가", "을", "를", "의", "에", "에서", "로", "으로",
        "와", "과", "도", "만", "부터", "까지", "에게", "한테", "께",
        "하고", "이고", "라고", "다고", "고", "면", "니까", "지만",
        "것", "수", "때", "중", "후", "전", "위", "내",
        // 영어
        "the", "a", "an", "is", "are", "was", "were", "be", "been",
        "have", "has", "had", "do", "does", "did", "will", "would",
        "can", "could", "shall", "should", "may", "might", "must",
        "to", "of", "in", "for", "on", "with", "at", "by", "from",
        "as", "into", "through", "during", "before", "after",
        "and", "or", "but", "not", "no", "nor", "so", "yet",
        "how", "what", "when", "where", "which", "who", "why",
        "it", "its", "this", "that", "these", "those",
        "i", "me", "my", "we", "us", "our", "you", "your",
        "he", "him", "his", "she", "her", "they", "them", "their",
    };

    // ── 하이픈 컴파운드 분리에 사용되는 영어 접두사 ──
    // 입력이 "reindex"이면 "re-index" 변형을 생성한다.
    // 흔한 접두사만 포함 — 오탐 최소화를 위해 2~5자 접두사 + 나머지 3자 이상일 때만 분리.
    private static readonly string[] CompoundPrefixes =
    [
        "re", "pre", "un", "non", "co", "de", "ex", "anti", "auto",
        "bi", "dis", "mis", "multi", "out", "over", "post", "semi",
        "sub", "super", "tri", "under", "inter", "intra", "macro", "micro"
    ];

    // ── 영어 약어/두문자어 화이트리스트 (3자 이하지만 prefix 매치 허용) ──
    private static readonly HashSet<string> AcronymWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "API", "SQL", "CEO", "CTO", "CFO", "COO", "VP", "HR", "IT", "AI", "ML",
        "UI", "UX", "PR", "QA", "CI", "CD", "DB", "OS", "IP", "ID", "URL",
        "PDF", "CSV", "XML", "JSON", "HTML", "CSS", "JS", "TS",
        "AWS", "GCP", "SDK", "CLI", "GUI", "ORM", "MCP", "LLM",
        "ROI", "KPI", "OKR", "P&L", "PnL", "NDA", "SLA", "MOU",
        "IOT", "VPN", "DNS", "TCP", "UDP", "SSH", "FTP", "HTTP",
    };

    /// <summary>쿼리를 FTS5 MATCH 표현식으로 변환한다.</summary>
    public static string ToFts5Query(string query)
    {
        // ── 따옴표 구문 검색: "exact phrase" → FTS5 phrase match ──
        var phraseMatch = PhraseRegex().Match(query);
        if (phraseMatch.Success)
        {
            var phrase = phraseMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(phrase))
            {
                var escaped = phrase.Replace("\"", "\"\"");
                var remaining = query[..phraseMatch.Index] + query[(phraseMatch.Index + phraseMatch.Length)..];
                remaining = remaining.Trim();

                var phraseExpr = $"\"{escaped}\"";

                if (!string.IsNullOrWhiteSpace(remaining))
                {
                    var remainingFts = ToFts5QueryInner(remaining);
                    if (!string.IsNullOrEmpty(remainingFts))
                        return $"({phraseExpr}) AND ({remainingFts})";
                }
                return phraseExpr;
            }
        }

        return ToFts5QueryInner(query);
    }

    /// <summary>내부 FTS5 쿼리 변환 (phrase 처리 후 호출).</summary>
    private static string ToFts5QueryInner(string query)
    {
        var tokens = Tokenize(query);
        if (tokens.Count == 0) return "";

        var parts = new List<string>();
        foreach (var token in tokens)
        {
            if (StopWords.Contains(token)) continue;

            var escaped = token.Replace("\"", "\"\"");

            // CJK bigram 분기 (한국어 제외)
            if (IsCjkToken(token))
            {
                var bigrams = CjkTextUtils.ExtractBigrams(token);
                if (bigrams.Count >= 2)
                {
                    var andParts = bigrams.Select(b =>
                    {
                        var esc = b.Replace("\"", "\"\"");
                        return $"\"{esc}\"*";
                    });
                    parts.Add($"({string.Join(" AND ", andParts)})");
                }
                else
                {
                    parts.Add($"\"{escaped}\"*");
                }
                continue;
            }

            if (IsKorean(token) || token.Length >= 4 || AcronymWhitelist.Contains(token))
                parts.Add($"\"{escaped}\"*");
            else
                parts.Add($"\"{escaped}\"");
        }

        if (parts.Count == 0) return "";

        // ── Korean compound decomposition (AND 방식) ──
        var koreanAndParts = ExpandKoreanCompound(tokens);
        if (koreanAndParts.Count > 0)
        {
            var mainExpr = parts.Count == 1 ? parts[0] : string.Join(" OR ", parts);
            var andExpr = string.Join(" AND ", koreanAndParts);
            parts = [$"({mainExpr}) OR ({andExpr})"];
        }

        var finalExpr = parts.Count == 1
            ? parts[0]
            : string.Join(" OR ", parts);

        // ── Keyword expansions (기존 QueryExpansionMap) ──
        var expansions = Constants.QueryExpansionMap.ExpandKeywordsOnly(query);

        // ── Hyphen/compound expansions ──
        var hyphenExpansions = ExpandHyphenVariants(query);
        foreach (var h in hyphenExpansions)
        {
            if (!expansions.Contains(h))
                expansions.Add(h);
        }

        if (expansions.Count > 0)
        {
            var orParts = expansions.Select(e =>
            {
                var esc = e.Replace("\"", "\"\"");
                return $"\"{esc}\"*";
            });
            return $"({finalExpr}) OR ({string.Join(" OR ", orParts)})";
        }

        return finalExpr;
    }

    // 한국어 조사 접미사 — 긴 것부터 매칭
    private static readonly string[] KoreanParticles =
    [
        "에서", "으로", "부터", "까지", "에게", "한테",
        "하고", "이고", "라고", "다고", "지만", "니까",
        "은", "는", "이", "가", "을", "를", "의", "에",
        "로", "와", "과", "도", "만", "께", "고", "면",
    ];

    /// <summary>쿼리에서 스톱워드를 제거한다. 한국어 조사도 분리한다.</summary>
    public static string RemoveStopwords(string query)
    {
        var tokens = Tokenize(query);
        var result = new List<string>();

        foreach (var token in tokens)
        {
            if (StopWords.Contains(token)) continue;

            // 한국어 토큰: 조사 접미사 분리 시도
            if (IsKorean(token))
            {
                var stripped = StripKoreanParticle(token);
                if (!StopWords.Contains(stripped) && stripped.Length > 0)
                    result.Add(stripped);
            }
            else
            {
                result.Add(token);
            }
        }

        return string.Join(" ", result);
    }

    private static string StripKoreanParticle(string token)
    {
        foreach (var particle in KoreanParticles)
        {
            if (token.EndsWith(particle) && token.Length > particle.Length)
                return token[..^particle.Length];
        }
        return token;
    }

    /// <summary>영어 단어를 Porter2 스테밍한다. (비FTS5 용도: 하이라이팅, 매칭 등)</summary>
    public static string Stem(string word)
    {
        if (IsKorean(word)) return word;
        return Stemmer.Stem(word).Value;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  하이픈/컴파운드 확장
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 쿼리 토큰에서 하이픈 변형을 생성한다.
    ///
    /// 규칙:
    /// 1) 하이픈 포함 토큰 → 하이픈 제거 버전 추가 ("e-mail" → "email")
    /// 2) 하이픈 없는 토큰 → 알려진 접두사로 분리 시도 ("reindex" → "re-index")
    ///
    /// FTS5에서 '-'가 separator이므로:
    /// - 콘텐츠 "e-mail" → 토큰 "e" + "mail"
    /// - 쿼리 "email" → 토큰 "email" → 매칭 안 됨
    /// - 확장으로 "email" 검색 시 "e" AND "mail" 도 함께 검색
    /// </summary>
    public static List<string> ExpandHyphenVariants(string query)
    {
        var results = new List<string>();
        var tokens = Tokenize(query);

        foreach (var token in tokens)
        {
            if (StopWords.Contains(token)) continue;
            if (IsKorean(token)) continue;
            if (token.Length < 4) continue; // 너무 짧은 토큰은 스킵

            // 규칙 1: "e-mail" → "email" (원본 쿼리에서 하이픈 포함 토큰 확인)
            // Tokenize가 '-'로 분리하므로 원본 쿼리에서 직접 확인
            // (이 케이스는 아래 원본 쿼리 스캔에서 처리)

            // 규칙 2: "email" → 접두사 분리 → "e" + "mail" (OR 검색)
            foreach (var prefix in CompoundPrefixes)
            {
                if (token.Length <= prefix.Length + 2) continue; // 나머지가 3자 미만이면 스킵
                if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                var remainder = token[prefix.Length..];
                // "reindex" → prefix="re", remainder="index" → 개별 토큰으로 추가
                // FTS5에서 "re" AND "index"로 검색하면 "re-index" 콘텐츠 매칭
                results.Add(remainder); // remainder만 추가 (prefix가 stop word일 수 있으므로)
                break; // 첫 매칭 접두사만 사용
            }
        }

        // 규칙 1: 원본 쿼리에서 하이픈 포함 단어 → 하이픈 제거 버전
        // Tokenize가 '-'로 분리하기 전의 원본을 스캔
        var words = query.Split([' ', ',', '.'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            if (!word.Contains('-')) continue;
            var joined = word.Replace("-", "");
            if (joined.Length >= 4 && !StopWords.Contains(joined))
            {
                results.Add(joined);
            }
        }

        return results;
    }

    /// <summary>
    /// 한국어 복합어를 AND 서브토큰으로 분해한다.
    /// "변경계약" → ["변경"*, "계약"*]
    /// 3글자 이상인 한국어 토큰만 분해. 서브토큰은 2글자 이상.
    /// </summary>
    private static List<string> ExpandKoreanCompound(List<string> tokens)
    {
        var results = new List<string>();

        foreach (var token in tokens)
        {
            if (!IsKorean(token)) continue;
            if (token.Length < 3) continue;

            var subs = new List<string>();
            for (int i = 0; i <= token.Length - 2; i += 2)
            {
                var sub = token.Substring(i, Math.Min(2, token.Length - i));
                if (sub.Length >= 2)
                {
                    var esc = sub.Replace("\"", "\"\"");
                    subs.Add($"\"{esc}\"*");
                }
            }

            if (subs.Count >= 2)
            {
                results.AddRange(subs);
                break;
            }
        }

        return results;
    }

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex PhraseRegex();

    private static List<string> Tokenize(string query)
    {
        return query.Split([' ', ',', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 1)
            .ToList();
    }

    private static bool IsKorean(string text)
    {
        return text.Any(c => (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0x3131 && c <= 0x318E));
    }

    /// <summary>토큰이 CJK 문자를 포함하고 한국어를 포함하지 않는지 확인한다.</summary>
    private static bool IsCjkToken(string text)
    {
        bool hasCjk = false;
        foreach (var c in text)
        {
            if (CjkTextUtils.IsKorean(c)) return false;
            if (CjkTextUtils.IsCjk(c)) hasCjk = true;
        }
        return hasCjk;
    }
}
