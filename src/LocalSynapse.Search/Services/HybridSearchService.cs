using System.Diagnostics;
using LocalSynapse.Core.Models;
using LocalSynapse.Search.Interfaces;

namespace LocalSynapse.Search.Services;

/// <summary>
/// BM25 + Dense 하이브리드 검색 서비스.
/// Phase 2c 이후 Dense search는 EmptyDenseSearch로 비활성화되어 사실상 FtsOnly 모드로 동작한다.
/// Phase 3에서 Dense search 재설계 예정. IsAvailable=true인 IDenseSearch 구현이 주입되면 Hybrid 모드 자동 활성화.
/// </summary>
public sealed class HybridSearchService : IHybridSearch
{
    private readonly IBm25Search _bm25;
    private readonly IDenseSearch _dense;

    /// <summary>현재 검색 모드.</summary>
    public SearchMode CurrentMode => _dense.IsAvailable ? SearchMode.Hybrid : SearchMode.FtsOnly;

    /// <summary>HybridSearchService 생성자.</summary>
    public HybridSearchService(IBm25Search bm25, IDenseSearch dense)
    {
        _bm25 = bm25;
        _dense = dense;
    }

    /// <summary>하이브리드 검색을 실행한다.</summary>
    public async Task<SearchResponse> SearchAsync(string query, SearchOptions options, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var cleaned = NaturalQueryParser.RemoveStopwords(query);

        // BM25 search
        var bm25Results = await Task.Run(() => _bm25.Search(cleaned, options), ct);
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<HybridHit> hybridHits;

        if (_dense.IsAvailable)
        {
            try
            {
                // Dense search with original query (preserves semantic context)
                var denseResults = await _dense.SearchAsync(query, options, ct);
                ct.ThrowIfCancellationRequested();

                if (denseResults.Count > 0)
                {
                    hybridHits = RrfFusion.Combine(bm25Results, denseResults, options);
                }
                else
                {
                    hybridHits = MapBm25ToHybrid(bm25Results);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Debug.WriteLine($"[HybridSearch] Dense fallback: {ex.Message}");
                hybridHits = MapBm25ToHybrid(bm25Results);
            }
        }
        else
        {
            hybridHits = MapBm25ToHybrid(bm25Results);
        }

        sw.Stop();

        return new SearchResponse
        {
            Query = query,
            Mode = CurrentMode,
            Items = hybridHits.ToList(),
            Stats = new SearchStats
            {
                Bm25Count = bm25Results.Count,
                DenseCount = _dense.IsAvailable ? hybridHits.Count(h => h.DenseScore > 0) : 0,
                TotalCandidates = bm25Results.Count,
                FinalCount = hybridHits.Count,
                DurationMs = (int)sw.ElapsedMilliseconds,
            }
        };
    }

    /// <summary>빠른 파일명 검색을 실행한다.</summary>
    public Task<SearchResponse> QuickSearchAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        var bm25Results = _bm25.QuickSearch(query, limit);
        var hybridHits = MapBm25ToHybrid(bm25Results);

        return Task.FromResult(new SearchResponse
        {
            Query = query,
            Mode = SearchMode.FtsOnly,
            Items = hybridHits.ToList(),
            Stats = new SearchStats
            {
                Bm25Count = bm25Results.Count,
                FinalCount = hybridHits.Count,
            }
        });
    }

    private static IReadOnlyList<HybridHit> MapBm25ToHybrid(IReadOnlyList<Bm25Hit> bm25Results)
    {
        return bm25Results.Select(b => new HybridHit
        {
            FileId = b.FileId,
            Filename = b.Filename,
            Path = b.Path,
            Extension = b.Extension,
            FolderPath = b.FolderPath,
            HybridScore = b.Score,
            Bm25Score = b.Score,
            DenseScore = 0,
            MatchedTerms = b.MatchedTerms,
            ModifiedAt = b.ModifiedAt,
            IsDirectory = b.IsDirectory,
            MatchSource = b.MatchSource,
        }).ToList();
    }
}
