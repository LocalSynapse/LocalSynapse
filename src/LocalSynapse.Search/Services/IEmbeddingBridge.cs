namespace LocalSynapse.Search.Services;

/// <summary>
/// 임베딩 생성 기능의 Search-local 추상화.
/// UI DI 레이어에서 Pipeline.IEmbeddingService를 이 인터페이스로 브릿지한다.
/// </summary>
public interface IEmbeddingBridge
{
    /// <summary>모델 준비 완료 여부.</summary>
    bool IsReady { get; }
    /// <summary>현재 활성 모델 ID.</summary>
    string? ActiveModelId { get; }
    /// <summary>텍스트의 임베딩 벡터를 생성한다.</summary>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
}
