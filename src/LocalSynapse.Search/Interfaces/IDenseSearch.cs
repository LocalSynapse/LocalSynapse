using LocalSynapse.Core.Models;

namespace LocalSynapse.Search.Interfaces;

public interface IDenseSearch
{
    bool IsAvailable { get; }
    Task<IReadOnlyList<DenseHit>> SearchAsync(string query, SearchOptions options, CancellationToken ct = default);
}
