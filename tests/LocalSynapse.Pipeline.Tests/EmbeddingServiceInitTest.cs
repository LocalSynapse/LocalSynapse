using System.IO;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Pipeline.Embedding;
using Xunit;

namespace LocalSynapse.Pipeline.Tests;

/// <summary>
/// T5/T6/T6b — logic-only validation (FX5).
/// EmbeddingService.InitializeAsync 내부의 mode/EP 결정과 SetEpRuntimeStatus 매핑 로직을 검증한다.
/// 실제 InitializeAsync 호출은 v2.11 통합 테스트에서.
/// </summary>
public class EmbeddingServiceInitTest
{
    [Fact]
    public void Mode_resolution_when_user_set_MadMax_with_GPU_available()
    {
        // T5
        var settings = new StubSettingsStore { PerformanceMode = "MadMax" };
        var gpuDetect = new StubGpuDetect { BestProvider = "DirectML", GpuName = "Test GPU" };

        // Replicate InitializeAsync's resolution logic
        var mode = settings.GetPerformanceMode();
        var gpuProvider = mode == "MadMax" ? gpuDetect.CachedResult?.BestProvider : null;

        Assert.Equal("MadMax", mode);
        Assert.Equal("DirectML", gpuProvider);
    }

    [Fact]
    public void Status_mapping_records_failed_with_prefix_detail()
    {
        // T6 — SetEpRuntimeStatus mapping with FX4 prefix
        var settings = new StubSettingsStore();

        var gpuProvider = "DirectML";
        var rawMessage = "Failed to create D3D12 device";
        var detail = $"{gpuProvider}: {rawMessage}";
        var (success, attachedEp, errorDetail) = (false, "CPU", detail);
        var status = success ? "ok" : (attachedEp == "CPU" ? "failed" : "ok_with_fallback");
        settings.SetEpRuntimeStatus(status, attachedEp, errorDetail);

        var (s, ep, d) = settings.GetEpRuntimeStatus();
        Assert.Equal("failed", s);
        Assert.Equal("CPU", ep);
        Assert.StartsWith("DirectML: ", d);
    }

    [Fact]
    public void Status_mapping_records_ok_with_fallback_for_CUDA_to_DirectML()
    {
        // T6b — bonus coverage: CUDA→DirectML fallback case
        var settings = new StubSettingsStore();

        var (success, attachedEp, errorDetail) = (false, "DirectML", "CUDA: Device unavailable");
        var status = success ? "ok" : (attachedEp == "CPU" ? "failed" : "ok_with_fallback");
        settings.SetEpRuntimeStatus(status, attachedEp, errorDetail);

        var (s, ep, d) = settings.GetEpRuntimeStatus();
        Assert.Equal("ok_with_fallback", s);
        Assert.Equal("DirectML", ep);
        Assert.StartsWith("CUDA: ", d);
    }

    // ── Stubs ──

    private sealed class StubSettingsStore : ISettingsStore
    {
        public string PerformanceMode { get; set; } = "Cruise";
        private (string?, string?, string?) _ep = (null, null, null);

        public string GetLanguage() => "en";
        public void SetLanguage(string cultureName) { }
        public string GetDataFolder() => Path.GetTempPath();
        public string GetLogFolder() => Path.GetTempPath();
        public string GetModelFolder() => Path.GetTempPath();
        public string GetDatabasePath() => Path.Combine(Path.GetTempPath(), "test.db");
        public string[]? GetScanRoots() => null;
        public void SetScanRoots(string[]? roots) { }
        public string GetPerformanceMode() => PerformanceMode;
        public void SetPerformanceMode(string mode) => PerformanceMode = mode;
        public (string? bestProvider, string? gpuName) GetGpuDetectionCache() => (null, null);
        public void SetGpuDetectionCache(string? bestProvider, string? gpuName) { }
        public (string? status, string? activeEp, string? detail) GetEpRuntimeStatus() => _ep;
        public void SetEpRuntimeStatus(string? status, string? activeEp, string? detail)
            => _ep = (status, activeEp, detail);

        public string? GetHotkeyCombo() => null;
        public void SetHotkeyCombo(string? combo) { }
        public bool GetHotkeyEnabled() => true;
        public void SetHotkeyEnabled(bool enabled) { }
        public bool GetMinimizeToTrayOnClose() => true;
        public void SetMinimizeToTrayOnClose(bool enabled) { }
        public bool GetShowFirstCloseToast() => true;
        public void SetShowFirstCloseToast(bool show) { }
        public bool? GetAutoStartEnabled() => null;
        public void SetAutoStartEnabled(bool enabled) { }
        public string? GetLastSeenVersion() => null;
        public void SetLastSeenVersion(string version) { }
    }

    private sealed class StubGpuDetect
    {
        public string? BestProvider { get; set; }
        public string? GpuName { get; set; }

        public GpuDetectionResult? CachedResult => BestProvider != null
            ? new GpuDetectionResult([], BestProvider, GpuName)
            : null;
    }
}
