using System.Diagnostics;
using LocalSynapse.Core.Diagnostics;
using Microsoft.ML.OnnxRuntime;

namespace LocalSynapse.Pipeline.Embedding;

/// <summary>
/// GPU/EP 감지 서비스. 앱 시작 시 사용 가능한 ONNX Runtime Execution Provider를 감지하고 캐싱한다.
/// </summary>
public sealed class GpuDetectionService
{
    private readonly SemaphoreSlim _detectLock = new(1, 1);
    private GpuDetectionResult? _cached;

    /// <summary>캐시된 감지 결과. Detect 호출 전에는 null.</summary>
    public GpuDetectionResult? CachedResult => _cached;

    /// <summary>Mad Max가 사용 가능한지 여부.</summary>
    public bool IsMadMaxAvailable => _cached?.BestProvider != null;

    /// <summary>사용 가능한 EP를 감지한다. 결과는 캐시되며 동시 호출 시 한 번만 실행된다.</summary>
    public async Task<GpuDetectionResult> Detect(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        await _detectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached is not null) return _cached;
            var result = DetectInternal();
            _cached = result;

            SpeedDiagLog.Log("EP_DETECT",
                "available_providers", string.Join(",", result.AvailableProviders),
                "best_provider", result.BestProvider ?? "",
                "gpu_name", result.GpuName ?? "",
                "detection_method", "session_options_append");

            return result;
        }
        finally
        {
            _detectLock.Release();
        }
    }

    private static GpuDetectionResult DetectInternal()
    {
        var result = new GpuDetectionResult();

        try
        {
            var available = OrtEnv.Instance().GetAvailableProviders();
            result = result with { AvailableProviders = available };
            Debug.WriteLine($"[GpuDetection] Available providers: {string.Join(", ", available)}");

            if (TryProvider("CoreMLExecutionProvider", available))
            {
                result = result with { BestProvider = "CoreML", GpuName = "Apple Silicon" };
                Debug.WriteLine("[GpuDetection] CoreML EP validated");
            }
            else if (TryProvider("DmlExecutionProvider", available))
            {
                result = result with { BestProvider = "DirectML", GpuName = "DirectX 12 GPU" };
                Debug.WriteLine("[GpuDetection] DirectML EP validated");
            }
            else if (TryProvider("CUDAExecutionProvider", available))
            {
                result = result with { BestProvider = "CUDA", GpuName = "NVIDIA GPU" };
                Debug.WriteLine("[GpuDetection] CUDA EP validated");
            }
            else
            {
                Debug.WriteLine("[GpuDetection] No GPU EP available");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GpuDetection] Detection failed: {ex.Message}");
        }

        return result;
    }

    private static bool TryProvider(string providerName, string[] available)
    {
        if (!available.Contains(providerName)) return false;

        try
        {
            using var options = new SessionOptions();
            options.AppendExecutionProvider(providerName);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GpuDetection] {providerName} listed but failed: {ex.Message}");
            return false;
        }
    }
}

/// <summary>GPU 감지 결과. Small record co-located per CLAUDE.md exception.</summary>
public sealed record GpuDetectionResult(
    string[] AvailableProviders,
    string? BestProvider,
    string? GpuName)
{
    /// <summary>기본 생성자 (감지 전 초기 상태).</summary>
    public GpuDetectionResult() : this([], null, null) { }
}
