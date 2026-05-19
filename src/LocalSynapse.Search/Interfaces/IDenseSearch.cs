using LocalSynapse.Core.Models;

namespace LocalSynapse.Search.Interfaces;

/// <summary>
/// Full-corpus dense enumeration interface, retained for the deferred Deep mode.
/// v2.11.0 dispatches via <see cref="ISearchStrategy"/>; the cascade strategy
/// fetches embeddings for BM25 candidates only and does not implement this.
/// </summary>
[Obsolete("Replaced by ISearchStrategy in v2.11.0. Retained for Deep mode reactivation in v2.12.0.")]
public interface IDenseSearch
{
    bool IsAvailable { get; }
    Task<IReadOnlyList<DenseHit>> SearchAsync(string query, SearchOptions options, CancellationToken ct = default);
}
