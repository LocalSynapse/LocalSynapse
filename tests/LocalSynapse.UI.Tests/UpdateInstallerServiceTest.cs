using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using LocalSynapse.Pipeline.Interfaces;
using LocalSynapse.UI.Services;
using Xunit;

namespace LocalSynapse.UI.Tests;

public class UpdateInstallerServiceTest : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeSettingsStore _settings;

    public UpdateInstallerServiceTest()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ls-installer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settings = new FakeSettingsStore { DataFolder = _tempDir };
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task HappyPath_DownloadsVerifiesAndReturnsArtifact()
    {
        var installerBytes = Encoding.UTF8.GetBytes("fake-installer-content");
        var hash = Convert.ToHexString(SHA256.HashData(installerBytes)).ToLowerInvariant();
        var fileName = "LocalSynapse-v2.99.0-Windows-Setup.exe";
        var sumsContent = $"{hash}  {fileName}\n";

        var handler = new FakeHandler(req =>
            req.RequestUri!.AbsoluteUri.EndsWith(fileName)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(installerBytes)
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(sumsContent)
                });

        var svc = new UpdateInstallerService(_settings, () => new HttpClient(handler));
        var assets = new UpdateCheckService.ReleaseAssets(
            $"https://example.invalid/{fileName}",
            installerBytes.Length,
            "https://example.invalid/SHA256SUMS.txt");

        var artifact = await svc.DownloadAsync(assets, new Progress<DownloadProgress>(_ => { }), default);

        Assert.True(File.Exists(artifact.LocalPath));
        Assert.Equal(hash, artifact.Sha256Hex);
        Assert.Equal(installerBytes.Length, artifact.SizeBytes);
    }

    [Fact]
    public async Task SHA256Mismatch_DeletesArtifactAndThrowsInvalidData()
    {
        var installerBytes = Encoding.UTF8.GetBytes("fake-installer-content");
        var fileName = "LocalSynapse-v2.99.0-Windows-Setup.exe";
        var wrongHash = new string('0', 64);
        var sumsContent = $"{wrongHash}  {fileName}\n";

        var handler = new FakeHandler(req =>
            req.RequestUri!.AbsoluteUri.EndsWith(fileName)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(installerBytes) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sumsContent) });

        var svc = new UpdateInstallerService(_settings, () => new HttpClient(handler));
        var assets = new UpdateCheckService.ReleaseAssets(
            $"https://example.invalid/{fileName}", installerBytes.Length,
            "https://example.invalid/SHA256SUMS.txt");

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await svc.DownloadAsync(assets, new Progress<DownloadProgress>(_ => { }), default));

        Assert.False(File.Exists(Path.Combine(_tempDir, "Updates", fileName)));
    }

    [Fact]
    public async Task SumsMissingMatchingLine_ThrowsInvalidData()
    {
        var installerBytes = Encoding.UTF8.GetBytes("x");
        var fileName = "LocalSynapse-v2.99.0-Windows-Setup.exe";
        var sumsContent = $"{new string('a', 64)}  some-other-file.exe\n";

        var handler = new FakeHandler(req =>
            req.RequestUri!.AbsoluteUri.EndsWith(fileName)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(installerBytes) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sumsContent) });

        var svc = new UpdateInstallerService(_settings, () => new HttpClient(handler));
        var assets = new UpdateCheckService.ReleaseAssets(
            $"https://example.invalid/{fileName}", installerBytes.Length,
            "https://example.invalid/SHA256SUMS.txt");

        var ex = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await svc.DownloadAsync(assets, new Progress<DownloadProgress>(_ => { }), default));
        Assert.Contains(fileName, ex.Message);
    }

    [Fact]
    public async Task SumsGetFailedAtClickTime_PropagatesHttpException()
    {
        var installerBytes = Encoding.UTF8.GetBytes("x");
        var fileName = "LocalSynapse-v2.99.0-Windows-Setup.exe";

        var handler = new FakeHandler(req =>
            req.RequestUri!.AbsoluteUri.EndsWith(fileName)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(installerBytes) }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("503") });

        var svc = new UpdateInstallerService(_settings, () => new HttpClient(handler));
        var assets = new UpdateCheckService.ReleaseAssets(
            $"https://example.invalid/{fileName}", installerBytes.Length,
            "https://example.invalid/SHA256SUMS.txt");

        // SUMS GetStringAsync throws HttpRequestException for non-2xx
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await svc.DownloadAsync(assets, new Progress<DownloadProgress>(_ => { }), default));

        // Artifact deleted (transient cleanup per SPEC-IU-1 §4.3.3 step 8)
        Assert.False(File.Exists(Path.Combine(_tempDir, "Updates", fileName)));
    }

    [Fact]
    public async Task Cancellation_PreCancelledToken_ThrowsOperationCanceled()
    {
        // DIFF-IU-1a Round-1 review W5 fix: pre-cancel the CTS instead of racing CancelAfter against the
        // download. The first await with the cancelled token throws deterministically.
        // .part deletion semantics are exercised by code inspection (TryDelete in catch path)
        // and by the AfterFirstProgressReport_DeletesPartFile test below.
        var fileName = "LocalSynapse-v2.99.0-Windows-Setup.exe";
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new FakeHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1024]) });
        var svc = new UpdateInstallerService(_settings, () => new HttpClient(handler));
        var assets = new UpdateCheckService.ReleaseAssets(
            $"https://example.invalid/{fileName}", 1024,
            "https://example.invalid/SHA256SUMS.txt");

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await svc.DownloadAsync(assets, new Progress<DownloadProgress>(_ => { }), cts.Token));
    }

    [Fact]
    public async Task Cancellation_AfterFirstProgressReport_DeletesPartFile()
    {
        // Cancellation triggered from the IProgress callback on first report — guaranteed to
        // fire after .part is created (so the catch-path's TryDelete(partPath) is exercised
        // and observable). Deterministic: doesn't rely on wall-clock timing.
        var fileName = "LocalSynapse-v2.99.0-Windows-Setup.exe";
        var bytes = new byte[1024 * 1024];  // 1 MB so multiple buffer fills happen

        using var cts = new CancellationTokenSource();
        var handler = new FakeHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var svc = new UpdateInstallerService(_settings, () => new HttpClient(handler));
        var assets = new UpdateCheckService.ReleaseAssets(
            $"https://example.invalid/{fileName}", bytes.Length,
            "https://example.invalid/SHA256SUMS.txt");

        var progress = new ImmediateCancelProgress(cts);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await svc.DownloadAsync(assets, progress, cts.Token));

        var partPath = Path.Combine(_tempDir, "Updates", fileName + ".part");
        var targetPath = Path.Combine(_tempDir, "Updates", fileName);
        Assert.False(File.Exists(partPath), "Part file should have been deleted on cancel");
        Assert.False(File.Exists(targetPath), "Target file should not have been created on cancel");
    }

    [Fact]
    public void Sweep_DeletesFilesNotMatchingCurrentVersion()
    {
        var dir = Path.Combine(_tempDir, "Updates");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "LocalSynapse-v0.0.1-Windows-Setup.exe"), "stale");
        File.WriteAllText(Path.Combine(dir, "unrelated-file.txt"), "stale");

        var svc = new UpdateInstallerService(_settings, () => new HttpClient());
        svc.SweepStaleArtifacts();

        // The current Assembly version may match v2.10.0 (production) or 0.0.0 (test runner) — either way,
        // the v0.0.1 file should not match unless test runner happens to be at v0.0.1, which is implausible.
        Assert.False(File.Exists(Path.Combine(dir, "LocalSynapse-v0.0.1-Windows-Setup.exe")));
        Assert.False(File.Exists(Path.Combine(dir, "unrelated-file.txt")));
    }

    /// <summary>Minimal HttpMessageHandler that maps requests via a delegate.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_responder(request));
    }

    /// <summary>IProgress that calls Cancel() synchronously on the first report. Synchronous so
    /// the cancellation is observable on the next loop iteration's ct check (Progress&lt;T&gt;
    /// would post to a sync context, racing the next await).</summary>
    private sealed class ImmediateCancelProgress : IProgress<DownloadProgress>
    {
        private readonly CancellationTokenSource _cts;
        public ImmediateCancelProgress(CancellationTokenSource cts) => _cts = cts;
        public void Report(DownloadProgress value) => _cts.Cancel();
    }
}
