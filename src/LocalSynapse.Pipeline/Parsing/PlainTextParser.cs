using System.Diagnostics;
using LocalSynapse.Core.Diagnostics;
using LocalSynapse.Pipeline.Interfaces;

namespace LocalSynapse.Pipeline.Parsing;

/// <summary>
/// 플레인 텍스트 파서. .txt .md .csv .json .log .xml .rtf 등 처리.
/// </summary>
internal static class PlainTextParser
{
    private const long MaxSizeBytes = 10 * 1024 * 1024; // 10MB

    /// <summary>텍스트 파일을 읽어 ExtractionResult를 반환한다.</summary>
    public static async Task<ExtractionResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MaxSizeBytes)
            return ExtractionResult.Fail("TOO_LARGE", $"File size {fileInfo.Length} exceeds 10MB limit");

        var readSw = Stopwatch.StartNew();
        var text = await File.ReadAllTextAsync(filePath, ct);
        readSw.Stop();
        SpeedDiagLog.Log("PARSE_DETAIL",
            "ext", Path.GetExtension(filePath).ToLowerInvariant(),
            "stage", "read",
            "time_ms", readSw.ElapsedMilliseconds, "size_bytes", fileInfo.Length);
        return ExtractionResult.Ok(text);
    }
}
