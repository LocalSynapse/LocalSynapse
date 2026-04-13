using LocalSynapse.Core.Models;
using LocalSynapse.Search.Interfaces;

namespace LocalSynapse.Search.Services;

/// <summary>
/// Null Object implementation of IDenseSearch. Returns empty results.
/// Dense search is disabled until Phase 3 redesign.
/// </summary>
public sealed class EmptyDenseSearch : IDenseSearch
{
    /// <summary>Always false — dense search is disabled.</summary>
    public bool IsAvailable => false;

    /// <summary>Returns empty list — dense search is disabled.</summary>
    public Task<IReadOnlyList<DenseHit>> SearchAsync(
        string query, SearchOptions options, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DenseHit>>(Array.Empty<DenseHit>());
}
