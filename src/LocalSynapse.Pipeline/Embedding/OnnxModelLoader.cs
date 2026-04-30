using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;

namespace LocalSynapse.Pipeline.Embedding;

/// <summary>
/// ONNX 모델 로더. InferenceSession을 관리하고 추론을 실행한다.
/// Thread-safe: _sessionLock으로 session 접근을 보호한다.
/// </summary>
public sealed class OnnxModelLoader : IDisposable
{
    private InferenceSession? _session;
    private string? _currentModelId;
    private int _embeddingDimension;
    private string? _currentModelPath;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    private static readonly Dictionary<string, int> ModelDimensions = new()
    {
        ["bge-m3"] = 1024,
        ["bge-small-en-v1.5"] = 384,
        ["all-MiniLM-L6-v2"] = 384,
        ["qwen3-embedding-0.6b"] = 2560,
    };

    /// <summary>현재 로드된 모델 ID.</summary>
    public string? CurrentModelId => _currentModelId;

    /// <summary>임베딩 벡터 차원.</summary>
    public int EmbeddingDimension => _embeddingDimension;

    /// <summary>모델 로드 여부.</summary>
    public bool IsLoaded => _session != null;

    /// <summary>ONNX 세션을 반환한다.</summary>
    public InferenceSession? GetSession() => _session;

    /// <summary>Check if model has a named input (e.g., token_type_ids). No lock needed — InputMetadata is immutable after load.</summary>
    public bool HasInput(string name) => _session?.InputMetadata.ContainsKey(name) ?? false;

    /// <summary>Run inference under session lock. Session reference never leaks outside.</summary>
    public async Task<IDisposableReadOnlyCollection<DisposableNamedOnnxValue>> RunInferenceAsync(
        IReadOnlyCollection<NamedOnnxValue> inputs, CancellationToken ct = default)
    {
        await _sessionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session is null)
                throw new InvalidOperationException("ONNX model not loaded");
            return _session.Run(inputs);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <summary>ONNX 모델을 로드한다.</summary>
    public Task LoadAsync(string modelId, string modelDir, string mode = "Cruise", CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();

            await _sessionLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _session?.Dispose();
                _session = null;

                var modelPath = FindModelPath(modelDir);
                if (modelPath == null)
                    throw new FileNotFoundException($"No .onnx model file found in {modelDir}");

                var options = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                };

                ApplyPerformanceMode(options, mode);

                Debug.WriteLine($"[OnnxModelLoader] Loading model: {modelPath}");
                _session = new InferenceSession(modelPath, options);
                _currentModelId = modelId;
                _currentModelPath = modelPath;
                _embeddingDimension = ModelDimensions.GetValueOrDefault(modelId, 1024);

                Debug.WriteLine($"[OnnxModelLoader] Model loaded: {modelId}, dim={_embeddingDimension}");
                Debug.WriteLine($"[OnnxModelLoader] Inputs: {string.Join(", ", _session.InputMetadata.Keys)}");
                Debug.WriteLine($"[OnnxModelLoader] Outputs: {string.Join(", ", _session.OutputMetadata.Keys)}");
            }
            finally
            {
                _sessionLock.Release();
            }
        }, ct);
    }

    /// <summary>모델을 해제한다.</summary>
    public void Unload()
    {
        _sessionLock.Wait();
        try
        {
            _session?.Dispose();
            _session = null;
            _currentModelId = null;
            _currentModelPath = null;
            _embeddingDimension = 0;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <summary>리소스를 해제한다.</summary>
    public void Dispose()
    {
        Unload();
        _sessionLock.Dispose();
    }

    /// <summary>현재 모델의 ONNX 세션을 새 성능 모드로 재생성한다. 토크나이저는 유지된다.</summary>
    public async Task ReloadSessionWithModeAsync(string mode, CancellationToken ct = default)
    {
        await _sessionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session is null || _currentModelPath is null)
                throw new InvalidOperationException("No model loaded — cannot reload session");

            _session.Dispose();

            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };
            ApplyPerformanceMode(options, mode);

            Debug.WriteLine($"[OnnxModelLoader] Reloading session with mode: {mode}");
            _session = new InferenceSession(_currentModelPath, options);
            Debug.WriteLine($"[OnnxModelLoader] Session reloaded: mode={mode}");
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private static void ApplyPerformanceMode(SessionOptions options, string mode)
    {
        var cpuCount = Environment.ProcessorCount;
        switch (mode)
        {
            case "Stealth":
                options.IntraOpNumThreads = 1;
                options.InterOpNumThreads = 1;
                break;
            case "Overdrive":
                options.IntraOpNumThreads = cpuCount;
                options.InterOpNumThreads = Math.Max(1, cpuCount / 2);
                break;
            default: // "Cruise" and any unknown value
                options.IntraOpNumThreads = Math.Max(1, cpuCount / 2);
                options.InterOpNumThreads = 1;
                break;
        }
        Debug.WriteLine($"[OnnxModelLoader] Performance mode: {mode}, IntraOp={options.IntraOpNumThreads}, InterOp={options.InterOpNumThreads}");
    }

    private static string? FindModelPath(string modelDir)
    {
        // Search in onnx/ subdirectory first, then root
        var searchDirs = new[] { Path.Combine(modelDir, "onnx"), modelDir };
        var patterns = new[] { "model_q8.onnx", "model_uint8.onnx", "model.onnx" };

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var pattern in patterns)
            {
                var path = Path.Combine(dir, pattern);
                if (File.Exists(path)) return path;
            }

            // Fallback: any .onnx file
            var anyOnnx = Directory.GetFiles(dir, "*.onnx").FirstOrDefault();
            if (anyOnnx != null) return anyOnnx;
        }

        return null;
    }
}
