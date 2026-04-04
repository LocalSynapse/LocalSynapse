using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Search;
using LocalSynapse.Search.Interfaces;
using LocalSynapse.Search.Services;
using LocalSynapse.UI.Services;

namespace LocalSynapse.UI.ViewModels;

/// <summary>Search filter tabs.</summary>
public enum SearchFilter { NameMatch, ContentMatch, SemanticMatch }

/// <summary>Sort options.</summary>
public enum SortOption { Relevance, Newest, Oldest, NameAZ }

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
}

/// <summary>
/// Search page ViewModel with 3-tab filtering, search-as-you-type,
/// type/date filters, sort options, and comprehensive empty states.
/// </summary>
public partial class SearchViewModel : ObservableObject
{
    private readonly IHybridSearch _hybridSearch;
    private readonly IBm25Search _bm25Search;
    private readonly ISnippetExtractor _snippetExtractor;
    private readonly IPipelineStampRepository _stampRepo;
    private readonly IFileRepository _fileRepo;
    private readonly IChunkRepository _chunkRepo;
    private readonly SearchClickService _clickService;

    // Search-as-you-type debounce
    private System.Threading.Timer? _debounceTimer;
    private string _activeSearchQuery = "";
    private const int DebounceMs = 150;

    // ── Bindable properties ──
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private SearchFilter _activeFilter = SearchFilter.NameMatch;
    [ObservableProperty] private int _nameMatchCount;
    [ObservableProperty] private int _contentMatchCount;
    [ObservableProperty] private int _semanticMatchCount;
    [ObservableProperty] private string _searchTime = "";
    [ObservableProperty] private PipelineStamps _stamps = new();

    // Filter + sort
    [ObservableProperty] private string _activeTypeFilter = "All";
    [ObservableProperty] private string _activeDateFilter = "All";
    [ObservableProperty] private SortOption _activeSort = SortOption.Relevance;

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
    [ObservableProperty] private bool _isSemanticUnavailable;
    [ObservableProperty] private bool _hasSearched;
    [ObservableProperty] private EmptyStateType _emptyState = EmptyStateType.Initial;
    [ObservableProperty] private bool _isEmptyNoResults;
    [ObservableProperty] private bool _isEmptyFilteredEmpty;

    /// <summary>플랫폼별 검색 단축키 텍스트.</summary>
    public string SearchShortcutText => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "⌘K" : "Ctrl+K";

    /// <summary>플랫폼별 인덱싱 상태 텍스트.</summary>
    public string IndexedSummaryText => RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        ? "files indexed"
        : "files indexed across all drives";

    /// <summary>Visible folders for current filter tab.</summary>
    public ObservableCollection<SearchResultFolder> VisibleFolders { get; } = [];

    /// <summary>Visible files for current filter tab.</summary>
    public ObservableCollection<SearchResultFile> VisibleFiles { get; } = [];

    /// <summary>폴더 섹션 접힘 여부. true = 3개만 보임.</summary>
    [ObservableProperty] private bool _isFolderCollapsed = true;

    /// <summary>화면에 표시할 폴더 목록 (접힌 상태면 최대 3개).</summary>
    public ObservableCollection<SearchResultFolder> DisplayFolders { get; } = [];

    /// <summary>토글 버튼 텍스트.</summary>
    [ObservableProperty] private string _folderToggleText = "";

    /// <summary>토글 버튼 표시 여부 (폴더 4개 이상일 때만).</summary>
    [ObservableProperty] private bool _showFolderToggle;

    /// <summary>Files in selected folder (detail panel).</summary>
    public ObservableCollection<SearchResultFile> DetailFolderFiles { get; } = [];

    /// <summary>Available type filter options.</summary>
    public string[] TypeFilterOptions { get; } = ["All", "DOCX", "XLSX", "PDF", "PPTX", "HWP", "TXT"];

    /// <summary>Available date filter options.</summary>
    public string[] DateFilterOptions { get; } = ["All", "30 days", "90 days", "This year"];

    // ── Internal result storage (one list per tab, pre-filter) ──
    private List<SearchResultFile> _nameFiles = [];
    private List<SearchResultFolder> _nameFolders = [];
    private List<SearchResultFile> _contentFiles = [];
    private List<SearchResultFolder> _contentFolders = [];
    private List<SearchResultFile> _semanticFiles = [];
    private List<SearchResultFolder> _semanticFolders = [];
    // Unfiltered copies for re-filtering without re-search
    private int _unfilteredNameCount;
    private int _unfilteredContentCount;
    private int _unfilteredSemanticCount;

