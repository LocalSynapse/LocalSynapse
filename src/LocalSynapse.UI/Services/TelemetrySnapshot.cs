namespace LocalSynapse.UI.Services;

/// <summary>텔레메트리 스냅샷. ping payload에 직렬화된다.</summary>
public sealed class TelemetrySnapshot
{
    /// <summary>총 검색 횟수.</summary>
    public int SearchCount { get; init; }

    /// <summary>결과 0건 검색 횟수.</summary>
    public int EmptyResultCount { get; init; }

    /// <summary>평균 응답 시간 (밀리초).</summary>
    public int AvgResponseMs { get; init; }

    /// <summary>응답 시간 샘플 수 (ResetCounters 차감용).</summary>
    public int ResponseTimeSamples { get; init; }

    /// <summary>BM25 검색 횟수.</summary>
    public int ModalityBm25 { get; init; }

    /// <summary>Dense 검색 횟수.</summary>
    public int ModalityDense { get; init; }

    /// <summary>Hybrid 검색 횟수.</summary>
    public int ModalityHybrid { get; init; }

    /// <summary>Rank-1 결과 클릭 횟수.</summary>
    public int TopResultClickCount { get; init; }

    /// <summary>인덱싱된 문서 수 버킷.</summary>
    public string IndexedDocCountBucket { get; init; } = "<1k";
}
