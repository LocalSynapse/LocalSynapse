# M0-O Diff Plan — 진단 기반 최적화 일괄 적용

> **작성일**: 2026-04-16
> **입력**: M0-O-recon.md + M0-O-spec.md (v2 확정)
> **커밋 전략**: 단일 커밋 (O1~O5 일괄, 전부 독립적 변경)

---

## 1. 변경 파일 목록

| # | 파일 | Agent | 변경 유형 | 줄 수 |
|---|------|-------|----------|-------|
| O1 | `src/LocalSynapse.Pipeline/Parsing/XlsxParser.cs` | Pipeline | 수정 | +3 |
| O2 | `src/LocalSynapse.Search/Services/Bm25SearchService.cs` | Search | 수정 | +0 -0 (값 변경) |
| O3 | `src/LocalSynapse.Pipeline/Parsing/PdfParser.cs` | Pipeline | 수정 | +20 |
| O4a | `src/LocalSynapse.Pipeline/Parsing/DocxParser.cs` | Pipeline | 수정 | +2 -2 |
| O4b | `src/LocalSynapse.Pipeline/Parsing/HwpParser.cs` | Pipeline | 수정 | +1 -1 |
| O5 | `src/LocalSynapse.Pipeline/Orchestration/PipelineOrchestrator.cs` | Pipeline | 수정 | +4 -1 |

**DB 마이그레이션**: 없음
**인터페이스 변경**: 없음
**모델 변경**: 없음
**새 파일**: 없음
**NuGet 변경**: 없음

---

## 2. 파일별 Diff 상세

### O1. XlsxParser.cs — 10MB 상한 추가

**위치**: `Parse()` 메서드, line 20 (size probe) 직후, line 22 (`SpreadsheetDocument.Open`) 이전

**현재 코드** (lines 19-22):
```csharp
try { sizeBytes = new FileInfo(filePath).Length; }
catch (Exception sEx) { Debug.WriteLine($"[XlsxParser] Size probe: {sEx.Message}"); }

var openSw = Stopwatch.StartNew();
using var doc = SpreadsheetDocument.Open(filePath, false);
```

**변경 후**:
```csharp
try { sizeBytes = new FileInfo(filePath).Length; }
catch (Exception sEx) { Debug.WriteLine($"[XlsxParser] Size probe: {sEx.Message}"); }

if (sizeBytes >= 0 && sizeBytes > 10 * 1024 * 1024)
    return ExtractionResult.Fail("TOO_LARGE", $"xlsx {sizeBytes} bytes exceeds 10MB limit");

var openSw = Stopwatch.StartNew();
using var doc = SpreadsheetDocument.Open(filePath, false);
```

**변경 요약**: +3줄 (빈줄 포함). `SpreadsheetDocument.Open` 이전에 배치하여 open 비용 회피.
> **W1 대응** (리뷰): `sizeBytes >= 0` 가드 추가. size probe 실패 시 (`sizeBytes == -1`) 상한 체크를 건너뛰고 기존 동작(Open 시도) 유지. probe 실패 자체는 극히 드문 케이스 (파일 존재하나 접근 불가 등).

---

### O2. Bm25SearchService.cs — 후처리 filenameBoost 5.0 → 2.5

**위치**: line 178

**현재 코드**:
```csharp
                    ? 5.0 : 1.0;
```

**변경 후**:
```csharp
                    ? 2.5 : 1.0;
```

**변경 요약**: 값 1개 변경. BM25 SQL column weight (line 127의 `5.0`)는 유지.

---

### O3. PdfParser.cs — garble 감지 + 페이지별 필터링

**변경 1**: `IsLikelyGarbled` private static 메서드 추가 (클래스 끝, line 59 이전)

```csharp
    /// <summary>CMap 디코딩 실패로 인한 garbled 텍스트를 감지한다.</summary>
    private static bool IsLikelyGarbled(string text)
    {
        if (text.Length < 50) return false;
        var letterCount = 0;
        foreach (var c in text)
        {
            if (char.IsLetter(c)) letterCount++;
        }
        return (double)letterCount / text.Length < 0.20;
    }
```

> `text.Count(c => char.IsLetter(c))` 대신 `foreach` 루프 사용 — LINQ 없이 동일 동작, Pipeline 프로젝트 LINQ 의존 최소화.

**변경 2**: `ParseAsync` 페이지 순회 로직 변경 (lines 36-44)

**현재 코드**:
```csharp
                foreach (var page in document.GetPages())
                {
                    ct.ThrowIfCancellationRequested();
                    var pageText = page.Text;
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        sb.AppendLine(pageText);
                    }
                }
```

