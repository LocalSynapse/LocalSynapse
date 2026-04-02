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
Core, Pipeline, Search, Email ← UI
```

Reverse references are forbidden. Pipeline must not reference Search. Core must not reference UI.

## Project Structure

```
src/
├── LocalSynapse.Core/        # Models, Interfaces, DB — no dependencies
├── LocalSynapse.Pipeline/    # Scan, parse, chunk, embed — Core only
├── LocalSynapse.Search/      # BM25, Dense, Hybrid search — Core only
├── LocalSynapse.Email/       # Email parsing, Graph sync — Core only
├── LocalSynapse.Mcp/         # MCP stdio server — Core, Search
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
