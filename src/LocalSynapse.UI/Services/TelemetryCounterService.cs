using System.Diagnostics;
using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.UI.Services;

/// <summary>
/// 인메모리 텔레메트리 카운터. Snapshot/Reset 분리 패턴.
/// Snapshot()은 읽기 전용, POST 성공 시에만 ResetCounters() 호출.
/// Singleton — 앱 세션 동안 누적. SQLite/디스크 저장 없음.
/// </summary>
public sealed class TelemetryCounterService
{
    private readonly object _lock = new();
    private readonly IPipelineStampRepository _stampRepo;
    private int _searchCount;
    private int _emptyResultCount;
    private long _responseTimeSumMs;
    private int _responseTimeSamples;
    private int _modalityBm25;
    private int _modalityDense;
    private int _modalityHybrid;
    private int _topResultClickCount;

    /// <summary>TelemetryCounterService 생성자.</summary>
    public TelemetryCounterService(IPipelineStampRepository stampRepo)
    {
        _stampRepo = stampRepo;
    }

    /// <summary>검색 실행 후 호출. 모든 검색 관련 카운터를 한 번에 기록한다.</summary>
    public void RecordSearch(string mode, int durationMs, int resultCount)
    {
        lock (_lock)
        {
            _searchCount++;
            if (resultCount == 0) _emptyResultCount++;
            _responseTimeSumMs += durationMs;
            _responseTimeSamples++;
            switch (mode)
            {
                case "FtsOnly": _modalityBm25++; break;
                case "Dense": _modalityDense++; break;
                case "Hybrid": _modalityHybrid++; break;
            }
        }
    }

    /// <summary>Rank-1 결과 클릭 시 호출.</summary>
    public void RecordTopResultClick()
    {
        lock (_lock) { _topResultClickCount++; }
    }

    /// <summary>
    /// 현재 카운터 값의 읽기 전용 스냅샷. 카운터를 리셋하지 않는다.
    /// indexed_doc_count_bucket은 내부적으로 IPipelineStampRepository를 조회해 계산.
    /// </summary>
    public TelemetrySnapshot Snapshot()
    {
        lock (_lock)
        {
            return new TelemetrySnapshot
            {
                SearchCount = _searchCount,
                EmptyResultCount = _emptyResultCount,
                AvgResponseMs = _responseTimeSamples > 0
                    ? (int)(_responseTimeSumMs / _responseTimeSamples)
                    : 0,
                ResponseTimeSamples = _responseTimeSamples,
                ModalityBm25 = _modalityBm25,
                ModalityDense = _modalityDense,
                ModalityHybrid = _modalityHybrid,
                TopResultClickCount = _topResultClickCount,
                IndexedDocCountBucket = ComputeBucket(),
            };
        }
    }

    /// <summary>
    /// 소비된 스냅샷 값을 카운터에서 차감한다. POST 성공 시에만 호출.
    /// 스냅샷 취득 후 POST 중 발생한 신규 카운트는 보존된다.
    /// </summary>
    public void ResetCounters(TelemetrySnapshot consumed)
    {
        lock (_lock)
        {
            _searchCount -= consumed.SearchCount;
            _emptyResultCount -= consumed.EmptyResultCount;
            if (_responseTimeSamples <= consumed.ResponseTimeSamples)
            {
                _responseTimeSumMs = 0;
                _responseTimeSamples = 0;
            }
            else
            {
                _responseTimeSamples -= consumed.ResponseTimeSamples;
                _responseTimeSumMs -= (long)consumed.AvgResponseMs * consumed.ResponseTimeSamples;
                if (_responseTimeSumMs < 0) _responseTimeSumMs = 0;
            }
            _modalityBm25 -= consumed.ModalityBm25;
            _modalityDense -= consumed.ModalityDense;
            _modalityHybrid -= consumed.ModalityHybrid;
            _topResultClickCount -= consumed.TopResultClickCount;
        }
    }

    private string ComputeBucket()
    {
        try
        {
            var stamps = _stampRepo.GetCurrent();
            return stamps.TotalFiles switch
            {
                < 1000 => "<1k",
                < 10000 => "1k-10k",
                < 100000 => "10k-100k",
                _ => "100k+",
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Telemetry] ComputeBucket error: {ex.Message}");
            return "<1k";
        }
    }
}
