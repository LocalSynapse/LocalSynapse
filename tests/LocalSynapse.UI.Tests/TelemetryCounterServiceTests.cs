using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.UI.Services;
using Xunit;

namespace LocalSynapse.UI.Tests;

/// <summary>Stub IPipelineStampRepository for telemetry tests.</summary>
internal sealed class StubPipelineStampRepository : IPipelineStampRepository
{
    private readonly int _totalFiles;
    public StubPipelineStampRepository(int totalFiles = 500) => _totalFiles = totalFiles;
    public PipelineStamps GetCurrent() => new() { TotalFiles = _totalFiles };
    public void StampScanComplete(int totalFiles, int totalFolders, int contentSearchableFiles) { }
    public void UpdateIndexingProgress(int indexedFiles, int totalChunks) { }
    public void StampIndexingComplete(int indexedFiles, int totalChunks) { }
    public void UpdateEmbeddableChunks(int embeddableChunks) { }
    public void UpdateEmbeddingProgress(int embeddedChunks) { }
    public void StampEmbeddingComplete(int embeddableChunks, int embeddedChunks) { }
    public void StampAutoRun() { }
}

public class TelemetryCounterServiceTests
{
    private static TelemetryCounterService CreateService(int totalFiles = 500)
        => new(new StubPipelineStampRepository(totalFiles));

    [Fact]
    public void RecordSearch_IncrementsCounts()
    {
        var svc = CreateService();
        svc.RecordSearch("FtsOnly", 100, 5);
        var snap = svc.Snapshot();
        Assert.Equal(1, snap.SearchCount);
        Assert.Equal(0, snap.EmptyResultCount);
        Assert.Equal(100, snap.AvgResponseMs);
        Assert.Equal(1, snap.ModalityBm25);
    }

    [Fact]
    public void RecordSearch_EmptyResult_IncrementsEmptyCount()
    {
        var svc = CreateService();
        svc.RecordSearch("FtsOnly", 50, 0);
        var snap = svc.Snapshot();
        Assert.Equal(1, snap.EmptyResultCount);
    }

    [Fact]
    public void RecordSearch_Hybrid_IncrementsHybridCounter()
    {
        var svc = CreateService();
        svc.RecordSearch("Hybrid", 200, 10);
        var snap = svc.Snapshot();
        Assert.Equal(1, snap.ModalityHybrid);
        Assert.Equal(0, snap.ModalityBm25);
    }

    [Fact]
    public void RecordTopResultClick_IncrementsOnly()
    {
        var svc = CreateService();
        svc.RecordTopResultClick();
        svc.RecordTopResultClick();
        var snap = svc.Snapshot();
        Assert.Equal(2, snap.TopResultClickCount);
        Assert.Equal(0, snap.SearchCount);
    }

    [Fact]
    public void Snapshot_DoesNotResetCounters()
    {
        var svc = CreateService();
        svc.RecordSearch("FtsOnly", 100, 5);
        var snap1 = svc.Snapshot();
        var snap2 = svc.Snapshot();
        Assert.Equal(snap1.SearchCount, snap2.SearchCount);
    }

    [Fact]
    public void ResetCounters_SubtractsConsumedValues()
    {
        var svc = CreateService();
        svc.RecordSearch("FtsOnly", 100, 5);
        svc.RecordSearch("FtsOnly", 200, 3);
        var snap = svc.Snapshot();

        // Simulate new search arriving during POST
        svc.RecordSearch("Hybrid", 50, 1);

        svc.ResetCounters(snap);
        var remaining = svc.Snapshot();
        Assert.Equal(1, remaining.SearchCount);
        Assert.Equal(1, remaining.ModalityHybrid);
        Assert.Equal(0, remaining.ModalityBm25);
    }

    [Fact]
    public void ResetCounters_PreservesPostSnapshotIncrements()
    {
        var svc = CreateService();
        svc.RecordSearch("FtsOnly", 100, 5);
        var snap = svc.Snapshot();
        svc.RecordTopResultClick();
        svc.ResetCounters(snap);
        var after = svc.Snapshot();
        Assert.Equal(1, after.TopResultClickCount);
        Assert.Equal(0, after.SearchCount);
    }

    [Theory]
    [InlineData(500, "<1k")]
    [InlineData(1000, "1k-10k")]
    [InlineData(9999, "1k-10k")]
    [InlineData(10000, "10k-100k")]
    [InlineData(100000, "100k+")]
    public void Snapshot_ComputesBucketCorrectly(int totalFiles, string expectedBucket)
    {
        var svc = CreateService(totalFiles);
        var snap = svc.Snapshot();
        Assert.Equal(expectedBucket, snap.IndexedDocCountBucket);
    }

    [Fact]
    public async Task ThreadSafety_ConcurrentRecordSearch()
    {
        var svc = CreateService();
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => svc.RecordSearch("FtsOnly", 10, 1)))
            .ToArray();
        await Task.WhenAll(tasks);
        var snap = svc.Snapshot();
        Assert.Equal(100, snap.SearchCount);
        Assert.Equal(100, snap.ModalityBm25);
    }
}
