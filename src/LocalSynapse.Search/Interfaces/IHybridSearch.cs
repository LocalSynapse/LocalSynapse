using LocalSynapse.Core.Models;

namespace LocalSynapse.Search.Interfaces;

public interface IHybridSearch
{
    SearchMode CurrentMode { get; }
    Task<SearchResponse> SearchAsync(string query, SearchOptions options, CancellationToken ct = default);
    Task<SearchResponse> QuickSearchAsync(string query, int limit = 20, CancellationToken ct = default);
}
