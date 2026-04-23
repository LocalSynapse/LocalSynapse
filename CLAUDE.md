# LocalSynapse v2.0 Development Guide

## ⚠️ Read Before Every Task

This project uses an Agent-based distributed architecture.
Each Agent (project) has an AGENT.md with rules that must be followed.

## Absolute Rules

1. **Do NOT modify files outside your assigned Agent project**
2. **Do NOT modify files in Interfaces/ folders** (Contracts are finalized)
3. **Do NOT add/remove/change public properties in Models/**
4. **Report and get approval before adding any new public class**
5. **Do NOT reference another Agent's implementation directly** (use Interfaces only)

## Dependency Direction (violations cause build failure)

```
Core ← Pipeline
Core ← Search
Core ← Email
Core, Search ← Mcp
Core, Search, Mcp ← Mcp.Stdio
Core, Pipeline, Search, Email ← UI
```

Reverse references are forbidden. Pipeline must not reference Search. Core must not reference UI.
Mcp.Stdio must not reference Pipeline or UI.

## Project Structure

```
src/
├── LocalSynapse.Core/        # Models, Interfaces, DB — no dependencies
├── LocalSynapse.Pipeline/    # Scan, parse, chunk, embed — Core only
├── LocalSynapse.Search/      # BM25, Dense, Hybrid search — Core only
├── LocalSynapse.Email/       # Email parsing, Graph sync — Core only
├── LocalSynapse.Mcp/         # MCP stdio server — Core, Search
├── LocalSynapse.Mcp.Stdio/   # Headless MCP stdio server — Core, Search, Mcp
└── LocalSynapse.UI/          # Avalonia MVVM — references all (entry point)
```

## Coding Rules

- **Namespace = folder structure**. `LocalSynapse.Core.Models`, `LocalSynapse.Search.Services`, etc.
- **One public class per file**. Exception: small records/enums may be co-located.
- **XML doc comments required** on all public methods.
- **CancellationToken**: last parameter on every async method.
- **Logging**: use `System.Diagnostics.Debug.WriteLine` (no external logging libraries).
- **Exception handling**: every caught exception must be logged. Empty catch blocks are forbidden.
- **Null handling**: nullable reference types enabled. Strict distinction between `string?` and `string`.
- **No legacy comments**: do not carry over `// Loop 58 Fix 2-1` style comments from v1.

## Approved NuGet Packages

| Package | Project | Purpose |
|---------|---------|---------|
| Microsoft.Data.Sqlite | Core | SQLite |
| Microsoft.ML.OnnxRuntime | Pipeline | ONNX embeddings |
| Microsoft.ML.OnnxRuntime.Extensions | Pipeline | ONNX extensions |
| DocumentFormat.OpenXml | Pipeline | DOCX/XLSX/PPTX |
| PdfPig | Pipeline | PDF parsing |
| MimeKit | Email | EML parsing |
| MsgReader | Email | MSG parsing |
| OpenMcdf | Pipeline | HWP parsing |
| Porter2StemmerStandard | Search | English stemming |
| Microsoft.Identity.Client | Email | Graph auth |
| Microsoft.Identity.Client.Extensions.Msal | Email | Token cache |
| Avalonia | UI | UI framework |
| Avalonia.Desktop | UI | Desktop hosting |
| Avalonia.Themes.Fluent | UI | Fluent theme |
| Avalonia.Fonts.Inter | UI | Inter font |
| CommunityToolkit.Mvvm | UI | MVVM infrastructure |
| xunit | tests/LocalSynapse.Core.Tests | 2.9.3 — Test framework. v3 3.2.2 attempted first (Phase 1 Step 0 probe), failed VSTest compatibility → fell back to v2. Do NOT revert to v3 without verifying VSTest adapter compatibility. |
| xunit.runner.visualstudio | tests/LocalSynapse.Core.Tests | 2.8.2 — VSTest adapter for `dotnet test`. Compatible with both v2 and v3 test projects. |
| Microsoft.NET.Test.Sdk | tests/LocalSynapse.Core.Tests | 17.12.0 — Required by VSTest runner. |
| Microsoft.Extensions.DependencyInjection | Mcp.Stdio | DI container |
| ModelContextProtocol | Mcp, UI | MCP C# SDK (official) |
| Microsoft.Extensions.Hosting | Mcp, Mcp.Stdio, UI | Generic Host for MCP stdio server |

Do NOT add packages without approval.

## Porting Principles

When porting logic from v1.2.0 code:
1. **Copy algorithms and SQL queries only**. Class structure, names, and namespaces follow v2 rules.
2. **Never port WebView2/IPC/React code**.
3. **Never port JSON serialization/deserialization IPC code**.
4. **Never port NativeCliAdapter, NativeBackendService, or WebViewBridge code**.

## Build & Run

```bash
# Build
dotnet build LocalSynapse.v2.sln

# Run (GUI)
dotnet run --project src/LocalSynapse.UI

# Run (MCP server)
dotnet run --project src/LocalSynapse.UI -- mcp

# Test
dotnet test LocalSynapse.v2.sln

# Gate check (required before reporting completion)
pwsh ./gate-check.ps1
```

---

## ⛔ Gate Rules (Apply to Every Task)

### Mandatory Gates Before Completion

Every code task (new implementation, bug fix, refactoring) must pass all 3 gates
before it can be reported as "complete."

```
Gate 1: Build
  dotnet build LocalSynapse.v2.sln
  → 0 errors. Report warning count.

Gate 2: All Tests
  dotnet test LocalSynapse.v2.sln
  → 0 failures. Report total test count.

Gate 3: Impact Scope
  List all modified files.
  State which Agent each file belongs to.
  If any file outside the assigned Agent was modified, explain why.
```

### On Gate Failure

- **Gate 1 failure**: Fix build errors and re-run. Do NOT report to user mid-fix.
- **Gate 2 failure**: Analyze failing tests and fix the implementation.
  Deleting or skipping tests is FORBIDDEN.
  If a fix cannot be found, report the failure reason and ask the user for guidance.
- **Gate 3 failure**: If cross-Agent modification is unavoidable,
  explain the reason and get user approval.

### Additional Gate for New Features

When adding a **new feature** (not fixing existing code):

```
Gate 4: Test First
  Write unit tests for the new feature BEFORE implementing it (TDD).
  Tests must be red (failing) before implementation starts.
  Tests must turn green (passing) after implementation.
  Minimum: 2 tests per new public method (happy path + error case).
```

### Completion Report Format

Use this format when reporting task completion:

```
## Task Completion Report

### Gate 1: Build ✅
Errors: 0, Warnings: N

### Gate 2: Tests ✅
Total: NN, Passed: NN, Failed: 0

### Gate 3: Impact Scope ✅
Modified files:
- src/LocalSynapse.Search/Services/Bm25SearchService.cs (Agent 3)
- tests/LocalSynapse.Search.Tests/Bm25SearchServiceTest.cs (Agent 3 tests)

### Summary
(What was changed, why, and how — 1-3 lines)
```

---

## 🔁 Loop Workflow Gates (For Phased Feature Work)

Phase 단위 작업(`/recon`, `/spec`, `/diff-plan`, `/execute`)은 아래 5게이트를 순서대로 통과해야 한다.
스킵 금지. 게이트 누락 시 작업 중단하고 사용자에게 알릴 것.

### Gate L1: Recon (Plan Mode)
- **트리거**: `/recon {phase}`
- 코드베이스 영향 범위 분석
- 2~3개 구현 옵션과 trade-off
- **산출물**: `Docs/plans/{phase}-recon.md`
- **다음**: 사용자에게 "다음: `/spec {phase}` 실행하세요" 안내

### Gate L2: Spec (초안)
- **트리거**: `/spec {phase}`
- 사용자와 대화하며 요구사항 초안 작성
- **산출물**: `Docs/plans/{phase}-spec.md` (초안)
- **전제조건**: recon 파일 존재 필수 (없으면 `/recon` 먼저 안내)
- **다음**: 사용자에게 반드시 다음을 안내:
  > "Spec 초안이 저장되었습니다. **Claude Web에서 spec을 검토하고 상세화해 주세요.**
  > 상세화 완료 후 이 파일을 업데이트하고 `/diff-plan {phase}`를 실행하세요."
- **절대 바로 diff-plan으로 넘어가지 말 것**

### Gate L2.5: Spec 확정 (사용자 주도)
- 사용자가 Claude Web 또는 직접 편집으로 spec을 상세화
- 상세화된 spec 파일을 `Docs/plans/{phase}-spec.md`에 반영
- 사용자가 "spec 확정" 또는 `/diff-plan` 실행 시 다음 단계 진입

### Gate L3: Diff Plan (Plan Mode)
- **트리거**: `/diff-plan {phase}`
- recon + spec 파일 기반 파일별 변경 계획
- **산출물**: `Docs/plans/{phase}-diff-plan.md`
- **전제조건**: spec 파일 존재 필수 (없으면 거부)
- **리뷰**: diff-reviewer subagent 호출 (적대적 시스템 프롬프트, 결함만 찾음)
- 리뷰 결과를 diff-plan 파일에 `## Review` 섹션으로 추가
- BLOCK 판정 시 수정 후 재리뷰
- **다음**: 사용자에게 "다음: `/execute {phase}` 실행하세요" 안내

### Gate L4: Execute
- **트리거**: `/execute {phase}`
- **전제조건**: diff-plan 파일 존재 필수 (없으면 거부)
- diff-plan에 명시된 변경만 실행. plan에 없는 변경 금지
- 각 변경 후 빌드 확인 (`dotnet build LocalSynapse.v2.sln`)
- 완료 시 커밋

### Gate L5: Post Review
- 실행 완료 후 자동 수행
- 변경된 파일 목록 + 빌드 결과 보고
- diff-reviewer subagent로 사후 리뷰

### Loop Workflow 행동 규칙

각 게이트 진입 시 Claude는 반드시:
1. 현재 단계에서 필요한 정보가 충분한지 점검
2. 부족한 정보에 대해 사용자에게 **질문을 먼저** 함
3. 가능한 **액션 옵션을 제안**
4. 사용자 확인 후 진행

### Loop Workflow Hard Rules
- Plan mode 산출물은 항상 **파일로 저장** (대화에만 남기지 말 것)
- 한 Phase = 한 기능. 스코프 확장 시 새 Phase 생성
- spec에 없는 기능 추가 금지. 발견 시 사용자에게 보고하고 spec 업데이트 요청
- 코드베이스 전체 읽기 금지. 영향 파일만 읽을 것
- 동일 파일 재읽기 금지 (이미 컨텍스트에 있으면 재사용)

### Spec Deviation in diff-plan

diff-plan 작성 중 spec의 약점, 버그, 또는 더 나은 대안을 발견하면 **즉시 본문에 반영하지 말 것**. 대신:

1. diff-plan 본문은 spec에 충실하게 작성
2. diff-plan 끝에 별도 "Spec Deviation Proposals" 섹션에 제안만 기록
3. 사용자에게 보고하고 승인 획득
4. 승인 후: spec을 먼저 업데이트 → diff-plan을 새 spec에 맞춰 갱신
5. 그 다음 execute 진입

**근거**: diff-plan 작성자가 spec을 "개선"하려는 판단 자체는 종종 옳지만, 무승인 변경은 spec/diff-plan/execute 3계층의 정합성을 무너뜨린다. 향후 다른 세션에서 spec을 읽고 작업할 때 "실제 구현은 어느 문서를 따랐는지" 추적 불가능해진다.

**예외**: 명확한 컴파일 에러, 타입 오류, 누락된 `required` 필드 같은 **단순 버그**는 diff-plan이 수정하되 spec에도 **동시에** 수정 사항을 반영해야 한다 (spec과 diff-plan의 영구 불일치 금지).

### Phase 번호 체계
Phase 번호는 `/recon 1a` 형식으로 사용.
1a, 1b, 1c... → 2a, 2b... 순서로 진행.
