using System.Text.Json;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Tests.Fakes;

/// <summary>
/// Deterministic IEmbeddingBridge fake that serves vectors out of a JSON
/// fixture (base64-encoded float arrays under "vectors"). Used by cascade
/// dense-path tests so that the inner embedding call has a fixed answer.
/// </summary>
internal sealed class FakeEmbeddingBridge : IEmbeddingBridge
{
    private readonly Dictionary<string, float[]> _vectors;

    public bool IsReady => true;
    public string? ActiveModelId => "bge-m3-fixture";

    public FakeEmbeddingBridge(string fixturePath)
    {
        if (!File.Exists(fixturePath))
        {
            throw new FileNotFoundException(
                $"Dense fixture missing at '{fixturePath}'. " +
                "Run DenseGoldenVectorTest.GenerateDenseGolden_Staging first and promote " +
                "TestData/dense-golden-vectors.staging.json to dense-golden-vectors.json.",
                fixturePath);
        }
        var json = File.ReadAllText(fixturePath);
        using var doc = JsonDocument.Parse(json);
        _vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var entry in doc.RootElement.GetProperty("vectors").EnumerateObject())
        {
            var b64 = entry.Value.GetProperty("vector").GetString()!;
            var bytes = Convert.FromBase64String(b64);
            var vec = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, vec, 0, bytes.Length);
            _vectors[entry.Name] = vec;
        }
    }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
        => Task.FromResult(_vectors.TryGetValue(text, out var v)
            ? v
            : throw new InvalidOperationException($"Text not in fixture: {text}"));
}