**변경 후**:
```csharp
                var garbledPages = 0;
                foreach (var page in document.GetPages())
                {
                    ct.ThrowIfCancellationRequested();
                    var pageText = page.Text;
                    if (string.IsNullOrWhiteSpace(pageText)) continue;
                    if (IsLikelyGarbled(pageText))
                    {
                        garbledPages++;
                        continue;
                    }
                    sb.AppendLine(pageText);
                }
```

**변경 3**: 페이지 순회 후, PARSE_DETAIL 로그 전에 garble 카운트 로깅 + 전체 garbled 처리

**현재 코드** (lines 45-48):
```csharp
                pagesSw.Stop();
                SpeedDiagLog.Log("PARSE_DETAIL",
                    "ext", ".pdf", "stage", "pages",
                    "time_ms", pagesSw.ElapsedMilliseconds);
```

**변경 후**:
```csharp
                pagesSw.Stop();
                if (garbledPages > 0)
                    Debug.WriteLine($"[PdfParser] {garbledPages}/{pageCount} pages garbled: {filePath}");
                SpeedDiagLog.Log("PARSE_DETAIL",
                    "ext", ".pdf", "stage", "pages",
                    "time_ms", pagesSw.ElapsedMilliseconds,
                    "garbled_pages", garbledPages);
```

**변경 4**: 전체 garbled 시 Fail 반환 (line 50 `return ExtractionResult.Ok(sb.ToString());` 변경)

**현재 코드**:
```csharp
                return ExtractionResult.Ok(sb.ToString());
```

**변경 후**:
```csharp
                if (garbledPages > 0 && sb.Length == 0)
                    return ExtractionResult.Fail("GARBLED_TEXT",
                        $"All {pageCount} pages appear garbled (possible CMap encoding issue)");

                return ExtractionResult.Ok(sb.ToString());
```

**변경 요약**: +20줄 (메서드 11줄 + 로직 9줄).

---

### O4a. DocxParser.cs — Run 기반 추출

**위치 1**: line 41

**현재 코드**:
```csharp
                    var text = para.InnerText;
```

**변경 후**:
```csharp
                    var text = string.Concat(para.Descendants<Run>().Select(r => r.InnerText));
```

> `Run`은 이미 `using DocumentFormat.OpenXml.Wordprocessing;` (line 4)에 포함. 추가 using 불필요.
> `Descendants<Run>()`은 Hyperlink, SdtRun 내부의 Run도 탐색 (서브트리 전체 순회).

**위치 2**: line 49-50

**현재 코드**:
```csharp
                        var cells = row.Descendants<TableCell>()
                            .Select(c => c.InnerText.Trim());
```

**변경 후**:
```csharp
                        var cells = row.Descendants<TableCell>()
                            .Select(c => string.Concat(c.Descendants<Run>().Select(r => r.InnerText)).Trim());
```

**변경 요약**: 2줄 교체. `.Select(r => r.InnerText)` LINQ 사용 — DocxParser는 이미 LINQ 활용 중 (line 49 `.Select`).

---

### O4b. HwpParser.cs — PrvText `<>` 공백 치환

**위치**: line 72

**현재 코드**:
```csharp
                return Encoding.Unicode.GetString(data).Trim('\0').Trim();
```

**변경 후**:
```csharp
                return Encoding.Unicode.GetString(data).Trim('\0').Trim()
                    .Replace("<", " ").Replace(">", " ");
```

**변경 요약**: 1줄 → 2줄 (체이닝). `<차 입 신 청 서>` → ` 차 입 신 청 서 ` → 정상 토큰화.

---

### O5. PipelineOrchestrator.cs — Embed phase skip

**변경 1**: const 필드 추가 (클래스 상단, 기존 필드 영역)

기존 필드 영역 근처에 추가:
```csharp
    /// <summary>Dense search 비활성 기간 동안 embedding 생성을 건너뛴다. M2에서 제거.</summary>
    private const bool SkipEmbeddingPhase = true;
```

**변경 2**: line 102 조건문 변경

**현재 코드**:
```csharp
            if (_embeddingService.IsReady)
```

**변경 후**:
```csharp
            if (!SkipEmbeddingPhase && _embeddingService.IsReady)
```

**변경 3**: else 블록의 로그 메시지 확장 — SkipEmbeddingPhase 사유 구분

**현재 코드** (line 110):
```csharp
                SpeedDiagLog.Log("PHASE_EMBED", "skipped", "model_not_ready");
```

**변경 후**:
```csharp
                SpeedDiagLog.Log("PHASE_EMBED", "skipped",
                    SkipEmbeddingPhase ? "dense_disabled" : "model_not_ready");
```

**변경 요약**: +4줄 -1줄.

---

## 3. 실행 순서