    /// <summary>SearchViewModel constructor.</summary>
    public SearchViewModel(
        IHybridSearch hybridSearch,
        IBm25Search bm25Search,
        ISnippetExtractor snippetExtractor,
        IPipelineStampRepository stampRepo,
        IFileRepository fileRepo,
        IChunkRepository chunkRepo,
        SearchClickService clickService)
    {
        _hybridSearch = hybridSearch;
        _bm25Search = bm25Search;
        _snippetExtractor = snippetExtractor;
        _stampRepo = stampRepo;
        _fileRepo = fileRepo;
        _chunkRepo = chunkRepo;
        _clickService = clickService;
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
                VisibleFolders.Clear();
                VisibleFiles.Clear();
                SelectedItem = null;
            }
            return;
        }

        _debounceTimer = new System.Threading.Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = ExecuteSearchAsync());
        }, null, DebounceMs, Timeout.Infinite);
    }

    /// <summary>Full search execution (also callable via Enter/button).</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        _debounceTimer?.Dispose();
        await ExecuteSearchAsync();
    }

    private async Task ExecuteSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Query)) return;
        if (Query.Trim() == _activeSearchQuery && HasSearched) return;

        // 재검색 시 bounce 감지 (직전 클릭 후 10초 이내 재검색 = bounce)
        _clickService.OnNewSearch(Query.Trim());

        IsSearching = true;
        HasSearched = true;
        _activeSearchQuery = Query.Trim();

        try
        {
            var sw = Stopwatch.StartNew();

            var response = await _hybridSearch.SearchAsync(_activeSearchQuery, new SearchOptions { TopK = 200 });
            var quickHits = _bm25Search.QuickSearch(_activeSearchQuery, 200);

            sw.Stop();
            SearchTime = $"{sw.Elapsed.TotalSeconds:F1}s";

            CategorizeResults(response, quickHits);
            ApplyFilterAndSort();

            Stamps = _stampRepo.GetCurrent();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SearchVM] Search error: {ex.Message}");
        }
        finally
        {
            IsSearching = false;
        }
    }

    partial void OnEmptyStateChanged(EmptyStateType value)
    {
        IsEmptyNoResults = value == EmptyStateType.NoResults;
        IsEmptyFilteredEmpty = value == EmptyStateType.FilteredEmpty;
    }

    partial void OnActiveFilterChanged(SearchFilter value) => ApplyFilterAndSort();
    partial void OnActiveTypeFilterChanged(string value) => ApplyFilterAndSort();
    partial void OnActiveDateFilterChanged(string value) => ApplyFilterAndSort();
    partial void OnActiveSortChanged(SortOption value) => ApplyFilterAndSort();
    partial void OnSelectedItemChanged(object? value) => UpdateDetailPanel(value);

    /// <summary>Open file with default program.</summary>
    [RelayCommand]
    private void OpenFile()
    {
        if (SelectedItem is SearchResultFile file && File.Exists(file.Path))
        {
            // 클릭 위치(결과 목록에서의 인덱스) 계산
            var position = VisibleFiles.IndexOf(file);
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

    /// <summary>폴더 접기/펴기 토글.</summary>
    [RelayCommand]
    private void ToggleFolders()
    {
        IsFolderCollapsed = !IsFolderCollapsed;
        RefreshDisplayFolders();
    }

    /// <summary>DisplayFolders를 VisibleFolders 기준으로 갱신.</summary>
    private void RefreshDisplayFolders()
    {
        DisplayFolders.Clear();
        var source = IsFolderCollapsed
            ? VisibleFolders.Take(3)
            : (IEnumerable<SearchResultFolder>)VisibleFolders;
        foreach (var f in source)
            DisplayFolders.Add(f);

        ShowFolderToggle = VisibleFolders.Count > 3;
        FolderToggleText = IsFolderCollapsed
            ? $"Show all {VisibleFolders.Count} folders"
            : "Show less";
    }

    /// <summary>Clear all type/date filters.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        ActiveTypeFilter = "All";
        ActiveDateFilter = "All";
    }

    // ─────────────────────────── Categorization ───────────────────────────

    private void CategorizeResults(SearchResponse response, IReadOnlyList<Bm25Hit> quickHits)
    {
        var queryLower = Query.ToLowerInvariant().Trim();
        // Split query into individual tokens for multi-word matching
        var queryTokens = queryLower.Split([' ', ',', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);

        // === Name matches: file/folder name contains ANY query token ===
        var nameFileSet = new Dictionary<string, SearchResultFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var hit in quickHits)
        {
            if (hit.IsDirectory) continue;
            nameFileSet.TryAdd(hit.FileId, new SearchResultFile
            {
                FileId = hit.FileId, Path = hit.Path, Filename = hit.Filename,
                Extension = hit.Extension, FolderPath = hit.FolderPath ?? "",
                ModifiedAt = FormatDate(hit.ModifiedAt), Score = hit.Score,
                Source = MatchSource.FileName,
            });
        }
        foreach (var hit in response.Items.Where(h =>
            h.MatchSource == MatchSource.FileName ||
            queryTokens.Any(t => h.Filename.Contains(t, StringComparison.OrdinalIgnoreCase))))
        {
            nameFileSet.TryAdd(hit.FileId, HybridToFile(hit));
        }
        _nameFiles = nameFileSet.Values.OrderByDescending(f => f.Score).ToList();
        _nameFolders = GroupIntoFolders(_nameFiles, queryTokens);

        // === Content matches ===
        _contentFiles = response.Items
            .Where(h => h.Bm25Score > 0)
            .Select(h => { var f = HybridToFile(h); f.Snippet = h.MatchSnippet ?? ""; return f; })
            .OrderByDescending(f => f.Score).ToList();
        _contentFolders = GroupIntoFolders(_contentFiles);

        // === Semantic matches ===
        IsSemanticUnavailable = _hybridSearch.CurrentMode == SearchMode.FtsOnly;
        _semanticFiles = response.Items
            .Where(h => h.DenseScore > 0)
            .Select(h =>
            {
                var f = HybridToFile(h);
                f.Concepts = h.MatchedTerms.Count > 0 ? string.Join(", ", h.MatchedTerms.Take(5)) : "";
                return f;
            })
            .OrderByDescending(f => f.Score).ToList();
        _semanticFolders = GroupIntoFolders(_semanticFiles);

        _unfilteredNameCount = _nameFiles.Count + _nameFolders.Count;
        _unfilteredContentCount = _contentFiles.Count;
        _unfilteredSemanticCount = _semanticFiles.Count;
    }

    /// <summary>Group files into folders. Token-based name filtering for Name tab.</summary>
    private static List<SearchResultFolder> GroupIntoFolders(
        List<SearchResultFile> files, string[] queryTokens)
    {
        var groups = files
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
                    SubText = parentPath,
                    Score = g.Max(f => f.Score),
                };
            })
            .Where(f => queryTokens.Any(t =>
                f.Name.Contains(t, StringComparison.OrdinalIgnoreCase)));

        return groups.OrderByDescending(f => f.Score).Take(50).ToList();
    }

    /// <summary>Group files into folders without name filtering (Content/Semantic tabs).</summary>
    private static List<SearchResultFolder> GroupIntoFolders(
        List<SearchResultFile> files)
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

    // ─────────────────────────── Filter + sort ───────────────────────────

    private void ApplyFilterAndSort()
    {
        var (folders, files) = ActiveFilter switch
        {
            SearchFilter.NameMatch => (_nameFolders, _nameFiles),
            SearchFilter.ContentMatch => (_contentFolders, _contentFiles),
            SearchFilter.SemanticMatch => (_semanticFolders, _semanticFiles),
            _ => (_nameFolders, _nameFiles),
        };

        // Apply type filter
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
                files = files.Where(f => extFilter.Contains(f.Extension.ToLowerInvariant())).ToList();
        }

        // Apply date filter
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
            {
                files = files.Where(f =>
                    DateTime.TryParse(f.ModifiedAt, out var dt) && dt >= cutoff).ToList();
            }
        }

        // Apply sort
        files = ActiveSort switch
        {
            SortOption.Newest => files.OrderByDescending(f => f.ModifiedAt).ToList(),
            SortOption.Oldest => files.OrderBy(f => f.ModifiedAt).ToList(),
            SortOption.NameAZ => files.OrderBy(f => f.Filename).ToList(),
            _ => files, // Relevance = already sorted by score
        };

        // Update visible collections
        VisibleFolders.Clear();
        foreach (var f in folders) VisibleFolders.Add(f);

        IsFolderCollapsed = true;
        RefreshDisplayFolders();

        VisibleFiles.Clear();
        foreach (var f in files) VisibleFiles.Add(f);

        // Update counts (show filtered count)
        NameMatchCount = ActiveFilter == SearchFilter.NameMatch
            ? files.Count + folders.Count : _unfilteredNameCount;
        ContentMatchCount = ActiveFilter == SearchFilter.ContentMatch
            ? files.Count : _unfilteredContentCount;
        SemanticMatchCount = _unfilteredSemanticCount;

        // Empty state
        if (VisibleFiles.Count == 0 && VisibleFolders.Count == 0)
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
            catch { DetailSize = "—"; }
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

            foreach (var f in VisibleFiles.Where(
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
