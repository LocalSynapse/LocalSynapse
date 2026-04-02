using LocalSynapse.Core.Models;

namespace LocalSynapse.Core.Interfaces;

public interface IPipelineStampRepository
{
    PipelineStamps GetCurrent();
    void StampScanComplete(int totalFiles, int totalFolders, int contentSearchableFiles);
    void UpdateIndexingProgress(int indexedFiles, int totalChunks);
    void StampIndexingComplete(int indexedFiles, int totalChunks);
    void UpdateEmbeddableChunks(int embeddableChunks);
    void UpdateEmbeddingProgress(int embeddedChunks);
    void StampEmbeddingComplete(int embeddableChunks, int embeddedChunks);
    void StampAutoRun();
}
