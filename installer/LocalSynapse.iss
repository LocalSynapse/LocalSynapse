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
CloseApplications=yes
RestartApplications=no

; ══════════════════════════════════════
; Languages
; ══════════════════════════════════════
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

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

; Tasks
english.TaskDesktopIcon=Create a desktop shortcut
english.TaskStartup=Start LocalSynapse with Windows
korean.TaskDesktopIcon=바탕화면 바로가기 만들기
korean.TaskStartup=Windows 시작 시 자동 실행

; Uninstall
english.DeleteDataPrompt=Do you also want to delete all LocalSynapse data (search index, settings)?%n%nData folder: %1
korean.DeleteDataPrompt=LocalSynapse 데이터(검색 인덱스, 설정)도 삭제하시겠습니까?%n%n데이터 폴더: %1

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "{cm:TaskStartup}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; ══════════════════════════════════════
; Files
; ══════════════════════════════════════
[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

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
