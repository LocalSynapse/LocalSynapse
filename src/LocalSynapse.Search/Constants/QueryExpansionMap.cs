namespace LocalSynapse.Search.Constants;

/// <summary>
/// 한국어 ↔ 영어 쿼리 확장 맵. 검색어를 확장자/동의어로 확장한다.
/// </summary>
public static class QueryExpansionMap
{
    private static readonly Dictionary<string, List<string>> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["인증서"] = [".pfx", ".p12", ".cer", ".pem", "certificate"],
        ["certificate"] = [".pfx", ".p12", ".cer", ".pem", "인증서"],
        ["도면"] = [".dwg", ".dxf", ".dwf", "CAD", "drawing"],
        ["CAD"] = [".dwg", ".dxf", ".dwf", "도면"],
        ["사진"] = [".jpg", ".jpeg", ".png", ".heic", ".raw", "photo"],
        ["photo"] = [".jpg", ".jpeg", ".png", ".heic", "사진"],
        ["동영상"] = [".mp4", ".avi", ".mov", ".mkv", "video"],
        ["video"] = [".mp4", ".avi", ".mov", ".mkv", "동영상"],
        ["음악"] = [".mp3", ".wav", ".flac", "music"],
        ["music"] = [".mp3", ".wav", ".flac", "음악"],
        ["한글"] = [".hwp", ".hwpx"],
        ["워드"] = [".docx", ".doc", "word"],
        ["word"] = [".docx", ".doc", "워드"],
        ["엑셀"] = [".xlsx", ".xls", "excel"],
        ["excel"] = [".xlsx", ".xls", "엑셀"],
        ["파워포인트"] = [".pptx", ".ppt", "powerpoint", "PPT"],
        ["powerpoint"] = [".pptx", ".ppt", "파워포인트"],
        ["PPT"] = [".pptx", ".ppt", "파워포인트"],
        ["PDF"] = [".pdf"],
        ["압축"] = [".zip", ".rar", ".7z", ".tar", ".gz", "archive"],
        ["archive"] = [".zip", ".rar", ".7z", ".tar", ".gz", "압축"],
        ["소스코드"] = [".cs", ".ts", ".tsx", ".js", ".py", ".java", ".cpp"],
        ["스크립트"] = [".py", ".sh", ".bat", ".ps1"],
        ["계약서"] = ["contract", "계약"],
        ["contract"] = ["계약서", "계약"],
        ["정관"] = ["AOI", "articles"],
        ["이사회"] = ["board", "이사회의사록"],
        ["주총"] = ["AGM", "주주총회"],
    };

    /// <summary>쿼리를 확장한다 (확장자 포함).</summary>
    public static List<string> Expand(string query)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            if (Map.TryGetValue(token, out var expansions))
            {
                foreach (var e in expansions) result.Add(e);
            }
        }

        if (tokens.Length > 1 && Map.TryGetValue(query.Trim(), out var fullExpansions))
        {
            foreach (var e in fullExpansions) result.Add(e);
        }

        return result.ToList();
    }

    /// <summary>키워드만 확장한다 (확장자 제외, FTS5용).</summary>
    public static List<string> ExpandKeywordsOnly(string query)
    {
        return Expand(query).Where(e => !e.StartsWith('.')).ToList();
    }
}
