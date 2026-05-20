using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Pipeline.Interfaces;
using LocalSynapse.Search;
using LocalSynapse.Search.Interfaces;
using LocalSynapse.Search.Services;
using LocalSynapse.UI.Services;
using LocalSynapse.UI.Services.Localization;

namespace LocalSynapse.UI.ViewModels;

/// <summary>Empty state types.</summary>
public enum EmptyStateType { None, Initial, NoResults, FilteredEmpty }

/// <summary>Badge color category.</summary>
public enum NoteColor
{
    Version, Format, ContentMatch, Title,
    TimeRecent, TimeOld, Opened, Frequent, Location
}

/// <summary>Smart Notes badge with pre-localized text and color-coded brush.</summary>
public record SmartNote(string Text, NoteColor Color)
{
    /// <summary>Badge background brush.</summary>
    public Avalonia.Media.SolidColorBrush BackgroundBrush => Color switch
    {
        NoteColor.Version      => new(Avalonia.Media.Color.Parse("#DBEAFE")),
        NoteColor.Format       => new(Avalonia.Media.Color.Parse("#FEE2E2")),
        NoteColor.ContentMatch => new(Avalonia.Media.Color.Parse("#E0E7FF")),
        NoteColor.Title        => new(Avalonia.Media.Color.Parse("#FCE7F3")),
        NoteColor.TimeRecent   => new(Avalonia.Media.Color.Parse("#D1FAE5")),
        NoteColor.TimeOld      => new(Avalonia.Media.Color.Parse("#F1F5F9")),
        NoteColor.Opened       => new(Avalonia.Media.Color.Parse("#FEF3C7")),
        NoteColor.Frequent     => new(Avalonia.Media.Color.Parse("#FFF7ED")),
        NoteColor.Location     => new(Avalonia.Media.Color.Parse("#F3E8FF")),
        _ => new(Avalonia.Media.Colors.Transparent)
    };

    /// <summary>Badge foreground brush.</summary>
    public Avalonia.Media.SolidColorBrush ForegroundBrush => Color switch
    {
        NoteColor.Version      => new(Avalonia.Media.Color.Parse("#1E40AF")),
        NoteColor.Format       => new(Avalonia.Media.Color.Parse("#991B1B")),
        NoteColor.ContentMatch => new(Avalonia.Media.Color.Parse("#4338CA")),
        NoteColor.Title        => new(Avalonia.Media.Color.Parse("#9D174D")),
        NoteColor.TimeRecent   => new(Avalonia.Media.Color.Parse("#065F46")),
        NoteColor.TimeOld      => new(Avalonia.Media.Color.Parse("#475569")),
        NoteColor.Opened       => new(Avalonia.Media.Color.Parse("#92400E")),
        NoteColor.Frequent     => new(Avalonia.Media.Color.Parse("#9A3412")),
        NoteColor.Location     => new(Avalonia.Media.Color.Parse("#6B21A8")),
        _ => new(Avalonia.Media.Colors.Black)
    };
}

/// <summary>Folder item in search results. MetaText is pre-localized at creation time.</summary>
public sealed class SearchResultFolder
{
    public required string Path { get; set; }
    public required string Name { get; set; }
    public required string ParentPath { get; set; }
    public int FileCount { get; set; }
    public string SubText { get; set; } = "";
    public double Score { get; set; }
    public string LastModified { get; set; } = "";

    /// <summary>Pre-localized meta text — set at creation time by SearchViewModel using ILocalizationService.</summary>
    public string MetaText { get; set; } = "";
}

/// <summary>Filter option token-display pair. Token is language-invariant for SelectedValue binding.</summary>
public sealed record FilterOption(string Token, string DisplayText);

/// <summary>File item in search results.</summary>
public sealed class SearchResultFile
{
    public required string FileId { get; set; }
    public required string Path { get; set; }
    public required string Filename { get; set; }
    public required string Extension { get; set; }
    public string FolderPath { get; set; } = "";
    public string ModifiedAt { get; set; } = "";
    public string Snippet { get; set; } = "";
    public string Concepts { get; set; } = "";
    public double Score { get; set; }
    public MatchSource Source { get; set; }
    public List<SmartNote> SmartNotes { get; set; } = [];

    /// <summary>Extension abbreviation for file icon.</summary>
    public string ExtensionAbbrev => Extension.TrimStart('.').ToUpperInvariant() switch
    {
        "DOCX" or "DOC" => "DOC",
        "XLSX" or "XLS" or "CSV" => "XLS",
        "PPTX" or "PPT" => "PPT",
        "PDF" => "PDF",
        "HWP" or "HWPX" => "HWP",
        "TXT" or "MD" or "LOG" => "TXT",
        "EML" or "MSG" => "EML",
        var e => e.Length > 4 ? e[..4] : e
    };
}

/// <summary>
/// Search page ViewModel with 4-section layout, 19-type Smart Notes badges,
/// bilingual ko/en support, and "show more" interactive expand/collapse.
/// </summary>
public partial class SearchViewModel : ObservableObject, IDisposable
{
    private readonly IHybridSearch _hybridSearch;
    private readonly IBm25Search _bm25Search;
    private readonly Bm25SearchService _bm25Concrete;
    private readonly ISnippetExtractor _snippetExtractor;
    private readonly IPipelineStampRepository _stampRepo;
    private readonly IFileRepository _fileRepo;
    private readonly IChunkRepository _chunkRepo;
    private readonly SearchClickService _clickService;
    private readonly TelemetryCounterService _telemetry;
    private readonly IDocumentFamilyService _familyService;
    private readonly ILocalizationService _loc;
    private readonly IModelInstaller _modelInstaller;
    private readonly ISettingsStore _settings;
    private System.Threading.Timer? _bannerTimer;

    // Search-as-you-type debounce
    private System.Threading.Timer? _debounceTimer;
    private string _activeSearchQuery = "";
    private CancellationTokenSource? _searchCts;
    private const int DebounceMs = 250;

    // ── i18n ──
    private bool IsKorean => _loc.Current == "ko";

    /// <summary>Section 1 label.</summary>
    public string SectionLabelFilename => _loc[StringKeys.Search.Section.Filename];
    /// <summary>Section 2 label.</summary>
    public string SectionLabelOpened => _loc[StringKeys.Search.Section.Opened];
    /// <summary>Section 3 label.</summary>
    public string SectionLabelContent => _loc[StringKeys.Search.Section.Content];
    /// <summary>Section 4 label.</summary>
    public string SectionLabelFolders => _loc[StringKeys.Search.Section.Folders];

    // ── Bindable properties ──
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _searchTime = "";
    [ObservableProperty] private PipelineStamps _stamps = new();

