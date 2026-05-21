using System.Text.Json;

namespace LocalSynapse.Mcp.Tests.Contract;

internal static class SchemaSnapshot
{
    public static void AssertContainsAllKeys(
        string jsonOutput,
        IReadOnlyDictionary<string, JsonValueKind> requiredKeys)
    {
        using var doc = JsonDocument.Parse(jsonOutput);
        var root = doc.RootElement;
        foreach (var (key, expectedKind) in requiredKeys)
        {
            Assert.True(root.TryGetProperty(key, out var prop),
                $"Required key missing: '{key}' in output: {jsonOutput[..Math.Min(200, jsonOutput.Length)]}");
            Assert.Equal(expectedKind, prop.ValueKind);
        }
    }
}
