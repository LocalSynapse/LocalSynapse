using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Pipeline.Interfaces;

namespace LocalSynapse.UI.ViewModels;

/// <summary>
/// Data Setup page ViewModel.
/// OBSERVER ONLY — does not own the pipeline lifecycle.
/// Pipeline runs at App level; this VM subscribes to events and displays state.
/// </summary>
public partial class DataSetupViewModel : ObservableObject, IDisposable
{
    private readonly IPipelineOrchestrator _orchestrator;
    private readonly IPipelineStampRepository _stampRepo;
    private readonly IModelInstaller _modelInstaller;
    private readonly IFileRepository _fileRepo;

    [ObservableProperty] private PipelineStamps _stamps = new();
    [ObservableProperty] private PipelinePhase _currentPhase;
    [ObservableProperty] private bool _isPipelinePaused;
    [ObservableProperty] private string _modelStatus = "";
    [ObservableProperty] private string _scanStatusText = "";
    [ObservableProperty] private int _scanFilesFound;
    [ObservableProperty] private bool _isCycleRunning;
    [ObservableProperty] private int _skippedFiles;

    // ── Stepper state (derived from CurrentPhase + Stamps) ──
    [ObservableProperty] private bool _isScanComplete;
    [ObservableProperty] private bool _isExtractComplete;
    [ObservableProperty] private bool _isEmbedComplete;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isExtracting;
    [ObservableProperty] private bool _isEmbedding;
    [ObservableProperty] private double _extractProgress;
    [ObservableProperty] private double _embedProgress;
    [ObservableProperty] private string _pipelineStatusText = "";
    [ObservableProperty] private bool _hasSkippedFiles;
    [ObservableProperty] private bool _isScanPending;
    [ObservableProperty] private bool _isExtractPending;
    [ObservableProperty] private bool _isEmbedPending;

    private readonly System.Threading.Timer _refreshTimer;

