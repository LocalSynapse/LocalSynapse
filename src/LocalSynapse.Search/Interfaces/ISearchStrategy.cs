using LocalSynapse.Core.Models;

namespace LocalSynapse.Search.Interfaces;

/// <summary>
/// One concrete search dispatch shape (Fast, Smart, Deep). The orchestrator
/// picks the strategy whose <see cref="Mode"/> matches the user's persisted
/// SearchMode and dispatches the query to it.
/// </summary>
/// <remarks>
/// Cancellation semantics are per-implementation:
/// <list type="bullet">
///   <item><description><c>Bm25SearchStrategy</c> — BM25 layer is synchronous;
///     <c>CancellationToken</c> is honored at scheduling boundaries (before
///     <c>Task.Run</c>) and best-effort otherwise.</description></item>
///   <item><description><c>CascadeSearchStrategy</c> — CT propagates to the
///     embedding-fetch SQL and the cosine accumulation loop. Microsoft.Data.Sqlite
///     honors CT between commands, not within a single <c>ExecuteReader</c>; for
///     the ~100 ms candidate fetch this is best-effort.</description></item>
/// </list>
/// </remarks>
public interface ISearchStrategy
{
    /// <summary>Which mode this strategy services. Used by the orchestrator to dispatch.</summary>
    SearchMode Mode { get; }

    /// <summary>Run the search and produce a hybrid-style response.</summary>
    Task<SearchResponse> SearchAsync(string query, SearchOptions options, CancellationToken ct = default);
}
