namespace LocalSynapse.Core.Models;

public sealed class Bm25Hit
{
    public required string FileId { get; set; }
    public required string Filename { get; set; }
    public required string Path { get; set; }
    public required string Extension { get; set; }
    public string? FolderPath { get; set; }
    public string? Content { get; set; }
    public double Score { get; set; }
    public List<string> MatchedTerms { get; set; } = [];
    public string? ModifiedAt { get; set; }
    public bool IsDirectory { get; set; }
    public MatchSource MatchSource { get; set; }
    public double FilenameRank { get; set; }
    public double ContentRank { get; set; }
    public double FolderRank { get; set; }
}

public sealed class DenseHit
{
    public required string FileId { get; set; }
    public int ChunkId { get; set; }
    public required string Content { get; set; }
    public double Score { get; set; }
    public string? Path { get; set; }
    public string? Filename { get; set; }
    public string? Extension { get; set; }
    public string? ModifiedAt { get; set; }
}

public sealed class HybridHit
{
    public required string FileId { get; set; }
    public required string Filename { get; set; }
    public required string Path { get; set; }
    public required string Extension { get; set; }
    public string? FolderPath { get; set; }
    public double HybridScore { get; set; }
    public double Bm25Score { get; set; }
    public double DenseScore { get; set; }
    public List<string> MatchedTerms { get; set; } = [];
    public string? ModifiedAt { get; set; }
    public string? MatchSnippet { get; set; }
    public MatchSource MatchSource { get; set; }
    public bool IsDirectory { get; set; }
    public string? FamilyKey { get; set; }
    public string? FamilyHeader { get; set; }
}

[Flags]
public enum MatchSource
{
    None = 0,
    FileName = 1,
    Content = 2,
    Folder = 4
}

public sealed class SearchResponse
{
    public required string Query { get; set; }
    public SearchMode Mode { get; set; }
    public int Count => Items.Count;
    public List<HybridHit> Items { get; set; } = [];
    public SearchStats Stats { get; set; } = new();
}

public sealed class SearchStats
{
    public int Bm25Count { get; set; }
    public int DenseCount { get; set; }
    public int TotalCandidates { get; set; }
    public int FinalCount { get; set; }
    public int DurationMs { get; set; }
}

public enum SearchMode { Hybrid, FtsOnly }
