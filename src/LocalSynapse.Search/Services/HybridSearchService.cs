using System.Diagnostics;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Search.Interfaces;

namespace LocalSynapse.Search.Services;

/// <summary>
/// Orchestrates search dispatch by mode. Holds one strategy per registered mode
/// and resolves the user's persisted choice on every SearchAsync call.
/// QuickSearch (filename-only) bypasses strategy dispatch and goes straight to
/// the BM25 service.
/// </summary>
public sealed class HybridSearchService : IHybridSearch
{
    private readonly IReadOnlyDictionary<SearchMode, ISearchStrategy> _strategies;
    private readonly IBm25Search _bm25;
    private readonly ISettingsStore _settings;
    private SearchMode _lastDispatched = SearchMode.Fast;

    /// <inheritdoc />
    public SearchMode CurrentMode => _lastDispatched;

    /// <summary>HybridSearchService 생성자.</summary>
    public HybridSearchService(
        IEnumerable<ISearchStrategy> strategies,
        IBm25Search bm25,
        ISettingsStore settings)
    {
        _strategies = strategies.ToDictionary(s => s.Mode);
        _bm25 = bm25;
        _settings = settings;
    }

    /// <inheritdoc />
    public async Task<SearchResponse> SearchAsync(string query, SearchOptions options, CancellationToken ct = default)
    {
        var requested = ParseMode(_settings.GetSearchMode());
        var strategy = ResolveStrategy(requested);
        _lastDispatched = strategy.Mode;
        return await strategy.SearchAsync(query, options, ct);
    }

    /// <inheritdoc />
    public Task<SearchResponse> QuickSearchAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        var bm25Results = _bm25.QuickSearch(query, limit);
        var items = bm25Results.Select(b => new HybridHit
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

        return Task.FromResult(new SearchResponse
        {
            Query = query,
            Mode  = SearchMode.Fast,
            Items = items,
            Stats = new SearchStats
            {
                Bm25Count  = bm25Results.Count,
                FinalCount = items.Count,
            },
        });
    }

    /// <summary>
    /// Resolves a SearchMode to a registered strategy. Falls back to Fast when
    /// the requested mode is not registered (Mcp.Stdio registers only Fast; a
    /// user with persisted SearchMode="smart" lands here without crashing).
    /// </summary>
    private ISearchStrategy ResolveStrategy(SearchMode requested)
    {
        if (_strategies.TryGetValue(requested, out var s)) return s;
        if (_strategies.TryGetValue(SearchMode.Fast, out var fast))
        {
            Debug.WriteLine($"[HybridSearch] {requested} not registered; falling back to Fast.");
            return fast;
        }
        // No Fast registered either — fall back to the first registered strategy.
        return _strategies.Values.First();
    }

    private static SearchMode ParseMode(string raw) => raw?.ToLowerInvariant() switch
    {
        "fast"  => SearchMode.Fast,
        "smart" => SearchMode.Smart,
        "deep"  => SearchMode.Deep,
        _       => SearchMode.Smart, // default
    };
}
