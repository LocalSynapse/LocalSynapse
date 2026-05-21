using System.Text.Json;

namespace LocalSynapse.Search.Tests;

/// <summary>
/// Cascade mode-diff regression. Fast TopK must be a verbatim match against
/// the golden; Smart TopK must diverge from Fast by at least the recorded
/// Jaccard threshold (initial baseline at fixture promotion; tightened when
/// cascade rerank is reshaped to expose smart-mode behavior).
///
/// The fixture starts absent — these facts then return as no-op pass so
/// the suite can land before the staging run. Once a baseline is measured
/// and the golden JSON is promoted, the facts begin asserting.
///
/// ACTIVATION CHECKLIST (when promoting the fixture for the first time):
///   1. Implement RunFast and RunSmart bodies (both currently throw
///      NotImplementedException). They must wire FakeBm25Search,
///      FakeEmbeddingBridge, FakeEmbeddingRepository, and
///      FakePipelineStampRepository (configured so cascade IsAvailable
///      passes the 80% embedding-coverage threshold) into the cascade
///      strategy and project results to Top-K paths.
///   2. Run GenerateCascadeGolden_Staging to measure the baseline
///      jaccard_threshold and write cascade-mode-diff-golden.staging.json.
///   3. Inspect tau in the staging file — promote only if tau > 0.
///      (A zero threshold would let the Smart verifier pass for any
///      result set including empty arrays.)
///   4. Rename staging file to cascade-mode-diff-golden.json.
/// Forgetting step 1 while completing steps 2–4 makes the verifier facts
/// throw NotImplementedException on the next Gate 4 run.
/// </summary>
public class CascadeModeDiffTest
{
    private const string FixturePath = "TestData/cascade-mode-diff-golden.json";

    [Fact]
    [Trait("Category", "GoldenMaster")]
    public void FastMode_ExactMatchAgainstGolden()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, FixturePath);
        if (!File.Exists(fixturePath)) return;  // baseline not yet promoted

        var fixture = LoadFixture(fixturePath);
        foreach (var scenario in fixture.Scenarios)
        {
            var actual = RunFast(scenario.Query);
            Assert.Equal(scenario.FastTopkPaths, actual);
        }
    }

    [Fact]
    [Trait("Category", "GoldenMaster")]
    public void SmartMode_JaccardDistanceMeetsThreshold()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, FixturePath);
        if (!File.Exists(fixturePath)) return;  // baseline not yet promoted

        var fixture = LoadFixture(fixturePath);
        var tau = fixture.Header.JaccardThreshold;
        foreach (var scenario in fixture.Scenarios)
        {
            var actualSmart = RunSmart(scenario.Query);
            var distance = JaccardDistance(scenario.FastTopkPaths, actualSmart);
            Assert.True(distance >= tau,
                $"Query '{scenario.Query}' Jaccard distance {distance:F4} below threshold {tau:F4}");
        }
    }

    [Fact(Skip = "Manual — measure current cascade behavior to seed the baseline, then promote .staging.json")]
    [Trait("Category", "GoldenMaster")]
    public void GenerateCascadeGolden_Staging()
    {
        // Activation contract: wire FakeBm25Search + FakeEmbeddingBridge +
        // FakeEmbeddingRepository + FakePipelineStampRepository (≥80% embedded)
        // into CascadeSearchStrategy, run each query in both Fast and Smart
        // modes, record Top-10 paths for each, compute baseline Jaccard
        // distance, and write the result to TestData/cascade-mode-diff-golden.staging.json.
        // Maintainer then promotes by renaming to cascade-mode-diff-golden.json.
    }

    private static string[] RunFast(string query)
    {
        // Activation contract: drive FakeBm25Search.Search(query, options) and
        // project the resulting Bm25Hit list to its Path[] (TopK = 10).
        throw new NotImplementedException("Implement when activating CascadeModeDiffTest.");
    }

    private static string[] RunSmart(string query)
    {
        // Activation contract: drive CascadeSearchStrategy.SearchAsync(query,
        // options, ct) with the fake quadruple, project HybridHit list to
        // its Path[] (TopK = 10).
        throw new NotImplementedException("Implement when activating CascadeModeDiffTest.");
    }

    private static double JaccardDistance(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var setA = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
        var setB = new HashSet<string>(b, StringComparer.OrdinalIgnoreCase);
        var intersection = new HashSet<string>(setA);
        intersection.IntersectWith(setB);
        var union = new HashSet<string>(setA);
        union.UnionWith(setB);
        return union.Count == 0 ? 0 : 1.0 - (double)intersection.Count / union.Count;
    }

    // ── Fixture model ──

    private sealed record FixtureHeader(int TopK, double JaccardThreshold);
    private sealed record Scenario(string Query, string[] FastTopkPaths, string[] SmartTopkPaths);
    private sealed record Fixture(FixtureHeader Header, IReadOnlyList<Scenario> Scenarios);

    private static Fixture LoadFixture(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var headerJson = doc.RootElement.GetProperty("header");
        var header = new FixtureHeader(
            headerJson.GetProperty("top_k").GetInt32(),
            headerJson.GetProperty("jaccard_threshold").GetDouble());
        var scenarios = new List<Scenario>();
        foreach (var s in doc.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            scenarios.Add(new Scenario(
                s.GetProperty("query").GetString()!,
                s.GetProperty("fast_topk_paths").EnumerateArray().Select(e => e.GetString()!).ToArray(),
                s.GetProperty("smart_topk_paths").EnumerateArray().Select(e => e.GetString()!).ToArray()));
        }
        return new Fixture(header, scenarios);
    }
}