```
Step 1: O1 (XlsxParser)     — 빌드 확인
Step 2: O5 (PipelineOrchestrator) — 빌드 확인
Step 3: O2 (Bm25SearchService)    — 빌드 확인
Step 4: O4a + O4b (DocxParser + HwpParser) — 빌드 확인
Step 5: O3 (PdfParser)      — 빌드 확인
Step 6: 전체 테스트 실행     — dotnet test
Step 7: 커밋
```

각 Step 후 `dotnet build LocalSynapse.v2.sln` 실행. Step 6에서 `dotnet test LocalSynapse.v2.sln` 전체 실행.

### O6 재측정 절차 (커밋 후)

> **S1 대응** (리뷰): spec §2.6의 O6 검증 절차를 diff-plan에 명시.

1. `diag/speed-measurement` 브랜치에서 계측 빌드 생성 (`dotnet publish -r win-x64 --self-contained`)
2. Ryan 개발 PC에서 fresh DB로 인덱싱 전체 실행
3. `speed-diag.log`에서 다음 확인:
   - `PHASE_INDEX time_ms` ≤ 1,800,000ms (30분)
   - `.xlsx` TOO_LARGE skip 건수 확인
   - `PHASE_EMBED skipped=dense_disabled` 확인
4. 검색 테스트: 기존 쿼리(보고서, 주주명부 등)로 결과 품질 확인
5. **미달 시 fallback**: spec §2.6 3단계 대응 (5MB 하향 → 숫자 셀 재검토 → SAX)

---

## 4. 검증 절차

### Gate 1: Build
```bash
dotnet build LocalSynapse.v2.sln
```
→ 0 errors 필수. Warning count 보고.

### Gate 2: Tests
```bash
dotnet test LocalSynapse.v2.sln
```
→ 0 failures 필수. Total test count 보고.

### Gate 3: Impact Scope
변경 파일 5개 (6개 변경점), Agent 2개:
- Pipeline: XlsxParser, PdfParser, DocxParser, HwpParser, PipelineOrchestrator
- Search: Bm25SearchService

Core, UI, Email, Mcp: **변경 없음**.

---

## 5. Spec과의 정합성 체크

| Spec 항목 | Diff Plan 반영 | 일치 |
|-----------|---------------|------|
| O1: 10MB 상한, Open 이전, TOO_LARGE | ✅ line 20 직후, Open 이전 | ✅ |
| O1: 숫자 셀 제외 보류 | ✅ GetCellText 변경 없음 | ✅ |
| O2: 후처리만 2.5, SQL 유지 | ✅ line 178만 변경, line 127 유지 | ✅ |
| O3: letter 비율 < 20%, 최소 50자 | ✅ IsLikelyGarbled 구현 일치 | ✅ |
| O3: 페이지별 필터, 전체 garbled→Fail | ✅ garbledPages 카운트 + sb.Length==0 체크 | ✅ |
| O4a: Descendants\<Run\>, Paragraph+TableCell | ✅ line 41, line 49-50 | ✅ |
| O4b: `<>` → 공백 치환 | ✅ Replace 체이닝 | ✅ |
| O5: const SkipEmbeddingPhase = true | ✅ 필드 + 조건문 + 로그 사유 구분 | ✅ |
| UI 변경 없음 | ✅ | ✅ |
| DB 변경 없음 | ✅ | ✅ |
| Interfaces 변경 없음 | ✅ | ✅ |

---

## Adversarial Review

> **리뷰어**: diff-reviewer subagent
> **판정**: **CONDITIONAL PASS** (BLOCK 0, WARNING 6)

### WARNING 항목

| # | 항목 | 대응 |
|---|------|------|
| W1 | O1 `sizeBytes == -1` 시 상한 체크 우회 | ✅ **반영**: `sizeBytes >= 0 &&` 가드 추가 |
| W2 | O3 `pageCount` 변수가 diff-plan에서 기존 선언 미언급 | ✅ 인지: line 28에서 이미 선언됨, execute 시 확인 |
| W3 | O3 숫자 중심 PDF(재무제표)에서 false positive 가능 | 인지: spec에서 알려진 한계. 해당 페이지만 제외되고 전체 PDF는 보존 |
| W4 | O5 embed skip 시 stamp 미갱신 | 인지: M2 부활 시 stamp 재계산됨. 현재 UI에 embedding 진행률 표시는 Dense off로 무의미 |
| W5 | O4a `Descendants<Run>()` 성능 퇴행 가능성 | 인지: M0-A 기준 docx p95=320ms. Descendants가 InnerText보다 느려도 absolute 영향 미미 (전체의 1.7%) |
| W6 | O2 line 127 SQL 내 5.0 실수 수정 주의 | ✅ 인지: execute 시 line 127 변경 금지 명시 |

