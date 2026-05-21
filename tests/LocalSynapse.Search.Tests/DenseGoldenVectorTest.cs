using System.Text.Json;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Tests;

/// <summary>
/// Manual-only dense embedding regression tests. The two facts are marked
/// Skip because they require a real bge-m3 ONNX model on disk that is not
/// shipped with the repository. To activate locally:
///   1. Place model.onnx and tokenizer.json under a directory of your choice.
///   2. Set LOCAL_SYNAPSE_BGE_M3_PATH to that directory.
///   3. Remove the Skip parameter and run.
/// CI does not exercise these — automated dense regression coverage is
/// scheduled with the next search-quality release.
/// </summary>
public class DenseGoldenVectorTest
{
    private const string FixturePath = "TestData/dense-golden-vectors.json";
    private const double Tolerance = 0.999;

    [Fact(Skip = "Manual — set LOCAL_SYNAPSE_BGE_M3_PATH to a directory containing model.onnx + tokenizer.json, then remove Skip to run")]
    [Trait("Category", "GoldenMaster")]
    public async Task EmbeddedQuery_StaysWithinTolerance_AgainstGolden()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, FixturePath);
        Assert.True(File.Exists(fixturePath),
            "Dense golden missing. Run GenerateDenseGolden_Staging first and promote.");

        var fixture = LoadFixture(fixturePath);
        var embedder = await BuildRealEmbedderCpuAsync();
        foreach (var (query, expectedVec) in fixture)
        {
            var actualVec = await embedder.GenerateEmbeddingAsync(query);
            var sim = CosineSimilarity(expectedVec, actualVec);
            Assert.True(sim >= Tolerance,
                $"Query '{query}' cosine {sim:F6} below threshold {Tolerance}");
        }
    }

    [Fact(Skip = "Manual — see EmbeddedQuery_StaysWithinTolerance_AgainstGolden for activation")]
    [Trait("Category", "GoldenMaster")]
    public async Task EmbeddedQuery_IsDeterministic_OnRepeatedCalls()
    {
        var embedder = await BuildRealEmbedderCpuAsync();
        const string q = "deterministic check query";
        var v1 = await embedder.GenerateEmbeddingAsync(q);
        var v2 = await embedder.GenerateEmbeddingAsync(q);
        Assert.Equal(v1.Length, v2.Length);
        var sim = CosineSimilarity(v1, v2);
        Assert.True(sim >= 0.99999, $"Repeated embedding diverged: cosine={sim:F8}");
    }

    [Fact(Skip = "Manual — extract fresh vectors with current model on CPU EP, then promote .staging.json")]
    [Trait("Category", "GoldenMaster")]
    public Task GenerateDenseGolden_Staging()
    {
        // Activation contract: when the skip is removed, this fact should
        // resolve BuildRealEmbedderCpuAsync, embed each query in
        // Bm25SearchServiceTests-equivalent set (or any agreed list), and
        // write TestData/dense-golden-vectors.staging.json with header
        // { model_id, generated_at, ep, dimension, tolerance } + vectors
        // map of { query: { vector: base64-encoded-floats } }.
        // The maintainer then renames .staging.json to dense-golden-vectors.json.
        return Task.CompletedTask;
    }

    private static Dictionary<string, float[]> LoadFixture(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var entry in doc.RootElement.GetProperty("vectors").EnumerateObject())
        {
            var b64 = entry.Value.GetProperty("vector").GetString()!;
            var bytes = Convert.FromBase64String(b64);
            var vec = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, vec, 0, bytes.Length);
            result[entry.Name] = vec;
        }
        return result;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        var denom = Math.Sqrt(na) * Math.Sqrt(nb);
        return denom <= 0 ? 0 : dot / denom;
    }

    private static Task<IEmbeddingBridge> BuildRealEmbedderCpuAsync()
    {
        // Placeholder until the activation contract is fulfilled. The real
        // implementation should construct an EmbeddingService against a
        // local model directory (e.g. read from LOCAL_SYNAPSE_BGE_M3_PATH),
        // force CPU EP, and wrap it with TestEmbeddingBridgeAdapter.
        throw new NotImplementedException(
            "Set LOCAL_SYNAPSE_BGE_M3_PATH and wire EmbeddingService here when activating these facts.");
    }
}
