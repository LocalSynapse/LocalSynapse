using System.Diagnostics;
using System.Text.Json;
using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.Mcp.Tools;

/// <summary>
/// MCP tool handler for retrieving extracted text content of an indexed file.
/// Returns parsed chunk text from DB — no raw file system access.
/// </summary>
public sealed class GetFileContentTool
{
    private readonly IFileRepository _fileRepository;
    private readonly IChunkRepository _chunkRepository;

    /// <summary>
    /// Initializes a new instance of <see cref="GetFileContentTool"/>.
    /// </summary>
    /// <param name="fileRepository">The file repository for looking up file metadata.</param>
    /// <param name="chunkRepository">The chunk repository for retrieving extracted text.</param>
    public GetFileContentTool(IFileRepository fileRepository, IChunkRepository chunkRepository)
    {
        _fileRepository = fileRepository;
        _chunkRepository = chunkRepository;
    }

    /// <summary>
    /// Executes the get_file_content tool with the given arguments.
    /// Returns extracted chunk text from DB, not raw file bytes.
    /// </summary>
    /// <param name="arguments">JSON element containing the fileId parameter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An object containing the extracted text content.</returns>
    public Task<object> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var fileId = arguments.TryGetProperty("fileId", out var idProp)
            ? idProp.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(fileId))
        {
            Debug.WriteLine("[GetFileContentTool] Empty fileId received.");
            return Task.FromResult<object>(new { error = "fileId parameter is required." });
        }

        Debug.WriteLine($"[GetFileContentTool] Getting content for fileId: {fileId}");

        var file = _fileRepository.GetById(fileId);
        if (file is null)
        {
            var refId = Guid.NewGuid().ToString("N")[..12];
            Debug.WriteLine($"[GetFileContentTool] File not found [{refId}]: {fileId}");
            return Task.FromResult<object>(new { error = $"File not found (ref: {refId})" });
        }

        // Return chunk text from DB — no raw file system access
        var chunks = _chunkRepository.GetChunksForFile(fileId);
        // GetChunksForFile returns ORDER BY chunk_index (ChunkRepository.cs:117)
        var chunkTexts = chunks.Select(c => c.Text).ToList();

        if (chunkTexts.Count == 0)
        {
            Debug.WriteLine($"[GetFileContentTool] No chunks for fileId: {fileId}");
            return Task.FromResult<object>(new
            {
                content = "This file has been scanned but its content has not been extracted yet. " +
                          "The file may be queued for processing, or its format may not be supported for text extraction.",
                path = file.Path,
                filename = file.Filename,
                chunksAvailable = 0
            });
        }

        var content = string.Join("\n\n", chunkTexts);
        Debug.WriteLine($"[GetFileContentTool] Returned {chunkTexts.Count} chunks, {content.Length} chars for: {file.Path}");

        return Task.FromResult<object>(new
        {
            content,
            path = file.Path,
            filename = file.Filename,
            extension = file.Extension,
            chunksAvailable = chunkTexts.Count
        });
    }
}
