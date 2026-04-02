namespace LocalSynapse.Search;

public sealed class SearchOptions
{
    public float Bm25Weight { get; set; } = 0.8f;
    public float DenseWeight { get; set; } = 0.2f;
    public int TopK { get; set; } = 20;
    public int ChunksPerFile { get; set; } = 1;
    public List<string>? ExtensionFilter { get; set; }
    public string? DateFilter { get; set; }
    public string? ScopeFilter { get; set; }
    public string? SortBy { get; set; }
}
