using System.Diagnostics;
using LocalSynapse.Core.Diagnostics;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Pipeline.Interfaces;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LocalSynapse.Pipeline.Embedding;

/// <summary>
/// ONNX 기반 임베딩 생성 서비스.
/// BertTokenizer + OnnxModelLoader를 조합하여 텍스트 → float[] 벡터를 생성한다.
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly ISettingsStore _settings;
    private readonly BertTokenizer _tokenizer = new();
    private readonly OnnxModelLoader _modelLoader = new();

    /// <summary>모델 준비 완료 여부.</summary>
    public bool IsReady => _tokenizer.IsLoaded && _modelLoader.IsLoaded;

    /// <summary>현재 활성 모델 ID.</summary>
    public string? ActiveModelId => _modelLoader.CurrentModelId;

    /// <summary>벡터 차원.</summary>
    public int VectorDimension => _modelLoader.EmbeddingDimension;

    /// <summary>EmbeddingService 생성자.</summary>
    public EmbeddingService(ISettingsStore settings)
    {
        _settings = settings;
    }

    /// <summary>토크나이저와 ONNX 모델을 초기화한다.</summary>
    public async Task InitializeAsync(string modelId, CancellationToken ct = default)
    {
        var modelDir = Path.Combine(_settings.GetModelFolder(), modelId);

        if (!Directory.Exists(modelDir))
            throw new DirectoryNotFoundException($"Model directory not found: {modelDir}");

        var sw = Stopwatch.StartNew();
        var tokSw = Stopwatch.StartNew();
        await _tokenizer.LoadAsync(modelDir, ct);
        var tokMs = tokSw.ElapsedMilliseconds;

        var modSw = Stopwatch.StartNew();
        await _modelLoader.LoadAsync(modelId, modelDir, "Cruise", ct);
        var modMs = modSw.ElapsedMilliseconds;

        SpeedDiagLog.Log("EMB_INIT",
            "model", modelId,
            "tokenizer_ms", tokMs,
            "model_load_ms", modMs,
            "total_ms", sw.ElapsedMilliseconds,
            "dim", VectorDimension);
        Debug.WriteLine($"[EmbeddingService] Initialized: {modelId}, dim={VectorDimension}");
    }

    /// <summary>단일 텍스트의 임베딩을 생성한다.</summary>
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!IsReady)
            throw new InvalidOperationException("EmbeddingService is not initialized");

        var (inputIds, attentionMask) = _tokenizer.Encode(text);

        var seqLen = inputIds.Length;
        var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, seqLen });
        var maskTensor = new DenseTensor<long>(attentionMask, new[] { 1, seqLen });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor),
        };

        // Add token_type_ids if model expects it (no lock needed — InputMetadata is immutable after load)
        if (_modelLoader.HasInput("token_type_ids"))
        {
            var typeIds = new long[seqLen];
            var typeIdsTensor = new DenseTensor<long>(typeIds, new[] { 1, seqLen });
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", typeIdsTensor));
        }

        using var results = await _modelLoader.RunInferenceAsync(inputs, ct).ConfigureAwait(false);
        return ExtractEmbedding(results, attentionMask);
    }

    /// <summary>배치 텍스트의 임베딩을 생성한다.</summary>
    public async Task<float[][]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default)
    {
        var embeddings = new float[texts.Length][];
        for (int i = 0; i < texts.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            embeddings[i] = await GenerateEmbeddingAsync(texts[i], ct);
        }
        return embeddings;
    }

    /// <summary>ONNX 세션을 새 성능 모드로 재생성한다. 토크나이저는 유지된다.</summary>
    public async Task ReloadSessionWithModeAsync(string mode, CancellationToken ct = default)
    {
        await _modelLoader.ReloadSessionWithModeAsync(mode, ct);
    }

    /// <summary>모델을 해제한다.</summary>
    public void Unload()
    {
        _modelLoader.Unload();
        Debug.WriteLine("[EmbeddingService] Model unloaded");
    }

    private float[] ExtractEmbedding(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        long[] attentionMask)
    {
        // Find output: prefer "sentence_embedding" > "embedding" > "last_hidden_state" > first
        DisposableNamedOnnxValue? output = null;
        foreach (var name in new[] { "sentence_embedding", "embedding", "pooler_output", "last_hidden_state" })
        {
            output = results.FirstOrDefault(r => r.Name == name);
            if (output != null) break;
        }
        output ??= results.First();

        var tensor = output.AsTensor<float>();
        var dims = tensor.Dimensions;

        if (dims.Length == 3)
        {
            // [batch, seq_len, hidden_size] → mean pooling
            var seqLen = dims[1];
            var hiddenSize = dims[2];
            return MeanPool(tensor, seqLen, hiddenSize, attentionMask);
        }

        if (dims.Length == 2)
        {
            // [batch, hidden_size] → extract first row
            var hiddenSize = dims[1];
            var embedding = new float[hiddenSize];
            for (int i = 0; i < hiddenSize; i++)
                embedding[i] = tensor[0, i];
            return Normalize(embedding);
        }

        throw new InvalidOperationException($"Unexpected output tensor rank: {dims.Length}");
    }

    private static float[] MeanPool(Tensor<float> tensor, int seqLen, int hiddenSize, long[] attentionMask)
    {
        var embedding = new float[hiddenSize];
        var maskSum = 0f;

        for (int s = 0; s < seqLen; s++)
        {
            var mask = s < attentionMask.Length ? attentionMask[s] : 0;
            if (mask == 0) continue;
            maskSum += mask;
            for (int h = 0; h < hiddenSize; h++)
                embedding[h] += tensor[0, s, h] * mask;
        }

        if (maskSum > 0)
        {
            for (int h = 0; h < hiddenSize; h++)
                embedding[h] /= maskSum;
        }

        return Normalize(embedding);
    }

    private static float[] Normalize(float[] vector)
    {
        var norm = 0f;
        foreach (var v in vector) norm += v * v;
        norm = MathF.Sqrt(norm);

        if (norm > 0)
        {
            for (int i = 0; i < vector.Length; i++)
                vector[i] /= norm;
        }

        return vector;
    }
}