    public DataSetupViewModel(
        IPipelineOrchestrator orchestrator,
        IPipelineStampRepository stampRepo,
        IModelInstaller modelInstaller,
        IFileRepository fileRepo)
    {
        _orchestrator = orchestrator;
        _stampRepo = stampRepo;
        _modelInstaller = modelInstaller;
        _fileRepo = fileRepo;

        // Subscribe to pipeline events (these fire on background threads)
        _orchestrator.ProgressChanged += OnProgressChanged;
        _orchestrator.CycleCompleted += OnCycleCompleted;

        // Load current state
        RefreshState();

        _refreshTimer = new System.Threading.Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshState);
        }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    /// <summary>Request an immediate pipeline cycle (non-blocking).</summary>
    [RelayCommand]
    private void ScanNow()
    {
        if (_orchestrator.IsRunning) return;

        if (_orchestrator.IsPaused)
            _orchestrator.Resume();

        Debug.WriteLine("[UI] ScanNow clicked — requesting immediate cycle");
        _orchestrator.RequestImmediateCycle();
    }

    /// <summary>Pause pipeline.</summary>
    [RelayCommand]
    private void PausePipeline()
    {
        _orchestrator.Pause();
        IsPipelinePaused = true;
        ScanStatusText = "Paused";
    }

    /// <summary>Resume pipeline.</summary>
    [RelayCommand]
    private void ResumePipeline()
    {
        _orchestrator.Resume();
        IsPipelinePaused = false;
        ScanStatusText = "";
    }

    /// <summary>Download embedding model.</summary>
    [RelayCommand]
    private async Task DownloadModelAsync()
    {
        ModelStatus = "Downloading... (0%)  —  ~2.3 GB total";
        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var pct = p.Percent;
                    var doneMB = p.BytesDone / (1024.0 * 1024);
                    var totalMB = p.BytesTotal / (1024.0 * 1024);
                    ModelStatus = totalMB > 1024
                        ? $"Downloading... ({pct:F0}%)  —  {doneMB / 1024:F1} / {totalMB / 1024:F1} GB"
                        : $"Downloading... ({pct:F0}%)  —  {doneMB:F0} / {totalMB:F0} MB";
                });
            });
            await _modelInstaller.DownloadModelAsync("bge-m3", progress);
            ModelStatus = "Installed";
        }
        catch (OperationCanceledException)
        {
            ModelStatus = "Download cancelled";
        }
        catch (HttpRequestException)
        {
            ModelStatus = "Download failed — check your internet connection and try again";
        }
        catch (IOException)
        {
            ModelStatus = "Download failed — not enough disk space (~2.3 GB required)";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataSetupVM] Model download error: {ex}");
            ModelStatus = "Download failed — try again later";
        }
    }

    // ─────────────────────────── Event handlers ───────────────────────────

    private void OnProgressChanged(PipelineProgress progress)
    {
        // Called on background thread — dispatch to UI
        Dispatcher.UIThread.Post(() =>
        {
            CurrentPhase = progress.Phase;
            ScanFilesFound = progress.Current;
            ScanStatusText = progress.StatusText ?? progress.Phase.ToString();
            IsCycleRunning = _orchestrator.IsRunning;
            Stamps = _stampRepo.GetCurrent();
            UpdateStepperState();
        });
    }

    private void OnCycleCompleted(string? error)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (error != null)
            {
                Debug.WriteLine($"[DataSetupVM] Cycle error: {error}");
                ScanStatusText = error.Contains("UnauthorizedAccess", StringComparison.OrdinalIgnoreCase)
                    ? "Some files could not be accessed — scan completed with partial results"
                    : error.Contains("disk", StringComparison.OrdinalIgnoreCase) || error.Contains("space", StringComparison.OrdinalIgnoreCase)
                    ? "Scan interrupted — not enough disk space for indexing"
                    : "Scan completed with errors — some files may not be searchable";
            }
            else
            {
                ScanStatusText = "";
            }

            IsCycleRunning = false;
            RefreshState();
        });
    }

    private void RefreshState()
    {
        Stamps = _stampRepo.GetCurrent();
        CurrentPhase = _orchestrator.CurrentPhase;
        IsPipelinePaused = _orchestrator.IsPaused;
        IsCycleRunning = _orchestrator.IsRunning;
        ModelStatus = _modelInstaller.IsModelInstalled("bge-m3") ? "Installed" : "Not installed";

        try { var (cloud, _, _, _) = _fileRepo.CountSkippedByCategory(); SkippedFiles = cloud; }
        catch (Exception ex) { Debug.WriteLine($"[DataSetupVM] RefreshState skipped count failed: {ex.Message}"); SkippedFiles = 0; }

        // Show last progress text if cycle is running
        if (_orchestrator.IsRunning && _orchestrator.LatestProgress.StatusText != null)
        {
            ScanStatusText = _orchestrator.LatestProgress.StatusText;
            ScanFilesFound = _orchestrator.LatestProgress.Current;
        }

        // ── Update stepper state ──
        UpdateStepperState();
    }

    private void UpdateStepperState()
    {
        // Step completion
        IsScanComplete = Stamps.ScanComplete;
        IsExtractComplete = Stamps.IndexingComplete;
        IsEmbedComplete = Stamps.EmbeddingComplete;

        // Step in-progress
        IsScanning = CurrentPhase == PipelinePhase.Scanning;
        IsExtracting = CurrentPhase == PipelinePhase.Indexing;
        IsEmbedding = CurrentPhase == PipelinePhase.Embedding;

        // Progress values
        ExtractProgress = Stamps.IndexingPercent;
        EmbedProgress = Stamps.EmbeddingPercent;

        // Pending state (not complete AND not in-progress)
        IsScanPending = !IsScanComplete && !IsScanning;
        IsExtractPending = !IsExtractComplete && !IsExtracting;
        IsEmbedPending = !IsEmbedComplete && !IsEmbedding;

        // Skipped
        HasSkippedFiles = SkippedFiles > 0;

        // Status text
        if (IsScanning)
            PipelineStatusText = $"Scanning...  —  {ScanFilesFound:N0} files checked";
        else if (IsExtracting)
            PipelineStatusText = $"Extracting...  —  {Stamps.IndexedFiles:N0} / {Stamps.ContentSearchableFiles:N0} files ({Stamps.IndexingPercent:F1}%)";
        else if (IsEmbedding)
            PipelineStatusText = $"Embedding...  —  {Stamps.EmbeddingPercent:F1}%";
        else if (Stamps.TotalFiles > 0)
        {
            var lastScan = Stamps.ScanCompletedAt;
            if (lastScan != null && DateTime.TryParse(lastScan, out var dt))
            {
                var ago = DateTime.UtcNow - dt;
                var agoText = ago.TotalMinutes < 1 ? "just now"
                    : ago.TotalMinutes < 60 ? $"{(int)ago.TotalMinutes} minutes ago"
                    : ago.TotalHours < 24 ? $"{(int)ago.TotalHours} hours ago"
                    : $"{(int)ago.TotalDays} days ago";
                PipelineStatusText = $"All search modes ready  —  last scan {agoText}";
            }
            else
                PipelineStatusText = "Ready";
        }
        else
            PipelineStatusText = "";
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
        _orchestrator.ProgressChanged -= OnProgressChanged;
        _orchestrator.CycleCompleted -= OnCycleCompleted;
    }
}
