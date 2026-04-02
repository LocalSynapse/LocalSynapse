using System.Diagnostics;
using System.Text.Json;
using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.Mcp.Tools;

/// <summary>
/// MCP tool handler for retrieving the full text content of an indexed file.
/// </summary>
public sealed class GetFileContentTool
{
    private readonly IFileRepository _fileRepository;

    /// <summary>
    /// Initializes a new instance of <see cref="GetFileContentTool"/>.
    /// </summary>
    /// <param name="fileRepository">The file repository for looking up file metadata.</param>
    public GetFileContentTool(IFileRepository fileRepository)
    {
        _fileRepository = fileRepository;
    }

    /// <summary>
    /// Executes the get_file_content tool with the given arguments.
    /// </summary>
    /// <param name="arguments">JSON element containing the fileId parameter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An object containing the file content and path.</returns>
    public async Task<object> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var fileId = arguments.TryGetProperty("fileId", out var idProp)
            ? idProp.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(fileId))
        {
            Debug.WriteLine("[GetFileContentTool] Empty fileId received.");
            return new { error = "fileId parameter is required." };
        }

        Debug.WriteLine($"[GetFileContentTool] Getting content for fileId: {fileId}");

        var file = _fileRepository.GetById(fileId);
        if (file is null)
        {
            Debug.WriteLine($"[GetFileContentTool] File not found: {fileId}");
            return new { error = $"File not found with id: {fileId}" };
        }

        if (!File.Exists(file.Path))
        {
            Debug.WriteLine($"[GetFileContentTool] File not found on disk: {file.Path}");
            return new { error = $"File not found on disk: {file.Path}", path = file.Path };
        }

        try
        {
            var content = await File.ReadAllTextAsync(file.Path, ct).ConfigureAwait(false);

            Debug.WriteLine($"[GetFileContentTool] Read {content.Length} chars from: {file.Path}");

            return new
            {
                content,
                path = file.Path,
                filename = file.Filename,
                extension = file.Extension,
                sizeBytes = file.SizeBytes,
                modifiedAt = file.ModifiedAt
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GetFileContentTool] Error reading file '{file.Path}': {ex.Message}");
            return new { error = $"Failed to read file: {ex.Message}", path = file.Path };
        }
    }
}
