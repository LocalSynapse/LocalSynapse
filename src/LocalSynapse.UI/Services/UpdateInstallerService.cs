using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Pipeline.Interfaces;

namespace LocalSynapse.UI.Services;

/// <summary>Verified installer artifact ready for Process.Start.</summary>
public sealed record InstallerArtifact(
    string LocalPath,
    string Sha256Hex,
    long SizeBytes);

/// <summary>
/// Downloads the Windows installer, verifies its SHA256, and launches the wizard.
/// IU-1a: Windows-only logic. Class shape is platform-neutral so IU-1b extends in place.
/// </summary>
public sealed class UpdateInstallerService
{
    private const int BufferSize = 65536;  // 64 KB, matches BgeM3Installer convention
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SumsTimeout = TimeSpan.FromSeconds(5);

    private readonly ISettingsStore _settings;

    /// <summary>Test seam: lets unit tests inject a mocked HttpMessageHandler.</summary>
    private readonly Func<HttpClient> _httpClientFactory;

    /// <summary>Constructor used by DI in production.</summary>
    public UpdateInstallerService(ISettingsStore settings)
        : this(settings, () => new HttpClient { Timeout = DownloadTimeout }) { }

    /// <summary>Test-only constructor for HttpMessageHandler injection.</summary>
    internal UpdateInstallerService(ISettingsStore settings, Func<HttpClient> httpClientFactory)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Returns the absolute path of the Updates/ subdir; creates it if missing.</summary>
    public string GetUpdatesDirectory()
    {
        var dir = Path.Combine(_settings.GetDataFolder(), "Updates");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Removes any file in Updates/ whose name doesn't match the currently-running version.
    /// Called once at app startup (SPEC-IU-1 §4.3.1). Best-effort: caller wraps in try/catch.
    /// </summary>
    public void SweepStaleArtifacts()
    {
        var dir = GetUpdatesDirectory();
        var currentVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        var currentTag = currentVersion is null
            ? null
            : $"v{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}";

        foreach (var path in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(path);
            // Keep only files that appear to belong to the current version.
            if (currentTag is not null && name.Contains(currentTag, StringComparison.OrdinalIgnoreCase))
                continue;

            try { File.Delete(path); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Installer] Sweep failed to delete {name}: {ex.Message}");
            }
        }
    }

