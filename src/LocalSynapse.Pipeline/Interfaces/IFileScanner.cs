namespace LocalSynapse.Pipeline.Interfaces;

public interface IFileScanner
{
    Task<ScanResult> ScanAllDrivesAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default);
}

public sealed class ScanResult
{
    public int FilesDiscovered { get; set; }
    public int FoldersDiscovered { get; set; }
    public int FilesUpserted { get; set; }
    public int FilesDeleted { get; set; }
    public TimeSpan Duration { get; set; }
}

public sealed class ScanProgress
{
    public string CurrentDrive { get; set; } = "";
    public int FilesFound { get; set; }
    public int FoldersFound { get; set; }
}
