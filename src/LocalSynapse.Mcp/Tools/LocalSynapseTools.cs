using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Search;
using LocalSynapse.Search.Interfaces;

namespace LocalSynapse.Mcp.Tools;

/// <summary>
/// MCP SDK [McpServerTool] 방식으로 구현된 LocalSynapse 도구 4종.
/// DI 서비스는 SDK가 파라미터에서 자동 주입한다.
/// </summary>
[McpServerToolType]
public sealed class LocalSynapseTools
{
    // ── search_files ──────────────────────────────────────────────

    /// <summary>하이브리드 BM25 검색을 실행한다.</summary>
    [McpServerTool(Name = "search_files"), Description(
        "Search inside document contents using hybrid BM25 search. " +
        "Finds files containing the query text even if the filename doesn't match. " +
        "Supports Word, Excel, PowerPoint, PDF, HWP, and more.")]
    public static async Task<string> SearchFiles(
        IHybridSearch hybridSearch,
        [Description("The search query text")] string query,
        [Description("Maximum number of results to return (1-100, default 20)")] int topK = 20,
        [Description("Filter by file extensions, e.g. [\".pdf\", \".docx\"]")] string[]? extensions = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return JsonSerializer.Serialize(new { error = "Query parameter is required and cannot be empty." });

        topK = Math.Clamp(topK, 1, 100);

        Debug.WriteLine($"[MCP:search_files] query='{query}', topK={topK}, extensions={extensions?.Length ?? 0}");

        var options = new SearchOptions
        {
            TopK = topK,
            ExtensionFilter = extensions?.ToList()
        };
        var response = await hybridSearch.SearchAsync(query, options, cancellationToken);

        var output = response.Items.Select(r => new
        {
            fileId = r.FileId,
            filename = r.Filename,
            path = r.Path,
            extension = r.Extension,
            score = Math.Round(r.HybridScore, 4),
            matchSnippet = r.MatchSnippet,
            matchSource = r.MatchSource.ToString(),
            modifiedAt = r.ModifiedAt
        });

        return JsonSerializer.Serialize(new
        {
            query = response.Query,
            mode = response.Mode.ToString(),
            count = response.Count,
            items = output,
            stats = new
            {
                bm25Count = response.Stats.Bm25Count,
                denseCount = response.Stats.DenseCount,
                totalCandidates = response.Stats.TotalCandidates,
                finalCount = response.Stats.FinalCount,
                durationMs = response.Stats.DurationMs
            }
        });
    }

    // ── get_file_content ──────────────────────────────────────────

    /// <summary>인덱싱된 파일의 추출 텍스트를 반환한다.</summary>
    [McpServerTool(Name = "get_file_content"), Description(
        "Read the extracted text content of an indexed file. " +
        "Returns parsed text chunks concatenated. " +
        "Content is capped at 1MB to prevent memory issues.")]
    public static Task<string> GetFileContent(
        IFileRepository fileRepository,
        IChunkRepository chunkRepository,
        [Description("The file ID to retrieve content for")] string fileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            return Task.FromResult(JsonSerializer.Serialize(new { error = "fileId parameter is required." }));

        Debug.WriteLine($"[MCP:get_file_content] fileId='{fileId}'");

        var file = fileRepository.GetById(fileId);
        if (file is null)
        {
            var refId = Guid.NewGuid().ToString("N")[..12];
            Debug.WriteLine($"[MCP:get_file_content] File not found [{refId}]: {fileId}");
            return Task.FromResult(JsonSerializer.Serialize(new { error = $"File not found (ref: {refId})" }));
        }

        var chunks = chunkRepository.GetChunksForFile(fileId);
        var chunkList = chunks.ToList();

        if (chunkList.Count == 0)
        {
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                content = "This file has been scanned but its content has not been extracted yet.",
                path = file.Path,
                filename = file.Filename,
                chunksAvailable = 0
            }));
        }

        // Content size cap: 1MB
        const int maxContentBytes = 1_048_576;
        var sb = new StringBuilder();
        var truncated = false;

        foreach (var chunk in chunkList)
        {
            if (sb.Length + chunk.Text.Length > maxContentBytes)
            {
                truncated = true;
                break;
            }
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(chunk.Text);
        }

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            content = sb.ToString(),
            path = file.Path,
            filename = file.Filename,
            extension = file.Extension,
            chunksAvailable = chunkList.Count,
            truncated
        }));
    }

    // ── list_indexed_files ────────────────────────────────────────

    /// <summary>인덱싱된 파일 목록을 반환한다.</summary>
    [McpServerTool(Name = "list_indexed_files"), Description(
        "List files that have been indexed. " +
        "Can filter by folder path and file extension.")]
    public static Task<string> ListIndexedFiles(
        IFileRepository fileRepository,
        [Description("Folder path to list files from (optional)")] string? folder = null,
        [Description("File extension filter, e.g. '.pdf' (optional)")] string? extension = null,
        [Description("Maximum number of files to return (1-200, default 50)")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 200);

        Debug.WriteLine($"[MCP:list_indexed_files] folder='{folder}', ext='{extension}', limit={limit}");

        var files = fileRepository.ListFilesUnderFolder(folder, extension, limit);

        var output = files.Select(f => new
        {
            id = f.Id,
            filename = f.Filename,
            path = f.Path,
            extension = f.Extension,
            sizeBytes = f.SizeBytes,
            modifiedAt = f.ModifiedAt,
            extractStatus = f.ExtractStatus,
            chunkCount = f.ChunkCount
        });

        return Task.FromResult(JsonSerializer.Serialize(new { count = files.Count, files = output }));
    }

    // ── get_pipeline_status ───────────────────────────────────────

    /// <summary>파이프라인 상태를 반환한다.</summary>
    [McpServerTool(Name = "get_pipeline_status"), Description(
        "Check the current indexing pipeline status. " +
        "Shows scan progress, indexing progress, and embedding status.")]
    public static Task<string> GetPipelineStatus(
        IPipelineStampRepository pipelineStampRepository,
        CancellationToken cancellationToken = default)
    {
        Debug.WriteLine("[MCP:get_pipeline_status]");

        var stamp = pipelineStampRepository.GetCurrent();

        if (stamp is null)
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                status = "No pipeline data available. Run the app to start indexing."
            }));

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            scanComplete = stamp.ScanComplete,
            totalFiles = stamp.TotalFiles,
            indexedFiles = stamp.IndexedFiles,
            totalChunks = stamp.TotalChunks,
            embeddedChunks = stamp.EmbeddedChunks,
            embeddingComplete = stamp.EmbeddingComplete,
            lastScanAt = stamp.ScanCompletedAt,
            lastIndexAt = stamp.IndexingCompletedAt
        }));
    }

}
