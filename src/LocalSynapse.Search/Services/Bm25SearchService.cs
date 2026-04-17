using System.Collections.Concurrent;
using System.Diagnostics;
using LocalSynapse.Core.Database;
using LocalSynapse.Core.Models;
using LocalSynapse.Search.Constants;
using LocalSynapse.Search.Interfaces;
using Microsoft.Data.Sqlite;

namespace LocalSynapse.Search.Services;

/// <summary>
/// FTS5 기반 BM25 검색 서비스. chunks_fts와 files_fts에 MATCH 쿼리를 실행한다.
/// </summary>
public sealed class Bm25SearchService : IBm25Search
{
    // H1 (M0-H): materialize 폭주 방지 상한.
    // 진단 v1.1 기반 — TopK*ChunksPerFile*3와 Math.Min으로 clamp.
    private const int MaxMaterializeRows = 600;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SearchClickService _clickService;
    private readonly ConcurrentDictionary<string, (DateTime ts, IReadOnlyList<Bm25Hit> hits)> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    /// <summary>Bm25SearchService 생성자.</summary>
    public Bm25SearchService(SqliteConnectionFactory connectionFactory, SearchClickService clickService)
    {
        _connectionFactory = connectionFactory;
        _clickService = clickService;
    }

    /// <summary>FTS5 MATCH 기반 BM25 검색을 실행한다.</summary>
    public IReadOnlyList<Bm25Hit> Search(string query, SearchOptions options)
    {
        var cacheKey = $"{query}|{options.TopK}|{options.ChunksPerFile}";
        if (_cache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.ts < CacheTtl)
            return cached.hits;

        var ftsSw = Stopwatch.StartNew();
        var ftsQuery = NaturalQueryParser.ToFts5Query(query);
        var ftsBuildMs = ftsSw.ElapsedMilliseconds;
        if (string.IsNullOrEmpty(ftsQuery)) return [];

        var sw = Stopwatch.StartNew();
        var results = ExecuteSearch(ftsQuery, query, options);
        sw.Stop();

        LocalSynapse.Core.Diagnostics.SpeedDiagLog.Log("BM25_SEARCH",
            "query", query,
            "fts_build_ms", ftsBuildMs,
            "execute_ms", sw.ElapsedMilliseconds,
            "results", results.Count);
        Debug.WriteLine($"[BM25] query=\"{query}\" fts=\"{ftsQuery}\" results={results.Count} time={sw.ElapsedMilliseconds}ms");

        if (_cache.Count > 10) _cache.Clear();
        _cache[cacheKey] = (DateTime.UtcNow, results);

        return results;
    }

