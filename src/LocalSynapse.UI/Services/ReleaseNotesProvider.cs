using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace LocalSynapse.UI.Services;

/// <summary>
/// 번들 ReleaseNotes.json에서 현재 버전 릴리즈 노트를 로드한다.
/// 네트워크 없이도 항상 표시 가능.
/// </summary>
public static class ReleaseNotesProvider
{
    /// <summary>현재 locale에 맞는 릴리즈 노트를 반환한다.</summary>
    public static List<string> GetCurrentNotes(string locale)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(
            "LocalSynapse.UI.Resources.ReleaseNotes.json");

        if (stream == null)
        {
            Debug.WriteLine("[ReleaseNotes] Embedded resource not found");
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(stream);
            var loc = locale ?? "en";
            var key = loc switch
            {
                "ko" => "notes_ko",
                "fr" => "notes_fr",
                "de" => "notes_de",
                "zh" => "notes_zh",
                _ => "notes_en"
            };

            if (doc.RootElement.TryGetProperty(key, out var notes))
            {
                return notes.EnumerateArray()
                    .Select(n => n.GetString() ?? "")
                    .Where(s => s.Length > 0)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ReleaseNotes] Parse error: {ex.Message}");
        }
        return [];
    }
}
