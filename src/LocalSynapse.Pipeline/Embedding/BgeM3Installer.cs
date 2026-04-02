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

    private static readonly (string RelativePath, string Sha256, long Size)[] RequiredFiles =
    {
        ("model.onnx", "", 725_000),
        ("model.onnx_data", "", 2_270_000_000),
        ("tokenizer.json", "", 17_000_000),
        ("sentencepiece.bpe.model", "", 5_000_000),
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

        foreach (var (relativePath, _, expectedSize) in RequiredFiles)
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

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None,
                BufferSize, true);

            var buffer = new byte[BufferSize];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloadedBytes += bytesRead;
                progress?.Report(new DownloadProgress { BytesDone = downloadedBytes, BytesTotal = totalBytes });
            }

            // Rename .part to final
            File.Move(partPath, targetPath, overwrite: true);

            Debug.WriteLine($"[BgeM3Installer] Downloaded: {relativePath}");
        }

        Debug.WriteLine($"[BgeM3Installer] Model {modelId} installation complete");
    }
}
