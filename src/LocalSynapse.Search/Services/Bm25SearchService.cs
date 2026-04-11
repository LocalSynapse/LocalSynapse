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

        var ftsQuery = NaturalQueryParser.ToFts5Query(query);
        if (string.IsNullOrEmpty(ftsQuery)) return [];

        var sw = Stopwatch.StartNew();
        var results = ExecuteSearch(ftsQuery, query, options);
        sw.Stop();

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
            });
        }
        return hits;
    }

    /// <summary>캐시를 초기화한다.</summary>
    public void ClearCache() => _cache.Clear();

    private IReadOnlyList<Bm25Hit> ExecuteSearch(string ftsQuery, string originalQuery, SearchOptions options)
    {
        // Phase 1 R5: reader를 먼저 완전 materialize (N+1 제거).
        // 이전 구현은 reader 루프 내부에서 매 row마다 _clickService.GetBoost()를
        // 호출했고, GetBoost는 각 호출마다 새 SqliteConnection을 열었다.
        // LIMIT = TopK * ChunksPerFile * 3 → 최대 ~240개 connection per search.
        // 새 구현: (1) reader materialize, (2) GetBoostBatch 1회, (3) 점수 계산.
        var materialized = new List<(
            string fileId, string filename, string path, string extension,
            string folderPath, string? content, double bm25Score, string modifiedAt, bool isDirectory
        )>();

        using (var conn = _connectionFactory.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            // bm25 weights: chunk_id(0), file_id(0), text(1.0), filename(5.0), folder_path(0.5)
            cmd.CommandText = @"
                SELECT
                    f.id, f.filename, f.path, f.extension, f.folder_path,
                    fc.text, f.modified_at, f.is_directory,
                    bm25(chunks_fts, 0, 0, 1.0, 5.0, 0.5) AS rank
                FROM chunks_fts
                JOIN file_chunks fc ON chunks_fts.chunk_id = fc.id
                JOIN files f ON fc.file_id = f.id
                WHERE chunks_fts MATCH $fts
                ORDER BY rank
                LIMIT $limit";
            cmd.Parameters.AddWithValue("$fts", ftsQuery);
            cmd.Parameters.AddWithValue("$limit", options.TopK * options.ChunksPerFile * 3);

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
                    Math.Abs(r.GetDouble(8)), // bm25() returns negative
                    r.GetString(6),
                    !r.IsDBNull(7) && r.GetInt32(7) == 1
                ));
            }
        } // reader + cmd + connection all disposed here

        // Phase 2: click boost batch lookup (단일 쿼리, N+1 제거의 핵심)
        var paths = materialized.Select(m => m.path).ToList();
        var clickBoosts = _clickService.GetBoostBatch(originalQuery, paths);

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
            var filenameBoost = meaningfulTokens.Length > 0 &&
                meaningfulTokens.Any(t => IsWordBoundaryMatch(m.filename, t))
                    ? 5.0 : 1.0;

            var clickBoost = clickBoosts.TryGetValue(m.path, out var cb) ? cb : 0.0;
            var finalScore = m.bm25Score * recencyBoost * extBoost * filenameBoost * (1.0 + clickBoost);

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
            }, finalScore));
        }

        // File-level dedup: keep best chunk per file
        var grouped = raw
            .GroupBy(x => x.hit.FileId)
            .Select(g => g.OrderByDescending(x => x.rawScore).First().hit)
            .OrderByDescending(h => h.Score)
            .Take(options.TopK)
            .ToList();

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
        return 1.0 / (1.0 + days / 730.0);
    }
}
