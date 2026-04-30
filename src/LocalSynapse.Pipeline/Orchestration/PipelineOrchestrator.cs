using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using LocalSynapse.Core.Diagnostics;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Pipeline.Interfaces;

namespace LocalSynapse.Pipeline.Orchestration;

/// <summary>
/// Pipeline orchestrator. Runs as App-level singleton.
/// Manages scan → index → embed with 10-minute auto-cycle.
/// ViewModels observe via ProgressChanged/CycleCompleted events.
/// </summary>
public sealed class PipelineOrchestrator : IPipelineOrchestrator
{
    private readonly IFileScanner _fileScanner;
    private readonly IContentExtractor _contentExtractor;
    private readonly ITextChunker _textChunker;
    private readonly IEmbeddingService _embeddingService;
    private readonly IModelInstaller _modelInstaller;
    private readonly IFileRepository _fileRepo;
    private readonly IChunkRepository _chunkRepo;
    private readonly IEmbeddingRepository _embeddingRepo;
    private readonly IPipelineStampRepository _stampRepo;
    private readonly ISettingsStore _settingsStore;
    private string _activePerformanceMode;

    private volatile bool _isPaused;
    private readonly ManualResetEventSlim _immediateRunSignal = new(false);
    private readonly SemaphoreSlim _cycleLock = new(1, 1);
    private DateTime _lastProgressReport = DateTime.MinValue;
    private static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(500);

    private const int BatchSize = 500;
    private static readonly TimeSpan AutoRunInterval = TimeSpan.FromMinutes(10);

    private const bool SkipEmbeddingPhase = false;

    public PipelinePhase CurrentPhase { get; private set; } = PipelinePhase.Idle;
    public bool IsRunning => CurrentPhase != PipelinePhase.Idle
                          && CurrentPhase != PipelinePhase.Complete
                          && CurrentPhase != PipelinePhase.Paused;
    public bool IsPaused => _isPaused;
    public PipelineProgress LatestProgress { get; private set; } = new();
    public string? LastError { get; private set; }

    /// <summary>Fired on every progress update (any thread — observers must dispatch to UI).</summary>
    public event Action<PipelineProgress>? ProgressChanged;

    /// <summary>Fired when cycle completes. null = success, string = error.</summary>
    public event Action<string?>? CycleCompleted;

    public PipelineOrchestrator(
        IFileScanner fileScanner,
        IContentExtractor contentExtractor,
        ITextChunker textChunker,
        IEmbeddingService embeddingService,
        IModelInstaller modelInstaller,
        IFileRepository fileRepo,
        IChunkRepository chunkRepo,
        IEmbeddingRepository embeddingRepo,
        IPipelineStampRepository stampRepo,
        ISettingsStore settingsStore)
    {
        _fileScanner = fileScanner;
        _contentExtractor = contentExtractor;
        _textChunker = textChunker;
        _embeddingService = embeddingService;
        _modelInstaller = modelInstaller;
        _fileRepo = fileRepo;
        _chunkRepo = chunkRepo;
        _embeddingRepo = embeddingRepo;
        _stampRepo = stampRepo;
        _settingsStore = settingsStore;
        _activePerformanceMode = settingsStore.GetPerformanceMode();
        ApplyProcessPriority(_activePerformanceMode);
    }

