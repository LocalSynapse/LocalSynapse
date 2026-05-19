using LocalSynapse.Core.Models;
using LocalSynapse.Search.Interfaces;

namespace LocalSynapse.Search.Services;

/// <summary>
/// Fast strategy — BM25 only. Wraps the synchronous Bm25SearchService.Search
/// behind ISearchStrategy. The CancellationToken is honored only at scheduling
/// (Task.Run pre-execution); the BM25 SQL itself is not mid-query cancellable.
/// </summary>
public sealed class Bm25SearchStrategy : ISearchStrategy
{
    private readonly IBm25Search _bm25;

    /// <inheritdoc />
    public SearchMode Mode => SearchMode.Fast;

    /// <summary>Bm25SearchStrategy 생성자.</summary>
    public Bm25SearchStrategy(IBm25Search bm25)
    {
        _bm25 = bm25;
    }

    /// <inheritdoc />
    public async Task<SearchResponse> SearchAsync(string query, SearchOptions options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var hits = await Task.Run(() => _bm25.Search(query, options), ct);
        ct.ThrowIfCancellationRequested();

        var items = hits.Select(b => new HybridHit
        {
            FileId       = b.FileId,
            Filename     = b.Filename,
            Path         = b.Path,
            Extension    = b.Extension,
            FolderPath   = b.FolderPath,
            HybridScore  = b.Score,
            Bm25Score    = b.Score,
            DenseScore   = 0,
            MatchedTerms = b.MatchedTerms,
            ModifiedAt   = b.ModifiedAt,
            IsDirectory  = b.IsDirectory,
            MatchSource  = b.MatchSource,
        }).ToList();

        return new SearchResponse
        {
            Query = query,
            Mode  = SearchMode.Fast,
            Items = items,
            Stats = new SearchStats
            {
                Bm25Count       = hits.Count,
                DenseCount      = 0,
                TotalCandidates = hits.Count,
                FinalCount      = items.Count,
            },
        };
    }
}
