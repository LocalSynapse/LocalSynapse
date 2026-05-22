using System.Text.Json;
using LocalSynapse.UI.Services;
using Xunit;

namespace LocalSynapse.UI.Tests;

public class UpdateCheckServiceTest : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeSettingsStore _settings;
    private readonly string _checkFilePath;
    private readonly TelemetryCounterService _telemetry;

    public UpdateCheckServiceTest()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ls-update-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settings = new FakeSettingsStore { DataFolder = _tempDir };
        _checkFilePath = Path.Combine(_tempDir, "update-check.json");
        _telemetry = new TelemetryCounterService(new StubPipelineStampRepository());
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task CheckAsync_WithoutForce_ReturnsEarlyWhenCooldownActive()
    {
        var freshTimestamp = DateTime.UtcNow.AddMinutes(-30).ToString("o");
        WriteCheckState(new
        {
            LastCheckAt = freshTimestamp,
            DismissedVersion = (string?)null,
            CheckEnabled = true,
            Iid = "test-iid-001",
        });

        var svc = new UpdateCheckService(_settings, _telemetry);

        var result = await svc.CheckAsync(ct: default, force: false);

        Assert.Null(result);
        Assert.False(svc.HasUpdateAvailable);
        Assert.Equal(freshTimestamp, ReadLastCheckAt());
    }

    [Fact]
    public async Task CheckAsync_WithForce_BypassesCooldownAndUpdatesLastCheckAt()
    {
        var freshTimestamp = DateTime.UtcNow.AddMinutes(-30).ToString("o");
        WriteCheckState(new
        {
            LastCheckAt = freshTimestamp,
            DismissedVersion = (string?)null,
            CheckEnabled = true,
            Iid = "test-iid-002",
        });

        var svc = new UpdateCheckService(_settings, _telemetry);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await svc.CheckAsync(ct: cts.Token, force: true);

        var lastCheckAfter = ReadLastCheckAt();
        Assert.NotNull(lastCheckAfter);
        var beforeParsed = DateTime.Parse(freshTimestamp).ToUniversalTime();
        var afterParsed = DateTime.Parse(lastCheckAfter!).ToUniversalTime();
        Assert.True(afterParsed > beforeParsed,
            $"Expected LastCheckAt to advance from {freshTimestamp}, got {lastCheckAfter}");
    }

    private void WriteCheckState(object state)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_checkFilePath, json);
    }

    private string? ReadLastCheckAt()
    {
        if (!File.Exists(_checkFilePath)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(_checkFilePath));
        return doc.RootElement.TryGetProperty("LastCheckAt", out var prop) ? prop.GetString() : null;
    }
}
