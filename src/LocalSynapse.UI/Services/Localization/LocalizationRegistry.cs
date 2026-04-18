using System.Collections.Generic;

namespace LocalSynapse.UI.Services.Localization;

/// <summary>
/// Master En/Ko string registry. Single source of truth — code is the master list.
/// </summary>
internal static class LocalizationRegistry
{
    /// <summary>Returns a fresh dictionary mapping key to (English, Korean) pair.</summary>
    public static Dictionary<string, (string En, string Ko)> Build()
    {
        return new Dictionary<string, (string En, string Ko)>
        {
            // Nav
            [StringKeys.Nav.Search] = ("Search", "검색"),
            [StringKeys.Nav.Data] = ("Data", "데이터"),
            [StringKeys.Nav.Mcp] = ("MCP", "MCP"),
            [StringKeys.Nav.Security] = ("Security", "보안"),
            [StringKeys.Nav.Settings] = ("Settings", "설정"),

            // Common
            [StringKeys.Common.All] = ("All", "전체"),
            [StringKeys.Common.More] = ("+ {0} more", "+ {0}개 더 보기"),
            [StringKeys.Common.Less] = ("- Show less", "- 접기"),
            [StringKeys.Common.Copy] = ("Copy", "복사"),
            [StringKeys.Common.Install] = ("Install", "설치"),
            [StringKeys.Common.Installed] = ("✓ Installed", "✓ 설치됨"),

            // Filter
            [StringKeys.Filter.Date30Days] = ("30 days", "30일"),
            [StringKeys.Filter.Date90Days] = ("90 days", "90일"),
            [StringKeys.Filter.DateThisYear] = ("This year", "올해"),

            // Folder
            [StringKeys.Folder.FileCount] = ("{0} files", "{0}개 파일"),
            [StringKeys.Folder.FileCountWithDate] = ("{0} files · last {1}", "{0}개 파일 · 최근 {1}"),

            // Search — core
            [StringKeys.Search.Placeholder] = ("Search files, content, or ask a question...", "파일·내용을 검색하거나 질문하세요..."),
            [StringKeys.Search.Button] = ("Search", "검색"),
            [StringKeys.Search.NoResults] = ("No results found", "결과 없음"),
            [StringKeys.Search.NoResultsHint] = ("Try different keywords or check spelling", "다른 키워드로 시도하거나 맞춤법을 확인하세요"),
            [StringKeys.Search.Initial] = ("Search across all your files", "모든 파일에서 검색"),
            [StringKeys.Search.InitialHint] = ("Start typing to search file names and content", "파일 이름과 내용을 검색하려면 입력을 시작하세요"),
            [StringKeys.Search.TryHint] = ("Try searching for", "검색해 보세요"),
            [StringKeys.Search.QuotesHint] = ("Use quotes for exact phrases", "정확한 문구는 따옴표로 감싸세요"),
            [StringKeys.Search.FilesIndexed] = ("files indexed", "파일 인덱스됨"),
            [StringKeys.Search.Chunks] = ("chunks", "청크"),
            [StringKeys.Search.Results] = ("results", "결과"),
            [StringKeys.Search.FilteredEmpty] = ("No results match your filters", "필터와 일치하는 결과가 없습니다"),
            [StringKeys.Search.FilteredEmptyHint] = ("Try changing the filter options", "필터 옵션을 변경해 보세요"),
            [StringKeys.Search.SelectToPreview] = ("Select an item to preview", "미리 볼 항목을 선택하세요"),

            // Search — indexed summary (platform-split)
            [StringKeys.Search.IndexedSummary.Mac] = ("files indexed", "파일 인덱스됨"),
            [StringKeys.Search.IndexedSummary.Desktop] = ("files indexed across all drives", "모든 드라이브에서 인덱스됨"),

            // Search — section labels
            [StringKeys.Search.Section.Filename] = ("Filename match", "파일명 일치"),
            [StringKeys.Search.Section.Opened] = ("Previously opened", "이전에 열어봄"),
            [StringKeys.Search.Section.Content] = ("Found in content", "내용에서 발견"),
            [StringKeys.Search.Section.Folders] = ("Related folders", "관련 폴더"),

            // Search — detail panel
            [StringKeys.Search.Detail.Info] = ("INFO", "정보"),
            [StringKeys.Search.Detail.ContentPreview] = ("CONTENT PREVIEW", "내용 미리보기"),
            [StringKeys.Search.Detail.OpenFile] = ("Open file", "파일 열기"),
            [StringKeys.Search.Detail.OpenFolder] = ("Open folder", "폴더 열기"),
            [StringKeys.Search.Detail.CopyPath] = ("Copy path", "경로 복사"),
            [StringKeys.Search.Detail.Modified] = ("MODIFIED", "수정일"),
            [StringKeys.Search.Detail.Size] = ("SIZE", "크기"),
            [StringKeys.Search.Detail.Type] = ("TYPE", "유형"),
            [StringKeys.Search.Detail.Score] = ("SCORE", "점수"),
            [StringKeys.Search.Detail.Files] = ("FILES", "파일"),

            // SmartNote
            [StringKeys.SmartNote.NotLatest] = ("Not latest version", "최신 버전 아님"),
            [StringKeys.SmartNote.Copy] = ("Copy", "복사본"),
            [StringKeys.SmartNote.LatestOfVersions] = ("Latest of {0} versions", "{0}개 버전 중 최신"),
            [StringKeys.SmartNote.NthOfVersions] = ("v{0} of {1}", "v{0} / {1}"),
            [StringKeys.SmartNote.OpenedToday] = ("Opened today", "오늘 열어봄"),
            [StringKeys.SmartNote.OpenedDaysAgo] = ("Opened {0} days ago", "{0}일 전에 열어봄"),
            [StringKeys.SmartNote.OpenedLastWeek] = ("Opened last week", "지난주에 열어봄"),
            [StringKeys.SmartNote.OpenedLastMonth] = ("Opened last month", "지난달에 열어봄"),
            [StringKeys.SmartNote.FrequentOpened] = ("Frequently opened ({0}×)", "자주 열어봄 ({0}회)"),
            [StringKeys.SmartNote.FoundInTitle] = ("Found in title", "제목에서 발견"),
            [StringKeys.SmartNote.FoundInFirstPage] = ("Found in first page", "첫 페이지에서 발견"),
            [StringKeys.SmartNote.FoundInPlaces] = ("Found in {0} places", "{0}곳에서 발견"),
            [StringKeys.SmartNote.ModifiedToday] = ("Modified today", "오늘 수정됨"),
            [StringKeys.SmartNote.ModifiedThisWeek] = ("Modified this week", "이번 주 수정됨"),
            [StringKeys.SmartNote.ModifiedThisMonth] = ("Modified this month", "이번 달 수정됨"),
            [StringKeys.SmartNote.NotModified2Years] = ("Not modified in 2+ years", "2년 이상 미수정"),
            [StringKeys.SmartNote.RelatedFilesInFolder] = ("{0} related files in folder", "폴더 내 관련 파일 {0}개"),
            [StringKeys.SmartNote.PdfFinalVersion] = ("PDF final version", "PDF 최종본"),
            [StringKeys.SmartNote.HasPdfVersion] = ("Has PDF version", "같은 이름 PDF 있음"),

            // Data
            [StringKeys.Data.Header] = ("Data & Indexing", "데이터 & 인덱싱"),
            [StringKeys.Data.Subtitle] = ("Manage file scanning and AI model for semantic search", "의미 검색을 위한 파일 스캔 및 AI 모델 관리"),
            [StringKeys.Data.PipelineStatus] = ("Pipeline status", "파이프라인 상태"),
            [StringKeys.Data.ScanTitle] = ("Scan", "스캔"),
            [StringKeys.Data.ScanSubtitle] = ("file discovery", "파일 탐색"),
            [StringKeys.Data.ScanFilesFound] = ("files found", "파일 발견"),
            [StringKeys.Data.ExtractTitle] = ("Extract", "추출"),
            [StringKeys.Data.ExtractSubtitle] = ("text chunking", "텍스트 청킹"),
            [StringKeys.Data.ExtractChunks] = ("chunks", "청크"),
            [StringKeys.Data.EmbedTitle] = ("Embed", "임베딩"),
            [StringKeys.Data.EmbedSubtitle] = ("semantic vectors", "의미 벡터"),
            [StringKeys.Data.ScanNow] = ("Scan now", "지금 스캔"),
            [StringKeys.Data.Pause] = ("Pause", "일시 정지"),
            [StringKeys.Data.Resume] = ("Resume", "재개"),
            [StringKeys.Data.AutoScanTitle] = ("Auto-scan on launch", "실행 시 자동 스캔"),
            [StringKeys.Data.AutoScanInfo] = ("LocalSynapse automatically scans your drives when the app starts.", "LocalSynapse가 앱 시작 시 자동으로 드라이브를 스캔합니다."),
            [StringKeys.Data.ManualScanTitle] = ("Manual re-scan", "수동 재스캔"),
            [StringKeys.Data.ManualScanInfo] = ("Trigger a fresh scan to pick up new or modified files.", "새 파일이나 변경된 파일을 반영하려면 재스캔을 실행하세요."),
            [StringKeys.Data.ModelTitle] = ("BGE-M3 embedding model", "BGE-M3 임베딩 모델"),
            [StringKeys.Data.ModelSubtitle] = ("~2.3 GB download · runs 100% locally", "약 2.3 GB 다운로드 · 100% 로컬 실행"),
            [StringKeys.Data.Ready] = ("Ready", "준비됨"),
            [StringKeys.Data.EmbedStat] = ("semantic search", "의미 검색"),
            [StringKeys.Data.FilesSkipped] = ("{0} files skipped", "{0}개 파일 건너뜀"),
            [StringKeys.Data.FilesSkippedSuffix] = ("files skipped", "파일 건너뜀"),
            [StringKeys.Data.SkippedInfo] = ("Files are skipped when content extraction is not supported: images (.jpg, .png, .gif), videos, archives (.zip, .rar), executables, and other binary formats. Cloud files (OneDrive, Google Drive, iCloud) that are online-only are also skipped — once downloaded locally, they will be indexed on the next scan. All skipped files are still searchable by filename.", "내용 추출이 지원되지 않으면 파일이 건너뛰어집니다: 이미지(.jpg, .png, .gif), 비디오, 압축 파일(.zip, .rar), 실행 파일 및 기타 바이너리 형식. 온라인 전용 클라우드 파일(OneDrive, Google Drive, iCloud)도 건너뛰며, 로컬에 다운로드되면 다음 스캔에서 인덱싱됩니다. 건너뛴 파일도 파일명으로 검색 가능합니다."),
            [StringKeys.Data.AiSearchTitle] = ("AI semantic search", "AI 의미 검색"),
            [StringKeys.Data.AutoScanInfo] = ("App automatically scans for new and modified files each time it starts. Only changed files are re-processed.", "앱이 시작될 때마다 새 파일과 변경된 파일을 자동 스캔합니다. 변경된 파일만 재처리됩니다."),
            [StringKeys.Data.ManualScanInfo] = ("Press \"Scan now\" to trigger a differential scan immediately. This picks up files added or changed since the last scan.", "\"지금 스캔\"을 눌러 즉시 차등 스캔을 시작합니다. 마지막 스캔 이후 추가되거나 변경된 파일을 반영합니다."),

            // Security
            [StringKeys.Security.Header] = ("Privacy & Security", "개인정보 보호 및 보안"),
            [StringKeys.Security.Subtitle] = ("Your data stays on your machine. Always.", "데이터는 항상 사용자의 기기에만 저장됩니다."),
            [StringKeys.Security.OfflineTitle] = ("100% offline AI", "100% 오프라인 AI"),
            [StringKeys.Security.OfflineDescription] = ("All file indexing, search, and AI embedding runs entirely on your local machine. Nothing leaves your device.", "모든 파일 인덱싱, 검색, AI 임베딩은 로컬 기기에서만 실행됩니다. 기기 외부로 나가지 않습니다."),
            [StringKeys.Security.StorageTitle] = ("Data storage", "데이터 저장소"),
            [StringKeys.Security.StorageLocation] = ("Location", "위치"),
            [StringKeys.Security.StorageSize] = ("Size", "크기"),
            [StringKeys.Security.OpenDataFolder] = ("Open data folder", "데이터 폴더 열기"),
            [StringKeys.Security.OfflineDescription] = ("All file indexing, search, and AI embedding runs entirely on your local machine. No data is ever sent to external servers. No login or account required.", "모든 파일 인덱싱, 검색, AI 임베딩이 로컬 기기에서만 실행됩니다. 외부 서버로 전송되는 데이터가 없으며 로그인이나 계정도 필요 없습니다."),
            [StringKeys.Security.HowItWorksTitle] = ("How your data is protected", "데이터 보호 방식"),
            [StringKeys.Security.HowItWorksBullet1] = ("File contents are processed locally and stored in a SQLite database on your disk", "파일 내용은 로컬에서 처리되어 디스크의 SQLite 데이터베이스에 저장됩니다"),
            [StringKeys.Security.HowItWorksBullet2] = ("AI embeddings (BGE-M3) run via ONNX Runtime — no API calls, no cloud", "AI 임베딩(BGE-M3)은 ONNX Runtime으로 실행됩니다 — API 호출 없음, 클라우드 없음"),
            [StringKeys.Security.HowItWorksBullet3] = ("MCP server communicates only with local AI clients via stdio — no network", "MCP 서버는 stdio로 로컬 AI 클라이언트와만 통신합니다 — 네트워크 사용 없음"),
            [StringKeys.Security.HowItWorksBullet4] = ("Uninstalling removes the app. Delete the data folder to remove all indexed data.", "앱을 제거하면 앱만 삭제됩니다. 인덱싱된 데이터를 모두 지우려면 데이터 폴더를 삭제하세요."),

            // Settings
            [StringKeys.Settings.Header] = ("Settings", "설정"),
            [StringKeys.Settings.Subtitle] = ("Configure LocalSynapse preferences", "LocalSynapse 환경설정"),
            [StringKeys.Settings.LanguageTitle] = ("Language", "언어"),
            [StringKeys.Settings.LanguageCurrent] = ("Current", "현재"),
            [StringKeys.Settings.AboutTitle] = ("About", "정보"),
            [StringKeys.Settings.AboutVersion] = ("Version", "버전"),
            [StringKeys.Settings.AboutData] = ("Data", "데이터"),
            [StringKeys.Settings.AboutLicense] = ("License", "라이선스"),
            [StringKeys.Settings.LinksTitle] = ("Links", "링크"),
            [StringKeys.Settings.LinksWebsite] = ("Website", "웹사이트"),
            [StringKeys.Settings.LinksGitHub] = ("GitHub", "GitHub"),

            // Mcp
            [StringKeys.Mcp.Header] = ("MCP Server", "MCP 서버"),
            [StringKeys.Mcp.Subtitle] = ("Connect LocalSynapse to AI coding agents via Model Context Protocol", "Model Context Protocol로 AI 코딩 에이전트와 LocalSynapse 연결"),
            [StringKeys.Mcp.ServerInfoTitle] = ("Server Info", "서버 정보"),
            [StringKeys.Mcp.Executable] = ("Executable", "실행 파일"),
            [StringKeys.Mcp.Transport] = ("Transport", "전송"),
            [StringKeys.Mcp.TransportValue] = ("stdio (JSON-RPC)", "stdio (JSON-RPC)"),
            [StringKeys.Mcp.Command] = ("Command", "명령어"),
            [StringKeys.Mcp.ClientsTitle] = ("Clients", "클라이언트"),
            [StringKeys.Mcp.Connect] = ("Connect", "연결"),
            [StringKeys.Mcp.Disconnect] = ("Disconnect", "연결 해제"),
            [StringKeys.Mcp.OpenConfigFolder] = ("Open config folder", "설정 폴더 열기"),
            [StringKeys.Mcp.RegisteredNotice] = ("✓ LocalSynapse is registered in Claude Desktop. Restart Claude Desktop to apply.", "✓ LocalSynapse가 Claude Desktop에 등록되었습니다. 적용하려면 Claude Desktop을 재시작하세요."),
            [StringKeys.Mcp.RunCommandHint] = ("Run this command in your terminal to register:", "등록하려면 터미널에서 다음 명령을 실행하세요:"),
            [StringKeys.Mcp.ToRemoveHint] = ("To remove:", "제거하려면:"),
            [StringKeys.Mcp.AvailableToolsTitle] = ("Available MCP Tools", "사용 가능한 MCP 도구"),
            [StringKeys.Mcp.QuickStartTitle] = ("Quick Start Guide", "빠른 시작 가이드"),
            [StringKeys.Mcp.QuickStart1Title] = ("1. Index your files", "1. 파일 인덱싱"),
            [StringKeys.Mcp.QuickStart1Desc] = ("Go to the Data tab and let LocalSynapse scan your folders. Wait for indexing to complete.", "데이터 탭으로 이동해서 LocalSynapse가 폴더를 스캔하도록 하세요. 인덱싱이 완료될 때까지 기다리세요."),
            [StringKeys.Mcp.QuickStart2Title] = ("2. Connect to Claude", "2. Claude와 연결"),
            [StringKeys.Mcp.QuickStart2Desc] = ("Click 'Connect' above for Claude Desktop, or copy the command for Claude Code. Restart Claude after connecting.", "Claude Desktop은 위의 '연결' 버튼을, Claude Code는 해당 명령을 복사하세요. 연결 후 Claude를 재시작하세요."),
            [StringKeys.Mcp.QuickStart3Title] = ("3. Start asking questions", "3. 질문 시작"),
            [StringKeys.Mcp.QuickStart3Desc] = ("Ask Claude about your files. Try: \"Find all documents related to Q3 budget\" or \"What files did I work on last week?\"", "Claude에게 파일에 대해 질문하세요. 예: \"Q3 예산 관련 문서 모두 찾아줘\" 또는 \"지난주에 작업한 파일이 뭐야?\""),
            [StringKeys.Mcp.NoteLabel] = ("Note: ", "참고: "),
            [StringKeys.Mcp.NoteText] = ("LocalSynapse does not need to be running for MCP to work. The MCP server starts as a separate process managed by Claude.", "MCP가 작동하는 데 LocalSynapse가 실행 중일 필요는 없습니다. MCP 서버는 Claude가 관리하는 별도 프로세스로 시작됩니다."),
        };
    }
}
