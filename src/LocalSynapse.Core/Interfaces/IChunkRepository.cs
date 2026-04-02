using LocalSynapse.Core.Models;

namespace LocalSynapse.Core.Interfaces;

public interface IChunkRepository
{
    int UpsertChunks(IEnumerable<FileChunk> chunks);
    IEnumerable<FileChunk> GetChunksForFile(string fileId);
    int DeleteChunksForFile(string fileId);
    int GetTotalCount();
}
