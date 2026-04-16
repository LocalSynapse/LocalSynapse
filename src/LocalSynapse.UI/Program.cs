using Avalonia;
using System.Diagnostics;
using System.Text.Json;
using LocalSynapse.Core.Constants;
using LocalSynapse.Pipeline.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace LocalSynapse.UI;

public static class Program
{
    private static readonly System.Text.Encoding Utf8NoBom = new System.Text.UTF8Encoding(false);

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("mcp", StringComparison.OrdinalIgnoreCase))
        {
            // MCP stdio server mode
            RunMcpServer(args).GetAwaiter().GetResult();
        }
        else if (args.Length > 0 && args[0].Equals("dump", StringComparison.OrdinalIgnoreCase))
        {
            // M0-B: Parser quality verification — dump mode.
            // Ctrl-C cooperative cancellation via Console.CancelKeyPress → ct.
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            try { Environment.ExitCode = RunDumpMode(args, cts.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { Environment.ExitCode = 130; }
        }
        else
        {
            // GUI mode
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static async Task RunMcpServer(string[] args)
    {
        Console.Error.WriteLine("LocalSynapse MCP Server starting...");

        var sc = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Services.DI.ServiceCollectionExtensions.AddLocalSynapseServices(sc);
        var sp = sc.BuildServiceProvider();

        sp.GetRequiredService<Core.Interfaces.IMigrationService>().RunMigrations();

        var server = sp.GetRequiredService<Mcp.Interfaces.IMcpServer>();
        await server.RunAsync(CancellationToken.None);
    }

    // M0-B: Parser quality verification dump mode.
    // Does NOT call RunMigrations() — ContentExtractor has no DB dependency (spec §1.8).
    private static async Task<int> RunDumpMode(string[] args, CancellationToken ct = default)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: dump <input-dir> <output-dir> [--only <ext,ext>] [--max-size <N>[KB|MB|GB]] [--include-failed] [--overwrite]");
            return 1;
        }
        var inputDir = new DirectoryInfo(args[1]);
        var outputDir = new DirectoryInfo(args[2]);
        if (!inputDir.Exists)
        {
            Console.Error.WriteLine($"Input directory not found: {inputDir.FullName}");
            return 1;
        }
        if (string.Equals(Path.GetFullPath(inputDir.FullName), Path.GetFullPath(outputDir.FullName), StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Input and output directories must differ.");
            return 1;
        }

        var onlyExts = ParseOnly(args);
        var maxBytes = ParseMaxSize(args);
        var includeFailed = HasFlag(args, "--include-failed");
        var overwrite = HasFlag(args, "--overwrite");

        if (outputDir.Exists && outputDir.EnumerateFileSystemInfos().Any() && !overwrite)
        {
            Console.Error.WriteLine($"Output directory not empty: {outputDir.FullName}. Use --overwrite.");
            return 3;
        }
        try { outputDir.Create(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Failed to create output directory: {ex.Message}");
            return 2;
        }

        // DI — no RunMigrations (DB isolation per spec §1.8). await using for disposal.
        var sc = new ServiceCollection();
        Services.DI.ServiceCollectionExtensions.AddLocalSynapseServices(sc);
        await using var sp = sc.BuildServiceProvider();
        var extractor = sp.GetRequiredService<IContentExtractor>();

        var summary = new DumpSummary(inputDir.FullName, outputDir.FullName);
        var sw = Stopwatch.StartNew();

        foreach (var fi in inputDir.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var ext = fi.Extension.ToLowerInvariant();
            if (!FileExtensions.IsContentSearchable(ext)) continue;
            if (onlyExts != null && !onlyExts.Contains(ext)) continue;
            if (maxBytes.HasValue && fi.Length > maxBytes.Value) continue;

            var relPath = Path.GetRelativePath(inputDir.FullName, fi.FullName);
            var entrySw = Stopwatch.StartNew();
            ExtractionResult result;
            try
            {
                result = await extractor.ExtractAsync(fi.FullName, ext, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result = ExtractionResult.Fail("UNEXPECTED", ex.Message);
                Debug.WriteLine($"[dump] Unexpected exception {relPath}: {ex.Message}");
            }
            var entryMs = entrySw.ElapsedMilliseconds;

            var entry = new DumpEntry
            {
                Path = relPath.Replace('\\', '/'),
                Ext = ext,
                SizeBytes = fi.Length,
                ExtractMs = entryMs,
            };

            if (result.Success)
            {
                var text = result.Text ?? "";
                var targetPath = System.IO.Path.Combine(outputDir.FullName, relPath + ".txt");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath)!);
                await File.WriteAllTextAsync(targetPath, text, Utf8NoBom, ct);
                entry.Result = text.Length == 0 ? "success_empty" : "success";
                entry.TextLength = text.Length;
            }
            else
            {
                entry.Result = "error";
                entry.ErrorCode = result.ErrorCode;
                entry.ErrorMessage = result.ErrorDetail;
                if (includeFailed)
                {
                    var errPath = System.IO.Path.Combine(outputDir.FullName, relPath + ".error.txt");
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(errPath)!);
                    await File.WriteAllTextAsync(errPath,
                        $"ErrorCode: {result.ErrorCode}\nDetail: {result.ErrorDetail}\n",
                        Utf8NoBom, ct);
                }
            }

            summary.Add(entry);
            Console.Out.WriteLine($"  [{entry.Result,-14}] {relPath}  ({fi.Length:N0} bytes, {entryMs} ms)");
        }

        sw.Stop();

        summary.GeneratedAt = DateTime.UtcNow.ToString("O");
        summary.TotalMs = sw.ElapsedMilliseconds;
        // SEC-D1 (Ryan 승인): snake_case JSON naming + SEC-D2 total_ms 필드 포함.
        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        var summaryPath = System.IO.Path.Combine(outputDir.FullName, "_summary.json");
        await File.WriteAllTextAsync(summaryPath, json, Utf8NoBom, ct);

        Console.Out.WriteLine();
        Console.Out.WriteLine($"Processed {summary.Files.Count} files in {sw.Elapsed.TotalSeconds:F1}s.");
        Console.Out.WriteLine($"  success: {summary.Files.Count(f => f.Result == "success")}, " +
                              $"empty: {summary.Files.Count(f => f.Result == "success_empty")}, " +
                              $"failed: {summary.Files.Count(f => f.Result == "error")}");
        Console.Out.WriteLine($"Output: {outputDir.FullName}");
        return 0;
    }

    private static HashSet<string>? ParseOnly(string[] args)
    {
        var idx = Array.IndexOf(args, "--only");
        if (idx < 0 || idx + 1 >= args.Length) return null;
        return args[idx + 1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .ToHashSet();
    }

    private static bool HasFlag(string[] args, string flag)
        => Array.Exists(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static long? ParseMaxSize(string[] args)
    {
        var idx = Array.IndexOf(args, "--max-size");
        if (idx < 0 || idx + 1 >= args.Length) return null;
        var raw = args[idx + 1].ToUpperInvariant();
        long mult = 1;
        if (raw.EndsWith("KB")) { mult = 1024L; raw = raw[..^2]; }
        else if (raw.EndsWith("MB")) { mult = 1024L * 1024; raw = raw[..^2]; }
        else if (raw.EndsWith("GB")) { mult = 1024L * 1024 * 1024; raw = raw[..^2]; }
        return long.TryParse(raw, out var n) ? n * mult : null;
    }

    private sealed class DumpSummary
    {
        public string GeneratedAt { get; set; } = "";
        public string InputDir { get; }
        public string OutputDir { get; }
        public long TotalMs { get; set; }
        public int TotalFiles => Files.Count;
        public Dictionary<string, int> ByExt { get; } = new();
        public Dictionary<string, int> ByResult { get; } = new();
        public List<DumpEntry> Files { get; } = new();

        public DumpSummary(string inputDir, string outputDir)
        {
            InputDir = inputDir;
            OutputDir = outputDir;
        }

        public void Add(DumpEntry e)
        {
            Files.Add(e);
            ByExt[e.Ext] = ByExt.GetValueOrDefault(e.Ext) + 1;
            ByResult[e.Result] = ByResult.GetValueOrDefault(e.Result) + 1;
        }
    }

    private sealed class DumpEntry
    {
        public string Path { get; set; } = "";
        public string Ext { get; set; } = "";
        public long SizeBytes { get; set; }
        public string Result { get; set; } = "";
        public int? TextLength { get; set; }
        public long ExtractMs { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
