using System.Collections.Generic;

namespace LocalSynapse.UI.Services.Localization;

/// <summary>
/// Master localization registry for en/ko/fr/de/zh.
/// Single source of truth — code is the master list.
/// </summary>
internal static class LocalizationRegistry
{
    /// <summary>Returns a fresh dictionary mapping key to per-locale string dictionary.</summary>
    public static Dictionary<string, Dictionary<string, string>> Build()
    {
        return new Dictionary<string, Dictionary<string, string>>
        {
            // ── Nav ──
            [StringKeys.Nav.Search] = new() { ["en"] = "Search", ["ko"] = "검색", ["fr"] = "Recherche", ["de"] = "Suche", ["zh"] = "搜索" },
            [StringKeys.Nav.Data] = new() { ["en"] = "Data", ["ko"] = "데이터", ["fr"] = "Données", ["de"] = "Daten", ["zh"] = "数据" },
            [StringKeys.Nav.Mcp] = new() { ["en"] = "MCP", ["ko"] = "MCP", ["fr"] = "MCP", ["de"] = "MCP", ["zh"] = "MCP" },
            [StringKeys.Nav.Security] = new() { ["en"] = "Security", ["ko"] = "보안", ["fr"] = "Sécurité", ["de"] = "Sicherheit", ["zh"] = "安全" },
            [StringKeys.Nav.Settings] = new() { ["en"] = "Settings", ["ko"] = "설정", ["fr"] = "Paramètres", ["de"] = "Einstellungen", ["zh"] = "设置" },

            // ── Common ──
            [StringKeys.Common.All] = new() { ["en"] = "All", ["ko"] = "전체", ["fr"] = "Tout", ["de"] = "Alle", ["zh"] = "全部" },
            [StringKeys.Common.More] = new() { ["en"] = "+ {0} more", ["ko"] = "+ {0}개 더 보기", ["fr"] = "+ {0} de plus", ["de"] = "+ {0} weitere", ["zh"] = "+ 还有{0}项" },
            [StringKeys.Common.Less] = new() { ["en"] = "- Show less", ["ko"] = "- 접기", ["fr"] = "- Réduire", ["de"] = "- Weniger", ["zh"] = "- 收起" },
            [StringKeys.Common.Copy] = new() { ["en"] = "Copy", ["ko"] = "복사", ["fr"] = "Copier", ["de"] = "Kopieren", ["zh"] = "复制" },
            [StringKeys.Common.Install] = new() { ["en"] = "Install", ["ko"] = "설치", ["fr"] = "Installer", ["de"] = "Installieren", ["zh"] = "安装" },
            [StringKeys.Common.Installed] = new() { ["en"] = "✓ Installed", ["ko"] = "✓ 설치됨", ["fr"] = "✓ Installé", ["de"] = "✓ Installiert", ["zh"] = "✓ 已安装" },

            // ── Filter ──
            [StringKeys.Filter.Date30Days] = new() { ["en"] = "30 days", ["ko"] = "30일", ["fr"] = "30 jours", ["de"] = "30 Tage", ["zh"] = "30天" },
            [StringKeys.Filter.Date90Days] = new() { ["en"] = "90 days", ["ko"] = "90일", ["fr"] = "90 jours", ["de"] = "90 Tage", ["zh"] = "90天" },
            [StringKeys.Filter.DateThisYear] = new() { ["en"] = "This year", ["ko"] = "올해", ["fr"] = "Cette année", ["de"] = "Dieses Jahr", ["zh"] = "今年" },

            // ── Folder ──
            [StringKeys.Folder.FileCount] = new() { ["en"] = "{0} files", ["ko"] = "{0}개 파일", ["fr"] = "{0} fichiers", ["de"] = "{0} Dateien", ["zh"] = "{0}个文件" },
            [StringKeys.Folder.FileCountWithDate] = new() { ["en"] = "{0} files · last {1}", ["ko"] = "{0}개 파일 · 최근 {1}", ["fr"] = "{0} fichiers · dernier {1}", ["de"] = "{0} Dateien · zuletzt {1}", ["zh"] = "{0}个文件 · 最近{1}" },

            // ── Search — core ──
            [StringKeys.Search.Placeholder] = new() { ["en"] = "Search files, content, or ask a question...", ["ko"] = "파일·내용을 검색하거나 질문하세요...", ["fr"] = "Rechercher des fichiers, du contenu ou poser une question...", ["de"] = "Dateien, Inhalte suchen oder eine Frage stellen...", ["zh"] = "搜索文件、内容或提问..." },
            [StringKeys.Search.Button] = new() { ["en"] = "Search", ["ko"] = "검색", ["fr"] = "Rechercher", ["de"] = "Suchen", ["zh"] = "搜索" },
            [StringKeys.Search.NoResults] = new() { ["en"] = "No results found", ["ko"] = "결과 없음", ["fr"] = "Aucun résultat", ["de"] = "Keine Ergebnisse", ["zh"] = "未找到结果" },
            [StringKeys.Search.NoResultsHint] = new() { ["en"] = "Try different keywords or check spelling", ["ko"] = "다른 키워드로 시도하거나 맞춤법을 확인하세요", ["fr"] = "Essayez d'autres mots-clés ou vérifiez l'orthographe", ["de"] = "Versuchen Sie andere Stichwörter oder prüfen Sie die Schreibweise", ["zh"] = "尝试其他关键词或检查拼写" },
            [StringKeys.Search.Initial] = new() { ["en"] = "Search across all your files", ["ko"] = "모든 파일에서 검색", ["fr"] = "Rechercher dans tous vos fichiers", ["de"] = "In allen Dateien suchen", ["zh"] = "搜索所有文件" },
            [StringKeys.Search.InitialHint] = new() { ["en"] = "Start typing to search file names and content", ["ko"] = "파일 이름과 내용을 검색하려면 입력을 시작하세요", ["fr"] = "Commencez à taper pour rechercher", ["de"] = "Beginnen Sie zu tippen, um zu suchen", ["zh"] = "开始输入以搜索文件名和内容" },
            [StringKeys.Search.TryHint] = new() { ["en"] = "Try searching for", ["ko"] = "검색해 보세요", ["fr"] = "Essayez de rechercher", ["de"] = "Versuchen Sie zu suchen", ["zh"] = "试试搜索" },
            [StringKeys.Search.QuotesHint] = new() { ["en"] = "Use quotes for exact phrases", ["ko"] = "정확한 문구는 따옴표로 감싸세요", ["fr"] = "Utilisez des guillemets pour les phrases exactes", ["de"] = "Verwenden Sie Anführungszeichen für exakte Phrasen", ["zh"] = "使用引号搜索精确短语" },
            [StringKeys.Search.FilesIndexed] = new() { ["en"] = "files indexed", ["ko"] = "파일 인덱스됨", ["fr"] = "fichiers indexés", ["de"] = "Dateien indexiert", ["zh"] = "文件已索引" },
            [StringKeys.Search.Chunks] = new() { ["en"] = "chunks", ["ko"] = "청크", ["fr"] = "fragments", ["de"] = "Abschnitte", ["zh"] = "分块" },
            [StringKeys.Search.Results] = new() { ["en"] = "results", ["ko"] = "결과", ["fr"] = "résultats", ["de"] = "Ergebnisse", ["zh"] = "结果" },
            [StringKeys.Search.FilteredEmpty] = new() { ["en"] = "No results match your filters", ["ko"] = "필터와 일치하는 결과가 없습니다", ["fr"] = "Aucun résultat ne correspond à vos filtres", ["de"] = "Keine Ergebnisse entsprechen Ihren Filtern", ["zh"] = "没有匹配筛选条件的结果" },
            [StringKeys.Search.FilteredEmptyHint] = new() { ["en"] = "Try changing the filter options", ["ko"] = "필터 옵션을 변경해 보세요", ["fr"] = "Essayez de modifier les filtres", ["de"] = "Versuchen Sie, die Filteroptionen zu ändern", ["zh"] = "尝试更改筛选选项" },
            [StringKeys.Search.SelectToPreview] = new() { ["en"] = "Select an item to preview", ["ko"] = "미리 볼 항목을 선택하세요", ["fr"] = "Sélectionnez un élément pour prévisualiser", ["de"] = "Wählen Sie ein Element zur Vorschau", ["zh"] = "选择项目进行预览" },

            // ── Search — indexed summary ──
            [StringKeys.Search.IndexedSummary.Mac] = new() { ["en"] = "files indexed", ["ko"] = "파일 인덱스됨", ["fr"] = "fichiers indexés", ["de"] = "Dateien indexiert", ["zh"] = "文件已索引" },
            [StringKeys.Search.IndexedSummary.Desktop] = new() { ["en"] = "files indexed across all drives", ["ko"] = "모든 드라이브에서 인덱스됨", ["fr"] = "fichiers indexés sur tous les disques", ["de"] = "Dateien auf allen Laufwerken indexiert", ["zh"] = "所有驱动器中的文件已索引" },

            // ── Search — section labels ──
            [StringKeys.Search.Section.Filename] = new() { ["en"] = "Filename match", ["ko"] = "파일명 일치", ["fr"] = "Nom de fichier", ["de"] = "Dateiname", ["zh"] = "文件名匹配" },
            [StringKeys.Search.Section.Opened] = new() { ["en"] = "Previously opened", ["ko"] = "이전에 열어봄", ["fr"] = "Ouvert précédemment", ["de"] = "Zuvor geöffnet", ["zh"] = "之前打开过" },
            [StringKeys.Search.Section.Content] = new() { ["en"] = "Found in content", ["ko"] = "내용에서 발견", ["fr"] = "Trouvé dans le contenu", ["de"] = "Im Inhalt gefunden", ["zh"] = "在内容中找到" },
            [StringKeys.Search.Section.Folders] = new() { ["en"] = "Related folders", ["ko"] = "관련 폴더", ["fr"] = "Dossiers associés", ["de"] = "Verwandte Ordner", ["zh"] = "相关文件夹" },

            // ── Search — detail panel ──
            [StringKeys.Search.Detail.Info] = new() { ["en"] = "INFO", ["ko"] = "정보", ["fr"] = "INFO", ["de"] = "INFO", ["zh"] = "信息" },
            [StringKeys.Search.Detail.ContentPreview] = new() { ["en"] = "CONTENT PREVIEW", ["ko"] = "내용 미리보기", ["fr"] = "APERÇU DU CONTENU", ["de"] = "INHALTSVORSCHAU", ["zh"] = "内容预览" },
            [StringKeys.Search.Detail.OpenFile] = new() { ["en"] = "Open file", ["ko"] = "파일 열기", ["fr"] = "Ouvrir le fichier", ["de"] = "Datei öffnen", ["zh"] = "打开文件" },
            [StringKeys.Search.Detail.OpenFolder] = new() { ["en"] = "Open folder", ["ko"] = "폴더 열기", ["fr"] = "Ouvrir le dossier", ["de"] = "Ordner öffnen", ["zh"] = "打开文件夹" },
            [StringKeys.Search.Detail.CopyPath] = new() { ["en"] = "Copy path", ["ko"] = "경로 복사", ["fr"] = "Copier le chemin", ["de"] = "Pfad kopieren", ["zh"] = "复制路径" },
            [StringKeys.Search.Detail.Modified] = new() { ["en"] = "MODIFIED", ["ko"] = "수정일", ["fr"] = "MODIFIÉ", ["de"] = "GEÄNDERT", ["zh"] = "修改日期" },
            [StringKeys.Search.Detail.Size] = new() { ["en"] = "SIZE", ["ko"] = "크기", ["fr"] = "TAILLE", ["de"] = "GRÖSSE", ["zh"] = "大小" },
            [StringKeys.Search.Detail.Type] = new() { ["en"] = "TYPE", ["ko"] = "유형", ["fr"] = "TYPE", ["de"] = "TYP", ["zh"] = "类型" },
            [StringKeys.Search.Detail.Score] = new() { ["en"] = "SCORE", ["ko"] = "점수", ["fr"] = "SCORE", ["de"] = "SCORE", ["zh"] = "评分" },
            [StringKeys.Search.Detail.Files] = new() { ["en"] = "FILES", ["ko"] = "파일", ["fr"] = "FICHIERS", ["de"] = "DATEIEN", ["zh"] = "文件" },
            [StringKeys.Search.Detail.NotIndexed] = new() { ["en"] = "File not yet indexed", ["ko"] = "아직 인덱싱되지 않은 파일", ["fr"] = "Fichier pas encore indexé", ["de"] = "Datei noch nicht indexiert", ["zh"] = "文件尚未索引" },
            [StringKeys.Search.Detail.PreviewError] = new() { ["en"] = "Unable to load preview", ["ko"] = "미리보기를 불러올 수 없습니다", ["fr"] = "Impossible de charger l'aperçu", ["de"] = "Vorschau kann nicht geladen werden", ["zh"] = "无法加载预览" },

            // ── SmartNote ──
            [StringKeys.SmartNote.NotLatest] = new() { ["en"] = "Not latest version", ["ko"] = "최신 버전 아님", ["fr"] = "Pas la dernière version", ["de"] = "Nicht die neueste Version", ["zh"] = "非最新版本" },
            [StringKeys.SmartNote.Copy] = new() { ["en"] = "Copy", ["ko"] = "복사본", ["fr"] = "Copie", ["de"] = "Kopie", ["zh"] = "副本" },
            [StringKeys.SmartNote.LatestOfVersions] = new() { ["en"] = "Latest of {0} versions", ["ko"] = "{0}개 버전 중 최신", ["fr"] = "Dernière de {0} versions", ["de"] = "Neueste von {0} Versionen", ["zh"] = "{0}个版本中最新" },
            [StringKeys.SmartNote.NthOfVersions] = new() { ["en"] = "v{0} of {1}", ["ko"] = "v{0} / {1}", ["fr"] = "v{0} sur {1}", ["de"] = "v{0} von {1}", ["zh"] = "v{0}/{1}" },
            [StringKeys.SmartNote.OpenedToday] = new() { ["en"] = "Opened today", ["ko"] = "오늘 열어봄", ["fr"] = "Ouvert aujourd'hui", ["de"] = "Heute geöffnet", ["zh"] = "今天打开" },
            [StringKeys.SmartNote.OpenedDaysAgo] = new() { ["en"] = "Opened {0} days ago", ["ko"] = "{0}일 전에 열어봄", ["fr"] = "Ouvert il y a {0} jours", ["de"] = "Vor {0} Tagen geöffnet", ["zh"] = "{0}天前打开" },
            [StringKeys.SmartNote.OpenedLastWeek] = new() { ["en"] = "Opened 1–2 weeks ago", ["ko"] = "1~2주 전 열어봄", ["fr"] = "Ouvert il y a 1-2 semaines", ["de"] = "Vor 1-2 Wochen geöffnet", ["zh"] = "1-2周前打开" },
            [StringKeys.SmartNote.OpenedLastMonth] = new() { ["en"] = "Opened 2 weeks–2 months ago", ["ko"] = "2주~2개월 전 열어봄", ["fr"] = "Ouvert il y a 2 sem.-2 mois", ["de"] = "Vor 2 Wochen-2 Monaten geöffnet", ["zh"] = "2周-2个月前打开" },
            [StringKeys.SmartNote.FrequentOpened] = new() { ["en"] = "Frequently opened ({0}×)", ["ko"] = "자주 열어봄 ({0}회)", ["fr"] = "Ouvert fréquemment ({0}×)", ["de"] = "Häufig geöffnet ({0}×)", ["zh"] = "经常打开（{0}次）" },
            [StringKeys.SmartNote.FoundInTitle] = new() { ["en"] = "Found in title", ["ko"] = "제목에서 발견", ["fr"] = "Trouvé dans le titre", ["de"] = "Im Titel gefunden", ["zh"] = "在标题中找到" },
            [StringKeys.SmartNote.FoundInFirstPage] = new() { ["en"] = "Found in first page", ["ko"] = "첫 페이지에서 발견", ["fr"] = "Trouvé en première page", ["de"] = "Auf der ersten Seite gefunden", ["zh"] = "在首页找到" },
            [StringKeys.SmartNote.FoundInPlaces] = new() { ["en"] = "Found in {0} places", ["ko"] = "{0}곳에서 발견", ["fr"] = "Trouvé à {0} endroits", ["de"] = "An {0} Stellen gefunden", ["zh"] = "在{0}处找到" },
            [StringKeys.SmartNote.ModifiedToday] = new() { ["en"] = "Modified today", ["ko"] = "오늘 수정됨", ["fr"] = "Modifié aujourd'hui", ["de"] = "Heute geändert", ["zh"] = "今天修改" },
            [StringKeys.SmartNote.ModifiedThisWeek] = new() { ["en"] = "Modified in last 7 days", ["ko"] = "최근 7일 내 수정", ["fr"] = "Modifié cette semaine", ["de"] = "Diese Woche geändert", ["zh"] = "本周修改" },
            [StringKeys.SmartNote.ModifiedThisMonth] = new() { ["en"] = "Modified in last 30 days", ["ko"] = "최근 30일 내 수정", ["fr"] = "Modifié ce mois-ci", ["de"] = "Diesen Monat geändert", ["zh"] = "本月修改" },
            [StringKeys.SmartNote.NotModified2Years] = new() { ["en"] = "Not modified in 2+ years", ["ko"] = "2년 이상 미수정", ["fr"] = "Non modifié depuis 2+ ans", ["de"] = "Seit 2+ Jahren nicht geändert", ["zh"] = "超过2年未修改" },
            [StringKeys.SmartNote.RelatedFilesInFolder] = new() { ["en"] = "{0} related files in folder", ["ko"] = "폴더 내 관련 파일 {0}개", ["fr"] = "{0} fichiers liés dans le dossier", ["de"] = "{0} verwandte Dateien im Ordner", ["zh"] = "同文件夹中{0}个相关文件" },
            [StringKeys.SmartNote.PdfFinalVersion] = new() { ["en"] = "PDF final version", ["ko"] = "PDF 최종본", ["fr"] = "Version finale PDF", ["de"] = "PDF-Endversion", ["zh"] = "PDF最终版" },
            [StringKeys.SmartNote.HasPdfVersion] = new() { ["en"] = "Has PDF version", ["ko"] = "같은 이름 PDF 있음", ["fr"] = "PDF de même nom existant", ["de"] = "Gleichnamige PDF vorhanden", ["zh"] = "存在同名PDF" },
            [StringKeys.SmartNote.HasPptVersion] = new() { ["en"] = "Same name PPT exists", ["ko"] = "같은 이름 PPT 원본 있음", ["fr"] = "PPT de même nom existant", ["de"] = "Gleichnamige PPT vorhanden", ["zh"] = "存在同名PPT原件" },

            // ── Data ──
            [StringKeys.Data.Header] = new() { ["en"] = "Data & Indexing", ["ko"] = "데이터 & 인덱싱", ["fr"] = "Données et indexation", ["de"] = "Daten & Indexierung", ["zh"] = "数据与索引" },
            [StringKeys.Data.Subtitle] = new() { ["en"] = "Manage file scanning and AI model for semantic search", ["ko"] = "의미 검색을 위한 파일 스캔 및 AI 모델 관리", ["fr"] = "Gérer l'analyse des fichiers et le modèle IA pour la recherche sémantique", ["de"] = "Dateiscan und KI-Modell für semantische Suche verwalten", ["zh"] = "管理文件扫描和语义搜索AI模型" },
            [StringKeys.Data.PipelineStatus] = new() { ["en"] = "Pipeline status", ["ko"] = "파이프라인 상태", ["fr"] = "État du pipeline", ["de"] = "Pipeline-Status", ["zh"] = "管道状态" },
            [StringKeys.Data.ScanTitle] = new() { ["en"] = "Scan", ["ko"] = "스캔", ["fr"] = "Analyse", ["de"] = "Scan", ["zh"] = "扫描" },
            [StringKeys.Data.ScanSubtitle] = new() { ["en"] = "file discovery", ["ko"] = "파일 탐색", ["fr"] = "découverte de fichiers", ["de"] = "Dateisuche", ["zh"] = "文件发现" },
            [StringKeys.Data.ScanFilesFound] = new() { ["en"] = "files found", ["ko"] = "파일 발견", ["fr"] = "fichiers trouvés", ["de"] = "Dateien gefunden", ["zh"] = "发现文件" },
            [StringKeys.Data.ExtractTitle] = new() { ["en"] = "Extract", ["ko"] = "추출", ["fr"] = "Extraction", ["de"] = "Extraktion", ["zh"] = "提取" },
            [StringKeys.Data.ExtractSubtitle] = new() { ["en"] = "text chunking", ["ko"] = "텍스트 청킹", ["fr"] = "découpage de texte", ["de"] = "Textaufteilung", ["zh"] = "文本分块" },
            [StringKeys.Data.ExtractChunks] = new() { ["en"] = "chunks", ["ko"] = "청크", ["fr"] = "fragments", ["de"] = "Abschnitte", ["zh"] = "分块" },
            [StringKeys.Data.EmbedTitle] = new() { ["en"] = "Embed", ["ko"] = "임베딩", ["fr"] = "Embedding", ["de"] = "Embedding", ["zh"] = "嵌入" },
            [StringKeys.Data.EmbedSubtitle] = new() { ["en"] = "semantic vectors", ["ko"] = "의미 벡터", ["fr"] = "vecteurs sémantiques", ["de"] = "semantische Vektoren", ["zh"] = "语义向量" },
            [StringKeys.Data.ScanNow] = new() { ["en"] = "Scan now", ["ko"] = "지금 스캔", ["fr"] = "Analyser maintenant", ["de"] = "Jetzt scannen", ["zh"] = "立即扫描" },
            [StringKeys.Data.Pause] = new() { ["en"] = "Pause", ["ko"] = "일시 정지", ["fr"] = "Pause", ["de"] = "Pause", ["zh"] = "暂停" },
            [StringKeys.Data.Resume] = new() { ["en"] = "Resume", ["ko"] = "재개", ["fr"] = "Reprendre", ["de"] = "Fortsetzen", ["zh"] = "继续" },
            [StringKeys.Data.AutoScanTitle] = new() { ["en"] = "Auto-scan on launch", ["ko"] = "실행 시 자동 스캔", ["fr"] = "Analyse automatique au lancement", ["de"] = "Automatischer Scan beim Start", ["zh"] = "启动时自动扫描" },
            [StringKeys.Data.AutoScanInfo] = new() { ["en"] = "App automatically scans for new and modified files each time it starts. Only changed files are re-processed.", ["ko"] = "앱이 시작될 때마다 새 파일과 변경된 파일을 자동 스캔합니다. 변경된 파일만 재처리됩니다.", ["fr"] = "L'application analyse automatiquement les fichiers nouveaux et modifiés à chaque démarrage. Seuls les fichiers modifiés sont retraités.", ["de"] = "Die App scannt bei jedem Start automatisch nach neuen und geänderten Dateien. Nur geänderte Dateien werden erneut verarbeitet.", ["zh"] = "应用每次启动时自动扫描新文件和修改过的文件。仅重新处理更改的文件。" },
            [StringKeys.Data.ManualScanTitle] = new() { ["en"] = "Manual re-scan", ["ko"] = "수동 재스캔", ["fr"] = "Nouvelle analyse manuelle", ["de"] = "Manueller Rescan", ["zh"] = "手动重新扫描" },
            [StringKeys.Data.ManualScanInfo] = new() { ["en"] = "Press \"Scan now\" to trigger a differential scan immediately. This picks up files added or changed since the last scan.", ["ko"] = "\"지금 스캔\"을 눌러 즉시 차등 스캔을 시작합니다. 마지막 스캔 이후 추가되거나 변경된 파일을 반영합니다.", ["fr"] = "Appuyez sur \"Analyser maintenant\" pour déclencher une analyse différentielle immédiatement.", ["de"] = "Drücken Sie \"Jetzt scannen\", um einen differenziellen Scan sofort auszulösen.", ["zh"] = "点击\"立即扫描\"立即触发差异扫描。" },
            [StringKeys.Data.ModelTitle] = new() { ["en"] = "BGE-M3 embedding model", ["ko"] = "BGE-M3 임베딩 모델", ["fr"] = "Modèle d'embedding BGE-M3", ["de"] = "BGE-M3 Embedding-Modell", ["zh"] = "BGE-M3 嵌入模型" },
            [StringKeys.Data.ModelSubtitle] = new() { ["en"] = "~2.3 GB download · runs 100% locally", ["ko"] = "약 2.3 GB 다운로드 · 100% 로컬 실행", ["fr"] = "~2,3 Go · fonctionne 100% en local", ["de"] = "~2,3 GB Download · läuft 100% lokal", ["zh"] = "约2.3 GB下载 · 100%本地运行" },
            [StringKeys.Data.Ready] = new() { ["en"] = "Ready", ["ko"] = "준비됨", ["fr"] = "Prêt", ["de"] = "Bereit", ["zh"] = "就绪" },
            [StringKeys.Data.EmbedStat] = new() { ["en"] = "semantic search", ["ko"] = "의미 검색", ["fr"] = "recherche sémantique", ["de"] = "semantische Suche", ["zh"] = "语义搜索" },
            [StringKeys.Data.FilesSkipped] = new() { ["en"] = "{0} files skipped", ["ko"] = "{0}개 파일 건너뜀", ["fr"] = "{0} fichiers ignorés", ["de"] = "{0} Dateien übersprungen", ["zh"] = "跳过了{0}个文件" },
            [StringKeys.Data.FilesSkippedSuffix] = new() { ["en"] = "files skipped", ["ko"] = "파일 건너뜀", ["fr"] = "fichiers ignorés", ["de"] = "Dateien übersprungen", ["zh"] = "文件被跳过" },
            [StringKeys.Data.SkippedInfo] = new() { ["en"] = "Files are skipped when content extraction is not supported: images (.jpg, .png, .gif), videos, archives (.zip, .rar), executables, and other binary formats. Cloud files (OneDrive, Google Drive, iCloud) that are online-only are also skipped — once downloaded locally, they will be indexed on the next scan. All skipped files are still searchable by filename.", ["ko"] = "내용 추출이 지원되지 않으면 파일이 건너뛰어집니다: 이미지(.jpg, .png, .gif), 비디오, 압축 파일(.zip, .rar), 실행 파일 및 기타 바이너리 형식. 온라인 전용 클라우드 파일(OneDrive, Google Drive, iCloud)도 건너뛰며, 로컬에 다운로드되면 다음 스캔에서 인덱싱됩니다. 건너뛴 파일도 파일명으로 검색 가능합니다.", ["fr"] = "Les fichiers sont ignorés quand l'extraction n'est pas supportée : images, vidéos, archives, exécutables et formats binaires. Les fichiers cloud en ligne uniquement sont également ignorés. Tous les fichiers ignorés restent cherchables par nom.", ["de"] = "Dateien werden übersprungen, wenn die Inhaltsextraktion nicht unterstützt wird: Bilder, Videos, Archive, ausführbare Dateien und binäre Formate. Reine Cloud-Dateien werden ebenfalls übersprungen. Alle übersprungenen Dateien sind weiterhin nach Dateinamen suchbar.", ["zh"] = "不支持内容提取的文件将被跳过：图片、视频、压缩包、可执行文件和其他二进制格式。仅在线的云文件也会被跳过。所有被跳过的文件仍可通过文件名搜索。" },
            [StringKeys.Data.AiSearchTitle] = new() { ["en"] = "AI semantic search", ["ko"] = "AI 의미 검색", ["fr"] = "Recherche sémantique IA", ["de"] = "KI-semantische Suche", ["zh"] = "AI语义搜索" },

            // ── Security ──
            [StringKeys.Security.Header] = new() { ["en"] = "Privacy & Security", ["ko"] = "개인정보 보호 및 보안", ["fr"] = "Confidentialité et sécurité", ["de"] = "Datenschutz & Sicherheit", ["zh"] = "隐私与安全" },
            [StringKeys.Security.Subtitle] = new() { ["en"] = "Your data stays on your machine. Always.", ["ko"] = "데이터는 항상 사용자의 기기에만 저장됩니다.", ["fr"] = "Vos données restent sur votre machine. Toujours.", ["de"] = "Ihre Daten bleiben auf Ihrem Gerät. Immer.", ["zh"] = "您的数据始终保留在您的设备上。" },
            [StringKeys.Security.OfflineTitle] = new() { ["en"] = "100% offline AI", ["ko"] = "100% 오프라인 AI", ["fr"] = "IA 100% hors ligne", ["de"] = "100% Offline-KI", ["zh"] = "100% 离线 AI" },
            [StringKeys.Security.OfflineDescription] = new() { ["en"] = "All file indexing, search, and AI embedding runs entirely on your local machine. Your documents never leave your device. No login or account required.", ["ko"] = "모든 파일 인덱싱, 검색, AI 임베딩은 사용자의 컴퓨터에서만 실행됩니다. 문서 데이터는 절대 외부로 전송되지 않습니다. 로그인이나 계정이 필요하지 않습니다.", ["fr"] = "Toute l'indexation, la recherche et l'embedding IA s'exécutent entièrement sur votre machine locale. Vos documents ne quittent jamais votre appareil. Aucune connexion ou compte requis.", ["de"] = "Alle Dateiindexierung, Suche und KI-Embedding läuft vollständig auf Ihrem lokalen Computer. Ihre Dokumente verlassen niemals Ihr Gerät. Keine Anmeldung oder Konto erforderlich.", ["zh"] = "所有文件索引、搜索和AI嵌入完全在您的本地计算机上运行。您的文档永远不会离开您的设备。无需登录或账户。" },
            [StringKeys.Security.StorageTitle] = new() { ["en"] = "Data storage", ["ko"] = "데이터 저장소", ["fr"] = "Stockage des données", ["de"] = "Datenspeicher", ["zh"] = "数据存储" },
            [StringKeys.Security.StorageLocation] = new() { ["en"] = "Location", ["ko"] = "위치", ["fr"] = "Emplacement", ["de"] = "Speicherort", ["zh"] = "位置" },
            [StringKeys.Security.StorageSize] = new() { ["en"] = "Size", ["ko"] = "크기", ["fr"] = "Taille", ["de"] = "Größe", ["zh"] = "大小" },
            [StringKeys.Security.OpenDataFolder] = new() { ["en"] = "Open data folder", ["ko"] = "데이터 폴더 열기", ["fr"] = "Ouvrir le dossier de données", ["de"] = "Datenordner öffnen", ["zh"] = "打开数据文件夹" },
            [StringKeys.Security.HowItWorksTitle] = new() { ["en"] = "How your data is protected", ["ko"] = "데이터 보호 방식", ["fr"] = "Comment vos données sont protégées", ["de"] = "Wie Ihre Daten geschützt werden", ["zh"] = "您的数据如何受到保护" },
            [StringKeys.Security.HowItWorksBullet1] = new() { ["en"] = "File contents are processed locally and stored in a SQLite database on your disk", ["ko"] = "파일 내용은 로컬에서 처리되어 디스크의 SQLite 데이터베이스에 저장됩니다", ["fr"] = "Le contenu des fichiers est traité localement et stocké dans une base SQLite sur votre disque", ["de"] = "Dateiinhalte werden lokal verarbeitet und in einer SQLite-Datenbank auf Ihrer Festplatte gespeichert", ["zh"] = "文件内容在本地处理并存储在磁盘上的SQLite数据库中" },
            [StringKeys.Security.HowItWorksBullet2] = new() { ["en"] = "AI embeddings (BGE-M3) run via ONNX Runtime — no API calls, no cloud", ["ko"] = "AI 임베딩(BGE-M3)은 ONNX Runtime으로 실행됩니다 — API 호출 없음, 클라우드 없음", ["fr"] = "Les embeddings IA (BGE-M3) s'exécutent via ONNX Runtime — aucun appel API, aucun cloud", ["de"] = "KI-Embeddings (BGE-M3) laufen über ONNX Runtime — keine API-Aufrufe, keine Cloud", ["zh"] = "AI嵌入(BGE-M3)通过ONNX Runtime运行——无API调用，无云端" },
            [StringKeys.Security.HowItWorksBullet3] = new() { ["en"] = "MCP server communicates only with local AI clients via stdio — no network", ["ko"] = "MCP 서버는 stdio로 로컬 AI 클라이언트와만 통신합니다 — 네트워크 사용 없음", ["fr"] = "Le serveur MCP communique uniquement avec les clients IA locaux via stdio — aucun réseau", ["de"] = "Der MCP-Server kommuniziert nur mit lokalen KI-Clients über stdio — kein Netzwerk", ["zh"] = "MCP服务器仅通过stdio与本地AI客户端通信——无网络" },
            [StringKeys.Security.HowItWorksBullet4] = new() { ["en"] = "Uninstalling removes the app. Delete the data folder to remove all indexed data.", ["ko"] = "앱을 제거하면 앱만 삭제됩니다. 인덱싱된 데이터를 모두 지우려면 데이터 폴더를 삭제하세요.", ["fr"] = "La désinstallation supprime l'application. Supprimez le dossier de données pour retirer toutes les données indexées.", ["de"] = "Die Deinstallation entfernt die App. Löschen Sie den Datenordner, um alle indexierten Daten zu entfernen.", ["zh"] = "卸载仅删除应用程序。删除数据文件夹以移除所有索引数据。" },
            [StringKeys.Security.HowItWorksBullet5] = new() { ["en"] = "Once daily, LocalSynapse sends anonymous app metadata and aggregated usage stats — no documents, filenames, or search queries are ever transmitted", ["ko"] = "하루 한 번, LocalSynapse는 익명 앱 메타데이터와 집계된 사용 통계를 전송합니다 — 문서, 파일명, 검색어는 절대 전송되지 않습니다", ["fr"] = "Une fois par jour, LocalSynapse envoie des métadonnées anonymes et des statistiques d'utilisation agrégées — aucun document, nom de fichier ou requête de recherche n'est transmis", ["de"] = "Einmal täglich sendet LocalSynapse anonyme App-Metadaten und aggregierte Nutzungsstatistiken — keine Dokumente, Dateinamen oder Suchanfragen werden übertragen", ["zh"] = "每天一次，LocalSynapse发送匿名应用元数据和汇总使用统计——不会传输任何文档、文件名或搜索查询" },

            // ── Settings ──
            [StringKeys.Settings.Header] = new() { ["en"] = "Settings", ["ko"] = "설정", ["fr"] = "Paramètres", ["de"] = "Einstellungen", ["zh"] = "设置" },
            [StringKeys.Settings.Subtitle] = new() { ["en"] = "Configure LocalSynapse preferences", ["ko"] = "LocalSynapse 환경설정", ["fr"] = "Configurer les préférences de LocalSynapse", ["de"] = "LocalSynapse-Einstellungen konfigurieren", ["zh"] = "配置LocalSynapse偏好设置" },
            [StringKeys.Settings.LanguageTitle] = new() { ["en"] = "Language", ["ko"] = "언어", ["fr"] = "Langue", ["de"] = "Sprache", ["zh"] = "语言" },
            [StringKeys.Settings.LanguageCurrent] = new() { ["en"] = "Current", ["ko"] = "현재", ["fr"] = "Actuelle", ["de"] = "Aktuell", ["zh"] = "当前" },
            [StringKeys.Settings.LanguageSearchHint] = new() { ["en"] = "Search is optimized for the selected language. Selecting a language applies language-specific search rules for better results.", ["ko"] = "선택한 언어에 최적화된 검색이 적용됩니다. 언어를 선택하면 해당 언어에 맞는 검색 규칙이 적용되어 더 정확한 결과를 제공합니다.", ["fr"] = "La recherche est optimisée pour la langue sélectionnée. Le choix d'une langue applique des règles de recherche spécifiques pour de meilleurs résultats.", ["de"] = "Die Suche ist für die ausgewählte Sprache optimiert. Die Sprachauswahl wendet sprachspezifische Suchregeln für bessere Ergebnisse an.", ["zh"] = "搜索已针对所选语言进行优化。选择语言后将应用相应的语言搜索规则，以提供更准确的结果。" },
            // ── Settings > Performance ──
            [StringKeys.Settings.Performance.Title] = new() { ["en"] = "Indexing performance", ["ko"] = "인덱싱 성능 모드", ["fr"] = "Performance d'indexation", ["de"] = "Indexierungsleistung", ["zh"] = "索引性能" },
            [StringKeys.Settings.Performance.Subtitle] = new() { ["en"] = "Choose how aggressively LocalSynapse indexes your documents. You can change this anytime.", ["ko"] = "LocalSynapse가 문서를 얼마나 적극적으로 인덱싱할지 선택하세요. 언제든 변경할 수 있습니다.", ["fr"] = "Choisissez l'intensité d'indexation de vos documents. Modifiable à tout moment.", ["de"] = "Wählen Sie, wie intensiv LocalSynapse Ihre Dokumente indexiert. Jederzeit änderbar.", ["zh"] = "选择 LocalSynapse 索引文档的强度。您可以随时更改。" },
            [StringKeys.Settings.Performance.StealthLabel] = new() { ["en"] = "Stealth", ["ko"] = "Stealth", ["fr"] = "Stealth", ["de"] = "Stealth", ["zh"] = "Stealth" },
            [StringKeys.Settings.Performance.StealthTech] = new() { ["en"] = "1 thread · low priority · ~5× slower", ["ko"] = "1 스레드 · 낮은 우선순위 · 약 5배 느림", ["fr"] = "1 thread · priorité basse · ~5× plus lent", ["de"] = "1 Thread · niedrige Priorität · ~5× langsamer", ["zh"] = "1 线程 · 低优先级 · 约慢5倍" },
            [StringKeys.Settings.Performance.StealthDesc] = new() { ["en"] = "Stays whisper-quiet — perfect for meetings or cafés", ["ko"] = "회의 중에도 팬 소리 없음, 카페에서 배터리 절약", ["fr"] = "Reste discret — idéal pour les réunions ou les cafés", ["de"] = "Flüsterleise — perfekt für Meetings oder Cafés", ["zh"] = "安静运行 · 适合会议或咖啡馆" },
            [StringKeys.Settings.Performance.CruiseLabel] = new() { ["en"] = "Cruise", ["ko"] = "Cruise", ["fr"] = "Cruise", ["de"] = "Cruise", ["zh"] = "Cruise" },
            [StringKeys.Settings.Performance.CruiseTech] = new() { ["en"] = "50% of cores · ~2× slower · default", ["ko"] = "코어 50% · 약 2배 느림 · 기본값", ["fr"] = "50% des cœurs · ~2× plus lent · par défaut", ["de"] = "50% der Kerne · ~2× langsamer · Standard", ["zh"] = "50% 核心 · 约慢2倍 · 默认" },
            [StringKeys.Settings.Performance.CruiseDesc] = new() { ["en"] = "Indexes in the background while you work", ["ko"] = "Word나 Outlook 쓰는 동안 백그라운드에서 조용히 진행", ["fr"] = "Indexe en arrière-plan pendant que vous travaillez", ["de"] = "Indexiert im Hintergrund, während Sie arbeiten", ["zh"] = "在您工作时后台索引" },
            [StringKeys.Settings.Performance.OverdriveLabel] = new() { ["en"] = "Overdrive", ["ko"] = "Overdrive", ["fr"] = "Overdrive", ["de"] = "Overdrive", ["zh"] = "Overdrive" },
            [StringKeys.Settings.Performance.OverdriveTech] = new() { ["en"] = "all cores · full throttle · baseline speed", ["ko"] = "모든 코어 · 풀스로틀 · 기본 속도", ["fr"] = "tous les cœurs · pleine puissance · vitesse de base", ["de"] = "alle Kerne · Vollgas · Basisgeschwindigkeit", ["zh"] = "全部核心 · 全速运行 · 基准速度" },
            [StringKeys.Settings.Performance.OverdriveDesc] = new() { ["en"] = "Best when you step away — lunch breaks, end of day", ["ko"] = "자리 비울 때 — 점심시간, 퇴근 직전", ["fr"] = "Idéal quand vous vous absentez — pause déjeuner, fin de journée", ["de"] = "Am besten wenn Sie weg sind — Mittagspause, Feierabend", ["zh"] = "最适合离开时使用 · 午休、下班前" },
            [StringKeys.Settings.Performance.MadMaxLabel] = new() { ["en"] = "Mad Max", ["ko"] = "Mad Max", ["fr"] = "Mad Max", ["de"] = "Mad Max", ["zh"] = "Mad Max" },
            [StringKeys.Settings.Performance.MadMaxTech] = new() { ["en"] = "GPU + all cores · up to 5× faster · GPU required", ["ko"] = "GPU + 모든 코어 · 최대 5배 빠름 · GPU 필요", ["fr"] = "GPU + tous les cœurs · jusqu'à 5× plus rapide · GPU requis", ["de"] = "GPU + alle Kerne · bis zu 5× schneller · GPU erforderlich", ["zh"] = "GPU + 全部核心 · 最高快5倍 · 需要GPU" },
            [StringKeys.Settings.Performance.MadMaxDesc] = new() { ["en"] = "Unleash your graphics card — fastest possible", ["ko"] = "그래픽카드 풀가동, 가능한 한 가장 빠른 속도", ["fr"] = "Libérez votre carte graphique — le plus rapide possible", ["de"] = "Entfesseln Sie Ihre Grafikkarte — schnellstmöglich", ["zh"] = "释放显卡性能 · 最快速度" },
            [StringKeys.Settings.Performance.MadMaxDetected] = new() { ["en"] = "Detected: {0} ({1})", ["ko"] = "감지됨: {0} ({1})", ["fr"] = "Détecté : {0} ({1})", ["de"] = "Erkannt: {0} ({1})", ["zh"] = "已检测: {0} ({1})" },
            [StringKeys.Settings.Performance.MadMaxUnavailable] = new() { ["en"] = "No compatible GPU detected on this device", ["ko"] = "호환되는 GPU가 감지되지 않았습니다", ["fr"] = "Aucun GPU compatible détecté sur cet appareil", ["de"] = "Keine kompatible GPU auf diesem Gerät erkannt", ["zh"] = "未检测到兼容的GPU" },
            [StringKeys.Settings.Performance.MadMaxComing] = new() { ["en"] = "Available in v2.11.0", ["ko"] = "v2.11.0에 제공 예정", ["fr"] = "Disponible dans v2.11.0", ["de"] = "Verfügbar in v2.11.0", ["zh"] = "将在 v2.11.0 中提供" },

            [StringKeys.Settings.AboutTitle] = new() { ["en"] = "About", ["ko"] = "정보", ["fr"] = "À propos", ["de"] = "Über", ["zh"] = "关于" },
            [StringKeys.Settings.AboutVersion] = new() { ["en"] = "Version", ["ko"] = "버전", ["fr"] = "Version", ["de"] = "Version", ["zh"] = "版本" },
            [StringKeys.Settings.AboutData] = new() { ["en"] = "Data", ["ko"] = "데이터", ["fr"] = "Données", ["de"] = "Daten", ["zh"] = "数据" },
            [StringKeys.Settings.AboutLicense] = new() { ["en"] = "License", ["ko"] = "라이선스", ["fr"] = "Licence", ["de"] = "Lizenz", ["zh"] = "许可证" },
            [StringKeys.Settings.LinksTitle] = new() { ["en"] = "Links", ["ko"] = "링크", ["fr"] = "Liens", ["de"] = "Links", ["zh"] = "链接" },
            [StringKeys.Settings.LinksWebsite] = new() { ["en"] = "Website", ["ko"] = "웹사이트", ["fr"] = "Site web", ["de"] = "Webseite", ["zh"] = "网站" },
            [StringKeys.Settings.LinksGitHub] = new() { ["en"] = "GitHub", ["ko"] = "GitHub", ["fr"] = "GitHub", ["de"] = "GitHub", ["zh"] = "GitHub" },

            // ── Mcp ──
            [StringKeys.Mcp.Header] = new() { ["en"] = "MCP Server", ["ko"] = "MCP 서버", ["fr"] = "Serveur MCP", ["de"] = "MCP-Server", ["zh"] = "MCP服务器" },
            [StringKeys.Mcp.Subtitle] = new() { ["en"] = "Connect LocalSynapse to AI coding agents via Model Context Protocol", ["ko"] = "Model Context Protocol로 AI 코딩 에이전트와 LocalSynapse 연결", ["fr"] = "Connecter LocalSynapse aux agents IA via Model Context Protocol", ["de"] = "LocalSynapse über Model Context Protocol mit KI-Agenten verbinden", ["zh"] = "通过Model Context Protocol将LocalSynapse连接到AI编码代理" },
            [StringKeys.Mcp.ServerInfoTitle] = new() { ["en"] = "Server Info", ["ko"] = "서버 정보", ["fr"] = "Infos serveur", ["de"] = "Server-Info", ["zh"] = "服务器信息" },
            [StringKeys.Mcp.Executable] = new() { ["en"] = "Executable", ["ko"] = "실행 파일", ["fr"] = "Exécutable", ["de"] = "Ausführbare Datei", ["zh"] = "可执行文件" },
            [StringKeys.Mcp.Transport] = new() { ["en"] = "Transport", ["ko"] = "전송", ["fr"] = "Transport", ["de"] = "Transport", ["zh"] = "传输" },
            [StringKeys.Mcp.TransportValue] = new() { ["en"] = "stdio (JSON-RPC)", ["ko"] = "stdio (JSON-RPC)", ["fr"] = "stdio (JSON-RPC)", ["de"] = "stdio (JSON-RPC)", ["zh"] = "stdio (JSON-RPC)" },
            [StringKeys.Mcp.Command] = new() { ["en"] = "Command", ["ko"] = "명령어", ["fr"] = "Commande", ["de"] = "Befehl", ["zh"] = "命令" },
            [StringKeys.Mcp.ClientsTitle] = new() { ["en"] = "Clients", ["ko"] = "클라이언트", ["fr"] = "Clients", ["de"] = "Clients", ["zh"] = "客户端" },
            [StringKeys.Mcp.Connect] = new() { ["en"] = "Connect", ["ko"] = "연결", ["fr"] = "Connecter", ["de"] = "Verbinden", ["zh"] = "连接" },
            [StringKeys.Mcp.Disconnect] = new() { ["en"] = "Disconnect", ["ko"] = "연결 해제", ["fr"] = "Déconnecter", ["de"] = "Trennen", ["zh"] = "断开" },
            [StringKeys.Mcp.OpenConfigFolder] = new() { ["en"] = "Open config folder", ["ko"] = "설정 폴더 열기", ["fr"] = "Ouvrir le dossier de config", ["de"] = "Konfigurationsordner öffnen", ["zh"] = "打开配置文件夹" },
            [StringKeys.Mcp.RegisteredNotice] = new() { ["en"] = "✓ LocalSynapse is registered in Claude Desktop. Restart Claude Desktop to apply.", ["ko"] = "✓ LocalSynapse가 Claude Desktop에 등록되었습니다. 적용하려면 Claude Desktop을 재시작하세요.", ["fr"] = "✓ LocalSynapse est enregistré dans Claude Desktop. Redémarrez Claude Desktop pour appliquer.", ["de"] = "✓ LocalSynapse ist in Claude Desktop registriert. Starten Sie Claude Desktop neu.", ["zh"] = "✓ LocalSynapse已在Claude Desktop中注册。请重启Claude Desktop以生效。" },
            [StringKeys.Mcp.RunCommandHint] = new() { ["en"] = "Run this command in your terminal to register:", ["ko"] = "등록하려면 터미널에서 다음 명령을 실행하세요:", ["fr"] = "Exécutez cette commande dans votre terminal :", ["de"] = "Führen Sie diesen Befehl in Ihrem Terminal aus:", ["zh"] = "在终端中运行此命令进行注册：" },
            [StringKeys.Mcp.ToRemoveHint] = new() { ["en"] = "To remove:", ["ko"] = "제거하려면:", ["fr"] = "Pour supprimer :", ["de"] = "Zum Entfernen:", ["zh"] = "要移除：" },
            [StringKeys.Mcp.AvailableToolsTitle] = new() { ["en"] = "Available MCP Tools", ["ko"] = "사용 가능한 MCP 도구", ["fr"] = "Outils MCP disponibles", ["de"] = "Verfügbare MCP-Tools", ["zh"] = "可用MCP工具" },
            [StringKeys.Mcp.QuickStartTitle] = new() { ["en"] = "Quick Start Guide", ["ko"] = "빠른 시작 가이드", ["fr"] = "Guide de démarrage rapide", ["de"] = "Schnellstartanleitung", ["zh"] = "快速入门指南" },
            [StringKeys.Mcp.QuickStart1Title] = new() { ["en"] = "1. Index your files", ["ko"] = "1. 파일 인덱싱", ["fr"] = "1. Indexer vos fichiers", ["de"] = "1. Dateien indexieren", ["zh"] = "1. 索引您的文件" },
            [StringKeys.Mcp.QuickStart1Desc] = new() { ["en"] = "Go to the Data tab and let LocalSynapse scan your folders. Wait for indexing to complete.", ["ko"] = "데이터 탭으로 이동해서 LocalSynapse가 폴더를 스캔하도록 하세요. 인덱싱이 완료될 때까지 기다리세요.", ["fr"] = "Allez dans l'onglet Données et laissez LocalSynapse analyser vos dossiers.", ["de"] = "Gehen Sie zum Daten-Tab und lassen Sie LocalSynapse Ihre Ordner scannen.", ["zh"] = "转到「数据」标签页，让LocalSynapse扫描您的文件夹。" },
            [StringKeys.Mcp.QuickStart2Title] = new() { ["en"] = "2. Connect to Claude", ["ko"] = "2. Claude와 연결", ["fr"] = "2. Connecter à Claude", ["de"] = "2. Mit Claude verbinden", ["zh"] = "2. 连接Claude" },
            [StringKeys.Mcp.QuickStart2Desc] = new() { ["en"] = "Click 'Connect' above for Claude Desktop, or copy the command for Claude Code. Restart Claude after connecting.", ["ko"] = "Claude Desktop은 위의 '연결' 버튼을, Claude Code는 해당 명령을 복사하세요. 연결 후 Claude를 재시작하세요.", ["fr"] = "Cliquez sur 'Connecter' pour Claude Desktop, ou copiez la commande pour Claude Code.", ["de"] = "Klicken Sie auf 'Verbinden' für Claude Desktop oder kopieren Sie den Befehl für Claude Code.", ["zh"] = "点击「连接」按钮连接Claude Desktop，或复制命令用于Claude Code。" },
            [StringKeys.Mcp.QuickStart3Title] = new() { ["en"] = "3. Start asking questions", ["ko"] = "3. 질문 시작", ["fr"] = "3. Commencez à poser des questions", ["de"] = "3. Fragen stellen", ["zh"] = "3. 开始提问" },
            [StringKeys.Mcp.QuickStart3Desc] = new() { ["en"] = "Ask Claude about your files. Try: \"Find all documents related to Q3 budget\" or \"What files did I work on last week?\"", ["ko"] = "Claude에게 파일에 대해 질문하세요. 예: \"Q3 예산 관련 문서 모두 찾아줘\" 또는 \"지난주에 작업한 파일이 뭐야?\"", ["fr"] = "Posez des questions à Claude sur vos fichiers. Essayez : \"Trouve tous les documents liés au budget Q3\"", ["de"] = "Fragen Sie Claude nach Ihren Dateien. Versuchen Sie: \"Finde alle Dokumente zum Q3-Budget\"", ["zh"] = "向Claude询问有关您的文件的问题。试试：「查找所有与Q3预算相关的文档」" },
            [StringKeys.Mcp.NoteLabel] = new() { ["en"] = "Note: ", ["ko"] = "참고: ", ["fr"] = "Note : ", ["de"] = "Hinweis: ", ["zh"] = "注意：" },
            [StringKeys.Mcp.NoteText] = new() { ["en"] = "LocalSynapse does not need to be running for MCP to work. The MCP server starts as a separate process managed by Claude.", ["ko"] = "MCP가 작동하는 데 LocalSynapse가 실행 중일 필요는 없습니다. MCP 서버는 Claude가 관리하는 별도 프로세스로 시작됩니다.", ["fr"] = "LocalSynapse n'a pas besoin d'être en cours d'exécution pour que MCP fonctionne. Le serveur MCP démarre comme un processus séparé géré par Claude.", ["de"] = "LocalSynapse muss nicht laufen, damit MCP funktioniert. Der MCP-Server startet als separater Prozess, der von Claude verwaltet wird.", ["zh"] = "MCP工作不需要LocalSynapse运行。MCP服务器作为Claude管理的独立进程启动。" },

            // ── Update Check ──
            [StringKeys.UpdateCheck.Available] = new() { ["en"] = "{0} available", ["ko"] = "{0} 업데이트 가능", ["fr"] = "{0} disponible", ["de"] = "{0} verfügbar", ["zh"] = "{0}可用" },
            [StringKeys.UpdateCheck.Download] = new() { ["en"] = "Download", ["ko"] = "다운로드", ["fr"] = "Télécharger", ["de"] = "Herunterladen", ["zh"] = "下载" },
            [StringKeys.UpdateCheck.WhatsNew] = new() { ["en"] = "What's new", ["ko"] = "새로운 기능", ["fr"] = "Nouveautés", ["de"] = "Neuigkeiten", ["zh"] = "新功能" },
            [StringKeys.UpdateCheck.Dismiss] = new() { ["en"] = "Dismiss", ["ko"] = "닫기", ["fr"] = "Ignorer", ["de"] = "Schließen", ["zh"] = "关闭" },
            [StringKeys.UpdateCheck.Toggle] = new() { ["en"] = "Check for updates", ["ko"] = "업데이트 확인", ["fr"] = "Vérifier les mises à jour", ["de"] = "Nach Updates suchen", ["zh"] = "检查更新" },
            // FirstRunNotice/Body/Ok/Disable removed (WO-SEC0)
            [StringKeys.UpdateCheck.WhatsNewTitle] = new() { ["en"] = "What's new in v{0}", ["ko"] = "v{0}의 새로운 기능", ["fr"] = "Nouveautés de la v{0}", ["de"] = "Neues in v{0}", ["zh"] = "v{0}的新功能" },
            [StringKeys.UpdateCheck.UpToDate] = new() { ["en"] = "You're up to date", ["ko"] = "최신 버전입니다", ["fr"] = "Vous êtes à jour", ["de"] = "Sie sind auf dem neuesten Stand", ["zh"] = "已是最新版本" },
            [StringKeys.UpdateCheck.UpdateAvailable] = new() { ["en"] = "Update available", ["ko"] = "업데이트가 있습니다", ["fr"] = "Mise à jour disponible", ["de"] = "Update verfügbar", ["zh"] = "有可用更新" },

            // ── Welcome ──
            [StringKeys.Welcome.Title] = new() { ["en"] = "Welcome to LocalSynapse", ["ko"] = "LocalSynapse에 오신 것을 환영합니다", ["fr"] = "Bienvenue sur LocalSynapse", ["de"] = "Willkommen bei LocalSynapse", ["zh"] = "欢迎使用 LocalSynapse" },
            [StringKeys.Welcome.Subtitle] = new() { ["en"] = "Your local file search assistant. Let's set up what to index.", ["ko"] = "로컬 파일 검색 도우미입니다. 인덱싱 대상을 설정하세요.", ["fr"] = "Votre assistant de recherche. Configurons l'indexation.", ["de"] = "Ihr lokaler Dateisuch-Assistent. Richten wir die Indexierung ein.", ["zh"] = "您的本地文件搜索助手。让我们设置索引范围。" },
            [StringKeys.Welcome.ScanAll] = new() { ["en"] = "All Drives", ["ko"] = "전체 드라이브", ["fr"] = "Tous les disques", ["de"] = "Alle Laufwerke", ["zh"] = "所有驱动器" },
            [StringKeys.Welcome.ScanAllDesc] = new() { ["en"] = "Scan everything on all fixed drives", ["ko"] = "모든 고정 드라이브를 스캔합니다", ["fr"] = "Analyser tous les disques fixes", ["de"] = "Alle Festplatten scannen", ["zh"] = "扫描所有固定驱动器" },
            [StringKeys.Welcome.MyDocs] = new() { ["en"] = "My Documents", ["ko"] = "내 문서", ["fr"] = "Mes documents", ["de"] = "Meine Dokumente", ["zh"] = "我的文档" },
            [StringKeys.Welcome.MyDocsDesc] = new() { ["en"] = "Documents, Desktop, and Downloads", ["ko"] = "문서, 바탕화면, 다운로드 폴더", ["fr"] = "Documents, Bureau et Téléchargements", ["de"] = "Dokumente, Desktop und Downloads", ["zh"] = "文档、桌面和下载" },
            [StringKeys.Welcome.Custom] = new() { ["en"] = "Custom", ["ko"] = "직접 선택", ["fr"] = "Personnalisé", ["de"] = "Benutzerdefiniert", ["zh"] = "自定义" },
            [StringKeys.Welcome.CustomDesc] = new() { ["en"] = "Choose exactly which folders to index", ["ko"] = "인덱싱할 폴더를 직접 선택합니다", ["fr"] = "Choisir les dossiers à indexer", ["de"] = "Ordner zum Indexieren auswählen", ["zh"] = "选择要索引的文件夹" },
            [StringKeys.Welcome.AddFolder] = new() { ["en"] = "+ Add Folder", ["ko"] = "+ 폴더 추가", ["fr"] = "+ Ajouter un dossier", ["de"] = "+ Ordner hinzufügen", ["zh"] = "+ 添加文件夹" },
            [StringKeys.Welcome.Start] = new() { ["en"] = "Start Indexing →", ["ko"] = "인덱싱 시작 →", ["fr"] = "Démarrer l'indexation →", ["de"] = "Indexierung starten →", ["zh"] = "开始索引 →" },

            // ── Security: What LocalSynapse sends ──
            [StringKeys.Security.Sends.Title] = new() { ["en"] = "What LocalSynapse sends", ["ko"] = "LocalSynapse가 외부와 주고받는 정보", ["fr"] = "Ce que LocalSynapse envoie", ["de"] = "Was LocalSynapse sendet", ["zh"] = "LocalSynapse 发送的内容" },
            [StringKeys.Security.Sends.Subtitle] = new() { ["en"] = "Full transparency about external communication.", ["ko"] = "외부 통신을 모두 공개합니다.", ["fr"] = "Transparence totale sur la communication externe.", ["de"] = "Volle Transparenz über externe Kommunikation.", ["zh"] = "外部通信完全透明。" },
            [StringKeys.Security.Sends.Receives] = new() { ["en"] = "Receives: GitHub release info & app updates, language model when you choose to install one", ["ko"] = "받음: GitHub 릴리스 정보 및 앱 업데이트, 사용자가 설치를 선택한 경우의 언어 모델", ["fr"] = "Reçoit : infos de version GitHub et mises à jour de l'application, modèle de langue lorsque vous choisissez de l'installer", ["de"] = "Empfängt: GitHub-Release-Infos und App-Updates, Sprachmodell bei Installation auf Wunsch", ["zh"] = "接收：GitHub 发布信息与应用更新；用户选择安装时的语言模型" },
            [StringKeys.Security.Sends.SendsLabel] = new() { ["en"] = "Sends: Anonymous app metadata and usage stats", ["ko"] = "전송: 익명 앱 메타데이터 및 사용 통계", ["fr"] = "Envoie : métadonnées anonymes et statistiques d'utilisation", ["de"] = "Sendet: Anonyme App-Metadaten und Nutzungsstatistiken", ["zh"] = "发送：匿名应用元数据和使用统计" },
            [StringKeys.Security.Sends.Frequency] = new() { ["en"] = "once per day", ["ko"] = "하루 1회", ["fr"] = "une fois par jour", ["de"] = "einmal pro Tag", ["zh"] = "每天一次" },
            [StringKeys.Security.Sends.Toggle] = new() { ["en"] = "External communication", ["ko"] = "외부 통신", ["fr"] = "Communication externe", ["de"] = "Externe Kommunikation", ["zh"] = "外部通信" },
            [StringKeys.Security.Sends.ExpandTitle] = new() { ["en"] = "What does LocalSynapse send?", ["ko"] = "LocalSynapse가 무엇을 전송하나요?", ["fr"] = "Qu'envoie LocalSynapse ?", ["de"] = "Was sendet LocalSynapse?", ["zh"] = "LocalSynapse 发送什么？" },
            [StringKeys.Security.Sends.ExpandBody] = new() { ["en"] = "Once per day, LocalSynapse sends anonymous app metadata and aggregated usage stats to help analyze how the app is used and improve search quality. This cannot be linked to you, your files, or your activity. Your documents are never sent.", ["ko"] = "하루 한 번, LocalSynapse는 앱 사용 분석과 검색 품질 향상을 위해 익명 앱 메타데이터와 집계된 사용 통계를 전송합니다. 이는 사용자, 파일 또는 활동과 연결될 수 없습니다. 문서는 절대 전송되지 않습니다.", ["fr"] = "Une fois par jour, LocalSynapse envoie des métadonnées anonymes et des statistiques d'utilisation agrégées pour analyser l'utilisation et améliorer la qualité de recherche. Cela ne peut pas être lié à vous, vos fichiers ou votre activité. Vos documents ne sont jamais envoyés.", ["de"] = "Einmal täglich sendet LocalSynapse anonyme App-Metadaten und aggregierte Nutzungsstatistiken zur Analyse und Verbesserung der Suchqualität. Dies kann nicht mit Ihnen, Ihren Dateien oder Ihrer Aktivität verknüpft werden. Ihre Dokumente werden nie gesendet.", ["zh"] = "每天一次，LocalSynapse发送匿名应用元数据和汇总使用统计，以帮助分析应用使用情况并提高搜索质量。这些无法与您、您的文件或活动关联。您的文档永远不会被发送。" },
            [StringKeys.Security.Sends.ConfirmTitle] = new() { ["en"] = "Turn off external communication?", ["ko"] = "외부 통신을 끄시겠습니까?", ["fr"] = "Désactiver la communication externe ?", ["de"] = "Externe Kommunikation deaktivieren?", ["zh"] = "关闭外部通信？" },
            [StringKeys.Security.Sends.ConfirmBody] = new() { ["en"] = "LocalSynapse will stop checking for updates and won't send the anonymous version ping. Your documents are never affected by this setting.", ["ko"] = "업데이트 확인이 중단되고 익명 버전 핑도 전송되지 않습니다. 이 설정은 사용자의 문서에 어떤 영향도 주지 않습니다.", ["fr"] = "LocalSynapse cessera de vérifier les mises à jour et n'enverra plus le ping anonyme. Vos documents ne sont jamais affectés par ce paramètre.", ["de"] = "LocalSynapse wird nicht mehr nach Updates suchen und keinen anonymen Ping mehr senden. Ihre Dokumente sind von dieser Einstellung nie betroffen.", ["zh"] = "LocalSynapse 将停止检查更新，也不会发送匿名版本 ping。您的文档不受此设置影响。" },
            [StringKeys.Security.Sends.ConfirmAction] = new() { ["en"] = "Turn off", ["ko"] = "끄기", ["fr"] = "Désactiver", ["de"] = "Deaktivieren", ["zh"] = "关闭" },
            [StringKeys.Security.Sends.ConfirmCancel] = new() { ["en"] = "Cancel", ["ko"] = "취소", ["fr"] = "Annuler", ["de"] = "Abbrechen", ["zh"] = "取消" },
            [StringKeys.Security.Sends.ViewLastSent] = new() { ["en"] = "View what was last sent", ["ko"] = "마지막 전송 내역 보기", ["fr"] = "Voir ce qui a été envoyé", ["de"] = "Zuletzt Gesendetes anzeigen", ["zh"] = "查看上次发送的内容" },
            [StringKeys.Security.Sends.LastSentTitle] = new() { ["en"] = "Last sent payload", ["ko"] = "마지막 전송 데이터", ["fr"] = "Dernières données envoyées", ["de"] = "Zuletzt gesendete Daten", ["zh"] = "上次发送的数据" },
            [StringKeys.Security.Sends.LastSentTimestamp] = new() { ["en"] = "Sent at", ["ko"] = "전송 시각", ["fr"] = "Envoyé le", ["de"] = "Gesendet um", ["zh"] = "发送时间" },
            [StringKeys.Security.Sends.LastSentNone] = new() { ["en"] = "No data has been sent yet.", ["ko"] = "아직 전송된 데이터가 없습니다.", ["fr"] = "Aucune donnée n'a encore été envoyée.", ["de"] = "Es wurden noch keine Daten gesendet.", ["zh"] = "尚未发送任何数据。" },
            [StringKeys.Security.Sends.CopyToClipboard] = new() { ["en"] = "Copy to clipboard", ["ko"] = "클립보드에 복사", ["fr"] = "Copier dans le presse-papiers", ["de"] = "In die Zwischenablage kopieren", ["zh"] = "复制到剪贴板" },
            [StringKeys.Security.Sends.Copied] = new() { ["en"] = "Copied!", ["ko"] = "복사됨!", ["fr"] = "Copié !", ["de"] = "Kopiert!", ["zh"] = "已复制！" },
            [StringKeys.Security.Sends.Close] = new() { ["en"] = "Close", ["ko"] = "닫기", ["fr"] = "Fermer", ["de"] = "Schließen", ["zh"] = "关闭" },

            // ── Banner ──
            [StringKeys.Banner.UpdateAvailable] = new() { ["en"] = "A new version of LocalSynapse is available.", ["ko"] = "LocalSynapse 새 버전을 사용할 수 있습니다.", ["fr"] = "Une nouvelle version de LocalSynapse est disponible.", ["de"] = "Eine neue Version von LocalSynapse ist verfügbar.", ["zh"] = "LocalSynapse 有新版本可用。" },
            [StringKeys.Banner.ViewReleaseNotes] = new() { ["en"] = "View release notes", ["ko"] = "릴리스 노트 보기", ["fr"] = "Voir les notes de version", ["de"] = "Versionshinweise anzeigen", ["zh"] = "查看发布说明" },
            [StringKeys.Banner.Dismiss] = new() { ["en"] = "Dismiss", ["ko"] = "닫기", ["fr"] = "Fermer", ["de"] = "Schließen", ["zh"] = "关闭" },

            // IU-1a Install update flow
            [StringKeys.Banner.InstallUpdate] = new() { ["en"] = "Install update", ["ko"] = "업데이트 설치", ["fr"] = "Installer la mise à jour", ["de"] = "Update installieren", ["zh"] = "安装更新" },
            [StringKeys.Banner.InstallProgress] = new() { ["en"] = "Cancel · {0}%", ["ko"] = "취소 · {0}%", ["fr"] = "Annuler · {0}%", ["de"] = "Abbrechen · {0}%", ["zh"] = "取消 · {0}%" },
            [StringKeys.Banner.InstallVerifying] = new() { ["en"] = "Verifying…", ["ko"] = "확인 중…", ["fr"] = "Vérification…", ["de"] = "Überprüfung…", ["zh"] = "正在验证…" },
            [StringKeys.Banner.InstallLaunching] = new() { ["en"] = "Launching installer…", ["ko"] = "설치 시작 중…", ["fr"] = "Lancement du programme d'installation…", ["de"] = "Installer wird gestartet…", ["zh"] = "正在启动安装程序…" },
            [StringKeys.Banner.InstallRetry] = new() { ["en"] = "Retry", ["ko"] = "다시 시도", ["fr"] = "Réessayer", ["de"] = "Wiederholen", ["zh"] = "重试" },
            [StringKeys.Banner.InstallOpenDownload] = new() { ["en"] = "Open download page", ["ko"] = "다운로드 페이지 열기", ["fr"] = "Ouvrir la page de téléchargement", ["de"] = "Download-Seite öffnen", ["zh"] = "打开下载页面" },
            [StringKeys.Banner.InstallError.Generic] = new() { ["en"] = "Download failed. {0}", ["ko"] = "다운로드 실패. {0}", ["fr"] = "Échec du téléchargement. {0}", ["de"] = "Download fehlgeschlagen. {0}", ["zh"] = "下载失败。{0}" },
            [StringKeys.Banner.InstallError.Network] = new() { ["en"] = "Couldn't reach the download server. Check your connection.", ["ko"] = "다운로드 서버에 연결할 수 없습니다. 연결을 확인하세요.", ["fr"] = "Impossible d'accéder au serveur de téléchargement. Vérifiez votre connexion.", ["de"] = "Download-Server nicht erreichbar. Prüfen Sie Ihre Verbindung.", ["zh"] = "无法连接下载服务器。请检查您的连接。" },
            [StringKeys.Banner.InstallError.Checksum] = new() { ["en"] = "Downloaded file is corrupted. Please retry.", ["ko"] = "다운로드한 파일이 손상되었습니다. 다시 시도하세요.", ["fr"] = "Le fichier téléchargé est corrompu. Veuillez réessayer.", ["de"] = "Heruntergeladene Datei ist beschädigt. Bitte erneut versuchen.", ["zh"] = "下载的文件已损坏。请重试。" },
            [StringKeys.Banner.InstallError.Disk] = new() { ["en"] = "Not enough disk space (~100 MB needed).", ["ko"] = "디스크 공간 부족 (~100 MB 필요).", ["fr"] = "Espace disque insuffisant (~100 Mo requis).", ["de"] = "Nicht genug Speicherplatz (~100 MB benötigt).", ["zh"] = "磁盘空间不足（需要约 100 MB）。" },

        };
    }
}
