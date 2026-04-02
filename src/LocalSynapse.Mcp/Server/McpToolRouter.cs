using System.Diagnostics;
using System.Text.Json;

namespace LocalSynapse.Mcp.Server;

/// <summary>
/// Routes MCP tool calls to their registered handler functions.
/// </summary>
public sealed class McpToolRouter
{
    private readonly Dictionary<string, Func<JsonElement, CancellationToken, Task<object>>> _handlers = new();

    /// <summary>
    /// Registers a tool handler with the given name.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <param name="handler">The async handler function that processes the tool call.</param>
    public void Register(string toolName, Func<JsonElement, CancellationToken, Task<object>> handler)
    {
        _handlers[toolName] = handler;
    }

    /// <summary>
    /// Invokes the handler registered for the specified tool name.
    /// </summary>
    /// <param name="toolName">The tool name to route to.</param>
    /// <param name="arguments">The JSON arguments passed to the tool.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result object from the handler.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no handler is registered for the tool name.</exception>
    public async Task<object> InvokeAsync(string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        if (!_handlers.TryGetValue(toolName, out var handler))
        {
            Debug.WriteLine($"[McpToolRouter] Unknown tool: {toolName}");
            throw new KeyNotFoundException($"Tool '{toolName}' is not registered.");
        }

        Debug.WriteLine($"[McpToolRouter] Invoking tool: {toolName}");
        return await handler(arguments, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the names of all registered tools.
    /// </summary>
    /// <returns>An enumerable of registered tool names.</returns>
    public IEnumerable<string> GetRegisteredToolNames() => _handlers.Keys;

    /// <summary>
    /// Checks if a tool with the specified name is registered.
    /// </summary>
    /// <param name="toolName">The tool name to check.</param>
    /// <returns>True if the tool is registered; otherwise false.</returns>
    public bool HasTool(string toolName) => _handlers.ContainsKey(toolName);
}
