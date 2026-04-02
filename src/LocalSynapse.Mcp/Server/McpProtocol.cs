using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalSynapse.Mcp.Server;

/// <summary>
/// Represents a JSON-RPC 2.0 request message.
/// </summary>
public sealed class JsonRpcRequest
{
    /// <summary>
    /// JSON-RPC protocol version. Always "2.0".
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    /// <summary>
    /// Request identifier. Null for notifications.
    /// </summary>
    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    /// <summary>
    /// Method name to invoke.
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Optional parameters for the method.
    /// </summary>
    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }

    /// <summary>
    /// Returns true if this message is a notification (no id).
    /// </summary>
    [JsonIgnore]
    public bool IsNotification => Id is null || Id.Value.ValueKind == JsonValueKind.Undefined;
}

/// <summary>
/// Represents a JSON-RPC 2.0 success response message.
/// </summary>
public sealed class JsonRpcResponse
{
    /// <summary>
    /// JSON-RPC protocol version. Always "2.0".
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    /// <summary>
    /// Request identifier echoed from the request.
    /// </summary>
    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    /// <summary>
    /// The result of the method invocation.
    /// </summary>
    [JsonPropertyName("result")]
    public object? Result { get; set; }
}

/// <summary>
/// Represents a JSON-RPC 2.0 error response message.
/// </summary>
public sealed class JsonRpcErrorResponse
{
    /// <summary>
    /// JSON-RPC protocol version. Always "2.0".
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    /// <summary>
    /// Request identifier echoed from the request.
    /// </summary>
    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    /// <summary>
    /// The error object describing the failure.
    /// </summary>
    [JsonPropertyName("error")]
    public JsonRpcError Error { get; set; } = new();
}

/// <summary>
/// Represents a JSON-RPC 2.0 error object.
/// </summary>
public sealed class JsonRpcError
{
    /// <summary>
    /// Error code as defined by JSON-RPC 2.0.
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Optional additional data about the error.
    /// </summary>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; set; }

    /// <summary>Standard error code: method not found.</summary>
    public const int MethodNotFound = -32601;

    /// <summary>Standard error code: invalid params.</summary>
    public const int InvalidParams = -32602;

    /// <summary>Standard error code: internal error.</summary>
    public const int InternalError = -32603;

    /// <summary>Standard error code: parse error.</summary>
    public const int ParseError = -32700;
}
