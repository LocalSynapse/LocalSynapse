using System.Diagnostics;
using System.Numerics;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Search.Interfaces;

namespace LocalSynapse.Search.Services;

/// <summary>
/// Streaming batch cosine similarity로 Dense search를 수행한다.
/// 쿼리 텍스트를 IEmbeddingBridge로 벡터화하고,
/// DB의 임베딩과 유사도를 계산하여 Top-K를 반환한다.
/// </summary>
public sealed class DenseSearchService : IDenseSearch
{
    private readonly IEmbeddingBridge _embeddingBridge;
    private readonly IEmbeddingRepository _embeddingRepo;
    private readonly IFileRepository _fileRepo;
    private readonly IChunkRepository _chunkRepo;
    private readonly IPipelineStampRepository _stampRepo;

    private const string DefaultModelId = "bge-m3";
    private const int DefaultBatchSize = 1000;
    private const double MinSimilarityThreshold = 0.1;
    private const double MinCoverageForActivation = 0.8;

    /// <summary>DenseSearchService 생성자.</summary>
    public DenseSearchService(
        IEmbeddingBridge embeddingBridge,
        IEmbeddingRepository embeddingRepo,
        IFileRepository fileRepo,
        IChunkRepository chunkRepo,
        IPipelineStampRepository stampRepo)
    {
        _embeddingBridge = embeddingBridge;
        _embeddingRepo = embeddingRepo;
        _fileRepo = fileRepo;
        _chunkRepo = chunkRepo;
        _stampRepo = stampRepo;
    }

    /// <summary>
    /// Dense search 사용 가능 여부.
    /// 모델 로드 완료 + 임베딩 커버리지 80% 이상일 때 true.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            if (!_embeddingBridge.IsReady)
                return false;

            var stamp = _stampRepo.GetCurrent();
            if (stamp.EmbeddableChunks == 0)
                return false;

            var coverage = (double)stamp.EmbeddedChunks / stamp.EmbeddableChunks;
            return coverage >= MinCoverageForActivation;
        }
    }

    /// <summary>쿼리 임베딩 생성 후 코사인 유사도 기반 Top-K 검색.</summary>
    public async Task<IReadOnlyList<DenseHit>> SearchAsync(
        string query, SearchOptions options, CancellationToken ct = default)
    {
        if (!IsAvailable)
            return Array.Empty<DenseHit>();

        var sw = Stopwatch.StartNew();
        var modelId = _embeddingBridge.ActiveModelId ?? DefaultModelId;

        // 1. 쿼리 벡터 생성
        var queryVector = await _embeddingBridge.GenerateEmbeddingAsync(query, ct);
        if (queryVector.Length == 0)
            return Array.Empty<DenseHit>();

        var embedTime = sw.ElapsedMilliseconds;
        Debug.WriteLine($"[Dense] Query embedding: {embedTime}ms, dim={queryVector.Length}");

        // 2. Streaming batch cosine similarity + min-heap (PriorityQueue)
        var topK = options.TopK > 0 ? options.TopK : 20;
        var heap = new PriorityQueue<(string FileId, int ChunkId), double>();
        var scannedCount = 0;

        await foreach (var emb in _embeddingRepo.EnumerateAllEmbeddingsAsync(
            modelId, DefaultBatchSize, ct))
        {
            scannedCount++;
            var sim = CosineSimilarity(queryVector, emb.Vector);

            if (sim < MinSimilarityThreshold)
                continue;

            if (heap.Count < topK)
            {
                heap.Enqueue((emb.FileId, emb.ChunkId), sim);
            }
            else
            {
                heap.EnqueueDequeue((emb.FileId, emb.ChunkId), sim);
            }
        }

        var searchTime = sw.ElapsedMilliseconds - embedTime;
        Debug.WriteLine($"[Dense] Vector search: {searchTime}ms, scanned={scannedCount}, hits={heap.Count}");

        // 3. DenseHit 생성 — heap에서 꺼내서 score 역순 정렬
        var scored = new List<(string FileId, int ChunkId, double Score)>(heap.Count);
        while (heap.Count > 0)
        {
            heap.TryDequeue(out var item, out var score);
            scored.Add((item.FileId, item.ChunkId, score));
        }
        scored.Reverse();

        // 4. 파일 메타데이터 + 청크 텍스트 조회 (file별 캐시)
        var chunkCache = new Dictionary<string, IEnumerable<FileChunk>>();
        var results = new List<DenseHit>(scored.Count);

        foreach (var (fileId, chunkId, score) in scored)
        {
            var file = _fileRepo.GetById(fileId);

            if (!chunkCache.TryGetValue(fileId, out var chunks))
            {
                chunks = _chunkRepo.GetChunksForFile(fileId);
                chunkCache[fileId] = chunks;
            }
            var chunk = chunks.FirstOrDefault(c => c.ChunkIndex == chunkId);

            results.Add(new DenseHit
            {
                FileId = fileId,
                ChunkId = chunkId,
                Content = chunk?.Text ?? "",
                Score = score,
                Path = file?.Path,
                Filename = file?.Filename,
                Extension = file?.Extension,
                ModifiedAt = file?.ModifiedAt
            });
        }

        Debug.WriteLine($"[Dense] Total: {sw.ElapsedMilliseconds}ms, results={results.Count}");
        return results;
    }

    /// <summary>
    /// Cosine similarity. SIMD 가속을 위해 System.Numerics.Vector 사용.
    /// L2 정규화된 벡터면 dot product와 동일하지만, 안전을 위해 full cosine 계산.
    /// </summary>
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
