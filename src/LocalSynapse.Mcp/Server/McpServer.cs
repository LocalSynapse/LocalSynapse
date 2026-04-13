using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalSynapse.Mcp.Interfaces;

namespace LocalSynapse.Mcp.Server;

/// <summary>
/// MCP stdio server implementation. Reads JSON-RPC 2.0 messages from stdin
/// and writes responses to stdout.
/// </summary>
public sealed class McpServer : IMcpServer
{
    private readonly McpToolRouter _router;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly List<ToolDefinition> _toolDefinitions;

    // ReadLineWithLimitAsync state — single reader loop assumed (no concurrent calls)
    private const int MaxLineLength = 16 * 1024 * 1024; // 16MB
    private readonly char[] _readBuffer = new char[4096];
    private string _leftover = "";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes the MCP server with stdin/stdout.
    /// </summary>
    /// <param name="router">The tool router containing registered tool handlers.</param>
    public McpServer(McpToolRouter router)
        : this(router, Console.In, Console.Out)
    {
    }

    /// <summary>
    /// Initializes the MCP server with custom input/output streams for testability.
    /// </summary>
    /// <param name="router">The tool router containing registered tool handlers.</param>
    /// <param name="input">The text reader to read JSON-RPC messages from.</param>
    /// <param name="output">The text writer to write JSON-RPC responses to.</param>
    public McpServer(McpToolRouter router, TextReader input, TextWriter output)
    {
        _router = router;
        _input = input;
        _output = output;
        _toolDefinitions = BuildToolDefinitions();
    }

    /// <summary>
    /// Runs the MCP server loop, reading and processing JSON-RPC messages until
    /// the input stream is closed or cancellation is requested.
    /// </summary>
    /// <param name="ct">Cancellation token to stop the server.</param>
    public async Task RunAsync(CancellationToken ct = default)
    {
        Debug.WriteLine("[McpServer] Starting MCP stdio server...");

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await ReadLineWithLimitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
            {
                Debug.WriteLine("[McpServer] Input stream closed.");
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            await ProcessMessageAsync(line, ct).ConfigureAwait(false);
        }

        Debug.WriteLine("[McpServer] MCP server stopped.");
    }

    private async Task ProcessMessageAsync(string line, CancellationToken ct)
    {
        JsonRpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<JsonRpcRequest>(line, s_jsonOptions);
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[McpServer] Failed to parse JSON-RPC message: {ex.Message}");
            var errorResponse = new JsonRpcErrorResponse
            {
                Id = null,
                Error = new JsonRpcError
                {
                    Code = JsonRpcError.ParseError,
                    Message = "Parse error: invalid JSON"
                }
            };
            await WriteResponseAsync(errorResponse, ct).ConfigureAwait(false);
            return;
        }

        if (request is null)
        {
            Debug.WriteLine("[McpServer] Deserialized request is null.");
            return;
        }

        Debug.WriteLine($"[McpServer] Received method: {request.Method}");

