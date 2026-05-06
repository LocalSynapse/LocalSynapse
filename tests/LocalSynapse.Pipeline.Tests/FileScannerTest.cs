using Xunit;
using LocalSynapse.Pipeline.Interfaces;
using LocalSynapse.Pipeline.Scanning;

namespace LocalSynapse.Pipeline.Tests;

public class FileScannerTest : IDisposable
{
    private readonly string _tempDir;
    private readonly TestDb _db;

    public FileScannerTest()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ls_fs_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        // Create test files
        File.WriteAllText(Path.Combine(_tempDir, "doc1.txt"), "Hello");
        File.WriteAllText(Path.Combine(_tempDir, "doc2.md"), "# Heading");

        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "doc3.csv"), "a,b,c");

        var nodeModules = Path.Combine(_tempDir, "node_modules");
        Directory.CreateDirectory(nodeModules);
        File.WriteAllText(Path.Combine(nodeModules, "package.json"), "{}");

        _db = TestDbHelper.Create();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task ScanAllDrives_FindsFilesInTestFolder()
    {
        var scanner = new FileScanner(_db.FileRepo, _db.StampRepo, new[] { _tempDir });

        var result = await scanner.ScanAllDrivesAsync();

        Assert.True(result.FilesDiscovered >= 3, $"Expected at least 3 files but found {result.FilesDiscovered}");
    }

    [Fact]
    public async Task ScanAllDrives_ExcludesNodeModules()
    {
        var scanner = new FileScanner(_db.FileRepo, _db.StampRepo, new[] { _tempDir });

        await scanner.ScanAllDrivesAsync();

        // Verify no file from node_modules is in the DB
        var nodeModulesFile = _db.FileRepo.GetByPath(Path.Combine(_tempDir, "node_modules", "package.json"));
        Assert.Null(nodeModulesFile);
    }

    [Fact]
    public async Task ScanAllDrives_ReportsProgress()
    {
        var scanner = new FileScanner(_db.FileRepo, _db.StampRepo, new[] { _tempDir });

        var progressReports = new List<ScanProgress>();
        var progress = new Progress<ScanProgress>(p => progressReports.Add(p));

        await scanner.ScanAllDrivesAsync(progress);

        // Give a moment for progress callbacks to be delivered (they may be async)
        await Task.Delay(100);

        Assert.True(progressReports.Count > 0, "Expected at least one progress report");
    }
}
