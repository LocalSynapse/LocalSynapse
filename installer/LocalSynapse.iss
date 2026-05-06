; ══════════════════════════════════════════════════════════════
; LocalSynapse Inno Setup Script
; Generates Setup.exe with language selection + feature intro
; ══════════════════════════════════════════════════════════════

#ifndef MyAppVersion
  #define MyAppVersion "2.0.0"
#endif

#define MyAppName "LocalSynapse"
#define MyAppPublisher "LocalSynapse"
#define MyAppURL "https://localsynapse.com"
#define MyAppExeName "LocalSynapse.exe"

[Setup]
AppId={{B7A1E3D2-4F5C-4E8B-9A6D-1C2E3F4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\publish\installer
OutputBaseFilename=LocalSynapse-v{#MyAppVersion}-Windows-Setup
SetupIconFile=..\assets\app-icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=110,110
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0
; 'force' makes the installer proceed past Restart Manager session-mismatch
; failures rather than surfacing the abort dialog. The MCP child process
; (localsynapse-mcp.exe) does not respond to RM's graceful-shutdown protocol;
; this directive paired with the PrepareToInstall taskkill below is the
; complete defense.
CloseApplications=force
RestartApplications=no
; Always-on installer log written to %TEMP%\Setup Log YYYY-MM-DD #N.txt.
; Enables future support requests to attach a log without re-running with /LOG=.
SetupLogging=yes

; ══════════════════════════════════════
; Languages
; ══════════════════════════════════════
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "chinese"; MessagesFile: "ChineseSimplified.isl"

; ══════════════════════════════════════
; Custom Messages (bilingual)
; ══════════════════════════════════════
[CustomMessages]
; Feature page
english.FeatureTitle=Welcome to LocalSynapse
english.FeatureDesc=AI Memory Layer for Your PC
english.Feature1=>> Search inside files%nFind documents by content across all your drives — not just filenames.
english.Feature2=>> 100%% offline%nAll AI processing runs locally. No cloud, no login, no data transfer.
english.Feature3=>> MCP Server built-in%nConnect Claude, Cursor, or any AI coding agent to search your local files.
english.Feature4=>> Hybrid search%nBM25 keyword + BGE-M3 semantic search combined for best results.
english.FeatureNote=System requirements: Windows 10+, 4 GB RAM, ~200 MB disk (+ 580 MB for optional AI model)

korean.FeatureTitle=LocalSynapse에 오신 것을 환영합니다
korean.FeatureDesc=PC를 위한 AI 메모리 레이어
korean.Feature1=>> 파일 내용 검색%n파일 이름이 아닌 내용으로 검색합니다. 모든 드라이브의 문서를 찾습니다.
korean.Feature2=>> 100%% 오프라인%n모든 AI 처리가 로컬에서 실행됩니다. 클라우드, 로그인, 데이터 전송 없음.
korean.Feature3=>> MCP 서버 내장%nClaude, Cursor 등 AI 코딩 에이전트와 연결하여 파일을 검색합니다.
korean.Feature4=>> 하이브리드 검색%nBM25 키워드 + BGE-M3 시맨틱 검색을 결합하여 최상의 결과를 제공합니다.
korean.FeatureNote=시스템 요구사항: Windows 10 이상, RAM 4 GB, 디스크 ~200 MB (선택적 AI 모델 580 MB 추가)

french.FeatureTitle=Bienvenue dans LocalSynapse
french.FeatureDesc=Couche mémoire IA pour votre PC
french.Feature1=>> Recherche dans les fichiers%nTrouvez des documents par leur contenu sur tous vos disques, pas seulement par nom.
french.Feature2=>> 100%% hors ligne%nTout le traitement IA s'exécute localement. Pas de cloud, pas de connexion, pas de transfert de données.
french.Feature3=>> Serveur MCP intégré%nConnectez Claude, Cursor ou tout agent IA pour rechercher vos fichiers locaux.
french.Feature4=>> Recherche hybride%nBM25 mots-clés + recherche sémantique BGE-M3 combinés pour les meilleurs résultats.
french.FeatureNote=Configuration requise : Windows 10+, 4 Go RAM, ~200 Mo disque (+ 580 Mo pour le modèle IA optionnel)

german.FeatureTitle=Willkommen bei LocalSynapse
german.FeatureDesc=KI-Speicherschicht für Ihren PC
german.Feature1=>> Dateiinhalte durchsuchen%nFinden Sie Dokumente nach Inhalt auf allen Laufwerken — nicht nur nach Dateinamen.
german.Feature2=>> 100%% offline%nAlle KI-Verarbeitung läuft lokal. Keine Cloud, kein Login, keine Datenübertragung.
german.Feature3=>> MCP-Server integriert%nVerbinden Sie Claude, Cursor oder andere KI-Agenten, um Ihre lokalen Dateien zu durchsuchen.
german.Feature4=>> Hybride Suche%nBM25-Stichwort + BGE-M3 semantische Suche kombiniert für beste Ergebnisse.
german.FeatureNote=Systemanforderungen: Windows 10+, 4 GB RAM, ~200 MB Festplatte (+ 580 MB für optionales KI-Modell)

chinese.FeatureTitle=欢迎使用 LocalSynapse
chinese.FeatureDesc=您电脑的 AI 记忆层
chinese.Feature1=>> 文件内容搜索%n通过内容搜索文档，覆盖所有驱动器——不仅仅是文件名。
chinese.Feature2=>> 100%% 离线%n所有 AI 处理在本地运行。无云端、无登录、无数据传输。
chinese.Feature3=>> 内置 MCP 服务器%n连接 Claude、Cursor 或任何 AI 编码代理来搜索您的本地文件。
chinese.Feature4=>> 混合搜索%nBM25 关键词 + BGE-M3 语义搜索组合，提供最佳结果。
chinese.FeatureNote=系统要求：Windows 10+，4 GB 内存，约 200 MB 磁盘空间（可选 AI 模型额外 580 MB）

; Tasks
english.TaskDesktopIcon=Create a desktop shortcut
english.TaskStartup=Start LocalSynapse with Windows
korean.TaskDesktopIcon=바탕화면 바로가기 만들기
korean.TaskStartup=Windows 시작 시 자동 실행
french.TaskDesktopIcon=Créer un raccourci sur le bureau
french.TaskStartup=Démarrer LocalSynapse avec Windows
german.TaskDesktopIcon=Desktopverknüpfung erstellen
german.TaskStartup=LocalSynapse mit Windows starten
chinese.TaskDesktopIcon=创建桌面快捷方式
chinese.TaskStartup=开机自动启动 LocalSynapse

; Uninstall
english.DeleteDataPrompt=Do you also want to delete all LocalSynapse data (search index, settings)?%n%nData folder: %1
korean.DeleteDataPrompt=LocalSynapse 데이터(검색 인덱스, 설정)도 삭제하시겠습니까?%n%n데이터 폴더: %1
french.DeleteDataPrompt=Voulez-vous également supprimer toutes les données LocalSynapse (index de recherche, paramètres) ?%n%nDossier de données : %1
german.DeleteDataPrompt=Möchten Sie auch alle LocalSynapse-Daten (Suchindex, Einstellungen) löschen?%n%nDatenordner: %1
chinese.DeleteDataPrompt=是否同时删除所有 LocalSynapse 数据（搜索索引、设置）？%n%n数据文件夹：%1

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "{cm:TaskStartup}"; GroupDescription: "{cm:AdditionalIcons}"

; ══════════════════════════════════════
; Files
; ══════════════════════════════════════
[Files]
; 'restartreplace' is an ADMIN-ONLY safety net. The flag uses
; MoveFileEx(MOVEFILE_DELAY_UNTIL_REBOOT) which requires admin privileges to
; actually move the file at next boot — it is a no-op for per-user installs.
; PrepareToInstall's taskkill remains the PRIMARY defense for both per-user
; and per-machine installs. This flag is added because:
;  (1) admin-elevated installs get a real safety net if any file remains
;      locked despite taskkill (e.g., AV scanning, race window).
;  (2) The flag is harmless when admin is absent; no behavior regression.
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

; ══════════════════════════════════════
; Pascal Script — Feature page + Uninstall cleanup
; ══════════════════════════════════════
[Code]
var
  FeaturePage: TWizardPage;

// Detect a prior all-users install in HKLM, warn the user before this
// per-user run creates a side-by-side duplicate.
//
// Background: with PrivilegesRequired=lowest + PrivilegesRequiredOverridesAllowed=dialog,
// Inno Setup's runtime elevation choice determines whether the install lands in
// Program Files (all-users, HKLM) or %LocalAppData%\Programs (per-user, HKCU).
// AppId match across the two scopes is detected, but the previous uninstaller
// cannot run across an elevation boundary in the same setup invocation —
// the result is two side-by-side installations with shared shortcuts.
// We cannot auto-elevate here (would force PrivilegesRequired=admin and
// break unprivileged installs entirely), so we warn and proceed.
function InitializeSetup(): Boolean;
var
  HasHklmInstall: Boolean;
  HklmUninstall: String;
begin
  // HKLM is globally readable; no elevation needed for this query.
  HasHklmInstall := RegQueryStringValue(HKLM,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B7A1E3D2-4F5C-4E8B-9A6D-1C2E3F4A5B6C}_is1',
    'UninstallString', HklmUninstall);
  if HasHklmInstall and not IsAdminInstallMode() then
  begin
    // WizardSilent() returns True under both /SILENT and /VERYSILENT.
    // Silent mode = log + proceed; do not abort, do not surface MsgBox
    // (CI / scripted callers depend on no UI).
    if WizardSilent() then
    begin
      Log('WARNING: HKLM previous install detected (UninstallString=' + HklmUninstall +
          '). Current run is non-elevated; this will create a side-by-side per-user install ' +
          'rather than upgrading. To upgrade in place, manually uninstall the all-users copy first.');
    end
    else
    begin
      MsgBox('A previous LocalSynapse install was detected in Program Files (all users).' + #13#10 + #13#10 +
             'Continuing without administrator permission will create a separate per-user installation alongside the existing one.' + #13#10 + #13#10 +
             'Recommended: cancel this installer, uninstall the existing copy from Windows Settings → Apps, then re-run this installer.',
             mbInformation, MB_OK);
    end;
  end;
  Result := True;  // True = proceed with installation
end;

// localsynapse-mcp.exe is spawned as a stdio child by Claude Desktop /
// Claude Code (registered in claude_desktop_config.json). When upgrading,
// Restart Manager detects this child process but cannot gracefully shut
// it down — the MCP server is a console process with no signal handlers,
// so it ignores RM's WM_QUERYENDSESSION / CTRL_SHUTDOWN_EVENT requests.
// RM waits 30s, times out, and the install aborts under /SUPPRESSMSGBOXES.
//
// taskkill /F bypasses the graceful shutdown protocol entirely. A future
// change to LocalSynapse.Mcp.Stdio adding signal handlers will make this
// taskkill redundant (but harmless to keep as defense in depth).
//
// !! DO NOT REMOVE without first confirming the MCP server has been
// !! updated to handle CTRL_SHUTDOWN_EVENT cleanly.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM localsynapse-mcp.exe',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM LocalSynapse.exe',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CreateFeaturePage;
var
  TitleLabel: TNewStaticText;
  SubtitleLabel: TNewStaticText;
  Memo: TNewMemo;
  NoteLabel: TNewStaticText;
begin
  FeaturePage := CreateCustomPage(wpWelcome,
    CustomMessage('FeatureTitle'),
    CustomMessage('FeatureDesc'));

  TitleLabel := TNewStaticText.Create(FeaturePage);
  TitleLabel.Parent := FeaturePage.Surface;
  TitleLabel.Caption := 'LocalSynapse';
  TitleLabel.Font.Size := 18;
  TitleLabel.Font.Style := [fsBold];
  TitleLabel.Top := 8;
  TitleLabel.Left := 0;
  TitleLabel.AutoSize := True;

  SubtitleLabel := TNewStaticText.Create(FeaturePage);
  SubtitleLabel.Parent := FeaturePage.Surface;
  SubtitleLabel.Caption := CustomMessage('FeatureDesc');
  SubtitleLabel.Font.Size := 10;
  SubtitleLabel.Font.Color := $666666;
  SubtitleLabel.Top := 38;
  SubtitleLabel.Left := 0;
  SubtitleLabel.AutoSize := True;

  Memo := TNewMemo.Create(FeaturePage);
  Memo.Parent := FeaturePage.Surface;
  Memo.Top := 70;
  Memo.Left := 0;
  Memo.Width := FeaturePage.SurfaceWidth;
  Memo.Height := 175;
  Memo.ReadOnly := True;
  Memo.ScrollBars := ssNone;
  Memo.Color := $FAFAFA;
  Memo.Font.Size := 9;
  Memo.Font.Name := 'Segoe UI';
  Memo.Lines.Add(CustomMessage('Feature1'));
  Memo.Lines.Add('');
  Memo.Lines.Add(CustomMessage('Feature2'));
  Memo.Lines.Add('');
  Memo.Lines.Add(CustomMessage('Feature3'));
  Memo.Lines.Add('');
  Memo.Lines.Add(CustomMessage('Feature4'));

  NoteLabel := TNewStaticText.Create(FeaturePage);
  NoteLabel.Parent := FeaturePage.Surface;
  NoteLabel.Caption := CustomMessage('FeatureNote');
  NoteLabel.Font.Size := 8;
  NoteLabel.Font.Color := $999999;
  NoteLabel.Top := 245;
  NoteLabel.Left := 0;
  NoteLabel.AutoSize := True;
  NoteLabel.WordWrap := True;
  NoteLabel.Width := FeaturePage.SurfaceWidth;
end;

procedure InitializeWizard;
begin
  CreateFeaturePage;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataFolder: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataFolder := ExpandConstant('{localappdata}\LocalSynapse');
    if DirExists(DataFolder) then
    begin
      if MsgBox(FmtMessage(CustomMessage('DeleteDataPrompt'), [DataFolder]),
                mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(DataFolder, True, True, True);
      end;
    end;
  end;
end;
