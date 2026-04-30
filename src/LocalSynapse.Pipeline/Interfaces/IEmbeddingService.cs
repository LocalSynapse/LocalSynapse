namespace LocalSynapse.Pipeline.Interfaces;

public interface IEmbeddingService
{
    bool IsReady { get; }
    string? ActiveModelId { get; }
    int VectorDimension { get; }
    Task InitializeAsync(string modelId, CancellationToken ct = default);
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
    Task<float[][]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default);
    /// <summary>Reloads the ONNX session with new performance mode options. Tokenizer is preserved.</summary>
    Task ReloadSessionWithModeAsync(string mode, CancellationToken ct = default);
    void Unload();
}
