using LocalSynapse.Core.Models;
using LocalSynapse.Search;
using LocalSynapse.Search.Interfaces;

namespace LocalSynapse.Search.Tests.Fakes;

/// <summary>
/// Deterministic IBm25Search fake. Returns a pre-seeded hit list per query
/// so the cascade rerank path has a fixed candidate set to operate on.
/// </summary>
internal sealed class FakeBm25Search : IBm25Search
{
    private readonly Dictionary<string, IReadOnlyList<Bm25Hit>> _byQuery;

    public FakeBm25Search(Dictionary<string, IReadOnlyList<Bm25Hit>>? seed = null)
        => _byQuery = seed ?? new Dictionary<string, IReadOnlyList<Bm25Hit>>(StringComparer.Ordinal);

    public IReadOnlyList<Bm25Hit> Search(string query, SearchOptions options)
        => _byQuery.TryGetValue(query, out var hits) ? hits : Array.Empty<Bm25Hit>();

    public IReadOnlyList<Bm25Hit> QuickSearch(string query, int limit = 20)
        => _byQuery.TryGetValue(query, out var hits) ? hits.Take(limit).ToList() : Array.Empty<Bm25Hit>();

    public void ClearCache() { }
}
