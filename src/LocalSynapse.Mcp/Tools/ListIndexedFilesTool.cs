using System.Diagnostics;
using System.Text.Json;
using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.Mcp.Tools;

/// <summary>
/// MCP tool handler for listing indexed files with optional folder and extension filters.
/// </summary>
public sealed class ListIndexedFilesTool
{
    private readonly IFileRepository _fileRepository;

    /// <summary>
    /// Initializes a new instance of <see cref="ListIndexedFilesTool"/>.
    /// </summary>
    /// <param name="fileRepository">The file repository for querying indexed files.</param>
    public ListIndexedFilesTool(IFileRepository fileRepository)
    {
        _fileRepository = fileRepository;
    }

    /// <summary>
    /// Executes the list_indexed_files tool with the given arguments.
    /// </summary>
    /// <param name="arguments">JSON element containing optional folder, extension, and limit parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An object containing the list of matching files.</returns>
    public Task<object> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var folder = arguments.TryGetProperty("folder", out var folderProp)
            ? folderProp.GetString()
            : null;

        var extension = arguments.TryGetProperty("extension", out var extProp)
            ? extProp.GetString()
            : null;

        var limit = arguments.TryGetProperty("limit", out var limitProp)
            ? limitProp.GetInt32()
            : 20;

        Debug.WriteLine($"[ListIndexedFilesTool] Listing files: folder='{folder}', extension='{extension}', limit={limit}");

        IEnumerable<string> paths;
        if (!string.IsNullOrWhiteSpace(folder))
        {
            paths = _fileRepository.ListPathsUnderFolder(folder);
        }
        else
        {
            paths = _fileRepository.ListPathsUnderFolder(string.Empty);
        }

        var files = new List<object>();
        var count = 0;

        foreach (var path in paths)
        {
            if (count >= limit)
                break;

            var file = _fileRepository.GetByPath(path);
            if (file is null)
                continue;

            if (file.IsDirectory)
                continue;

            if (!string.IsNullOrWhiteSpace(extension) &&
                !file.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            files.Add(new
            {
                id = file.Id,
                filename = file.Filename,
                path = file.Path,
                extension = file.Extension,
                sizeBytes = file.SizeBytes,
                modifiedAt = file.ModifiedAt,
                extractStatus = file.ExtractStatus,
                chunkCount = file.ChunkCount
            });

            count++;
        }

        Debug.WriteLine($"[ListIndexedFilesTool] Returning {files.Count} files.");

        object result = new
        {
            count = files.Count,
            files
        };

        return Task.FromResult(result);
    }
}
