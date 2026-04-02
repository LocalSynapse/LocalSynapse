namespace LocalSynapse.Core.Models;

public sealed class PipelineStamps
{
    public int TotalFiles { get; set; }
    public int TotalFolders { get; set; }
    public string? ScanCompletedAt { get; set; }
    public int ContentSearchableFiles { get; set; }
    public int IndexedFiles { get; set; }
    public int TotalChunks { get; set; }
    public string? IndexingCompletedAt { get; set; }
    public int EmbeddableChunks { get; set; }
    public int EmbeddedChunks { get; set; }
    public string? EmbeddingCompletedAt { get; set; }
    public string? LastAutoRunAt { get; set; }
    public int AutoRunCount { get; set; }
    public int SkippedCloud { get; set; }
    public int SkippedTooLarge { get; set; }
    public int SkippedEncrypted { get; set; }
    public int SkippedParseError { get; set; }
    public int PendingFiles { get; set; }
    public bool ScanComplete => ScanCompletedAt != null;
    public bool IndexingComplete => IndexingCompletedAt != null;
    public bool EmbeddingComplete => EmbeddingCompletedAt != null;
    public bool SearchReady => TotalFiles > 0;
    public bool HasEmbeddings => EmbeddedChunks > 0;
    public double IndexingPercent => ContentSearchableFiles > 0
        ? Math.Min(100.0, (double)IndexedFiles / ContentSearchableFiles * 100) : 0;
    public double EmbeddingPercent => EmbeddableChunks > 0
        ? Math.Min(100.0, (double)EmbeddedChunks / EmbeddableChunks * 100) : 0;
}
