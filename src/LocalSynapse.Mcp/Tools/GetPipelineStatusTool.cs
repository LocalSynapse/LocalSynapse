using System.Diagnostics;
using System.Text.Json;
using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.Mcp.Tools;

/// <summary>
/// MCP tool handler for retrieving the current pipeline processing status.
/// </summary>
public sealed class GetPipelineStatusTool
{
    private readonly IPipelineStampRepository _stampRepository;

    /// <summary>
    /// Initializes a new instance of <see cref="GetPipelineStatusTool"/>.
    /// </summary>
    /// <param name="stampRepository">The pipeline stamp repository.</param>
    public GetPipelineStatusTool(IPipelineStampRepository stampRepository)
    {
        _stampRepository = stampRepository;
    }

    /// <summary>
    /// Executes the get_pipeline_status tool.
    /// </summary>
    /// <param name="arguments">JSON element (empty object expected).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An object containing the current pipeline status.</returns>
    public Task<object> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        Debug.WriteLine("[GetPipelineStatusTool] Getting pipeline status.");

        var stamps = _stampRepository.GetCurrent();

        object result = new
        {
            totalFiles = stamps.TotalFiles,
            totalFolders = stamps.TotalFolders,
            contentSearchableFiles = stamps.ContentSearchableFiles,
            scanCompletedAt = stamps.ScanCompletedAt,
            scanComplete = stamps.ScanComplete,
            indexedFiles = stamps.IndexedFiles,
            totalChunks = stamps.TotalChunks,
            indexingCompletedAt = stamps.IndexingCompletedAt,
            indexingComplete = stamps.IndexingComplete,
            indexingPercent = stamps.IndexingPercent,
            embeddableChunks = stamps.EmbeddableChunks,
            embeddedChunks = stamps.EmbeddedChunks,
            embeddingCompletedAt = stamps.EmbeddingCompletedAt,
            embeddingComplete = stamps.EmbeddingComplete,
            embeddingPercent = stamps.EmbeddingPercent,
            searchReady = stamps.SearchReady,
            hasEmbeddings = stamps.HasEmbeddings,
            pendingFiles = stamps.PendingFiles,
            skippedCloud = stamps.SkippedCloud,
            skippedTooLarge = stamps.SkippedTooLarge,
            skippedEncrypted = stamps.SkippedEncrypted,
            skippedParseError = stamps.SkippedParseError,
            lastAutoRunAt = stamps.LastAutoRunAt,
            autoRunCount = stamps.AutoRunCount
        };

        Debug.WriteLine($"[GetPipelineStatusTool] Status: {stamps.TotalFiles} files, indexing={stamps.IndexingPercent:F1}%, embedding={stamps.EmbeddingPercent:F1}%");

        return Task.FromResult(result);
    }
}
