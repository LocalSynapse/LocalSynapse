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
        ModelStatus = "Downloading...";
        try
        {
            await _modelInstaller.DownloadModelAsync("bge-m3");
            ModelStatus = "Installed";
        }
        catch (Exception ex)
        {
            ModelStatus = $"Error: {ex.Message}";
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
        });
    }

    private void OnCycleCompleted(string? error)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (error != null)
            {
                ScanStatusText = $"Error: {error}";
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
        catch { SkippedFiles = 0; }

        // Show last progress text if cycle is running
        if (_orchestrator.IsRunning && _orchestrator.LatestProgress.StatusText != null)
        {
            ScanStatusText = _orchestrator.LatestProgress.StatusText;
            ScanFilesFound = _orchestrator.LatestProgress.Current;
        }
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
        _orchestrator.ProgressChanged -= OnProgressChanged;
        _orchestrator.CycleCompleted -= OnCycleCompleted;
    }
}
