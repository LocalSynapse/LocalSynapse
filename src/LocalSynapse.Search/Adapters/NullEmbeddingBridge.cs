using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Adapters;

/// <summary>
/// Dense search 비활성 시 사용하는 IEmbeddingBridge null object.
/// IsReady=false를 반환하여 HybridSearchService가 dense 경로를 skip하게 한다.
/// </summary>
public sealed class NullEmbeddingBridge : IEmbeddingBridge
{
    /// <summary>모델 준비 완료 여부. 항상 false.</summary>
    public bool IsReady => false;

    /// <summary>현재 활성 모델 ID. 항상 null.</summary>
    public string? ActiveModelId => null;

    /// <summary>호출 시 빈 배열을 반환한다. Dense 비활성 상태이므로 실제 호출되지 않는다.</summary>
    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
        => Task.FromResult(Array.Empty<float>());
}