    // ── Banner state ──
    [ObservableProperty] private string _bannerText = "";
    [ObservableProperty] private bool _showBanner;
    [ObservableProperty] private bool _bannerHasAction;
    [ObservableProperty] private IBrush? _bannerBackground;

    // ── Search mode badge ──
    [ObservableProperty] private bool _showModeBadge;
    [ObservableProperty] private string _modeBadgeText = "";
    [ObservableProperty] private IBrush? _modeBadgeBackground;

    // ── Search mode selector (v2.11.0) ──
    [ObservableProperty] private bool _isFastSelected;
    [ObservableProperty] private bool _isSmartSelected;
    [ObservableProperty] private bool _isSmartEnabled = true;
    [ObservableProperty] private string _smartDisabledHint = "";
    [ObservableProperty] private bool _showSmartFallbackBanner;
    [ObservableProperty] private string _smartFallbackBannerText = "";
    private bool _suppressModeChange;
    // Tracks which strategy actually produced the on-screen results. Compared
    // against the persisted mode by the periodic availability check to detect
    // a silent flip (Smart→Fast) that must trigger a re-issue. Initialized
    // from settings in the constructor; updated by ExecuteSearchAsync after
    // each search completes.
    private string _lastAppliedSearchMode = "smart";
    private const double SmartModeMinimumPercentage = 0.80;

    // Filter — string tokens are the source of truth; *Option properties wrap for ComboBox SelectedItem binding
    [ObservableProperty] private string _activeTypeFilter = "All";
    [ObservableProperty] private string _activeDateFilter = "All";

    /// <summary>ComboBox-bindable FilterOption for type filter. Language-aware DisplayText.</summary>
    public FilterOption ActiveTypeFilterOption
    {
        get => TypeFilterOptions.FirstOrDefault(o => o.Token == ActiveTypeFilter) ?? TypeFilterOptions[0];
        set
        {
            if (value != null && value.Token != ActiveTypeFilter)
                ActiveTypeFilter = value.Token;
        }
    }

    /// <summary>ComboBox-bindable FilterOption for date filter.</summary>
    public FilterOption ActiveDateFilterOption
    {
        get => DateFilterOptions.FirstOrDefault(o => o.Token == ActiveDateFilter) ?? DateFilterOptions[0];
        set
        {
            if (value != null && value.Token != ActiveDateFilter)
                ActiveDateFilter = value.Token;
        }
    }

    // ── Selected item + detail panel ──
    [ObservableProperty] private object? _selectedItem;
    [ObservableProperty] private bool _isFileSelected;
    [ObservableProperty] private bool _isFolderSelected;
    [ObservableProperty] private string _detailName = "";
    [ObservableProperty] private string _detailPath = "";
    [ObservableProperty] private string _detailSnippet = "";
    [ObservableProperty] private string _detailModified = "";
    [ObservableProperty] private string _detailSize = "";
    [ObservableProperty] private string _detailType = "";
    [ObservableProperty] private string _detailScore = "";
    [ObservableProperty] private string _detailExtension = "";
    [ObservableProperty] private int _detailFileCount;
    [ObservableProperty] private bool _hasSearched;
    [ObservableProperty] private EmptyStateType _emptyState = EmptyStateType.Initial;
    [ObservableProperty] private bool _isEmptyNoResults;
    [ObservableProperty] private bool _isEmptyFilteredEmpty;

    // ── 4-Section collections ──
    public ObservableCollection<SearchResultFile> FilenameMatchFiles { get; } = [];
    public ObservableCollection<SearchResultFile> PreviouslyOpenedFiles { get; } = [];
    public ObservableCollection<SearchResultFile> ContentMatchFiles { get; } = [];
    public ObservableCollection<SearchResultFolder> RelatedFolders { get; } = [];

    // ── 4-Section counts & visibility ──
    [ObservableProperty] private int _filenameMatchCount;
    [ObservableProperty] private bool _showFilenameMatch;
    [ObservableProperty] private int _previouslyOpenedCount;
    [ObservableProperty] private bool _showPreviouslyOpened;
    [ObservableProperty] private int _contentMatchCount;
    [ObservableProperty] private bool _showContentMatch;
    [ObservableProperty] private int _relatedFolderCount;
    [ObservableProperty] private bool _showRelatedFolders;
    [ObservableProperty] private int _totalResultCount;

    // ── 섹션 접기/펼치기 ──
    [ObservableProperty] private bool _isFilenameExpanded = true;
    [ObservableProperty] private bool _isPreviouslyOpenedExpanded = true;
    [ObservableProperty] private bool _isContentExpanded = true;
    [ObservableProperty] private bool _isRelatedFoldersExpanded = true;

    // ── "더 보기" Filename ──
    [ObservableProperty] private bool _isFilenameShowingAll;
    [ObservableProperty] private int _moreFilenameCount;
    [ObservableProperty] private string _moreFilenameText = "";
    [ObservableProperty] private bool _showMoreFilename;

    // ── "더 보기" Content ──
    [ObservableProperty] private bool _isContentShowingAll;
    [ObservableProperty] private int _moreContentCount;
    [ObservableProperty] private string _moreContentText = "";
    [ObservableProperty] private bool _showMoreContentVisible;

    // ── "더 보기" Related Folders ──
    [ObservableProperty] private bool _isFoldersShowingAll;
    [ObservableProperty] private int _moreFoldersCount;
    [ObservableProperty] private string _moreFoldersText = "";
    [ObservableProperty] private bool _showMoreFolders;

    /// <summary>Platform search shortcut text.</summary>
    public string SearchShortcutText => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "⌘K" : "Ctrl+K";

    /// <summary>Platform indexed summary text (localized).</summary>
    public string IndexedSummaryText => RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        ? _loc[StringKeys.Search.IndexedSummary.Mac]
        : _loc[StringKeys.Search.IndexedSummary.Desktop];

    /// <summary>Files in selected folder (detail panel).</summary>
    public ObservableCollection<SearchResultFile> DetailFolderFiles { get; } = [];

    /// <summary>Available type filter options. Token is language-invariant; DisplayText is localized.</summary>
    public FilterOption[] TypeFilterOptions => new[]
    {
        new FilterOption("All", _loc[StringKeys.Common.All]),
        new FilterOption("DOCX", "DOCX"),
        new FilterOption("XLSX", "XLSX"),
        new FilterOption("PDF", "PDF"),
        new FilterOption("PPTX", "PPTX"),
        new FilterOption("HWP", "HWP"),
        new FilterOption("TXT", "TXT"),
    };

    /// <summary>Available date filter options. Token is language-invariant; DisplayText is localized.</summary>
    public FilterOption[] DateFilterOptions => new[]
    {
        new FilterOption("All", _loc[StringKeys.Common.All]),
        new FilterOption("30 days", _loc[StringKeys.Filter.Date30Days]),
        new FilterOption("90 days", _loc[StringKeys.Filter.Date90Days]),
        new FilterOption("This year", _loc[StringKeys.Filter.DateThisYear]),
    };

