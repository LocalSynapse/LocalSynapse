namespace LocalSynapse.Pipeline.Interfaces;

/// <summary>
/// Pipeline orchestrator that manages scan → index → embed lifecycle.
/// Runs as a long-lived singleton at App level.
/// ViewModels observe state via events; they do NOT own or control the lifecycle.
/// </summary>
public interface IPipelineOrchestrator
{
    /// <summary>Current pipeline phase.</summary>
    PipelinePhase CurrentPhase { get; }

    /// <summary>Whether a cycle is currently running.</summary>
    bool IsRunning { get; }

    /// <summary>Whether the pipeline is paused.</summary>
    bool IsPaused { get; }

    /// <summary>Latest pipeline stamps snapshot (updated after each phase).</summary>
    PipelineProgress LatestProgress { get; }

    /// <summary>Error message from last cycle, null if successful.</summary>
    string? LastError { get; }

    /// <summary>Run one full cycle: scan → index → embed.</summary>
    Task RunCycleAsync(CancellationToken ct = default);

    /// <summary>Start auto-run loop (10-minute interval). Call once at app startup.</summary>
    Task StartAutoRunAsync(CancellationToken ct = default);

    /// <summary>Request an immediate cycle (non-blocking). Signals the auto-run loop.</summary>
    void RequestImmediateCycle();

    /// <summary>Pause pipeline (next cycle will be skipped).</summary>
    void Pause();

    /// <summary>Resume pipeline.</summary>
    void Resume();

    /// <summary>Fired whenever pipeline progress changes (phase, file count, etc.).</summary>
    event Action<PipelineProgress>? ProgressChanged;

    /// <summary>Fired when a cycle completes. Null = success, string = error message.</summary>
    event Action<string?>? CycleCompleted;
}

/// <summary>Pipeline phase.</summary>
public enum PipelinePhase
{
    Idle, Scanning, Indexing, DownloadingModel, Embedding, Paused, Complete
}

/// <summary>Pipeline progress snapshot.</summary>
public sealed class PipelineProgress
{
    public PipelinePhase Phase { get; set; }
    public int Current { get; set; }
    public int Total { get; set; }
    public string? CurrentFile { get; set; }
    public string? StatusText { get; set; }
}
