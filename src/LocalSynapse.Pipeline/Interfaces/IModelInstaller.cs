namespace LocalSynapse.Pipeline.Interfaces;

public interface IModelInstaller
{
    Task DownloadModelAsync(string modelId, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default);
    bool IsModelInstalled(string modelId);
    string GetModelPath(string modelId);
    IReadOnlyList<ModelInfo> GetAvailableModels();
}

public sealed class DownloadProgress
{
    public long BytesDone { get; set; }
    public long BytesTotal { get; set; }
    public double Percent => BytesTotal > 0 ? (double)BytesDone / BytesTotal * 100 : 0;
}

public sealed class ModelInfo
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public int Dimension { get; set; }
    public long SizeBytes { get; set; }
    public bool IsDefault { get; set; }
}
