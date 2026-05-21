using LocalSynapse.Pipeline.Embedding;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Tests.Fakes;

/// <summary>
/// Test-local adapter that wraps the real Pipeline EmbeddingService as a
/// Search-side IEmbeddingBridge. Mirrors the production adapter that lives
/// in the UI DI layer; duplicated here because the production adapter is
/// private and not reachable from this test assembly.
/// </summary>
internal sealed class TestEmbeddingBridgeAdapter : IEmbeddingBridge
{
    private readonly EmbeddingService _inner;

    public TestEmbeddingBridgeAdapter(EmbeddingService inner) => _inner = inner;

    public bool IsReady => _inner.IsReady;
    public string? ActiveModelId => _inner.ActiveModelId;

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
        => _inner.GenerateEmbeddingAsync(text, ct);
}
