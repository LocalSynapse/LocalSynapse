namespace LocalSynapse.Core.Constants;

/// <summary>
/// SSOT: 콘텐츠 인덱싱 가능 파일 확장자 (23종).
/// </summary>
public static class FileExtensions
{
    public static readonly HashSet<string> ContentSearchable = new(StringComparer.OrdinalIgnoreCase)
    {
        // Plain text
        ".txt", ".md", ".csv", ".json", ".log", ".xml",
        // PDF
        ".pdf",
        // Microsoft Office
        ".docx", ".xlsx", ".pptx",
        // Other documents
        ".rtf", ".odt", ".ods", ".odp", ".html", ".htm",
        // Email
        ".eml", ".msg",
        // Korean office
        ".hwp", ".hwpx"
    };

    /// <summary>콘텐츠 인덱싱 가능 여부 확인.</summary>
    public static bool IsContentSearchable(string extension)
        => ContentSearchable.Contains(extension);

    /// <summary>SQL IN 절 생성 (dot/no-dot 양쪽 포함).</summary>
    public static string ToSqlInClause()
    {
        var withDot = ContentSearchable.Select(e => $"'{e.ToLowerInvariant()}'");
        var withoutDot = ContentSearchable.Select(e => $"'{e.TrimStart('.').ToLowerInvariant()}'");
        return string.Join(",", withDot.Concat(withoutDot));
    }
}
