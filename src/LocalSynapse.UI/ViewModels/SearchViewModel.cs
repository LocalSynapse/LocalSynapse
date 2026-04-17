using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Search;
using LocalSynapse.Search.Interfaces;
using LocalSynapse.Search.Services;
using LocalSynapse.UI.Services;

namespace LocalSynapse.UI.ViewModels;

/// <summary>Empty state types.</summary>
public enum EmptyStateType { None, Initial, NoResults, FilteredEmpty }

/// <summary>Folder item in search results.</summary>
public sealed class SearchResultFolder
{
    public required string Path { get; set; }
    public required string Name { get; set; }
    public required string ParentPath { get; set; }
    public int FileCount { get; set; }
    public string SubText { get; set; } = "";
    public double Score { get; set; }
}

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
    public string Note { get; set; } = "";
}

/// <summary>
/// Search page ViewModel with 4-section layout: Filename Match, Previously Opened,
/// Found in Content, Related Folders. Smart Notes on each result.
/// </summary>
public partial class SearchViewModel : ObservableObject
{
    private readonly IHybridSearch _hybridSearch;
    private readonly IBm25Search _bm25Search;
    private readonly Bm25SearchService _bm25Concrete;
    private readonly ISnippetExtractor _snippetExtractor;
    private readonly IPipelineStampRepository _stampRepo;
    private readonly IFileRepository _fileRepo;
    private readonly IChunkRepository _chunkRepo;
    private readonly SearchClickService _clickService;
    private readonly IDocumentFamilyService _familyService;

    // Search-as-you-type debounce
    private System.Threading.Timer? _debounceTimer;
    private string _activeSearchQuery = "";
    private int _searchInFlight; // 0 or 1 (Interlocked). H1 (M0-H): 재진입 방지
    private int _searchVersion;  // Enter가 debounce 결과를 무효화하는 version counter
    private const int DebounceMs = 250;

    // ── Bindable properties ──
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _searchTime = "";
    [ObservableProperty] private PipelineStamps _stamps = new();

    // Filter (Sort 제거 — 섹션별 고정 정렬)
    [ObservableProperty] private string _activeTypeFilter = "All";
    [ObservableProperty] private string _activeDateFilter = "All";

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
    /// <summary>섹션 1: 파일명 일치.</summary>
    public ObservableCollection<SearchResultFile> FilenameMatchFiles { get; } = [];
    /// <summary>섹션 2: 이전에 열어본 파일.</summary>
    public ObservableCollection<SearchResultFile> PreviouslyOpenedFiles { get; } = [];
    /// <summary>섹션 3: 본문 일치.</summary>
    public ObservableCollection<SearchResultFile> ContentMatchFiles { get; } = [];
    /// <summary>섹션 4: 관련 폴더.</summary>
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

    // Content 섹션 threshold 접기
    [ObservableProperty] private int _hiddenContentCount;
    [ObservableProperty] private bool _showMoreContent;

    /// <summary>플랫폼별 검색 단축키 텍스트.</summary>
    public string SearchShortcutText => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "⌘K" : "Ctrl+K";

    /// <summary>플랫폼별 인덱싱 상태 텍스트.</summary>
    public string IndexedSummaryText => RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        ? "files indexed"
        : "files indexed across all drives";

    /// <summary>Files in selected folder (detail panel).</summary>
    public ObservableCollection<SearchResultFile> DetailFolderFiles { get; } = [];

    /// <summary>Available type filter options.</summary>
    public string[] TypeFilterOptions { get; } = ["All", "DOCX", "XLSX", "PDF", "PPTX", "HWP", "TXT"];

    /// <summary>Available date filter options.</summary>
    public string[] DateFilterOptions { get; } = ["All", "30 days", "90 days", "This year"];

