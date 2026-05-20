using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Search.Interfaces;

namespace LocalSynapse.Search.Services;

/// <summary>
/// Smart strategy — BM25 retrieves its top-K candidates, then a single query
/// embedding is computed once and each candidate's stored embedding is cosine-
/// scored against it. Reciprocal Rank Fusion combines the BM25 order with the
/// dense rerank. No full-corpus embedding enumeration; the dense pass is bounded
/// by the BM25 candidate count (typically ≤ TopK).
/// </summary>
/// <remarks>
/// Cancellation: the CT is propagated to the embedding fetch SQL and is checked
/// before the cosine accumulation loop. Microsoft.Data.Sqlite honors CT between
/// commands but not within a single ExecuteReader, so mid-query cancellation of
/// the fetch is best-effort. At ~200 candidates the fetch is short enough that
/// this is not a practical concern.
///
/// Cache: results are cached for 30 seconds keyed on (query, TopK, ChunksPerFile).
/// The dense rerank is deterministic given identical BM25 candidates and an
/// identical query embedding, so a hit on the same key is safe.
/// </remarks>
public sealed class CascadeSearchStrategy : ISearchStrategy
{
    private readonly IBm25Search _bm25;
    private readonly IEmbeddingBridge _embeddingBridge;
    private readonly IEmbeddingRepository _embeddingRepo;
    private readonly IPipelineStampRepository _stampRepo;

    private const string DefaultModelId = "bge-m3";
    private const double SmartModeMinimumPercentage = 0.80;

    private readonly ConcurrentDictionary<string, (DateTime ts, SearchResponse response)> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public SearchMode Mode => SearchMode.Smart;

    /// <summary>CascadeSearchStrategy 생성자.</summary>
    public CascadeSearchStrategy(
        IBm25Search bm25,
        IEmbeddingBridge embeddingBridge,
        IEmbeddingRepository embeddingRepo,
        IPipelineStampRepository stampRepo)
    {
        _bm25 = bm25;
        _embeddingBridge = embeddingBridge;
        _embeddingRepo = embeddingRepo;
        _stampRepo = stampRepo;
    }

