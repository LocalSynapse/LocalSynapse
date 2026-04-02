using LocalSynapse.Core.Models;

namespace LocalSynapse.Search.Interfaces;

public interface IBm25Search
{
    IReadOnlyList<Bm25Hit> Search(string query, SearchOptions options);
    IReadOnlyList<Bm25Hit> QuickSearch(string query, int limit = 20);
    void ClearCache();
}
