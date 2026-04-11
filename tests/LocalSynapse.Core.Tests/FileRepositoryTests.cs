using LocalSynapse.Core.Database;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Repositories;
using Microsoft.Data.Sqlite;

namespace LocalSynapse.Core.Tests;

public class FileRepositoryTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  Test double — counts CreateConnection invocations
    //  (SqliteConnectionFactory is non-sealed + CreateConnection is virtual
    //   per Step 1 M7)
    // ═══════════════════════════════════════════════════════════════════

    private sealed class CountingConnectionFactory : SqliteConnectionFactory
    {
        public int ConnectionCount;
        public CountingConnectionFactory(ISettingsStore s) : base(s) { }

        public override SqliteConnection CreateConnection()
        {
            Interlocked.Increment(ref ConnectionCount);
            return base.CreateConnection();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  T7. Sub-batch 분할 — 200 files → 3 connections (ceil(200/75))
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void UpsertFiles_ChunksInto75SizedSubBatches()
    {
        using var temp = new TempDbFixture();

        // fixture는 이미 migration을 기본 factory로 수행했음.
        // counter는 repository 호출부터 세도록 새로 생성 — 같은 _dbPath 공유.
        var counter = new CountingConnectionFactory(temp.Settings);
        var repo = new FileRepository(counter);

        // 200 files → ceil(200/75) = 3 sub-batches = 3 connections
        var files = Enumerable.Range(0, 200)
            .Select(i => TestHelpers.CreateTestFile($"/test/file{i}.txt"))
            .ToList();

        repo.UpsertFiles(files);

        Assert.Equal(3, counter.ConnectionCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  T8. Total count 회귀 방지
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void UpsertFiles_TotalInsertedCount_MatchesInput()
    {
        using var temp = new TempDbFixture();
        var repo = new FileRepository(temp.Factory);

        var files = Enumerable.Range(0, 200)
            .Select(i => TestHelpers.CreateTestFile($"/test/file{i}.txt"))
            .ToList();

        var count = repo.UpsertFiles(files);
        Assert.Equal(200, count);

        var (totalFiles, _, _) = repo.CountScanStampTotals();
        Assert.Equal(200, totalFiles);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  T9. W3 regression guard — indexedAt 일관성
    //  원본은 단일 tx 내 DateTime.UtcNow 1회 호출로 모든 파일 동일값.
    //  Sub-batch 분할 시 naïve 구현(각 sub-batch마다 재계산)은 timestamp
    //  상이를 유발하여 recency ranking 경계에서 회귀. W3 수정은 outer에서
    //  1회 계산 후 파라미터 전달. 이 테스트가 영구 가드.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void UpsertFiles_AssignsSameIndexedAtToAllFilesInBatch()
    {
        using var temp = new TempDbFixture();
        var repo = new FileRepository(temp.Factory);

        // 200 files → 3 sub-batches — but all files must share indexedAt
        var files = Enumerable.Range(0, 200)
            .Select(i => TestHelpers.CreateTestFile($"/test/file{i}.txt"))
            .ToList();

        repo.UpsertFiles(files);

        // SQL로 SELECT DISTINCT indexed_at 확인 — 정확히 1개 값이어야 함
        using var conn = temp.Factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(DISTINCT indexed_at) FROM files";
        var distinctCount = (long)cmd.ExecuteScalar()!;

        Assert.Equal(1L, distinctCount);
    }
}
