using System.Text.Json;
using LocalSynapse.Core.Database;
using LocalSynapse.Core.Models;
using LocalSynapse.Core.Repositories;
using LocalSynapse.Search;           // SearchOptions
using LocalSynapse.Search.Services;  // SearchClickService, Bm25SearchService

namespace LocalSynapse.Core.Tests;

public class Bm25SearchServiceTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  Test seed
    // ═══════════════════════════════════════════════════════════════════

    private static void SeedSearchCorpus(TempDbFixture temp)
    {
        var fileRepo = new FileRepository(temp.Factory);
        var chunkRepo = new ChunkRepository(temp.Factory);

        var files = new[]
        {
            ("/corpus/budget-2024-report.docx",    "budget report for fiscal year 2024 quarterly analysis"),
            ("/corpus/project-plan-q1.docx",       "project plan document q1 milestones deliverables"),
            ("/corpus/meeting-notes-jan.txt",      "meeting notes january team sync budget discussion"),
            ("/corpus/annual-report-2023.pdf",     "annual report 2023 revenue growth key achievements"),
            ("/corpus/roadmap-vision.md",          "product roadmap vision long-term strategic goals"),
            ("/corpus/budget-proposal.xlsx",       "budget proposal draft spending estimates proposal"),
            ("/corpus/readme.md",                  "readme overview installation quickstart guide"),
            ("/corpus/explanation.txt",            "explanation details context reasoning background"),
            ("/corpus/finance-summary.docx",       "finance summary expenses revenue profit margin"),
            ("/corpus/plan-template.docx",         "plan template sections checklist empty fields"),
        };

        var metadataList = files.Select((t, i) => new FileMetadata
        {
            Id = "",  // UpsertFiles overwrites via GenerateFileId(path)
            Path = t.Item1,
            Filename = System.IO.Path.GetFileName(t.Item1),
            Extension = System.IO.Path.GetExtension(t.Item1),
            SizeBytes = 1000,
            ModifiedAt = DateTime.UtcNow.AddDays(-i).ToString("o"),
            IndexedAt = DateTime.UtcNow.ToString("o"),  // UpsertFiles overwrites with batch value
            FolderPath = "/corpus",
            MtimeMs = DateTimeOffset.UtcNow.AddDays(-i).ToUnixTimeMilliseconds(),
            IsDirectory = false,
            ExtractStatus = ExtractStatuses.Success,
        }).ToList();
        fileRepo.UpsertFiles(metadataList);

        var chunks = files.Select((t, i) => new FileChunk
        {
            Id = $"chunk-{i}",
            FileId = FileRepository.GenerateFileId(t.Item1),
            ChunkIndex = 0,
            Text = t.Item2,
            SourceType = ChunkSourceTypes.Text,
            ContentHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(t.Item2))),
            CreatedAt = DateTime.UtcNow.ToString("o"),
        }).ToList();
        chunkRepo.UpsertChunks(chunks);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  T4. GetBoostBatch 기본 동작
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetBoostBatch_ReturnsBoostForAllRecordedPaths()
    {
        using var temp = new TempDbFixture();
        var svc = new SearchClickService(temp.Factory);

        svc.RecordClick("test", "/corpus/budget-2024-report.docx", 0);
        svc.RecordClick("test", "/corpus/plan-template.docx", 3);
        svc.RecordClick("test", "/corpus/budget-2024-report.docx", 0); // 2nd click, count=2

        var boosts = svc.GetBoostBatch("test",
            new[]
            {
                "/corpus/budget-2024-report.docx",
                "/corpus/plan-template.docx",
                "/corpus/unseen.txt",
            });

        Assert.True(boosts.ContainsKey("/corpus/budget-2024-report.docx"));
        Assert.True(boosts.ContainsKey("/corpus/plan-template.docx"));
        Assert.False(boosts.ContainsKey("/corpus/unseen.txt"));

        Assert.True(boosts["/corpus/budget-2024-report.docx"] > 0.0);
        // position 3 > position 0 → 더 강한 부스트
        Assert.True(boosts["/corpus/plan-template.docx"]
                  > boosts["/corpus/budget-2024-report.docx"]);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  T5. N+1 회귀 방지
    // ═══════════════════════════════════════════════════════════════════

    private sealed class CountingSearchClickService : SearchClickService
    {
        public int BatchCallCount { get; private set; }
        public CountingSearchClickService(SqliteConnectionFactory f) : base(f) { }

        public override Dictionary<string, double> GetBoostBatch(
            string query, IReadOnlyList<string> paths)
        {
            BatchCallCount++;
            return base.GetBoostBatch(query, paths);
        }
    }

    [Fact]
    public void ExecuteSearch_CallsGetBoostBatchOnce_NotPerResult()
    {
        using var temp = new TempDbFixture();
        SeedSearchCorpus(temp);

        var counter = new CountingSearchClickService(temp.Factory);
        var svc = new Bm25SearchService(temp.Factory, counter);

        svc.ClearCache();
        var results = svc.Search("report", new SearchOptions { TopK = 10, ChunksPerFile = 4 });

        Assert.True(results.Count > 0);
        Assert.Equal(1, counter.BatchCallCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  T6. Golden master ranking (회귀 방지)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ExecuteSearch_ProducesSameRanking_AsGoldenMaster()
    {
        using var temp = new TempDbFixture();
        SeedSearchCorpus(temp);

        var clickSvc = new SearchClickService(temp.Factory);
        var bm25 = new Bm25SearchService(temp.Factory, clickSvc);

        var goldenPath = Path.Combine(
            AppContext.BaseDirectory, "TestData", "search-golden-master.json");
        Assert.True(File.Exists(goldenPath),
            "Golden master missing. Run GenerateGoldenMaster_Staging first and promote.");

        var golden = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            File.ReadAllText(goldenPath))!;

        foreach (var q in new[] { "report", "budget 2024", "plan" })
        {
            bm25.ClearCache();
            var hits = bm25.Search(q, new SearchOptions { TopK = 10, ChunksPerFile = 4 });
            var ranking = hits.Select(h => h.Path).ToArray();
            Assert.Equal(golden[q], ranking);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Golden master generator (manual only, .staging pattern)
    //  To run: temporarily remove [Fact(Skip=...)] attribute, rebuild,
    //    dotnet test --filter "FullyQualifiedName~GenerateGoldenMaster_Staging"
    //  Then copy TestData/search-golden-master.staging.json from bin/
    //  to the repo TestData/ directory and rename to search-golden-master.json.
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Manual-only — remove '.staging' suffix to promote to real golden master")]
    public void GenerateGoldenMaster_Staging()
    {
        using var temp = new TempDbFixture();
        SeedSearchCorpus(temp);
        var clickSvc = new SearchClickService(temp.Factory);
        var bm25 = new Bm25SearchService(temp.Factory, clickSvc);

        var result = new Dictionary<string, string[]>();
        foreach (var q in new[] { "report", "budget 2024", "plan" })
        {
            bm25.ClearCache();
            var hits = bm25.Search(q, new SearchOptions { TopK = 10, ChunksPerFile = 4 });
            result[q] = hits.Select(h => h.Path).ToArray();
        }

        var stagingPath = Path.Combine(
            AppContext.BaseDirectory, "TestData", "search-golden-master.staging.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
        File.WriteAllText(
            stagingPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Benchmark (manual only, connection-count based)
    //  Note: 10-file corpus is too small for meaningful latency measurement.
    //  The N+1 elimination is proven theoretically via T5 (BatchCallCount == 1)
    //  and architecturally via GetBoostBatch replacing per-row GetBoost calls.
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Manual benchmark — run explicitly to measure latency")]
    public void MeasureExecuteSearchLatency()
    {
        using var temp = new TempDbFixture();
        SeedSearchCorpus(temp);
        var clickSvc = new SearchClickService(temp.Factory);
        var bm25 = new Bm25SearchService(temp.Factory, clickSvc);

        var sw = new System.Diagnostics.Stopwatch();
        var durations = new List<long>();

        // Warmup
        bm25.ClearCache();
        bm25.Search("report", new SearchOptions { TopK = 10, ChunksPerFile = 4 });

        for (int i = 0; i < 10; i++)
        {
            bm25.ClearCache();
            sw.Restart();
            bm25.Search("report", new SearchOptions { TopK = 10, ChunksPerFile = 4 });
            sw.Stop();
            durations.Add(sw.ElapsedMilliseconds);
        }

        var avg = durations.Average();
        Console.WriteLine($"[Benchmark] Bm25SearchService.Search('report') avg: {avg:F2}ms");
        Console.WriteLine($"[Benchmark] Individual runs: [{string.Join(", ", durations)}]");
    }
}