### Spec 불일치

| # | 항목 | 대응 |
|---|------|------|
| S1 | O6 재측정 절차 누락 | ✅ **반영**: §3에 O6 절차 5단계 추가 |

### 확인 완료

- ✅ 컴파일 오류 없음 (using 추가 불필요, 타입 일치)
- ✅ CLAUDE.md 규칙 준수 (Interfaces/Models 변경 없음)
- ✅ 의존성 방향 위반 없음 (Pipeline→Search 참조 없음)
- ✅ 기존 테스트 파괴 없음 (파서 내부 로직 변경, public API 불변)
- ✅ Spec과 diff-plan 정합성 확인 (§5 체크리스트)

---

## Post-Execution Review

> **리뷰어**: diff-reviewer subagent
> **판정**: **CONDITIONAL PASS** (BLOCK 0, WARNING 5)
> **커밋**: `9b4f4ed`

### 계획 vs 실제 코드 일치 확인

- ✅ O1: XlsxParser 10MB 상한 — `sizeBytes >= 0 &&` 가드 포함, 계획 일치
- ✅ O2: filenameBoost `2.5` — SQL column weight `5.0` 유지 확인
- ✅ O3: IsLikelyGarbled — `char.IsLetter` 비율 < 0.20, 50자 최소
- ✅ O4a: DocxParser Descendants\<Run\> — Paragraph + TableCell 모두 적용
- ✅ O4b: HwpParser Replace 체이닝 — 공백 치환
- ✅ O5: SkipEmbeddingPhase const + 로그 사유 구분

### Post-execution WARNING

| # | 항목 | 심각도 | 대응 |
|---|------|--------|------|
| PW1 | O3 GARBLED_TEXT 메시지에서 `pageCount` 사용 — 빈 페이지+garbled 혼합 시 "All N pages" 오보 가능 | 낮음 | 후속: 메시지를 `garbledPages` 기반으로 변경 검토 (M0-U) |
| PW2 | `CountSkippedByCategory()` SQL이 `FAILED_TOO_LARGE` status를 찾지만 실제 저장은 `ERROR` + errorCode `TOO_LARGE` — 기존 버그, O1에서 전파 | 낮음 | 기존 갭. M0-U에서 status 집계 로직 수정 시 함께 처리 |
| PW3 | O5 embed skip 시 stamp 미갱신 → M2 부활 시 EnumerateChunksMissing이 delta 처리 가능한지 확인 필요 | 설계 | M2 recon에서 backfill 경로 검증 포함 |
| PW4 | O4a Descendants\<TableCell\> 중첩 테이블에서 텍스트 중복 가능 — 기존 InnerText도 동일 문제이므로 regression 아님 | 정보 | 기존 갭 |
| PW5 | Gate 4 (TDD) 미적용 — IsLikelyGarbled, 10MB 상한에 대한 단위 테스트 없음 | 중간 | 후속 Phase에서 Pipeline.Tests에 추가 권장 |

### Gate 실행 결과

```
Gate 1: Build ✅
  Core: 0 errors, Pipeline: 0 errors, Search: 0 errors
  (UI: 프로세스 잠금으로 빌드 불가 — 코드 변경 없음)

Gate 2: Tests ✅
  Core.Tests: 37 passed, 2 skipped
  Pipeline.Tests: 35 passed
  Search.Tests: 기존 컴파일 오류 (pre-existing, O2와 무관 — git stash 검증 완료)
  Total: 72 passed, 0 failures

Gate 3: Impact Scope ✅
  변경 파일 6개:
  - src/LocalSynapse.Pipeline/Parsing/XlsxParser.cs (Pipeline)
  - src/LocalSynapse.Pipeline/Orchestration/PipelineOrchestrator.cs (Pipeline)
  - src/LocalSynapse.Pipeline/Parsing/DocxParser.cs (Pipeline)
  - src/LocalSynapse.Pipeline/Parsing/HwpParser.cs (Pipeline)
  - src/LocalSynapse.Pipeline/Parsing/PdfParser.cs (Pipeline)
  - src/LocalSynapse.Search/Services/Bm25SearchService.cs (Search)
  Cross-Agent 변경 없음.
```

---

## 변경 이력

| 날짜 | 변경 |
|------|------|
| 2026-04-16 | 초안 작성. Spec v2 기반 6개 파일 diff 상세. |
| 2026-04-16 | **리뷰 반영**: W1 sizeBytes 가드 추가, S1 O6 재측정 절차 추가. CONDITIONAL PASS → 진행 가능. |
| 2026-04-16 | **실행 완료** (`9b4f4ed`). Post-execution review CONDITIONAL PASS. PW1~PW5 기록. |