    /// <summary>Run one full cycle: scan → index → embed.</summary>
    public async Task RunCycleAsync(CancellationToken ct = default)
    {
        if (_isPaused) return;

        // Prevent concurrent cycles
        if (!await _cycleLock.WaitAsync(0, ct))
        {
            Debug.WriteLine("[Orch] Cycle already running, skipping");
            return;
        }

        try
        {
            LastError = null;
            Debug.WriteLine("[Orch] === Cycle started ===");
            var cycleSw = Stopwatch.StartNew();
            SpeedDiagLog.Log("CYCLE_START");

            // Phase 1: Scan (fast — just discover files)
            var scanSw = Stopwatch.StartNew();
            await RunScanPhaseAsync(ct);
            SpeedDiagLog.Log("PHASE_SCAN", "time_ms", scanSw.ElapsedMilliseconds);
            if (_isPaused || ct.IsCancellationRequested) return;

            // Phase 2: Index (slow — parse + chunk, runs in background)
            var indexSw = Stopwatch.StartNew();
            await RunIndexingPhaseAsync(ct);
            SpeedDiagLog.Log("PHASE_INDEX", "time_ms", indexSw.ElapsedMilliseconds);
            if (_isPaused || ct.IsCancellationRequested) return;

            // Performance mode — apply if changed
            var requestedMode = _settingsStore.GetPerformanceMode();
            if (requestedMode != _activePerformanceMode)
            {
                ApplyProcessPriority(requestedMode);
                if (_embeddingService.IsReady)
                {
                    Debug.WriteLine($"[Orch] Performance mode changed: {_activePerformanceMode} → {requestedMode}");
                    await _embeddingService.ReloadSessionWithModeAsync(requestedMode, ct);
                }
                _activePerformanceMode = requestedMode;
            }

            // Phase 3: Embed (slow — only if model ready)
            // Auto-initialize embedding model if installed but not loaded
            if (!SkipEmbeddingPhase && !_embeddingService.IsReady)
            {
                var installed = _modelInstaller.IsModelInstalled("bge-m3");
                var modelPath = _modelInstaller.GetModelPath("bge-m3");
                SpeedDiagLog.Log("EMB_AUTO_INIT_CHECK",
                    "is_ready", _embeddingService.IsReady,
                    "is_installed", installed,
                    "model_path", modelPath);

                if (installed)
                {
                    try
                    {
                        await _embeddingService.InitializeAsync("bge-m3", ct);
                        if (_activePerformanceMode != "Cruise")
                            await _embeddingService.ReloadSessionWithModeAsync(_activePerformanceMode, ct);
                        SpeedDiagLog.Log("EMB_AUTO_INIT_OK",
                            "is_ready", _embeddingService.IsReady);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        SpeedDiagLog.Log("EMB_AUTO_INIT_FAIL", "error", ex.Message);
                    }
                }
            }

            if (!SkipEmbeddingPhase && _embeddingService.IsReady)
            {
                var embSw = Stopwatch.StartNew();
                await RunEmbeddingPhaseAsync(ct);
                SpeedDiagLog.Log("PHASE_EMBED", "time_ms", embSw.ElapsedMilliseconds);
            }
            else
            {
                SpeedDiagLog.Log("PHASE_EMBED", "skipped",
                    SkipEmbeddingPhase ? "dense_disabled" : "model_not_ready");
            }

            CurrentPhase = PipelinePhase.Complete;
            _stampRepo.StampAutoRun();
            ReportProgress(PipelinePhase.Complete, 0, statusText: "Cycle complete");
            CycleCompleted?.Invoke(null);

            SpeedDiagLog.Log("CYCLE_COMPLETE", "total_ms", cycleSw.ElapsedMilliseconds);
            Debug.WriteLine("[Orch] === Cycle complete ===");
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[Orch] Cycle cancelled");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Debug.WriteLine($"[Orch] Cycle error: {ex.Message}");
            CycleCompleted?.Invoke(ex.Message);
        }
        finally
        {
            if (CurrentPhase == PipelinePhase.Complete)
            {
                // Complete 유지 — 사이클 정상 종료
            }
            else if (_isPaused)
            {
                CurrentPhase = PipelinePhase.Paused;
            }
            else
            {
                CurrentPhase = PipelinePhase.Idle;
            }
            _cycleLock.Release();
        }
    }