    /// <summary>Downloads installer + SHA256SUMS, verifies, returns artifact ready for Launch.
    /// Throws OperationCanceledException on cancel; HttpRequestException on transient network;
    /// InvalidDataException on SHA256 mismatch / missing-line; IOException on disk full;
    /// FileNotFoundException if the .part vanishes (AV quarantine).</summary>
    public async Task<InstallerArtifact> DownloadAsync(
        UpdateCheckService.ReleaseAssets assets,
        IProgress<DownloadProgress> progress,
        CancellationToken ct)
    {
        if (assets is null) throw new ArgumentNullException(nameof(assets));

        var dir = GetUpdatesDirectory();
        var fileName = ExtractFileNameFromUrl(assets.WindowsAssetUrl);
        var targetPath = Path.Combine(dir, fileName);
        var partPath = targetPath + ".part";

        // Step 1-7: download to .part with progress
        try
        {
            using var http = _httpClientFactory();
            using (var response = await http.GetAsync(assets.WindowsAssetUrl,
                       HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength
                                 ?? assets.WindowsAssetSize;
                var downloadedBytes = 0L;

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, BufferSize, useAsync: true);

                var buffer = new byte[BufferSize];
                int bytesRead;
                while (true)
                {
                    // SPEC-IU-1 §4.3.2 step 6: explicit ct check at each loop iteration (in addition
                    // to the implicit check inside ReadAsync). Belt + suspenders.
                    ct.ThrowIfCancellationRequested();
                    bytesRead = await stream.ReadAsync(buffer, ct);
                    if (bytesRead == 0) break;
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    downloadedBytes += bytesRead;
                    progress?.Report(new DownloadProgress
                    {
                        BytesDone = downloadedBytes,
                        BytesTotal = totalBytes,
                    });
                }

                await fileStream.FlushAsync(ct);
            }
            File.Move(partPath, targetPath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            TryDelete(partPath);
            throw;
        }
        catch (Exception ex)
        {
            // CLAUDE.md: every caught exception must be logged.
            Debug.WriteLine($"[Installer] Download failed: {ex.GetType().Name}: {ex.Message}");
            TryDelete(partPath);
            throw;
        }

        // Step 8: SHA256 verification (separate HttpClient with short timeout for the small SUMS file)
        string expectedHex;
        try
        {
            expectedHex = await FetchAndParseSha256SumsAsync(assets.Sha256SumsUrl, fileName, ct);
        }
        catch (System.IO.InvalidDataException)
        {
            // Missing line for our filename — permanent failure.
            Debug.WriteLine($"[Installer] SHA256SUMS missing line for {fileName}");
            TryDelete(targetPath);
            throw;
        }
        catch (Exception ex)
        {
            // Network failure fetching SUMS — transient. Delete artifact and propagate.
            // CLAUDE.md: every caught exception must be logged.
            Debug.WriteLine($"[Installer] SHA256SUMS fetch failed: {ex.GetType().Name}: {ex.Message}");
            TryDelete(targetPath);
            throw;
        }

        // Compute hash of downloaded file
        string actualHex;
        try
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(targetPath);
            var hash = await sha.ComputeHashAsync(fs, ct);
            actualHex = Convert.ToHexString(hash);
        }
        catch (FileNotFoundException)
        {
            // C5: AV quarantine between download finish and verify start
            throw;
        }

        if (!actualHex.Equals(expectedHex, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(targetPath);
            throw new InvalidDataException(
                $"SHA256 mismatch for {fileName}: expected {expectedHex.ToLowerInvariant()}, got {actualHex.ToLowerInvariant()}");
        }

        Debug.WriteLine($"[Installer] Verified {fileName} ({new FileInfo(targetPath).Length} bytes, SHA256 {actualHex.ToLowerInvariant()})");
        return new InstallerArtifact(targetPath, actualHex.ToLowerInvariant(), new FileInfo(targetPath).Length);
    }

    /// <summary>Launches the installer. Fire-and-forget; see SPEC-IU-1 §4.4.</summary>
    public void Launch(InstallerArtifact artifact)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        if (!File.Exists(artifact.LocalPath))
            throw new FileNotFoundException("Installer artifact missing", artifact.LocalPath);

        // Fire-and-forget: do not capture or wait on the returned Process.
        // Inno Setup's PrepareToInstall step (installer/LocalSynapse.iss:222) runs
        // `taskkill /F /IM LocalSynapse.exe` — capturing the handle and waiting
        // would race that taskkill and surface as a process-already-exited exception
        // in the GUI just before the GUI itself dies.
        Process.Start(new ProcessStartInfo(artifact.LocalPath) { UseShellExecute = true });
    }

    /// <summary>Fetches SHA256SUMS.txt and returns the hex hash for the given filename.
    /// Throws InvalidDataException if no matching line is found.</summary>
    private async Task<string> FetchAndParseSha256SumsAsync(string sumsUrl, string fileName, CancellationToken ct)
    {
        // Use the injected factory so test mocks are honored. Override Timeout post-construction
        // (HttpClient.Timeout is settable until the first request is made; factory always returns
        // a fresh instance, so this is safe).
        using var http = _httpClientFactory();
        http.Timeout = SumsTimeout;
        var sumsContent = await http.GetStringAsync(sumsUrl, ct);

        foreach (var line in sumsContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimEnd('\r', ' ', '\t');
            if (trimmed.Length == 0) continue;

            // Format: "{64-hex}  {filename}" (two spaces separator)
            var sepIdx = trimmed.IndexOf("  ", StringComparison.Ordinal);
            if (sepIdx <= 0) continue;

            var hex = trimmed[..sepIdx];
            var name = trimmed[(sepIdx + 2)..];
            if (string.Equals(name, fileName, StringComparison.Ordinal))
                return hex;
        }

        throw new InvalidDataException($"SHA256SUMS does not list {fileName}");
    }

    private static string ExtractFileNameFromUrl(string url)
    {
        var lastSlash = url.LastIndexOf('/');
        return lastSlash >= 0 && lastSlash + 1 < url.Length ? url[(lastSlash + 1)..] : url;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Debug.WriteLine($"[Installer] TryDelete({path}) failed: {ex.Message}"); }
    }
}
