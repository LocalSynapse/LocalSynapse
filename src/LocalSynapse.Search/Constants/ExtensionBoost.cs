namespace LocalSynapse.Search.Constants;

/// <summary>
/// 확장자별 BM25 점수 가중치.
/// </summary>
public static class ExtensionBoost
{
    private static readonly Dictionary<string, float> Boosts = new(StringComparer.OrdinalIgnoreCase)
    {
        // Tier 1: Core office docs (1.5x)
        [".docx"] = 1.5f, [".doc"] = 1.5f,
        [".xlsx"] = 1.5f, [".xls"] = 1.5f,
        [".pptx"] = 1.5f, [".ppt"] = 1.5f,
        [".pdf"] = 1.5f,
        [".hwp"] = 1.5f, [".hwpx"] = 1.5f,

        // Tier 2: Other docs/email (1.1~1.2x)
        [".txt"] = 1.2f, [".rtf"] = 1.2f,
        [".csv"] = 1.2f, [".md"] = 1.2f,
        [".msg"] = 1.2f, [".eml"] = 1.2f,
        [".html"] = 1.1f, [".htm"] = 1.1f,

        // Tier 3: Dev/config (0.2~0.5x)
        [".json"] = 0.5f, [".xml"] = 0.5f, [".yaml"] = 0.5f, [".cfg"] = 0.5f,
        [".log"] = 0.3f,
        [".cs"] = 0.2f, [".ts"] = 0.2f, [".py"] = 0.2f,
        [".js"] = 0.2f, [".sh"] = 0.2f,
    };

    /// <summary>확장자에 대한 부스트 가중치를 반환한다 (기본값 1.0).</summary>
    public static float GetBoost(string extension)
        => Boosts.GetValueOrDefault(extension, 1.0f);
}
