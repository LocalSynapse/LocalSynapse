using System.Diagnostics;
using System.Security.Cryptography;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Pipeline.Interfaces;

namespace LocalSynapse.Pipeline.Embedding;

/// <summary>
/// BGE-M3 모델 다운로드 및 설치를 관리한다.
/// HuggingFace에서 ONNX 파일을 다운로드하고 무결성을 검증한다.
/// </summary>
public sealed class BgeM3Installer : IModelInstaller
{
    private readonly ISettingsStore _settings;
    private readonly HttpClient _httpClient;
    private const int BufferSize = 65536; // 64KB

    private static readonly string BaseUrl = "https://huggingface.co/BAAI/bge-m3/resolve/main/onnx";

    private static readonly ModelInfo[] Models =
    {
        new() { Id = "bge-m3", Name = "BAAI/bge-m3", Dimension = 1024, SizeBytes = 2_300_000_000, IsDefault = true },
    };

    // RequiredFiles의 Size 필드는 "최소 허용 크기"를 의미한다 (정확한 파일 크기가 아님).
    // HuggingFace UI 표시값은 반올림되므로 실제 바이트가 표시값보다 작을 수 있다.
    // 예: model.onnx는 UI상 "725 kB"지만 실제 724,923 바이트 → "725_000"으로 검증하면 100% 실패.
    // 값은 HF API 실측값의 약 88~90%로 의도적으로 보수화했다 —
    // truncated 다운로드(네트워크 중단은 보통 0~80%에서 발생)는 거르면서 false positive를 피한다.
    // HF 원본/표시값으로 되돌리지 말 것. 값 변경 시 HF API로 실제 바이트를 재조회한 후 ~90%로 조정할 것.
    // HF API: https://huggingface.co/api/models/BAAI/bge-m3/tree/main/onnx
    private static readonly (string RelativePath, string Sha256, long Size)[] RequiredFiles =
    {
        ("model.onnx",              "",           650_000), // HF actual 724,923
        ("model.onnx_data",         "", 2_040_000_000),     // HF actual 2,266,820,608
        ("tokenizer.json",          "",    15_000_000),     // HF actual 17,082,821
        ("sentencepiece.bpe.model", "",     4_500_000),     // HF actual 5,069,051
    };

    /// <summary>BgeM3Installer 생성자.</summary>
    public BgeM3Installer(ISettingsStore settings)
    {
        _settings = settings;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    }

    /// <summary>사용 가능한 모델 목록을 반환한다.</summary>
    public IReadOnlyList<ModelInfo> GetAvailableModels() => Models;

    /// <summary>모델 설치 여부를 확인한다.</summary>
    public bool IsModelInstalled(string modelId)
    {
        var modelDir = GetModelPath(modelId);
        if (!Directory.Exists(modelDir)) return false;

        // Check for model.onnx
        var onnxDir = Path.Combine(modelDir, "onnx");
        if (Directory.Exists(onnxDir) && Directory.GetFiles(onnxDir, "*.onnx").Length > 0)
            return true;

        return Directory.GetFiles(modelDir, "*.onnx").Length > 0;
    }

    /// <summary>모델 경로를 반환한다.</summary>
    public string GetModelPath(string modelId)
    {
        return Path.Combine(_settings.GetModelFolder(), modelId);
    }

    /// <summary>HuggingFace에서 모델을 다운로드하고 설치한다.</summary>
    public async Task DownloadModelAsync(string modelId, IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var modelDir = GetModelPath(modelId);
        var onnxDir = Path.Combine(modelDir, "onnx");
        Directory.CreateDirectory(onnxDir);

        var totalBytes = RequiredFiles.Sum(f => f.Size);
        var downloadedBytes = 0L;

        foreach (var (relativePath, expectedSha256, expectedSize) in RequiredFiles)
        {
            ct.ThrowIfCancellationRequested();

            var targetPath = Path.Combine(onnxDir, relativePath);
            if (File.Exists(targetPath))
            {
                downloadedBytes += expectedSize;
                progress?.Report(new DownloadProgress { BytesDone = downloadedBytes, BytesTotal = totalBytes });
                continue;
            }

            var url = $"{BaseUrl}/{relativePath}";
            var partPath = targetPath + ".part";

            Debug.WriteLine($"[BgeM3Installer] Downloading: {relativePath}");

            using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync(ct))
                using (var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None,
                           BufferSize, useAsync: true))
                {
                    var buffer = new byte[BufferSize];
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        downloadedBytes += bytesRead;
                        progress?.Report(new DownloadProgress { BytesDone = downloadedBytes, BytesTotal = totalBytes });
                    }

                    await fileStream.FlushAsync(ct);
                } // fileStream / stream disposed — file handles released before File.Move below
            }

            // Verify downloaded file size meets minimum threshold.
            // Strict ">=" is safe because expectedSize is set to ~90% of HF actual bytes (see RequiredFiles comment).
            var actualSize = new FileInfo(partPath).Length;
            if (actualSize < expectedSize)
            {
                // Best-effort cleanup; catch broadly so the InvalidDataException below is always propagated.
                try { File.Delete(partPath); }
                catch (Exception delEx) when (delEx is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine($"[BgeM3Installer] Failed to delete corrupt .part file: {delEx.Message}");
                }
                throw new InvalidDataException(
                    $"Downloaded file {relativePath} is smaller than expected minimum size " +
                    $"({actualSize} < {expectedSize} bytes). The download may be truncated. Please retry.");
            }

            // SHA256 verification (streaming for large files like model.onnx_data 2GB+)
            if (!string.IsNullOrEmpty(expectedSha256))
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                using var fs = File.OpenRead(partPath);
                var hash = await sha.ComputeHashAsync(fs, ct).ConfigureAwait(false);
                var hex = Convert.ToHexString(hash);
                if (!hex.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(partPath); }
                    catch (Exception delEx) when (delEx is IOException or UnauthorizedAccessException)
                    {
                        Debug.WriteLine($"[BgeM3Installer] Failed to delete hash-mismatch file: {delEx.Message}");
                    }
                    throw new InvalidDataException(
                        $"SHA256 mismatch for {relativePath}: expected {expectedSha256}, got {hex}");
                }
                Debug.WriteLine($"[BgeM3Installer] SHA256 verified for {relativePath}");
            }

            // Rename .part to final (file handles already released above).
            File.Move(partPath, targetPath, overwrite: true);

            Debug.WriteLine($"[BgeM3Installer] Downloaded: {relativePath} ({actualSize} bytes)");
        }

        Debug.WriteLine($"[BgeM3Installer] Model {modelId} installation complete");
    }
}
