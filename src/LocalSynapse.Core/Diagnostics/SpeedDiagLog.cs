using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace LocalSynapse.Core.Diagnostics;

/// <summary>
/// Append-only TSV logger for speed diagnostics.
/// Output: {ISO-8601 timestamp}\t{elapsed since start}\t{category}\tkey=value ...
/// Parsed by scripts/analyze-speed-diag.py.
/// </summary>
public static class SpeedDiagLog
{
    private static readonly object Lock = new();
    private static readonly Stopwatch AppStopwatch = Stopwatch.StartNew();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LocalSynapse", "speed-diag.log");

    /// <summary>
    /// Logs application start with version info. Resets the elapsed timer.
    /// </summary>
    public static void AppStart(string version)
    {
        Log("APP_START", "version", version);
    }

    /// <summary>
    /// Logs a diagnostic event with optional key-value pairs.
    /// Keys and values alternate: Log("CAT", "k1", v1, "k2", v2).
    /// </summary>
    public static void Log(string category, params object[] kvPairs)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            sb.Append('\t');
            sb.Append(AppStopwatch.Elapsed.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));
            sb.Append('\t');
            sb.Append(category);

            for (int i = 0; i + 1 < kvPairs.Length; i += 2)
            {
                var key = kvPairs[i]?.ToString() ?? "";
                var val = kvPairs[i + 1]?.ToString() ?? "";
                sb.Append('\t');
                if (val.Contains(' ') || val.Contains('\t') || val.Contains('"'))
                {
                    sb.Append(key).Append("=\"").Append(val.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                }
                else
                {
                    sb.Append(key).Append('=').Append(val);
                }
            }

            var line = sb.ToString();

            lock (Lock)
            {
                var dir = Path.GetDirectoryName(LogPath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SpeedDiagLog] write failed: {ex.Message}");
        }
    }
}