    /// <summary>
    /// True when the embedding bridge is loaded and the corpus is at least 80% embedded.
    /// Below the threshold the orchestrator should fall back to Fast.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            if (!_embeddingBridge.IsReady) return false;
            var stamp = _stampRepo.GetCurrent();
            if (stamp.EmbeddableChunks == 0) return false;
            var coverage = (double)stamp.EmbeddedChunks / stamp.EmbeddableChunks;
            return coverage >= SmartModeMinimumPercentage;
        }
    }

    /// <inheritdoc />
    public async Task<SearchResponse> SearchAsync(string query, SearchOptions options, CancellationToken ct = default)
    {
        var cacheKey = $"{query}|{options.TopK}|{options.ChunksPerFile}";
        if (_cache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.ts < CacheTtl)
            return cached.response;

        var sw = Stopwatch.StartNew();
        ct.ThrowIfCancellationRequested();

        // 1. BM25 candidate set
        var bm25Hits = await Task.Run(() => _bm25.Search(query, options), ct);
        var bm25Ms = sw.ElapsedMilliseconds;
        ct.ThrowIfCancellationRequested();

        if (bm25Hits.Count == 0)
        {
            // Empty BM25 is not a fallback — the Smart strategy was dispatched
            // and ran; it just got zero candidates. Badge stays "Smart".
            return new SearchResponse
            {
                Query = query,
                Mode  = SearchMode.Smart,
                Items = new List<HybridHit>(),
                Stats = new SearchStats { DurationMs = (int)sw.ElapsedMilliseconds },
            };
        }

        // 2. Query embedding (single forward pass).
        // Two guarded paths around the model call:
        //   - !IsReady — skip the call entirely; we'd just take the same throw
        //     path inside EmbeddingService.GenerateEmbeddingAsync.
        //   - exception during inference — model file truncated, OOM, GPU
        //     provider mid-reload. Catch and fall through.
        // In both cases the BM25 candidates are already on hand, so we emit
        // them as a Fast-shaped response. The result is NOT cached so the next
        // search retries the embedding path once the model is healthy.
        if (!_embeddingBridge.IsReady)
        {
            return BuildBm25OnlyResponse(query, bm25Hits, sw);
        }

        var embedSw = Stopwatch.StartNew();
        float[] queryVector;
        try
        {
            queryVector = await _embeddingBridge.GenerateEmbeddingAsync(query, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Cascade] Query embedding failed: {ex.Message}. Falling back to BM25-only.");
            return BuildBm25OnlyResponse(query, bm25Hits, sw);
        }
        var embedMs = embedSw.ElapsedMilliseconds;
        if (queryVector.Length == 0)
        {
            return BuildBm25OnlyResponse(query, bm25Hits, sw);
        }
        ct.ThrowIfCancellationRequested();

        // 3. Batched candidate-embedding fetch
        var modelId = _embeddingBridge.ActiveModelId ?? DefaultModelId;
        var keys = bm25Hits.Select(b => (b.FileId, b.ChunkId)).ToArray();
        var fetchSw = Stopwatch.StartNew();
        var embeddings = await _embeddingRepo.GetEmbeddingsByChunkIdsAsync(keys, modelId, ct);
        var fetchMs = fetchSw.ElapsedMilliseconds;
        ct.ThrowIfCancellationRequested();

        // 4. Build a lookup keyed on (file_id, chunk_id). Candidates whose chunk
        //    has no stored embedding stay in the BM25 ranking only.
        var embedLookup = new Dictionary<(string, int), float[]>(embeddings.Count);
        foreach (var e in embeddings)
            embedLookup[(e.FileId, e.ChunkId)] = e.Vector;

        // 5. Cosine similarity for hits with embeddings
        var cosineSw = Stopwatch.StartNew();
        var denseHits = new List<DenseHit>(bm25Hits.Count);
        foreach (var b in bm25Hits)
        {
            ct.ThrowIfCancellationRequested();
            if (!embedLookup.TryGetValue((b.FileId, b.ChunkId), out var vec))
                continue;
            var sim = CosineSimilarity(queryVector, vec);
            denseHits.Add(new DenseHit
            {
                FileId     = b.FileId,
                ChunkId    = b.ChunkId,
                Content    = b.Content ?? "",
                Score      = sim,
                Path       = b.Path,
                Filename   = b.Filename,
                Extension  = b.Extension,
                ModifiedAt = b.ModifiedAt,
            });
        }
        // Dense rerank is by similarity descending — feeds RRF as its rank order.
        denseHits = denseHits.OrderByDescending(d => d.Score).ToList();
        var cosineMs = cosineSw.ElapsedMilliseconds;

        // 6. RRF fusion
        var fusionSw = Stopwatch.StartNew();
        var fused = RrfFusion.Combine(bm25Hits, denseHits, options);
        var fusionMs = fusionSw.ElapsedMilliseconds;

        sw.Stop();

        Debug.WriteLine($"[Cascade] q=\"{query}\" bm25={bm25Ms}ms embed={embedMs}ms fetch={fetchMs}ms cosine={cosineMs}ms fusion={fusionMs}ms total={sw.ElapsedMilliseconds}ms bm25={bm25Hits.Count} dense={denseHits.Count} fused={fused.Count}");
        LocalSynapse.Core.Diagnostics.SpeedDiagLog.Log("CASCADE_SEARCH",
            "query", query,
            "bm25_ms", bm25Ms,
            "embed_ms", embedMs,
            "fetch_ms", fetchMs,
            "cosine_ms", cosineMs,
            "fusion_ms", fusionMs,
            "total_ms", sw.ElapsedMilliseconds,
            "bm25_count", bm25Hits.Count,
            "dense_count", denseHits.Count,
            "fused_count", fused.Count);

        var response = new SearchResponse
        {
            Query = query,
            Mode  = SearchMode.Smart,
            Items = fused.ToList(),
            Stats = new SearchStats
            {
                Bm25Count       = bm25Hits.Count,
                DenseCount      = denseHits.Count,
                TotalCandidates = bm25Hits.Count,
                FinalCount      = fused.Count,
                DurationMs      = (int)sw.ElapsedMilliseconds,
            },
        };

        if (_cache.Count > 10) _cache.Clear();
        _cache[cacheKey] = (DateTime.UtcNow, response);
        return response;
    }

    /// <summary>Clears the per-strategy cache. Called by the orchestrator on re-index events.</summary>
    public void ClearCache() => _cache.Clear();

    private static SearchResponse BuildBm25OnlyResponse(string query, IReadOnlyList<Bm25Hit> bm25Hits, Stopwatch sw)
    {
        sw.Stop();
        var items = bm25Hits.Select(b => new HybridHit
        {
            FileId       = b.FileId,
            Filename     = b.Filename,
            Path         = b.Path,
            Extension    = b.Extension,
            FolderPath   = b.FolderPath,
            HybridScore  = b.Score,
            Bm25Score    = b.Score,
            DenseScore   = 0,
            MatchedTerms = b.MatchedTerms,
            ModifiedAt   = b.ModifiedAt,
            IsDirectory  = b.IsDirectory,
            MatchSource  = b.MatchSource,
        }).ToList();

        return new SearchResponse
        {
            Query = query,
            // Fall-back path: actual dispatch ended up keyword-only, so the
            // badge should say "Fast" not "Smart". Matches the user-visible
            // SmartFallbackBanner copy ("Smart mode unavailable — using Fast.").
            Mode  = SearchMode.Fast,
            Items = items,
            Stats = new SearchStats
            {
                Bm25Count       = bm25Hits.Count,
                DenseCount      = 0,
                TotalCandidates = bm25Hits.Count,
                FinalCount      = items.Count,
                DurationMs      = (int)sw.ElapsedMilliseconds,
            },
        };
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        var dot = 0f;
        var normA = 0f;
        var normB = 0f;
        var simdLength = Vector<float>.Count;
        var i = 0;
        for (; i <= a.Length - simdLength; i += simdLength)
        {
            var va = new Vector<float>(a, i);
            var vb = new Vector<float>(b, i);
            dot += Vector.Dot(va, vb);
            normA += Vector.Dot(va, va);
            normB += Vector.Dot(vb, vb);
        }
        for (; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom > 0 ? dot / denom : 0;
    }
}