        switch (request.Method)
        {
            case "initialize":
                await HandleInitializeAsync(request, ct).ConfigureAwait(false);
                break;

            case "notifications/initialized":
                Debug.WriteLine("[McpServer] Client initialized notification received.");
                break;

            case "tools/list":
                await HandleToolsListAsync(request, ct).ConfigureAwait(false);
                break;

            case "tools/call":
                await HandleToolsCallAsync(request, ct).ConfigureAwait(false);
                break;

            default:
                if (!request.IsNotification)
                {
                    Debug.WriteLine($"[McpServer] Unknown method: {request.Method}");
                    var errorResponse = new JsonRpcErrorResponse
                    {
                        Id = request.Id,
                        Error = new JsonRpcError
                        {
                            Code = JsonRpcError.MethodNotFound,
                            Message = $"Method not found: {request.Method}"
                        }
                    };
                    await WriteResponseAsync(errorResponse, ct).ConfigureAwait(false);
                }
                break;
        }
    }

    private async Task HandleInitializeAsync(JsonRpcRequest request, CancellationToken ct)
    {
        var result = new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { listChanged = false }
            },
            serverInfo = new
            {
                name = "localsynapse",
                version = "2.0.0"
            }
        };

        var response = new JsonRpcResponse
        {
            Id = request.Id,
            Result = result
        };

        await WriteResponseAsync(response, ct).ConfigureAwait(false);
    }

    private async Task HandleToolsListAsync(JsonRpcRequest request, CancellationToken ct)
    {
        var result = new { tools = _toolDefinitions };

        var response = new JsonRpcResponse
        {
            Id = request.Id,
            Result = result
        };

        await WriteResponseAsync(response, ct).ConfigureAwait(false);
    }

    private async Task HandleToolsCallAsync(JsonRpcRequest request, CancellationToken ct)
    {
        string toolName = string.Empty;
        JsonElement arguments = default;

        try
        {
            if (request.Params.HasValue)
            {
                var p = request.Params.Value;
                if (p.TryGetProperty("name", out var nameProp))
                    toolName = nameProp.GetString() ?? string.Empty;
                if (p.TryGetProperty("arguments", out var argsProp))
                    arguments = argsProp;
            }

            if (string.IsNullOrEmpty(toolName))
            {
                var errorResponse = new JsonRpcErrorResponse
                {
                    Id = request.Id,
                    Error = new JsonRpcError
                    {
                        Code = JsonRpcError.InvalidParams,
                        Message = "Missing 'name' in tools/call params"
                    }
                };
                await WriteResponseAsync(errorResponse, ct).ConfigureAwait(false);
                return;
            }

            var result = await _router.InvokeAsync(toolName, arguments, ct).ConfigureAwait(false);
            var contentText = JsonSerializer.Serialize(result, s_jsonOptions);

            var response = new JsonRpcResponse
            {
                Id = request.Id,
                Result = new
                {
                    content = new[]
                    {
                        new { type = "text", text = contentText }
                    }
                }
            };

            await WriteResponseAsync(response, ct).ConfigureAwait(false);
        }
        catch (KeyNotFoundException ex)
        {
            Debug.WriteLine($"[McpServer] Tool not found: {toolName} - {ex.Message}");
            var errorResponse = new JsonRpcErrorResponse
            {
                Id = request.Id,
                Error = new JsonRpcError
                {
                    Code = JsonRpcError.MethodNotFound,
                    Message = $"Tool not found: {toolName}"
                }
            };
            await WriteResponseAsync(errorResponse, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var correlationId = Guid.NewGuid().ToString("N")[..12];
            Debug.WriteLine($"[McpServer] Tool error [{correlationId}] '{toolName}': {ex.Message}");
            var response = new JsonRpcResponse
            {
                Id = request.Id,
                Result = new
                {
                    content = new[]
                    {
                        new { type = "text", text = $"Internal error (ref: {correlationId})" }
                    },
                    isError = true
                }
            };
            await WriteResponseAsync(response, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Read a line with length limit. Single reader loop assumed (no concurrent calls).</summary>
    private async Task<string?> ReadLineWithLimitAsync(CancellationToken ct)
    {
        var sb = new StringBuilder(_leftover);
        _leftover = "";

        // Check if leftover already contains a newline
        // _leftover length bounded by _readBuffer.Length (4KB), so no length check needed here.
        var current = sb.ToString();
        var nlIdx = current.IndexOf('\n');
        if (nlIdx >= 0)
        {
            _leftover = current[(nlIdx + 1)..];
            return current[..nlIdx].TrimEnd('\r');
        }

        while (true)
        {
            var read = await _input.ReadAsync(_readBuffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0)
                return sb.Length > 0 ? sb.ToString() : null;

            var newlineIdx = Array.IndexOf(_readBuffer, '\n', 0, read);

            if (newlineIdx >= 0)
            {
                sb.Append(_readBuffer, 0, newlineIdx);
                _leftover = new string(_readBuffer, newlineIdx + 1, read - newlineIdx - 1);
                return sb.ToString().TrimEnd('\r');
            }

            sb.Append(_readBuffer, 0, read);
            if (sb.Length > MaxLineLength)
                throw new InvalidOperationException($"MCP message exceeds maximum line length ({MaxLineLength} bytes)");
        }
    }

    private async Task WriteResponseAsync(object response, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(response, s_jsonOptions);
        await _output.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
        await _output.FlushAsync(ct).ConfigureAwait(false);
    }

    private static List<ToolDefinition> BuildToolDefinitions()
    {
        return
        [
            new ToolDefinition
            {
                Name = "search_files",
                Description = "Search indexed files using hybrid (BM25 + dense vector) search.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["query"] = new { type = "string", description = "The search query text." },
                        ["topK"] = new { type = "integer", description = "Maximum number of results to return. Default is 20." },
                        ["extensions"] = new { type = "array", items = new { type = "string" }, description = "Filter results by file extensions (e.g., [\".pdf\", \".docx\"])." }
                    },
                    required = new[] { "query" }
                }
            },
            new ToolDefinition
            {
                Name = "get_file_content",
                Description = "Returns extracted text content from an indexed file. Content is parsed from the original document format (not raw file bytes). Returns empty if the file has not been processed yet.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["fileId"] = new { type = "string", description = "The file ID to retrieve content for." }
                    },
                    required = new[] { "fileId" }
                }
            },
            new ToolDefinition
            {
                Name = "get_pipeline_status",
                Description = "Get the current pipeline processing status including scan, indexing, and embedding progress.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>()
                }
            },
            new ToolDefinition
            {
                Name = "list_indexed_files",
                Description = "List indexed files, optionally filtered by folder path or file extension.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["folder"] = new { type = "string", description = "Filter by folder path prefix." },
                        ["extension"] = new { type = "string", description = "Filter by file extension (e.g., \".pdf\")." },
                        ["limit"] = new { type = "integer", description = "Maximum number of files to return. Default is 20." }
                    }
                }
            }
        ];
    }
}

/// <summary>
/// Defines an MCP tool's name, description, and input schema for tool listing.
/// </summary>
internal sealed class ToolDefinition
{
    /// <summary>
    /// The tool name used in tools/call requests.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of what the tool does.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// JSON Schema describing the tool's input parameters.
    /// </summary>
    [JsonPropertyName("inputSchema")]
    public object InputSchema { get; set; } = new();
}
