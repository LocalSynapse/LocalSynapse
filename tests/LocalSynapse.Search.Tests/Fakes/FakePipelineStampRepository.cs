using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;

namespace LocalSynapse.Search.Tests.Fakes;

/// <summary>
/// IPipelineStampRepository fake. GetCurrent returns a pre-seeded stamp;
/// callers can supply a stamp tuned to drive cascade IsAvailable past or
/// below the 80% embedding-coverage threshold.
/// </summary>
internal sealed class FakePipelineStampRepository : IPipelineStampRepository
{
    private readonly PipelineStamps _stamp;

    public FakePipelineStampRepository(PipelineStamps? stamp = null)
        => _stamp = stamp ?? new PipelineStamps();

    public PipelineStamps GetCurrent() => _stamp;

    public void StampScanComplete(int totalFiles, int totalFolders, int contentSearchableFiles) => throw new NotSupportedException();
    public void UpdateIndexingProgress(int indexedFiles, int totalChunks) => throw new NotSupportedException();
    public void StampIndexingComplete(int indexedFiles, int totalChunks) => throw new NotSupportedException();
    public void UpdateEmbeddableChunks(int embeddableChunks) => throw new NotSupportedException();
    public void UpdateEmbeddingProgress(int embeddedChunks) => throw new NotSupportedException();
    public void StampEmbeddingComplete(int embeddableChunks, int embeddedChunks) => throw new NotSupportedException();
    public void StampAutoRun() => throw new NotSupportedException();
}