    /// <summary>Start 10-minute auto-run loop. Call once at app startup.</summary>
    public async Task StartAutoRunAsync(CancellationToken ct = default)
    {
        Debug.WriteLine("[Orch] Auto-run started (10min interval)");

        // Recover pipeline_stamps if previous scan was interrupted
        RecoverStampsIfNeeded();

        // First-run detection: defer first cycle until user chooses scan scope
        var stamps = _stampRepo.GetCurrent();
        var isFirstRun = !stamps.ScanComplete && stamps.TotalFiles == 0;

        if (!isFirstRun)
        {
            try { await RunCycleAsync(ct); }
            catch (Exception ex) { Debug.WriteLine($"[Orch] Initial cycle error: {ex.Message}"); }
        }
        else
        {
            Debug.WriteLine("[Orch] First-run: deferring initial cycle until user chooses scan scope");
            SpeedDiagLog.Log("FIRST_RUN_DEFER", "reason", "awaiting scan scope choice");
        }

        while (!ct.IsCancellationRequested)
        {
            // Wait for timer OR immediate signal
            var signaled = _immediateRunSignal.Wait(AutoRunInterval, ct);
            if (signaled) _immediateRunSignal.Reset();

            if (_isPaused || ct.IsCancellationRequested) continue;

            try
            {
                await RunCycleAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Orch] Auto-run cycle error: {ex.Message}");
            }
        }
    }

    /// <summary>Request an immediate cycle (non-blocking).</summary>
    public void RequestImmediateCycle()
    {
        Debug.WriteLine("[Orch] Immediate cycle requested");
        _immediateRunSignal.Set();
    }

    /// <summary>Pause pipeline.</summary>
    public void Pause()
    {
        _isPaused = true;
        CurrentPhase = PipelinePhase.Paused;
        ReportProgress(PipelinePhase.Paused, 0, statusText: "Paused");
        Debug.WriteLine("[Orch] Paused");
    }

    /// <summary>Resume pipeline.</summary>
    public void Resume()
    {
        _isPaused = false;
        CurrentPhase = PipelinePhase.Idle;
        _immediateRunSignal.Set();
        ReportProgress(PipelinePhase.Idle, 0, statusText: "Resumed");
        Debug.WriteLine("[Orch] Resumed");
    }

    /// <summary>스캔이 완료 전에 앱이 종료되어 stamps가 0인 경우 복구한다.</summary>
    private void RecoverStampsIfNeeded()
    {
        var stamps = _stampRepo.GetCurrent();
        if (stamps.TotalFiles > 0) return; // stamps 정상

        var (files, folders, contentSearchable) = _fileRepo.CountScanStampTotals();
        if (files == 0) return; // DB에도 데이터 없음 — 복구 불필요

        _stampRepo.StampScanComplete(files, folders, contentSearchable);
        Debug.WriteLine($"[Orch] Recovering pipeline_stamps from existing data: {files} files, {folders} folders");
    }

    // ─────────────────────────── Phase 1: Scan ───────────────────────────

    private async Task RunScanPhaseAsync(CancellationToken ct)
    {
        CurrentPhase = PipelinePhase.Scanning;
        ReportProgress(PipelinePhase.Scanning, 0, statusText: "Starting scan...");

        var scanProgress = new ActionProgress<ScanProgress>(sp =>
        {
            ReportProgress(PipelinePhase.Scanning, sp.FilesFound,
                statusText: "Scanning...");
        });

        var scanCoreSw = Stopwatch.StartNew();
        await _fileScanner.ScanAllDrivesAsync(scanProgress, ct);
        SpeedDiagLog.Log("SCAN_CORE", "time_ms", scanCoreSw.ElapsedMilliseconds);

        // Stamp scan results
        var (files, folders, contentSearchable) = _fileRepo.CountScanStampTotals();
        SpeedDiagLog.Log("SCAN_RESULT", "files", files, "folders", folders, "content_searchable", contentSearchable);
        _stampRepo.StampScanComplete(files, folders, contentSearchable);

        ReportProgress(PipelinePhase.Scanning, files,
            statusText: $"Scan complete: {files:N0} files found");

        Debug.WriteLine($"[Orch] Scan complete: {files} files, {folders} folders");
    }

    // ─────────────────────────── Phase 2: Indexing ───────────────────────────

    private async Task RunIndexingPhaseAsync(CancellationToken ct)
    {
        CurrentPhase = PipelinePhase.Indexing;
        ReportProgress(PipelinePhase.Indexing, 0, statusText: "Starting indexing...");

        await Task.Run(async () =>
        {
            var indexedFiles = 0;
            var totalChunks = 0;
            var extractTotalMs = 0L;
            var chunkTotalMs = 0L;
            var upsertTotalMs = 0L;
            var extractCount = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (_isPaused) return;

                var pendingFiles = _fileRepo.GetFilesPendingExtraction(BatchSize).ToList();
                if (pendingFiles.Count == 0) break;

                var totalPending = _fileRepo.CountPendingExtraction() + indexedFiles;

                foreach (var file in pendingFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_isPaused) return;

                    var fileSw = Stopwatch.StartNew();
                    long sizeBytes = -1;
                    try { sizeBytes = new FileInfo(file.Path).Length; }
                    catch (Exception sizeEx) { Debug.WriteLine($"[Orch] Size probe failed {file.Path}: {sizeEx.Message}"); }
                    long extractMs = 0, chunkMs = 0, upsertMs = 0;  // W5: catch 블록에서 실측값 보존

                    try
                    {
                        // Skip cloud files — they are in DB for filename search only
                        if (file.ExtractStatus == ExtractStatuses.Skipped
                            || file.LastExtractErrorCode == "CLOUD_FILE")
                        {
                            Debug.WriteLine($"[Pipeline] Skipping cloud file: {file.Path}");
                            SpeedDiagLog.Log("EXTRACT_FILE",
                                "path", file.Path, "ext", file.Extension, "size_bytes", sizeBytes,
                                "extract_ms", 0, "chunks", 0, "chunk_ms", 0, "upsert_ms", 0,
                                "total_ms", fileSw.ElapsedMilliseconds, "result", "skip_cloud");
                            continue;
                        }

                        var exSw = Stopwatch.StartNew();
                        var result = await _contentExtractor.ExtractAsync(file.Path, file.Extension, ct);
                        extractMs = exSw.ElapsedMilliseconds;
                        extractTotalMs += extractMs;
                        extractCount++;

                        if (!result.Success)
                        {
                            _fileRepo.UpdateExtractStatus(file.Id, ExtractStatuses.Error, result.ErrorCode);
                            SpeedDiagLog.Log("EXTRACT_FILE",
                                "path", file.Path, "ext", file.Extension, "size_bytes", sizeBytes,
                                "extract_ms", extractMs, "chunks", 0, "chunk_ms", 0, "upsert_ms", 0,
                                "total_ms", fileSw.ElapsedMilliseconds,
                                "result", $"error_{result.ErrorCode ?? "PARSE_ERROR"}");
                            continue;
                        }

                        var chSw = Stopwatch.StartNew();
                        var chunks = _textChunker.Chunk(
                            result.Text ?? "",
                            result.SourceType,
                            result.OriginMeta);
                        chunkMs = chSw.ElapsedMilliseconds;
                        chunkTotalMs += chunkMs;

                        if (chunks.Count > 0)
                        {
                            var now = DateTime.UtcNow.ToString("o");
                            var fileChunks = chunks.Select((c, i) => new FileChunk
                            {
                                Id = GenerateChunkId(file.Id, i),
                                FileId = file.Id,
                                ChunkIndex = i,
                                Text = c.Text,
                                SourceType = c.SourceType,
                                OriginMeta = c.OriginMeta,
                                ContentHash = c.ContentHash,
                                CreatedAt = now,
                                StartOffset = c.StartOffset,
                                EndOffset = c.EndOffset,
                            });

                            var upSw = Stopwatch.StartNew();
                            _chunkRepo.UpsertChunks(fileChunks);
                            upsertMs = upSw.ElapsedMilliseconds;
                            upsertTotalMs += upsertMs;
                            totalChunks += chunks.Count;
                        }

                        _fileRepo.UpdateExtractStatus(file.Id, ExtractStatuses.Success);
                        indexedFiles++;

                        SpeedDiagLog.Log("EXTRACT_FILE",
                            "path", file.Path, "ext", file.Extension, "size_bytes", sizeBytes,
                            "extract_ms", extractMs, "chunks", chunks.Count,
                            "chunk_ms", chunkMs, "upsert_ms", upsertMs,
                            "total_ms", fileSw.ElapsedMilliseconds,
                            "result", chunks.Count > 0 ? "success" : "success_empty");

                        if (indexedFiles % 50 == 0)
                        {
                            ReportProgress(PipelinePhase.Indexing, indexedFiles,
                                total: totalPending,
                                statusText: $"Indexing {indexedFiles:N0}/{totalPending:N0} files...");
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Orch] Index error: {file.Path} - {ex.Message}");
                        _fileRepo.UpdateExtractStatus(file.Id, ExtractStatuses.Error, "PARSE_ERROR");
                        SpeedDiagLog.Log("EXTRACT_FILE",
                            "path", file.Path, "ext", file.Extension, "size_bytes", sizeBytes,
                            "extract_ms", extractMs, "chunks", 0,
                            "chunk_ms", chunkMs, "upsert_ms", upsertMs,
                            "total_ms", fileSw.ElapsedMilliseconds, "result", "error_PARSE_ERROR");
                    }
                }

                _stampRepo.UpdateIndexingProgress(
                    _fileRepo.CountIndexedContentSearchableFiles(),
                    _chunkRepo.GetTotalCount());
            }

            _stampRepo.StampIndexingComplete(
                _fileRepo.CountIndexedContentSearchableFiles(),
                _chunkRepo.GetTotalCount());

            ReportProgress(PipelinePhase.Indexing, indexedFiles,
                statusText: $"Indexing complete: {indexedFiles:N0} files, {totalChunks:N0} chunks");

            SpeedDiagLog.Log("INDEX_RESULT",
                "files", indexedFiles,
                "chunks", totalChunks,
                "extract_avg_ms", extractCount > 0 ? extractTotalMs / extractCount : 0,
                "extract_total_ms", extractTotalMs,
                "chunk_total_ms", chunkTotalMs,
                "upsert_total_ms", upsertTotalMs);
            Debug.WriteLine($"[Orch] Indexing complete: {indexedFiles} files, {totalChunks} chunks");
        }, ct);
    }

    // ─────────────────────────── Phase 3: Embedding ───────────────────────────

    private async Task RunEmbeddingPhaseAsync(CancellationToken ct)
    {
        CurrentPhase = PipelinePhase.Embedding;
        var modelId = _embeddingService.ActiveModelId ?? "bge-m3";
        var embeddedCount = 0;

        var totalEmbeddable = _chunkRepo.GetTotalCount();
        _stampRepo.UpdateEmbeddableChunks(totalEmbeddable);

        ReportProgress(PipelinePhase.Embedding, 0, total: totalEmbeddable,
            statusText: "Starting embedding...");
        SpeedDiagLog.Log("EMB_PHASE_START", "total_embeddable", totalEmbeddable, "model", modelId);

        var errorCount = 0;
        await foreach (var chunk in _embeddingRepo.EnumerateChunksMissingEmbeddingAsync(modelId, BatchSize, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (_isPaused) return;

            try
            {
                var sw = Stopwatch.StartNew();
                var vector = await _embeddingService.GenerateEmbeddingAsync(chunk.Text, ct);
                var inferMs = sw.ElapsedMilliseconds;
                await _embeddingRepo.UpsertEmbeddingAsync(chunk.FileId, chunk.ChunkIndex, modelId, vector, ct);
                embeddedCount++;

                if (embeddedCount <= 3 || embeddedCount % 100 == 0)
                {
                    var currentCount = await _embeddingRepo.GetEmbeddingCountAsync(modelId, ct);
                    _stampRepo.UpdateEmbeddingProgress(currentCount);

                    SpeedDiagLog.Log("EMB_PROGRESS",
                        "count", currentCount, "total", totalEmbeddable,
                        "last_infer_ms", inferMs, "text_len", chunk.Text.Length);

                    ReportProgress(PipelinePhase.Embedding, currentCount,
                        total: totalEmbeddable,
                        statusText: $"Embedding {currentCount:N0}/{totalEmbeddable:N0} chunks...");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                errorCount++;
                if (errorCount <= 5)
                    SpeedDiagLog.Log("EMB_CHUNK_ERROR", "chunk", chunk.ChunkId,
                        "text_len", chunk.Text.Length, "error", ex.Message);
                Debug.WriteLine($"[Orch] Embedding error: chunk {chunk.ChunkId} - {ex.Message}");
            }
        }

        var finalCount = await _embeddingRepo.GetEmbeddingCountAsync(modelId, ct);
        _stampRepo.StampEmbeddingComplete(totalEmbeddable, finalCount);

        ReportProgress(PipelinePhase.Embedding, finalCount, total: totalEmbeddable,
            statusText: $"Embedding complete: {finalCount:N0} chunks");

        Debug.WriteLine($"[Orch] Embedding complete: {embeddedCount} new embeddings");
    }

    // ─────────────────────────── Helpers ───────────────────────────

    private void ReportProgress(PipelinePhase phase, int current,
        int total = 0, string? currentFile = null, string? statusText = null)
    {
        var progress = new PipelineProgress
        {
            Phase = phase,
            Current = current,
            Total = total,
            CurrentFile = currentFile,
            StatusText = statusText,
        };

        // Throttle: always fire on phase change, throttle count-only updates
        var now = DateTime.UtcNow;
        bool phaseChanged = LatestProgress.Phase != phase;

        LatestProgress = progress;  // always update internal state

        if (!phaseChanged && (now - _lastProgressReport) < ProgressThrottle)
            return;  // skip event firing, internal state still updated

        _lastProgressReport = now;
        ProgressChanged?.Invoke(progress);
    }

    private static string GenerateChunkId(string fileId, int chunkIndex)
    {
        var input = $"{fileId}:{chunkIndex}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    private void ApplyProcessPriority(string mode)
    {
        try
        {
            var priority = mode == "Stealth"
                ? System.Diagnostics.ProcessPriorityClass.BelowNormal
                : System.Diagnostics.ProcessPriorityClass.Normal;
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass = priority;
            Debug.WriteLine($"[Orch] Process priority set to {priority} for mode {mode}");
        }
        catch (Exception ex)
        {
            // macOS: raising priority from BelowNormal to Normal may fail without root
            Debug.WriteLine($"[Orch] Failed to set process priority: {ex.Message}");
        }
    }
}

/// <summary>
/// IProgress that invokes callback directly without SynchronizationContext.
/// Safe to create on any thread.
/// </summary>
internal sealed class ActionProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    public ActionProgress(Action<T> handler) => _handler = handler;
    public void Report(T value) => _handler(value);
}
