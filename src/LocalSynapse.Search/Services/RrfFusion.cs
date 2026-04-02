using LocalSynapse.Core.Models;

namespace LocalSynapse.Search.Services;

/// <summary>
/// Reciprocal Rank Fusion. BM25와 Dense 결과를 RRF Score로 결합한다.
/// RrfScore = Σ(1 / (K + rank_i)), K=60
/// </summary>
public static class RrfFusion
{
    private const int K = 60;

    /// <summary>BM25와 Dense 결과를 RRF로 결합한다.</summary>
    public static IReadOnlyList<HybridHit> Combine(
        IReadOnlyList<Bm25Hit> bm25Results,
        IReadOnlyList<DenseHit> denseResults,
        SearchOptions options)
    {
        var map = new Dictionary<string, HybridHit>();

        // BM25 results
        for (int i = 0; i < bm25Results.Count; i++)
        {
            var b = bm25Results[i];
            var rrfScore = 1.0 / (K + i + 1);

            map[b.FileId] = new HybridHit
            {
                FileId = b.FileId,
                Filename = b.Filename,
                Path = b.Path,
                Extension = b.Extension,
                FolderPath = b.FolderPath,
                HybridScore = rrfScore,
                Bm25Score = b.Score,
                DenseScore = 0,
                MatchedTerms = b.MatchedTerms,
                ModifiedAt = b.ModifiedAt,
                IsDirectory = b.IsDirectory,
                MatchSource = MatchSource.Content,
            };
        }

        // Dense results
        for (int i = 0; i < denseResults.Count; i++)
        {
            var d = denseResults[i];
            var rrfScore = 1.0 / (K + i + 1);

            if (map.TryGetValue(d.FileId, out var existing))
            {
                existing.HybridScore += rrfScore;
                existing.DenseScore = d.Score;
            }
            else
            {
                map[d.FileId] = new HybridHit
                {
                    FileId = d.FileId,
                    Filename = d.Filename ?? Path.GetFileName(d.Path ?? ""),
                    Path = d.Path ?? "",
                    Extension = d.Extension ?? "",
                    FolderPath = Path.GetDirectoryName(d.Path ?? "") ?? "",
                    HybridScore = rrfScore,
                    Bm25Score = 0,
                    DenseScore = d.Score,
                    ModifiedAt = d.ModifiedAt ?? "",
                    MatchSource = MatchSource.Content,
                };
            }
        }

        return map.Values
            .OrderByDescending(h => h.HybridScore)
            .Take(options.TopK)
            .ToList();
    }
}