    /// <summary>LIKE 기반 빠른 파일명 검색. Multi-word queries match ANY token.</summary>
    public IReadOnlyList<Bm25Hit> QuickSearch(string query, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var tokens = query.Split([' ', ',', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return [];

        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();

        // Build OR conditions for each token
        var conditions = new List<string>();
        for (int i = 0; i < tokens.Length; i++)
        {
            conditions.Add($"(filename LIKE $p{i} COLLATE NOCASE OR path LIKE $p{i} COLLATE NOCASE)");
            cmd.Parameters.AddWithValue($"$p{i}", $"%{tokens[i]}%");
        }

        cmd.CommandText = $@"
            SELECT id, filename, path, extension, folder_path, content, modified_at, is_directory
            FROM files
            WHERE is_directory = 0 AND ({string.Join(" OR ", conditions)})
            ORDER BY modified_at DESC
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        var hits = new List<Bm25Hit>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            hits.Add(new Bm25Hit
            {
                FileId = r.GetString(0),
                Filename = r.GetString(1),
                Path = r.GetString(2),
                Extension = r.GetString(3),
                FolderPath = r.GetString(4),
                Content = r.IsDBNull(5) ? null : r.GetString(5),
                Score = 1.0,
                ModifiedAt = r.GetString(6),
                IsDirectory = !r.IsDBNull(7) && r.GetInt32(7) == 1,
                MatchSource = MatchSource.FileName, // QuickSearch = LIKE 기반 파일명 매치
            });
        }
        return hits;
    }

    /// <summary>FTS5 쿼리에 매치되는 chunk 수를 파일별로 반환한다.</summary>
    public Dictionary<string, (int matchCount, int firstMatchIndex)> GetMatchChunkCounts(
        string ftsQuery, IReadOnlyList<string> fileIds)
    {
        if (string.IsNullOrWhiteSpace(ftsQuery) || fileIds.Count == 0) return new();
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();

        var placeholders = new List<string>();
        for (int i = 0; i < fileIds.Count; i++)
        {
            placeholders.Add($"$f{i}");
            cmd.Parameters.AddWithValue($"$f{i}", fileIds[i]);
        }

        cmd.CommandText = $@"
            SELECT fc.file_id, COUNT(*) as match_count,
                   MIN(fc.chunk_index) as min_chunk_index
            FROM chunks_fts
            JOIN file_chunks fc ON chunks_fts.chunk_id = fc.id
            WHERE chunks_fts MATCH $fts
              AND fc.file_id IN ({string.Join(",", placeholders)})
            GROUP BY fc.file_id";
        cmd.Parameters.AddWithValue("$fts", ftsQuery);

        var result = new Dictionary<string, (int, int)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result[r.GetString(0)] = (r.GetInt32(1), r.GetInt32(2));
        return result;
    }

    /// <summary>캐시를 초기화한다.</summary>
    public void ClearCache() => _cache.Clear();

    private IReadOnlyList<Bm25Hit> ExecuteSearch(string ftsQuery, string originalQuery, SearchOptions options)
    {
        var matSw = Stopwatch.StartNew();
        var materialized = new List<(
            string fileId, string filename, string path, string extension,
            string folderPath, string? content, double bm25Score, string modifiedAt, bool isDirectory,
            double filenameRank, double contentRank, double folderRank
        )>();

        using (var conn = _connectionFactory.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            // bm25 weights: chunk_id(0), file_id(0), text(1.0), filename(3.0), folder_path(1.0)
            cmd.CommandText = @"
                SELECT
                    f.id, f.filename, f.path, f.extension, f.folder_path,
                    fc.text, f.modified_at, f.is_directory,
                    bm25(chunks_fts, 0, 0, 1.0, 3.0, 1.0) AS rank,
                    bm25(chunks_fts, 0, 0, 0, 1.0, 0) AS filename_rank,
                    bm25(chunks_fts, 0, 0, 1.0, 0, 0) AS content_rank,
                    bm25(chunks_fts, 0, 0, 0, 0, 1.0) AS folder_rank
                FROM chunks_fts
                JOIN file_chunks fc ON chunks_fts.chunk_id = fc.id
                JOIN files f ON fc.file_id = f.id
                WHERE chunks_fts MATCH $fts
                ORDER BY rank
                LIMIT $limit";
            cmd.Parameters.AddWithValue("$fts", ftsQuery);
            var limit = Math.Min(options.TopK * options.ChunksPerFile * 3, MaxMaterializeRows);
            cmd.Parameters.AddWithValue("$limit", limit);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                materialized.Add((
                    r.GetString(0),
                    r.GetString(1),
                    r.GetString(2),
                    r.GetString(3),
                    r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    Math.Abs(r.GetDouble(8)), // bm25() returns negative — Score용 양수
                    r.GetString(6),
                    !r.IsDBNull(7) && r.GetInt32(7) == 1,
                    r.GetDouble(9),   // filename_rank (음수 원본 — MatchSource 판별용)
                    r.GetDouble(10),  // content_rank
                    r.GetDouble(11)   // folder_rank
                ));
            }
        } // reader + cmd + connection all disposed here
        var matMs = matSw.ElapsedMilliseconds;

        // Phase 2: click boost batch lookup (단일 쿼리, N+1 제거의 핵심)
        var boostSw = Stopwatch.StartNew();
        var paths = materialized.Select(m => m.path).ToList();
        var clickBoosts = _clickService.GetBoostBatch(originalQuery, paths);
        var boostMs = boostSw.ElapsedMilliseconds;

        var scoreSw = Stopwatch.StartNew();

        // Phase 3: 점수 계산
        // [FIX] stop word를 제거한 토큰으로 filenameBoost 계산
        // 이전: "the budget report" → "the"가 모든 파일명에 매칭 → 항상 5.0x
        // 수정: stop word 제거 후 의미 있는 토큰만 비교
        var meaningfulTokens = NaturalQueryParser.RemoveStopwords(originalQuery)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var raw = new List<(Bm25Hit hit, double rawScore)>();
        foreach (var m in materialized)
        {
            var recencyBoost = ComputeRecencyBoost(m.modifiedAt);
            var extBoost = ExtensionBoost.GetBoost(m.extension);
            // R3: MatchSource 판별 (컬럼별 BM25 음수 점수 기반)
            var source = MatchSource.None;
            if (m.filenameRank < 0) source |= MatchSource.FileName;
            if (m.contentRank < 0)  source |= MatchSource.Content;
            if (m.folderRank < 0)   source |= MatchSource.Folder;
            if (source == MatchSource.None) source = MatchSource.Content; // fallback

            // R4: filenameBoost 가산 방식 (곱셈 제거)
            var hasFilenameMatch = meaningfulTokens.Length > 0 &&
                meaningfulTokens.Any(t => IsWordBoundaryMatch(m.filename, t));
            var clickBoost = clickBoosts.TryGetValue(m.path, out var cb) ? cb : 0.0;
            var baseScore = m.bm25Score * recencyBoost * extBoost * (1.0 + clickBoost);
            var filenameBonus = hasFilenameMatch ? baseScore * 0.5 : 0;
            var finalScore = baseScore + filenameBonus;

            raw.Add((new Bm25Hit
            {
                FileId = m.fileId,
                Filename = m.filename,
                Path = m.path,
                Extension = m.extension,
                FolderPath = m.folderPath,
                Content = m.content,
                Score = finalScore,
                MatchedTerms = meaningfulTokens.ToList(),
                ModifiedAt = m.modifiedAt,
                IsDirectory = m.isDirectory,
                MatchSource = source,
                FilenameRank = m.filenameRank,
                ContentRank = m.contentRank,
                FolderRank = m.folderRank,
            }, finalScore));
        }

        // File-level dedup: keep best chunk per file
        var grouped = raw
            .GroupBy(x => x.hit.FileId)
            .Select(g => g.OrderByDescending(x => x.rawScore).First().hit)
            .OrderByDescending(h => h.Score)
            .Take(options.TopK)
            .ToList();

        var scoreMs = scoreSw.ElapsedMilliseconds;

        LocalSynapse.Core.Diagnostics.SpeedDiagLog.Log("BM25_EXEC",
            "materialize_ms", matMs,
            "materialize_rows", materialized.Count,
            "click_boost_ms", boostMs,
            "score_ms", scoreMs,
            "final_count", grouped.Count);

        // Apply extension filter
        if (options.ExtensionFilter is { Count: > 0 })
        {
            var filter = new HashSet<string>(options.ExtensionFilter, StringComparer.OrdinalIgnoreCase);
            return grouped.Where(h => filter.Contains(h.Extension)).ToList();
        }

        return grouped;
    }

    /// <summary>
    /// 파일명에서 단어 경계 기준 매칭 여부를 확인한다.
    /// "plan" → "project-plan.docx" ✅, "explanation.pdf" ✗
    /// </summary>
    private static bool IsWordBoundaryMatch(string filename, string token)
    {
        var pos = 0;
        while (pos < filename.Length)
        {
            var idx = filename.IndexOf(token, pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;

            var before = idx == 0 || !char.IsLetterOrDigit(filename[idx - 1]);
            var afterIdx = idx + token.Length;
            var after = afterIdx >= filename.Length || !char.IsLetterOrDigit(filename[afterIdx]);

            if (before && after) return true;
            if (before && afterIdx < filename.Length && filename[afterIdx] == '.') return true;

            pos = idx + 1;
        }
        return false;
    }

    private static double ComputeRecencyBoost(string modifiedAt)
    {
        if (!DateTime.TryParse(modifiedAt, out var date)) return 1.0;
        var days = (DateTime.UtcNow - date).TotalDays;
        var boost = 1.0 / (1.0 + days / 365.0);
        return Math.Max(0.3, boost);
    }
}
