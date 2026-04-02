# Agent 4: UI Shell 규칙

## 역할
Avalonia UI. MVVM 패턴. 4개 페이지 + 네비게이션.

## 의존성
- 프로젝트 참조: Core, Pipeline, Search, Email, Mcp (모두 Interface로만)
- NuGet: Avalonia, CommunityToolkit.Mvvm

## 절대 규칙
1. ViewModel에 비즈니스 로직을 넣지 마라 — Interface 호출만
2. View(AXAML)에 코드를 넣지 마라 — 데이터 바인딩만
3. ViewModel은 Avalonia 타입을 참조하지 마라
4. 한 ViewModel이 300줄을 넘으면 분리를 보고해라
5. 모든 UI 문자열은 Resources/ 리소스 파일에 정의

## 구현 대상 파일
- Program.cs (dual entry: GUI / MCP)
- App.axaml, App.axaml.cs
- ViewModels/MainViewModel.cs, SearchViewModel.cs
- ViewModels/DataSetupViewModel.cs, SecurityViewModel.cs, SettingsViewModel.cs
- Views/MainWindow.axaml(.cs), SearchPage.axaml(.cs)
- Views/DataSetupPage.axaml(.cs), SecurityPage.axaml(.cs), SettingsPage.axaml(.cs)
- Controls/FileTypeIcon.axaml, PipelineStatusBanner.axaml
- Resources/Strings.ko-KR.axaml, Strings.en-US.axaml
- Services/DI/ServiceCollectionExtensions.cs