    // ── Internal result storage (pre-filter) ──
    private List<SearchResultFile> _allFilenameFiles = [];
    private List<SearchResultFile> _allPreviouslyOpenedFiles = [];
    private List<SearchResultFile> _allContentFiles = [];
    private List<SearchResultFolder> _allRelatedFolders = [];
    private int _allHiddenContentCount;

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
        IDocumentFamilyService familyService)
    {
        _hybridSearch = hybridSearch;
        _bm25Search = bm25Search;
        _bm25Concrete = bm25Concrete;
        _snippetExtractor = snippetExtractor;
        _stampRepo = stampRepo;
        _fileRepo = fileRepo;
        _chunkRepo = chunkRepo;
        _clickService = clickService;
        _familyService = familyService;
        Stamps = _stampRepo.GetCurrent();
    }

    // ── Search-as-you-type: debounce on Query change ──
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

        var version = _searchVersion;
        _debounceTimer = new System.Threading.Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_searchVersion == version)
                    _ = ExecuteSearchAsync();
            });
        }, null, DebounceMs, Timeout.Infinite);
    }

    /// <summary>Full search execution (also callable via Enter/button).</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        _debounceTimer?.Dispose();
        Interlocked.Increment(ref _searchVersion);
        await ExecuteSearchAsync();
    }

    private async Task ExecuteSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Query)) return;
        var trimmed = Query.Trim();
        if (trimmed == _activeSearchQuery && HasSearched) return;

        if (Interlocked.CompareExchange(ref _searchInFlight, 1, 0) != 0) return;

        _clickService.OnNewSearch(trimmed);

        IsSearching = true;
        HasSearched = true;
        _activeSearchQuery = trimmed;

        try
        {
            var sw = Stopwatch.StartNew();

            var hybridSw = Stopwatch.StartNew();
            var response = await _hybridSearch.SearchAsync(_activeSearchQuery, new SearchOptions { TopK = 200 });
            var hybridMs = hybridSw.ElapsedMilliseconds;

            var quickSw = Stopwatch.StartNew();
            var quickHits = _bm25Search.QuickSearch(_activeSearchQuery, 200);
            var quickMs = quickSw.ElapsedMilliseconds;

            var catSw = Stopwatch.StartNew();
            CategorizeIntoSections(response, quickHits);
            ApplyTypeAndDateFilter();
            var catMs = catSw.ElapsedMilliseconds;

            sw.Stop();
            SearchTime = $"{sw.Elapsed.TotalSeconds:F1}s";

            LocalSynapse.Core.Diagnostics.SpeedDiagLog.Log("SEARCH_UI",
                "query", _activeSearchQuery,
                "hybrid_ms", hybridMs,
                "quick_ms", quickMs,
                "categorize_ms", catMs,
                "total_ms", sw.ElapsedMilliseconds,
                "hybrid_count", response.Items.Count,
                "quick_count", quickHits.Count);

            Stamps = _stampRepo.GetCurrent();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SearchVM] Search error: {ex.Message}");
        }
        finally
        {
            IsSearching = false;
            Interlocked.Exchange(ref _searchInFlight, 0);
        }
    }

    partial void OnEmptyStateChanged(EmptyStateType value)
    {
        IsEmptyNoResults = value == EmptyStateType.NoResults;
        IsEmptyFilteredEmpty = value == EmptyStateType.FilteredEmpty;
    }

    partial void OnActiveTypeFilterChanged(string value) => ApplyTypeAndDateFilter();
    partial void OnActiveDateFilterChanged(string value) => ApplyTypeAndDateFilter();
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
        if (target == null) return;

        PlatformHelper.RevealInFileManager(target);
    }

    /// <summary>Open path in Explorer (called when user clicks the path text).</summary>
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

    /// <summary>섹션 1: 파일명 일치 접기/펼치기.</summary>
    [RelayCommand]
    private void ToggleFilename() => IsFilenameExpanded = !IsFilenameExpanded;

    /// <summary>섹션 2: Previously Opened 접기/펼치기.</summary>
    [RelayCommand]
    private void TogglePreviouslyOpened() => IsPreviouslyOpenedExpanded = !IsPreviouslyOpenedExpanded;

    /// <summary>섹션 3: Content 접기/펼치기.</summary>
    [RelayCommand]
    private void ToggleContent() => IsContentExpanded = !IsContentExpanded;

    /// <summary>섹션 4: Related Folders 접기/펼치기.</summary>
    [RelayCommand]
    private void ToggleRelatedFolders() => IsRelatedFoldersExpanded = !IsRelatedFoldersExpanded;

    /// <summary>Clear all type/date filters.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        ActiveTypeFilter = "All";
        ActiveDateFilter = "All";
    }

    // ─────────────────────────── Categorization ───────────────────────────

    private void CategorizeIntoSections(SearchResponse response, IReadOnlyList<Bm25Hit> quickHits)
    {
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. 모든 결과를 SearchResultFile로 변환
        var hybridFiles = response.Items.Select(HybridToFile).ToList();
        var quickFiles = quickHits
            .Where(h => !h.IsDirectory)
            .Select(h => new SearchResultFile
            {
                FileId = h.FileId,
                Path = h.Path,
                Filename = h.Filename,
                Extension = h.Extension,
                FolderPath = h.FolderPath ?? "",
                ModifiedAt = FormatDate(h.ModifiedAt),
                Score = h.Score,
                Source = h.MatchSource,
            })
            .ToList();

        // QuickSearch 정렬 개선: recencyBoost 적용
        foreach (var qf in quickFiles)
        {
            if (qf.Score <= 1.0)
                qf.Score = ComputeRecencyBoost(qf.ModifiedAt);
        }

        // 2. 섹션 1: Filename Match (max 10)
        var filenameMatches = new List<SearchResultFile>();
        foreach (var f in hybridFiles.Where(h => h.Source.HasFlag(MatchSource.FileName))
                                      .OrderByDescending(h => h.Score))
        {
            if (placed.Add(f.FileId) && filenameMatches.Count < 10)
                filenameMatches.Add(f);
        }
        foreach (var f in quickFiles.OrderByDescending(f => f.Score))
        {
            if (placed.Add(f.FileId) && filenameMatches.Count < 10)
                filenameMatches.Add(f);
        }

        // 3. 섹션 2: Previously Opened (max 5)
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

        // 4. 섹션 3: Found in Content (max 20, threshold)
        var contentMatches = hybridFiles
            .Where(f => !placed.Contains(f.FileId) && f.Source.HasFlag(MatchSource.Content))
            .OrderByDescending(f => f.Score)
            .ToList();
        var threshold = 0.0;
        if (contentMatches.Count >= 3)
            threshold = contentMatches.Take(3).Average(f => f.Score) * 0.25;
        var visibleContent = contentMatches.Where(f => f.Score >= threshold).Take(20).ToList();
        var hiddenCount = contentMatches.Count - visibleContent.Count;
        foreach (var f in visibleContent) placed.Add(f.FileId);

        // 5. 섹션 4: Related Folders (max 5)
        var allPlacedFiles = filenameMatches.Concat(previouslyOpenedList).Concat(visibleContent).ToList();
        var folders = GroupIntoFolders(allPlacedFiles);
        var folderMatchFiles = hybridFiles.Where(f => f.Source.HasFlag(MatchSource.Folder)).ToList();
        var folderGroups = GroupIntoFolders(folderMatchFiles);
        var relatedFolders = folders.Concat(folderGroups)
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(f => f.FileCount).First())
            .OrderByDescending(f => f.FileCount)
            .Take(5).ToList();

        // 6. Smart Notes 생성
        GenerateSmartNotes(filenameMatches, previouslyOpenedList, visibleContent,
                           recentlyOpened, response.Items, _activeSearchQuery);

        // 7. 내부 저장소에 저장 (필터 적용 전 원본)
        _allFilenameFiles = filenameMatches;
        _allPreviouslyOpenedFiles = previouslyOpenedList;
        _allContentFiles = visibleContent;
        _allRelatedFolders = relatedFolders;
        _allHiddenContentCount = hiddenCount;
    }

    /// <summary>Group files into folders without name filtering.</summary>
    private static List<SearchResultFolder> GroupIntoFolders(List<SearchResultFile> files)
    {
        return files
            .Where(f => !string.IsNullOrEmpty(f.FolderPath))
            .GroupBy(f => f.FolderPath, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var folderName = System.IO.Path.GetFileName(g.Key) ?? g.Key;
                var parentPath = System.IO.Path.GetDirectoryName(g.Key) ?? "";
                return new SearchResultFolder
                {
                    Path = g.Key,
                    Name = folderName,
                    ParentPath = parentPath,
                    FileCount = g.Count(),
                    SubText = $"{g.Count()} files",
                    Score = g.Max(f => f.Score),
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

    // ─────────────────────────── Smart Notes ───────────────────────────

    private void GenerateSmartNotes(
        List<SearchResultFile> filenameFiles,
        List<SearchResultFile> openedFiles,
        List<SearchResultFile> contentFiles,
        Dictionary<string, (DateTime lastOpened, int totalClicks)> recentlyOpened,
        IReadOnlyList<HybridHit> hybridHits,
        string query)
    {
        var allVisible = filenameFiles.Concat(openedFiles).Concat(contentFiles).ToList();
        if (allVisible.Count == 0) return;

        // 버전 그룹 계산 (DocumentFamilyService)
        var familyHits = hybridHits.Where(h =>
            allVisible.Any(f => f.FileId.Equals(h.FileId, StringComparison.OrdinalIgnoreCase))).ToList();
        var families = _familyService.GroupResults(familyHits);
        // fileId → family 매핑
        var fileFamilyMap = new Dictionary<string, (int versionCount, bool isLatest, int position)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var family in families)
        {
            if (family.Files.Count <= 1) continue;
            for (int i = 0; i < family.Files.Count; i++)
            {
                fileFamilyMap[family.Files[i].FileId] = (
                    family.Files.Count,
                    i == 0, // 첫 번째 = 최고 점수 = 최신
                    i + 1
                );
            }
        }

        // 매치 chunk 수 (Content 섹션만)
        var chunkCounts = new Dictionary<string, (int matchCount, bool titleMatch)>();
        if (contentFiles.Count > 0)
        {
            try
            {
                var ftsQuery = NaturalQueryParser.ToFts5Query(query);
                if (!string.IsNullOrEmpty(ftsQuery))
                    chunkCounts = _bm25Concrete.GetMatchChunkCounts(ftsQuery,
                        contentFiles.Select(f => f.FileId).ToList());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchVM] GetMatchChunkCounts error: {ex.Message}");
            }
        }

        foreach (var file in allVisible)
        {
            var notes = new List<string>(2);

            // 우선순위 1: 버전 정보
            if (fileFamilyMap.TryGetValue(file.FileId, out var family))
            {
                if (family.isLatest)
                    notes.Add($"v{family.versionCount}개 중 최신");
                else if (family.versionCount > 1)
                    notes.Add($"v{family.versionCount}개 중 {family.position}번째");
            }

            // 우선순위 2: 열어본 이력
            if (notes.Count < 2 && recentlyOpened.TryGetValue(NormalizePath(file.Path), out var opened))
            {
                var daysAgo = (int)(DateTime.UtcNow - opened.lastOpened).TotalDays;
                if (daysAgo <= 30)
                {
                    notes.Add(daysAgo == 0 ? "오늘 열어봄" :
                               daysAgo <= 7 ? $"{daysAgo}일 전에 열어봄" :
                               "지난 주에 열어봄");
                }
            }

            // 우선순위 3: 매치 chunk 수
            if (notes.Count < 2 && chunkCounts.TryGetValue(file.FileId, out var chunks))
            {
                if (chunks.titleMatch)
                    notes.Add("제목에서 발견");
                else if (chunks.matchCount > 1)
                    notes.Add($"본문 {chunks.matchCount}곳에서 발견");
            }

            // 우선순위 4: 이번 주 수정
            if (notes.Count < 2 && DateTime.TryParse(file.ModifiedAt, out var modDate)
                && (DateTime.UtcNow - modDate).TotalDays <= 7)
            {
                notes.Add("이번 주 수정됨");
            }

            file.Note = string.Join(" · ", notes);
        }
    }

    // ─────────────────────────── Filter ───────────────────────────

    private void ApplyTypeAndDateFilter()
    {
        var fnFiles = FilterFiles(_allFilenameFiles);
        var poFiles = FilterFiles(_allPreviouslyOpenedFiles);
        var ctFiles = FilterFiles(_allContentFiles);

        // UI 컬렉션 업데이트
        UpdateCollection(FilenameMatchFiles, fnFiles);
        UpdateCollection(PreviouslyOpenedFiles, poFiles);
        UpdateCollection(ContentMatchFiles, ctFiles);

        RelatedFolders.Clear();
        foreach (var f in _allRelatedFolders) RelatedFolders.Add(f);

        // Counts & visibility
        FilenameMatchCount = fnFiles.Count;
        ShowFilenameMatch = fnFiles.Count > 0;
        PreviouslyOpenedCount = poFiles.Count;
        ShowPreviouslyOpened = poFiles.Count > 0;
        ContentMatchCount = ctFiles.Count;
        ShowContentMatch = ctFiles.Count > 0;
        RelatedFolderCount = _allRelatedFolders.Count;
        ShowRelatedFolders = _allRelatedFolders.Count > 0;
        TotalResultCount = fnFiles.Count + poFiles.Count + ctFiles.Count;
        HiddenContentCount = _allHiddenContentCount;
        ShowMoreContent = _allHiddenContentCount > 0;

        // Empty state
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

        if (ActiveTypeFilter != "All")
        {
            var extFilter = ActiveTypeFilter.ToLowerInvariant() switch
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

        if (ActiveDateFilter != "All")
        {
            var cutoff = ActiveDateFilter switch
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
                        DetailSnippet = "File not yet indexed";
                    else
                        DetailSnippet = chunks[0].Text.Length > 500
                            ? chunks[0].Text[..500] + "..." : chunks[0].Text;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SearchVM] Snippet error: {ex.Message}");
                    DetailSnippet = "Unable to load preview";
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

            // 4섹션 전체에서 폴더 매칭
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

    /// <summary>4섹션 전체에서 파일 위치를 탐색한다 (클릭 위치 계산용).</summary>
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
}