    // ── Internal result storage (pre-filter) ──
    private List<SearchResultFile> _allFilenameFiles = [];
    private List<SearchResultFile> _allPreviouslyOpenedFiles = [];
    private List<SearchResultFile> _allContentFiles = [];
    private List<SearchResultFolder> _allRelatedFolders = [];

    // ── Cache for language-change rebuild ──
    private SearchResponse? _lastResponse;
    private IReadOnlyList<Bm25Hit>? _lastQuickHits;
    private string _lastQuery = "";

    /// <summary>SearchViewModel constructor.</summary>
    public SearchViewModel(
        IHybridSearch hybridSearch,
        IBm25Search bm25Search,
        Bm25SearchService bm25Concrete,
        ISnippetExtractor snippetExtractor,
        IPipelineStampRepository stampRepo,
        IFileRepository fileRepo,
        IChunkRepository chunkRepo,
        SearchClickService clickService,
        IDocumentFamilyService familyService,
        ILocalizationService loc,
        IModelInstaller modelInstaller,
        TelemetryCounterService telemetry,
        ISettingsStore settings)
    {
        _telemetry = telemetry;
        _hybridSearch = hybridSearch;
        _bm25Search = bm25Search;
        _bm25Concrete = bm25Concrete;
        _snippetExtractor = snippetExtractor;
        _stampRepo = stampRepo;
        _fileRepo = fileRepo;
        _chunkRepo = chunkRepo;
        _clickService = clickService;
        _familyService = familyService;
        _loc = loc;
        Stamps = _stampRepo.GetCurrent();
        _loc.LanguageChanged += OnLanguageChanged;
        _modelInstaller = modelInstaller;
        _settings = settings;

        // Initialize mode selection from persisted setting (default: smart).
        _suppressModeChange = true;
        var persistedMode = _settings.GetSearchMode();
        IsSmartSelected = persistedMode == "smart";
        IsFastSelected = persistedMode == "fast";
        if (!IsSmartSelected && !IsFastSelected)
        {
            IsSmartSelected = true; // fallback to default
        }
        _suppressModeChange = false;
        // Seed the silent-flip detector with the persisted mode. Must happen
        // after _settings is set and before the banner timer starts, otherwise
        // the first timer tick can fire a phantom re-issue.
        _lastAppliedSearchMode = persistedMode == "fast" ? "fast" : "smart";

        _bannerTimer = new System.Threading.Timer(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshBannerState),
            null, 1000, 3000);
    }

    private static IBrush GetBrush(string key)
    {
        Avalonia.Application.Current!.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var res);
        return (IBrush)res!;
    }

    private void RefreshBannerState()
    {
        Stamps = _stampRepo.GetCurrent();
        UpdateSmartModeAvailability();

        if (!Stamps.ScanComplete)
        {
            BannerText = $"Scanning files... ({Stamps.TotalFiles:N0} found)";
            BannerBackground = GetBrush("AccentLightBrush");
            BannerHasAction = false;
            ShowBanner = true;
        }
        else if (!Stamps.IndexingComplete)
        {
            BannerText = $"Indexing documents... {Stamps.IndexingPercent:F0}%";
            BannerBackground = GetBrush("AccentLightBrush");
            BannerHasAction = false;
            ShowBanner = true;
        }
        else if (!_modelInstaller.IsModelInstalled("bge-m3"))
        {
            BannerText = "Download AI model for semantic search";
            BannerBackground = GetBrush("WarningLightBrush");
            BannerHasAction = true;
            ShowBanner = true;
        }
        else if (!Stamps.EmbeddingComplete)
        {
            BannerText = $"Building semantic index... {Stamps.EmbeddingPercent:F0}%";
            BannerBackground = GetBrush("AccentLightBrush");
            BannerHasAction = false;
            ShowBanner = true;
        }
        else
        {
            ShowBanner = false;
        }
    }

    /// <summary>Navigate to DataSetup page (for model download).</summary>
    [RelayCommand]
    private void GoToDataSetup()
        => WeakReferenceMessenger.Default.Send(new NavigateMessage(PageType.DataSetup));

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Fire PropertyChanged for all localized computed properties
        OnPropertyChanged(nameof(SectionLabelFilename));
        OnPropertyChanged(nameof(SectionLabelOpened));
        OnPropertyChanged(nameof(SectionLabelContent));
        OnPropertyChanged(nameof(SectionLabelFolders));
        OnPropertyChanged(nameof(IndexedSummaryText));
        OnPropertyChanged(nameof(TypeFilterOptions));
        OnPropertyChanged(nameof(DateFilterOptions));
        OnPropertyChanged(nameof(ActiveTypeFilterOption));
        OnPropertyChanged(nameof(ActiveDateFilterOption));

        // Rebuild SmartNote + MetaText from cached response (collection regeneration strategy).
        // SmartNote and MetaText are pre-localized at creation — reconstruct with current _loc.
        if (_lastResponse != null && _lastQuickHits != null)
        {
            try
            {
                CategorizeIntoSections(_lastResponse, _lastQuickHits);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchVM] Rebuild on language change failed: {ex.Message}");
            }
        }
        // Always re-apply filter to refresh "더 보기/Show less" text
        ApplyTypeAndDateFilter();
    }

    /// <summary>
    /// Recomputes Smart-mode availability from the current pipeline stamp.
    /// Polled every 3 seconds by the existing banner timer. Handles two
    /// transitions: (a) coverage crosses below 80% mid-session while user has
    /// Smart persisted → auto-fall back to Fast with a banner; (b) coverage
    /// crosses back above 80% → re-enable the Smart radio without auto-switching.
    /// </summary>
    private void UpdateSmartModeAvailability()
    {
        var stamp = Stamps;
        double coverage = stamp.EmbeddableChunks > 0
            ? (double)stamp.EmbeddedChunks / stamp.EmbeddableChunks
            : 0.0;
        var available = coverage >= SmartModeMinimumPercentage;
        IsSmartEnabled = available;
        var percent = (int)Math.Round(coverage * 100);
        SmartDisabledHint = _loc.Format(StringKeys.SearchMode.SmartDisabled, percent);

        // Auto-fallback: persisted Smart but coverage dropped below threshold.
        if (!available && _settings.GetSearchMode() == "smart")
        {
            // Don't recursively trigger HandleModeChangeAsync — flip the selection
            // and persist directly. The banner explains the state to the user.
            _suppressModeChange = true;
            IsFastSelected = true;
            IsSmartSelected = false;
            _suppressModeChange = false;
            _settings.SetSearchMode("fast");
            SmartFallbackBannerText = _loc[StringKeys.SearchMode.SmartFallbackBanner];
            ShowSmartFallbackBanner = true;

            // Edge-triggered re-issue: only on the first tick after the silent
            // flip (when _lastAppliedSearchMode is still "smart"). The
            // ModeChange source bypasses the same-query guard in
            // ExecuteSearchAsync. Subsequent ticks see _lastAppliedSearchMode
            // already "fast" and skip — no re-issue loop. The re-search itself
            // updates _lastAppliedSearchMode from the response's actual Mode.
            if (_lastAppliedSearchMode != "fast"
                && HasSearched
                && !string.IsNullOrWhiteSpace(Query)
                && _activeSearchQuery == Query.Trim())
            {
                _lastAppliedSearchMode = "fast";
                _ = ExecuteSearchAsync(Query, SearchTriggerSource.ModeChange);
            }
            else
            {
                _lastAppliedSearchMode = "fast";
            }
        }
        else if (available && ShowSmartFallbackBanner)
        {
            // Coverage recovered — clear the banner but leave the selection alone.
            ShowSmartFallbackBanner = false;
        }
    }

    // ── Mode selector (v2.11.0) ──
    //
    // Two RadioButtons bind to IsFastSelected / IsSmartSelected. The partial
    // change methods drive the mode-change logic, with a suppress flag to
    // prevent re-entrance when the inverse property is updated.

    partial void OnIsFastSelectedChanged(bool value)
    {
        if (_suppressModeChange) return;
        if (!value) return; // unchecking is a side-effect of the other one being checked
        _suppressModeChange = true;
        IsSmartSelected = false;
        _suppressModeChange = false;
        _ = HandleModeChangeAsync("fast");
    }

    partial void OnIsSmartSelectedChanged(bool value)
    {
        if (_suppressModeChange) return;
        if (!value) return;
        _suppressModeChange = true;
        IsFastSelected = false;
        _suppressModeChange = false;
        _ = HandleModeChangeAsync("smart");
    }

    private async Task HandleModeChangeAsync(string newMode)
    {
        // Sequence: cancel any in-flight search BEFORE persisting the mode, then
        // persist, then re-issue. Cancel-before-persist prevents the old-mode
        // search from rendering its results under the new-mode badge.
        if (!string.IsNullOrWhiteSpace(Query))
        {
            try { _searchCts?.Cancel(); } catch (ObjectDisposedException) { }
        }
        _settings.SetSearchMode(newMode);

        if (!string.IsNullOrWhiteSpace(Query))
        {
            await ExecuteSearchAsync(Query, SearchTriggerSource.ModeChange);
        }
    }

    // ── Search-as-you-type ──
    partial void OnQueryChanged(string value)
    {
        _debounceTimer?.Dispose();

        if (string.IsNullOrWhiteSpace(value))
        {
            if (HasSearched)
            {
                HasSearched = false;
                EmptyState = EmptyStateType.Initial;
                ClearAllSections();
                SelectedItem = null;
            }
            return;
        }

        var pending = value.Trim();
        _debounceTimer = new System.Threading.Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _ = ExecuteSearchAsync(pending, SearchTriggerSource.Debounce);
            });
        }, null, DebounceMs, Timeout.Infinite);
    }

    /// <summary>Run search triggered by Enter key in the search box.</summary>
    [RelayCommand]
    private async Task SearchFromEnter()
    {
        _debounceTimer?.Dispose();
        await ExecuteSearchAsync(Query, SearchTriggerSource.EnterKey);
    }

    /// <summary>Run search triggered by the search button or an example query chip.</summary>
    [RelayCommand]
    private async Task SearchFromButton()
    {
        _debounceTimer?.Dispose();
        await ExecuteSearchAsync(Query, SearchTriggerSource.SearchButton);
    }

    private async Task ExecuteSearchAsync(string query, SearchTriggerSource source)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        var trimmed = query.Trim();
        // Mode changes must always re-issue, even for the same query — different
        // strategy dispatches produce different results. Without this carve-out,
        // toggling mode after a completed search would silently keep the prior
        // mode's results under the new badge.
        if (trimmed == _activeSearchQuery && HasSearched && source != SearchTriggerSource.ModeChange)
            return;

        // Race-safe CTS replacement. A newer search cancels any older in-flight
        // search and renders alone. Without this, a slow first search would
        // block the second via a boolean guard and the user's later query would
        // be silently dropped — the symptom reported pre-v2.11.0 where pressing
        // Enter quickly after typing produced only QuickSearch results.
        var fresh = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _searchCts, fresh);
        try { oldCts?.Cancel(); } catch (ObjectDisposedException) { }
        oldCts?.Dispose();
        var ct = fresh.Token;

        _clickService.OnNewSearch(trimmed);

        IsSearching = true;
        HasSearched = true;
        _activeSearchQuery = trimmed;

        try
        {
            var sw = Stopwatch.StartNew();

            var hybridSw = Stopwatch.StartNew();
            var response = await _hybridSearch.SearchAsync(trimmed, new SearchOptions { TopK = 200 }, ct);
            if (ct.IsCancellationRequested) return;
            var hybridMs = hybridSw.ElapsedMilliseconds;

            var quickSw = Stopwatch.StartNew();
            var quickHits = _bm25Search.QuickSearch(trimmed, 200);
            if (ct.IsCancellationRequested) return;
            var quickMs = quickSw.ElapsedMilliseconds;

            var catSw = Stopwatch.StartNew();
            CategorizeIntoSections(response, quickHits);
            ApplyTypeAndDateFilter();
            var catMs = catSw.ElapsedMilliseconds;

            // Search mode badge — localized label of the strategy that actually ran.
            var mode = response.Mode;
            ShowModeBadge = true;
            ModeBadgeText = mode == SearchMode.Smart
                ? _loc[StringKeys.SearchMode.SmartLabel]
                : _loc[StringKeys.SearchMode.FastLabel];
            ModeBadgeBackground = GetBrush(mode == SearchMode.Smart ? "SuccessLightBrush" : "BgMutedBrush");

            // Cache for language-change rebuild
            _lastResponse = response;
            _lastQuickHits = quickHits;
            _lastQuery = trimmed;

            sw.Stop();
            SearchTime = $"{sw.Elapsed.TotalSeconds:F1}s";

            // Telemetry counter hook
            _telemetry.RecordSearch(
                response.Mode.ToString(),
                (int)sw.ElapsedMilliseconds,
                response.Items.Count);

            LocalSynapse.Core.Diagnostics.SpeedDiagLog.Log("SEARCH_UI",
                "query", trimmed,
                "source", source.ToString(),
                "hybrid_ms", hybridMs,
                "quick_ms", quickMs,
                "categorize_ms", catMs,
                "total_ms", sw.ElapsedMilliseconds,
                "hybrid_count", response.Items.Count,
                "quick_count", quickHits.Count);

            // Steady-state record: which strategy produced the on-screen results.
            // The silent-flip detector in UpdateSmartModeAvailability compares
            // against this on each timer tick.
            _lastAppliedSearchMode = response.Mode == SearchMode.Smart ? "smart" : "fast";

            Stamps = _stampRepo.GetCurrent();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // expected — newer search has superseded this one
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SearchVM] Search error: {ex.Message}");
        }
        finally
        {
            // Only clear the searching indicator if we are still the active CTS.
            // If we were superseded, the newer search already set IsSearching=true.
            if (!ct.IsCancellationRequested)
                IsSearching = false;
        }
    }

    partial void OnEmptyStateChanged(EmptyStateType value)
    {
        IsEmptyNoResults = value == EmptyStateType.NoResults;
        IsEmptyFilteredEmpty = value == EmptyStateType.FilteredEmpty;
    }

    partial void OnActiveTypeFilterChanged(string value)
    {
        if (value == null) { ActiveTypeFilter = "All"; return; }
        OnPropertyChanged(nameof(ActiveTypeFilterOption));
        ApplyTypeAndDateFilter();
    }
    partial void OnActiveDateFilterChanged(string value)
    {
        if (value == null) { ActiveDateFilter = "All"; return; }
        OnPropertyChanged(nameof(ActiveDateFilterOption));
        ApplyTypeAndDateFilter();
    }
    partial void OnSelectedItemChanged(object? value) => UpdateDetailPanel(value);

    /// <summary>Open file with default program.</summary>
    [RelayCommand]
    private void OpenFile()
    {
        if (SelectedItem is SearchResultFile file && File.Exists(file.Path))
        {
            var position = FindFilePosition(file);
            if (position >= 0 && !string.IsNullOrWhiteSpace(_activeSearchQuery))
            {
                _clickService.RecordClick(_activeSearchQuery, file.Path, position);
                if (position == 0)
                    _telemetry.RecordTopResultClick();
            }
            PlatformHelper.OpenFile(file.Path);
        }
    }

    /// <summary>Open folder in Explorer with file selected.</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        string? target = SelectedItem switch
        {
            SearchResultFile f => f.Path,
            SearchResultFolder f => f.Path,
            _ => null,
        };
        if (target != null) PlatformHelper.RevealInFileManager(target);
    }

    /// <summary>Open path in Explorer.</summary>
    [RelayCommand]
    private void OpenDetailPath()
    {
        if (string.IsNullOrEmpty(DetailPath)) return;
        var dir = File.Exists(DetailPath)
            ? System.IO.Path.GetDirectoryName(DetailPath) ?? DetailPath
            : DetailPath;
        if (Directory.Exists(dir))
            PlatformHelper.OpenFolder(dir);
    }

    [RelayCommand] private void ToggleFilename() => IsFilenameExpanded = !IsFilenameExpanded;
    [RelayCommand] private void TogglePreviouslyOpened() => IsPreviouslyOpenedExpanded = !IsPreviouslyOpenedExpanded;
    [RelayCommand] private void ToggleContent() => IsContentExpanded = !IsContentExpanded;
    [RelayCommand] private void ToggleRelatedFolders() => IsRelatedFoldersExpanded = !IsRelatedFoldersExpanded;

    [RelayCommand] private void ClearFilters() { ActiveTypeFilter = "All"; ActiveDateFilter = "All"; }

    /// <summary>"더 보기" Filename section.</summary>
    [RelayCommand]
    private void ToggleMoreFilename() { IsFilenameShowingAll = !IsFilenameShowingAll; ApplyTypeAndDateFilter(); }

    /// <summary>"더 보기" Content section.</summary>
    [RelayCommand]
    private void ToggleMoreContent() { IsContentShowingAll = !IsContentShowingAll; ApplyTypeAndDateFilter(); }

    /// <summary>"더 보기" Related Folders section.</summary>
    [RelayCommand]
    private void ToggleMoreFolders() { IsFoldersShowingAll = !IsFoldersShowingAll; ApplyTypeAndDateFilter(); }

    // ─────────────────────────── Categorization ───────────────────────────

    private void CategorizeIntoSections(SearchResponse response, IReadOnlyList<Bm25Hit> quickHits)
    {
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var hybridFiles = response.Items.Select(HybridToFile).ToList();
        var quickFiles = quickHits
            .Where(h => !h.IsDirectory)
            .Select(h => new SearchResultFile
            {
                FileId = h.FileId, Path = h.Path, Filename = h.Filename,
                Extension = h.Extension, FolderPath = h.FolderPath ?? "",
                ModifiedAt = FormatDate(h.ModifiedAt), Score = h.Score,
                Source = h.MatchSource,
            })
            .ToList();

        foreach (var qf in quickFiles)
        {
            if (qf.Score <= 1.0)
                qf.Score = ComputeRecencyBoost(qf.ModifiedAt);
        }

        // Section 1: Filename Match (max 20)
        var filenameMatches = new List<SearchResultFile>();
        foreach (var f in hybridFiles.Where(h => h.Source.HasFlag(MatchSource.FileName))
                                      .OrderByDescending(h => h.Score))
        {
            if (placed.Add(f.FileId) && filenameMatches.Count < 20)
                filenameMatches.Add(f);
        }
        foreach (var f in quickFiles.OrderByDescending(f => f.Score))
        {
            if (placed.Add(f.FileId) && filenameMatches.Count < 20)
                filenameMatches.Add(f);
        }

        // Section 2: Previously Opened (max 5)
        var candidatePaths = hybridFiles.Concat(quickFiles)
            .Where(f => !placed.Contains(f.FileId))
            .Select(f => f.Path).ToList();
        var recentlyOpened = _clickService.GetRecentlyOpenedPaths(candidatePaths, 5);
        var previouslyOpenedList = new List<SearchResultFile>();
        foreach (var f in hybridFiles.Concat(quickFiles)
            .Where(f => !placed.Contains(f.FileId) && recentlyOpened.ContainsKey(NormalizePath(f.Path)))
            .OrderByDescending(f =>
            {
                recentlyOpened.TryGetValue(NormalizePath(f.Path), out var info);
                return info.lastOpened;
            }))
        {
            if (placed.Add(f.FileId) && previouslyOpenedList.Count < 5)
                previouslyOpenedList.Add(f);
        }

        // Section 3: Found in Content (threshold, max 20)
        var contentMatches = hybridFiles
            .Where(f => !placed.Contains(f.FileId) && f.Source.HasFlag(MatchSource.Content))
            .OrderByDescending(f => f.Score)
            .ToList();
        var threshold = 0.0;
        if (contentMatches.Count >= 3)
            threshold = contentMatches.Take(3).Average(f => f.Score) * 0.25;
        var visibleContent = contentMatches.Where(f => f.Score >= threshold).Take(50).ToList();
        foreach (var f in visibleContent) placed.Add(f.FileId);

        // Section 4: Related Folders (max 10)
        var allPlacedFiles = filenameMatches.Concat(previouslyOpenedList).Concat(visibleContent).ToList();
        var folders = GroupIntoFolders(allPlacedFiles);
        var folderMatchFiles = hybridFiles.Where(f => f.Source.HasFlag(MatchSource.Folder)).ToList();
        var folderGroups = GroupIntoFolders(folderMatchFiles);
        var relatedFolders = folders.Concat(folderGroups)
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(f => f.FileCount).First())
            .OrderByDescending(f => f.FileCount)
            .Take(10).ToList();

        // Family map (version detection)
        var visibleFileIds = new HashSet<string>(
            allPlacedFiles.Select(f => f.FileId), StringComparer.OrdinalIgnoreCase);
        var familyHits = response.Items
            .Where(h => visibleFileIds.Contains(h.FileId)).ToList();
        var families = _familyService.GroupResults(familyHits);
        var fileFamilyMap = new Dictionary<string, (int versionCount, bool isLatest, int position)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var family in families)
        {
            if (family.Files.Count <= 1) continue;
            for (int i = 0; i < family.Files.Count; i++)
                fileFamilyMap[family.Files[i].FileId] = (family.Files.Count, i == 0, i + 1);
        }

        // Chunk counts (content match detection)
        var chunkCounts = new Dictionary<string, (int matchCount, int firstMatchIndex)>();
        if (visibleContent.Count > 0)
        {
            try
            {
                var ftsQuery = NaturalQueryParser.ToFts5Query(_activeSearchQuery);
                if (!string.IsNullOrEmpty(ftsQuery))
                    chunkCounts = _bm25Concrete.GetMatchChunkCounts(ftsQuery,
                        visibleContent.Select(f => f.FileId).ToList());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchVM] GetMatchChunkCounts error: {ex.Message}");
            }
        }

        // Smart Notes (19 badge types)
        GenerateSmartNotes(allPlacedFiles, recentlyOpened, fileFamilyMap, chunkCounts);

        // Store (pre-filter)
        _allFilenameFiles = filenameMatches;
        _allPreviouslyOpenedFiles = previouslyOpenedList;
        _allContentFiles = visibleContent;
        _allRelatedFolders = relatedFolders;
    }

    private List<SearchResultFolder> GroupIntoFolders(List<SearchResultFile> files)
    {
        return files
            .Where(f => !string.IsNullOrEmpty(f.FolderPath))
            .GroupBy(f => f.FolderPath, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var folderName = System.IO.Path.GetFileName(g.Key) ?? g.Key;
                var parentPath = System.IO.Path.GetDirectoryName(g.Key) ?? "";
                var fileCount = g.Count();
                var lastMod = g.Max(f => f.ModifiedAt) ?? "";
                var metaText = string.IsNullOrEmpty(lastMod)
                    ? _loc.Format(StringKeys.Folder.FileCount, fileCount)
                    : _loc.Format(StringKeys.Folder.FileCountWithDate, fileCount, lastMod);
                return new SearchResultFolder
                {
                    Path = g.Key,
                    Name = folderName,
                    ParentPath = parentPath,
                    FileCount = fileCount,
                    SubText = _loc.Format(StringKeys.Folder.FileCount, fileCount),
                    Score = g.Max(f => f.Score),
                    LastModified = lastMod,
                    MetaText = metaText,
                };
            })
            .OrderByDescending(f => f.Score).Take(50).ToList();
    }

    private static SearchResultFile HybridToFile(HybridHit hit) => new()
    {
        FileId = hit.FileId, Path = hit.Path, Filename = hit.Filename,
        Extension = hit.Extension, FolderPath = hit.FolderPath ?? "",
        ModifiedAt = FormatDate(hit.ModifiedAt), Score = hit.HybridScore,
        Source = hit.MatchSource, Snippet = hit.MatchSnippet ?? "",
    };

    // ─────────────────────────── Smart Notes (19 badges) ───────────────────────────

    private void GenerateSmartNotes(
        List<SearchResultFile> allVisibleFiles,
        Dictionary<string, (DateTime lastOpened, int totalClicks)> recentlyOpened,
        Dictionary<string, (int versionCount, bool isLatest, int position)> fileFamilyMap,
        Dictionary<string, (int matchCount, int firstMatchIndex)> chunkCounts)
    {
        if (allVisibleFiles.Count == 0) return;

        var formatSiblings = DetectFormatSiblings(allVisibleFiles);
        var folderCounts = allVisibleFiles
            .GroupBy(f => f.FolderPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var file in allVisibleFiles)
        {
            var candidates = new List<(int priority, SmartNote note)>();

            // Category B: Format (P1)
            if (formatSiblings.TryGetValue(file.FileId, out var fmt))
                candidates.Add((1, fmt));

            // Category A: Version (P2, P10, P11)
            if (fileFamilyMap.TryGetValue(file.FileId, out var family))
            {
                if (family.isLatest)
                    candidates.Add((2, new SmartNote(
                        _loc.Format(StringKeys.SmartNote.LatestOfVersions, family.versionCount),
                        NoteColor.Version)));
                else
                {
                    candidates.Add((2, new SmartNote(_loc[StringKeys.SmartNote.NotLatest], NoteColor.Version)));
                    candidates.Add((11, new SmartNote(
                        _loc.Format(StringKeys.SmartNote.NthOfVersions, family.position, family.versionCount),
                        NoteColor.Version)));
                }
            }
            if (IsCopyPattern(file.Filename))
                candidates.Add((10, new SmartNote(_loc[StringKeys.SmartNote.Copy], NoteColor.Version)));

            // Category E: History (P3, P4)
            if (recentlyOpened.TryGetValue(NormalizePath(file.Path), out var opened))
            {
                if (opened.totalClicks >= 5)
                    candidates.Add((3, new SmartNote(
                        _loc.Format(StringKeys.SmartNote.FrequentOpened, opened.totalClicks),
                        NoteColor.Frequent)));

                var daysAgo = (int)(DateTime.UtcNow - opened.lastOpened).TotalDays;
                if (daysAgo < 1)
                    candidates.Add((4, new SmartNote(_loc[StringKeys.SmartNote.OpenedToday], NoteColor.Opened)));
                else if (daysAgo < 7)
                    candidates.Add((4, new SmartNote(_loc.Format(StringKeys.SmartNote.OpenedDaysAgo, daysAgo), NoteColor.Opened)));
                else if (daysAgo < 14)
                    candidates.Add((4, new SmartNote(_loc[StringKeys.SmartNote.OpenedLastWeek], NoteColor.Opened)));
                else if (daysAgo < 60)
                    candidates.Add((4, new SmartNote(_loc[StringKeys.SmartNote.OpenedLastMonth], NoteColor.Opened)));
            }

            // Category C: Content Match (P5, P6)
            if (chunkCounts.TryGetValue(file.FileId, out var chunks))
            {
                if (chunks.firstMatchIndex == 0)
                    candidates.Add((5, new SmartNote(_loc[StringKeys.SmartNote.FoundInTitle], NoteColor.Title)));
                else if (chunks.firstMatchIndex <= 2)
                    candidates.Add((5, new SmartNote(_loc[StringKeys.SmartNote.FoundInFirstPage], NoteColor.Title)));

                if (chunks.matchCount >= 2)
                    candidates.Add((6, new SmartNote(
                        _loc.Format(StringKeys.SmartNote.FoundInPlaces, chunks.matchCount),
                        NoteColor.ContentMatch)));
            }

            // Category D: Time (P7, P9, P10)
            if (DateTime.TryParse(file.ModifiedAt, out var modDate))
            {
                var daysSince = (DateTime.UtcNow - modDate).TotalDays;
                if (daysSince < 1)
                    candidates.Add((7, new SmartNote(_loc[StringKeys.SmartNote.ModifiedToday], NoteColor.TimeRecent)));
                else if (daysSince < 7)
                    candidates.Add((7, new SmartNote(_loc[StringKeys.SmartNote.ModifiedThisWeek], NoteColor.TimeRecent)));
                else if (daysSince < 30)
                    candidates.Add((9, new SmartNote(_loc[StringKeys.SmartNote.ModifiedThisMonth], NoteColor.TimeRecent)));
                else if (daysSince > 730)
                    candidates.Add((10, new SmartNote(_loc[StringKeys.SmartNote.NotModified2Years], NoteColor.TimeOld)));
            }

            // Category F: Location (P8)
            if (folderCounts.TryGetValue(file.FolderPath, out var folderCount) && folderCount >= 3)
                candidates.Add((8, new SmartNote(
                    _loc.Format(StringKeys.SmartNote.RelatedFilesInFolder, folderCount),
                    NoteColor.Location)));

            file.SmartNotes = candidates
                .OrderBy(c => c.priority)
                .Take(3)
                .Select(c => c.note)
                .ToList();
        }
    }

    /// <summary>Detect PDF/PPT/DOCX siblings in same folder with similar filename.</summary>
    private Dictionary<string, SmartNote> DetectFormatSiblings(List<SearchResultFile> files)
    {
        var result = new Dictionary<string, SmartNote>(StringComparer.OrdinalIgnoreCase);
        var groups = files
            .GroupBy(f => (
                folder: f.FolderPath.ToLowerInvariant(),
                stem: System.IO.Path.GetFileNameWithoutExtension(f.Filename).ToLowerInvariant()
            ))
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var exts = group.Select(f => f.Extension.ToLowerInvariant()).ToHashSet();
            bool hasPdf = exts.Contains(".pdf");
            bool hasPptOrDocx = exts.Any(e => e is ".pptx" or ".ppt" or ".docx" or ".doc");

            if (hasPdf && hasPptOrDocx)
            {
                foreach (var f in group)
                {
                    if (f.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                        result[f.FileId] = new SmartNote(_loc[StringKeys.SmartNote.PdfFinalVersion], NoteColor.Format);
                    else if (f.Extension.ToLowerInvariant() is ".pptx" or ".ppt" or ".docx" or ".doc")
                        result[f.FileId] = new SmartNote(_loc[StringKeys.SmartNote.HasPdfVersion], NoteColor.Format);
                }
            }
        }
        return result;
    }

    private static bool IsCopyPattern(string filename)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(filename);
        return Regex.IsMatch(name, @"\(\d+\)$|복사본|[\s\-]copy$", RegexOptions.IgnoreCase);
    }

    // ─────────────────────────── Filter + Show More ───────────────────────────

    private void ApplyTypeAndDateFilter()
    {
        var fnFiles = FilterFiles(_allFilenameFiles);
        var poFiles = FilterFiles(_allPreviouslyOpenedFiles);
        var ctFiles = FilterFiles(_allContentFiles);

        // "더 보기" — Filename
        var fnVisible = IsFilenameShowingAll ? fnFiles : fnFiles.Take(5).ToList();
        MoreFilenameCount = Math.Max(0, fnFiles.Count - 5);
        ShowMoreFilename = (MoreFilenameCount > 0 || IsFilenameShowingAll) && fnFiles.Count > 0;
        MoreFilenameText = IsFilenameShowingAll
            ? _loc[StringKeys.Common.Less]
            : _loc.Format(StringKeys.Common.More, MoreFilenameCount);

        // "더 보기" — Content
        var ctVisible = IsContentShowingAll ? ctFiles : ctFiles.Take(5).ToList();
        MoreContentCount = Math.Max(0, ctFiles.Count - 5);
        ShowMoreContentVisible = (MoreContentCount > 0 || IsContentShowingAll) && ctFiles.Count > 0;
        MoreContentText = IsContentShowingAll
            ? _loc[StringKeys.Common.Less]
            : _loc.Format(StringKeys.Common.More, MoreContentCount);

        // "더 보기" — Related Folders
        var fldrVisible = IsFoldersShowingAll
            ? _allRelatedFolders.Take(10).ToList()
            : _allRelatedFolders.Take(5).ToList();
        MoreFoldersCount = Math.Max(0, Math.Min(_allRelatedFolders.Count, 10) - 5);
        ShowMoreFolders = (MoreFoldersCount > 0 || IsFoldersShowingAll) && fldrVisible.Count > 0;
        MoreFoldersText = IsFoldersShowingAll
            ? _loc[StringKeys.Common.Less]
            : _loc.Format(StringKeys.Common.More, MoreFoldersCount);

        UpdateCollection(FilenameMatchFiles, fnVisible);
        UpdateCollection(PreviouslyOpenedFiles, poFiles);
        UpdateCollection(ContentMatchFiles, ctVisible);
        UpdateCollection(RelatedFolders, fldrVisible);

        // Counts & visibility (total, not visible slice)
        FilenameMatchCount = fnFiles.Count;
        ShowFilenameMatch = fnFiles.Count > 0;
        PreviouslyOpenedCount = poFiles.Count;
        ShowPreviouslyOpened = poFiles.Count > 0;
        ContentMatchCount = ctFiles.Count;
        ShowContentMatch = ctFiles.Count > 0;
        RelatedFolderCount = _allRelatedFolders.Count;
        ShowRelatedFolders = _allRelatedFolders.Count > 0;
        TotalResultCount = fnFiles.Count + poFiles.Count + ctFiles.Count;

        if (TotalResultCount == 0 && _allRelatedFolders.Count == 0)
        {
            bool hasFilters = ActiveTypeFilter != "All" || ActiveDateFilter != "All";
            EmptyState = hasFilters ? EmptyStateType.FilteredEmpty : EmptyStateType.NoResults;
        }
        else
        {
            EmptyState = EmptyStateType.None;
        }

        SelectedItem = null;
    }

    private List<SearchResultFile> FilterFiles(List<SearchResultFile> files)
    {
        var result = files.AsEnumerable();
        var typeFilter = ActiveTypeFilter ?? "All";
        var dateFilter = ActiveDateFilter ?? "All";

        if (typeFilter != "All")
        {
            var extFilter = typeFilter.ToLowerInvariant() switch
            {
                "docx" => new[] { ".docx", ".doc" },
                "xlsx" => new[] { ".xlsx", ".xls", ".csv" },
                "pdf" => new[] { ".pdf" },
                "pptx" => new[] { ".pptx", ".ppt" },
                "hwp" => new[] { ".hwp", ".hwpx" },
                "txt" => new[] { ".txt", ".md", ".log" },
                _ => Array.Empty<string>(),
            };
            if (extFilter.Length > 0)
                result = result.Where(f => extFilter.Contains(f.Extension.ToLowerInvariant()));
        }

        if (dateFilter != "All")
        {
            var cutoff = dateFilter switch
            {
                "30 days" => DateTime.UtcNow.AddDays(-30),
                "90 days" => DateTime.UtcNow.AddDays(-90),
                "This year" => new DateTime(DateTime.UtcNow.Year, 1, 1),
                _ => DateTime.MinValue,
            };
            if (cutoff > DateTime.MinValue)
                result = result.Where(f => DateTime.TryParse(f.ModifiedAt, out var dt) && dt >= cutoff);
        }

        return result.ToList();
    }

    private static void UpdateCollection<T>(ObservableCollection<T> collection, List<T> items)
    {
        collection.Clear();
        foreach (var item in items) collection.Add(item);
    }

    private void ClearAllSections()
    {
        FilenameMatchFiles.Clear();
        PreviouslyOpenedFiles.Clear();
        ContentMatchFiles.Clear();
        RelatedFolders.Clear();
        ShowFilenameMatch = false;
        ShowPreviouslyOpened = false;
        ShowContentMatch = false;
        ShowRelatedFolders = false;
        TotalResultCount = 0;
    }

    // ─────────────────────────── Detail panel ───────────────────────────

    private void UpdateDetailPanel(object? item)
    {
        DetailFolderFiles.Clear();

        if (item is SearchResultFile file)
        {
            IsFileSelected = true;
            IsFolderSelected = false;
            DetailName = file.Filename;
            DetailPath = file.Path;
            DetailExtension = file.Extension;
            DetailModified = file.ModifiedAt;
            DetailScore = file.Score.ToString("F1");
            DetailType = file.Extension.TrimStart('.').ToUpperInvariant();

            if (!string.IsNullOrEmpty(file.Snippet))
            {
                DetailSnippet = file.Snippet;
            }
            else
            {
                try
                {
                    var chunks = _chunkRepo.GetChunksForFile(file.FileId).ToList();
                    if (chunks.Count > 0 && !string.IsNullOrEmpty(Query))
                    {
                        var allText = string.Join("\n", chunks.Select(c => c.Text));
                        DetailSnippet = _snippetExtractor.Extract(
                            allText, Query.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                    }
                    else if (chunks.Count == 0)
                        DetailSnippet = _loc[StringKeys.Search.Detail.NotIndexed];
                    else
                        DetailSnippet = chunks[0].Text.Length > 500
                            ? chunks[0].Text[..500] + "..." : chunks[0].Text;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SearchVM] Snippet error: {ex.Message}");
                    DetailSnippet = _loc[StringKeys.Search.Detail.PreviewError];
                }
            }

            try
            {
                DetailSize = File.Exists(file.Path)
                    ? FormatFileSize(new FileInfo(file.Path).Length) : "—";
            }
            catch (Exception ex) { Debug.WriteLine($"[SearchVM] Failed to get file size: {ex.Message}"); DetailSize = "—"; }
        }
        else if (item is SearchResultFolder folder)
        {
            IsFileSelected = false;
            IsFolderSelected = true;
            DetailName = folder.Name;
            DetailPath = folder.Path;
            DetailExtension = "";
            DetailSnippet = "";
            DetailFileCount = folder.FileCount;

            var allFiles = FilenameMatchFiles
                .Concat(PreviouslyOpenedFiles)
                .Concat(ContentMatchFiles);
            foreach (var f in allFiles.Where(
                f => f.FolderPath.Equals(folder.Path, StringComparison.OrdinalIgnoreCase)))
                DetailFolderFiles.Add(f);
        }
        else
        {
            IsFileSelected = false;
            IsFolderSelected = false;
            DetailName = "";
            DetailPath = "";
        }
    }

    // ─────────────────────────── Helpers ───────────────────────────

    private int FindFilePosition(SearchResultFile file)
    {
        var idx = IndexInCollection(FilenameMatchFiles, file);
        if (idx >= 0) return idx;

        idx = IndexInCollection(PreviouslyOpenedFiles, file);
        if (idx >= 0) return FilenameMatchFiles.Count + idx;

        idx = IndexInCollection(ContentMatchFiles, file);
        if (idx >= 0) return FilenameMatchFiles.Count + PreviouslyOpenedFiles.Count + idx;

        return -1;
    }

    private static int IndexInCollection(ObservableCollection<SearchResultFile> collection, SearchResultFile file)
    {
        for (int i = 0; i < collection.Count; i++)
        {
            if (collection[i].FileId.Equals(file.FileId, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static string NormalizePath(string path)
        => path.ToLowerInvariant().TrimEnd('\\', '/');

    private static double ComputeRecencyBoost(string modifiedAt)
    {
        if (!DateTime.TryParse(modifiedAt, out var date)) return 1.0;
        var days = (DateTime.UtcNow - date).TotalDays;
        var boost = 1.0 / (1.0 + days / 365.0);
        return Math.Max(0.3, boost);
    }

    private static string FormatDate(string? isoDate)
    {
        if (string.IsNullOrEmpty(isoDate)) return "";
        return DateTime.TryParse(isoDate, out var dt) ? dt.ToString("yyyy-MM-dd") : isoDate;
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
    };

    /// <summary>Dispose timers to prevent leaks on minimize-to-tray or shutdown.</summary>
    public void Dispose()
    {
        _bannerTimer?.Dispose();
        _debounceTimer?.Dispose();
        try { _searchCts?.Cancel(); } catch (ObjectDisposedException) { }
        _searchCts?.Dispose();
    }
}
