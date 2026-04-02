using System.Diagnostics;
using System.Text.Json;
using LocalSynapse.Search;
using LocalSynapse.Search.Interfaces;

namespace LocalSynapse.Mcp.Tools;

/// <summary>
/// MCP tool handler for searching indexed files using hybrid search.
/// </summary>
public sealed class SearchFilesTool
{
    private readonly IHybridSearch _hybridSearch;

    /// <summary>
    /// Initializes a new instance of <see cref="SearchFilesTool"/>.
    /// </summary>
    /// <param name="hybridSearch">The hybrid search service.</param>
    public SearchFilesTool(IHybridSearch hybridSearch)
    {
        _hybridSearch = hybridSearch;
    }

    /// <summary>
    /// Executes the search_files tool with the given arguments.
    /// </summary>
    /// <param name="arguments">JSON element containing query, topK, and extensions parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The search response as an object.</returns>
    public async Task<object> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var query = arguments.TryGetProperty("query", out var queryProp)
            ? queryProp.GetString() ?? string.Empty
            : string.Empty;

        var topK = arguments.TryGetProperty("topK", out var topKProp)
            ? topKProp.GetInt32()
            : 20;

        List<string>? extensions = null;
        if (arguments.TryGetProperty("extensions", out var extProp) && extProp.ValueKind == JsonValueKind.Array)
        {
            extensions = [];
            foreach (var ext in extProp.EnumerateArray())
            {
                var val = ext.GetString();
                if (val is not null)
                    extensions.Add(val);
            }
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            Debug.WriteLine("[SearchFilesTool] Empty query received.");
            return new { error = "Query parameter is required and cannot be empty." };
        }

        Debug.WriteLine($"[SearchFilesTool] Searching: query='{query}', topK={topK}, extensions={extensions?.Count ?? 0}");

        var options = new SearchOptions
        {
            TopK = topK,
            ExtensionFilter = extensions
        };

        var response = await _hybridSearch.SearchAsync(query, options, ct).ConfigureAwait(false);

        Debug.WriteLine($"[SearchFilesTool] Found {response.Count} results.");

        return new
        {
            query = response.Query,
            mode = response.Mode.ToString(),
            count = response.Count,
            items = response.Items.Select(hit => new
            {
                fileId = hit.FileId,
                filename = hit.Filename,
                path = hit.Path,
                extension = hit.Extension,
                score = hit.HybridScore,
                matchSnippet = hit.MatchSnippet,
                matchSource = hit.MatchSource.ToString(),
                modifiedAt = hit.ModifiedAt
            }),
            stats = new
            {
                bm25Count = response.Stats.Bm25Count,
                denseCount = response.Stats.DenseCount,
                totalCandidates = response.Stats.TotalCandidates,
                finalCount = response.Stats.FinalCount,
                durationMs = response.Stats.DurationMs
            }
        };
    }
}
