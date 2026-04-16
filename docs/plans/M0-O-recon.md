# M0-O Recon — 진단 기반 최적화 일괄 적용

> **작성일**: 2026-04-16
> **입력**: M0-A (속도), M0-B (품질), M0-C (BM25) 진단 보고서 + Ryan 결정사항
> **범위**: O1~O5 코드 변경 + O6 재측정

---

## 1. 영향 범위 분석

### 변경 대상 파일 (6개)

| # | 파일 | Agent | 변경 규모 | 목적 |
|---|------|-------|----------|------|
| O1 | `src/LocalSynapse.Pipeline/Parsing/XlsxParser.cs` (97줄) | Pipeline | ~8줄 | 텍스트만 추출 + 10MB 상한 |
| O2 | `src/LocalSynapse.Search/Services/Bm25SearchService.cs` (255줄) | Search | ~1줄 | filename boost 감소 |
| O3 | `src/LocalSynapse.Pipeline/Parsing/PdfParser.cs` (60줄) | Pipeline | ~15줄 | garbled text 감지 |
| O4a | `src/LocalSynapse.Pipeline/Parsing/DocxParser.cs` (68줄) | Pipeline | ~2줄 | numbering ID 누출 제거 |
| O4b | `src/LocalSynapse.Pipeline/Parsing/HwpParser.cs` (207줄) | Pipeline | ~1줄 | `<>` 태그 제거 |
| O5 | `src/LocalSynapse.Pipeline/Orchestration/PipelineOrchestrator.cs` (499줄) | Pipeline | ~2줄 | embed phase skip |

**총 변경**: ~29줄, 6개 파일, 2개 Agent (Pipeline 5, Search 1)

### 영향받지 않는 파일

- Interfaces/ 폴더: 변경 없음 (CLAUDE.md 준수)
- Models/: 변경 없음
- Core/: 변경 없음
- UI/: 변경 없음
- `ExtractionResult`: 기존 `Fail("TOO_LARGE")` 코드 재사용, 변경 불필요

---

## 2. O1 — xlsx 텍스트만 추출 + 10MB 상한

### 현재 상태

[XlsxParser.cs:81-96](src/LocalSynapse.Pipeline/Parsing/XlsxParser.cs#L81-L96):
```csharp
private static string GetCellText(Cell cell, SharedStringTable? sst)
{
    if (cell.CellValue == null) return "";
    var value = cell.CellValue.Text;
    if (cell.DataType?.Value == CellValues.SharedString && sst != null)
    {
        if (int.TryParse(value, out var idx))
        {
            var item = sst.ElementAt(idx);
            return item.InnerText;
        }
    }
    return value ?? "";   // ← 숫자/날짜/수식 모두 반환
}
```

### 구현 옵션

#### Option A: XlsxParser 내부에서 처리 (권장)

1. **텍스트만 추출**: `GetCellText` 마지막 줄 변경
   ```csharp
   return "";  // 숫자/날짜/수식 셀은 검색 대상에서 제외
   ```

2. **10MB 상한**: `Parse()` 진입부 size probe 직후 추가
   ```csharp
   if (sizeBytes > 10 * 1024 * 1024)
       return ExtractionResult.Fail("TOO_LARGE", $"xlsx {sizeBytes} bytes exceeds 10MB limit");
   ```
   - `SpreadsheetDocument.Open` 이전에 배치 → open 비용도 회피
   - 에러 코드 `"TOO_LARGE"` = PlainTextParser/HtmlParser와 동일 관례

**장점**: 기존 패턴(파서별 자체 상한) 일관. 최소 변경.
**단점**: xlsx 전용 정책이 파서 내부에 하드코딩.

#### Option B: ContentExtractor에서 중앙 처리

`ContentExtractor.ExtractAsync` switch 문 이전에 확장자별 크기 검사 추가.

**장점**: 중앙 관리.
**단점**: 기존 패턴 위반 (PlainTextParser, HtmlParser는 자체 내부에서 검사). xlsx만 특별 취급할 이유 불충분.

#### 권장: Option A

### 기대 효과

| 지표 | Before | After (예상) |
|------|--------|-------------|
| xlsx 총 시간 | 8,827초 | <500초 (숫자 셀 skip + >10MB skip) |
| 전체 인덱싱 | 171분 | ~30분 |
| 청크 수 (xlsx) | 수만 개 | 격감 (텍스트 셀만) |

### 위험

- **숫자만 있는 xlsx**: 추출 결과 빈 문자열 → 파일명 검색으로만 접근 가능. Ryan 확인: 이 동작 수용.
- **10MB 직전 파일 (9.9MB)**: 여전히 처리됨. 텍스트만 추출이면 충분히 빠를 것으로 예상.

---

## 3. O2 — Filename boost 5.0x → 2.5x

### 현재 상태

[Bm25SearchService.cs:127](src/LocalSynapse.Search/Services/Bm25SearchService.cs#L127):
```sql
bm25(chunks_fts, 0, 0, 1.0, 5.0, 0.5) AS rank
```

[Bm25SearchService.cs:178](src/LocalSynapse.Search/Services/Bm25SearchService.cs#L178):
```csharp
var filenameBoost = meaningfulTokens.Any(t => IsWordBoundaryMatch(...)) ? 5.0 : 1.0;
```

**두 곳의 5.0 역할이 다름**:
- BM25 column weight (SQL): FTS5가 어떤 행을 materialize할지 결정 (recall 영향)
- 후처리 boost (C#): 최종 순위 결정 (precision 영향)

### 구현 옵션

#### Option A: 둘 다 2.5x

순효과: `(2.5/5.0)² ≈ 0.25x` — filename 영향력 4배 감소. 과교정 위험.

#### Option B: 후처리만 2.5x, BM25 weight 유지 (권장)

순효과: `0.5x` — filename 영향력 2배 감소. BM25 weight 5.0 유지로 filename 매치 행이 candidate set에 진입하는 것(recall)은 보존.

**장점**: recall 손실 없이 precision만 개선. 1줄 변경.
**단점**: BM25 SQL 내 5.0이 여전히 높아 보이지만, 이는 materialization 범위 문제이지 최종 순위 문제가 아님.

#### Option C: BM25 3.0 + 후처리 2.5

순효과: `(3.0/5.0) × (2.5/5.0) ≈ 0.3x`. 중간 지점.

#### 권장: Option B

O6 재측정에서 여전히 filename이 과도하면 BM25 weight도 후속 조정.

---

## 4. O3 — PDF CMap 인코딩 실패 감지

### 현재 상태

[PdfParser.cs:39](src/LocalSynapse.Pipeline/Parsing/PdfParser.cs#L39): `page.Text`가 CMap 실패 시 `!"#$%!&'!()*+,-` 류 garbled text 반환. 감지 없이 그대로 인덱싱.

### 구현 방안

**Garble 감지 heuristic**: 페이지 텍스트에서 "의미 없는 문자" 비율 검사.

```
IsLikelyGarbled(text):
  if text.Length < 50 → false (너무 짧으면 판단 불가)
  punctRatio = count(c in !"#$%&'()*+,-./:;<=>?@[\]^_{|}~) / text.Length
  if punctRatio > 0.4 → true
```

- 전체 페이지가 garbled → 해당 페이지 제외
- 모든 페이지가 garbled → `ExtractionResult.Fail("GARBLED_TEXT", ...)`
- 일부만 garbled → 정상 페이지만 포함 + Debug.WriteLine 경고

### 위험

- **False positive**: 수식/코드가 많은 PDF. 40% 임계값 + 50자 최소 길이로 완화.
- **PdfPig 버전**: 현재 승인 버전에서 CMap 개선 여부 불명. heuristic은 버전과 무관하게 안전망 역할.

---

## 5. O4 — DOCX ID 누출 + HWP `<>` 태그

### O4a: DOCX numbering ID 누출

[DocxParser.cs:41](src/LocalSynapse.Pipeline/Parsing/DocxParser.cs#L41):
```csharp
var text = para.InnerText;  // ← NumberingProperties 등 모든 자식의 텍스트 포함
```

**원인**: `Paragraph.InnerText`는 `NumberingProperties`, `ParagraphProperties` 등 비가시 요소의 텍스트도 포함.

**수정**: `Run` 후손만 추출
```csharp
var text = string.Concat(para.Descendants<Run>().Select(r => r.InnerText));
```

같은 문제가 [DocxParser.cs:50](src/LocalSynapse.Pipeline/Parsing/DocxParser.cs#L50) TableCell에도 존재:
```csharp
.Select(c => c.InnerText.Trim())  // ← 동일 문제
```

수정:
```csharp
.Select(c => string.Concat(c.Descendants<Run>().Select(r => r.InnerText)).Trim())
```

### O4b: HWP `<>` 태그

[HwpParser.cs:72](src/LocalSynapse.Pipeline/Parsing/HwpParser.cs#L72):
```csharp
return Encoding.Unicode.GetString(data).Trim('\0').Trim();
// PrvText 내용: "<차 입 신 청 서><인감대조필><팀 원>"
```

**수정**: `<`, `>` 제거 후처리 추가
```csharp
return Encoding.Unicode.GetString(data).Trim('\0').Trim()
    .Replace("<", " ").Replace(">", " ");
```

> 공백으로 치환하면 `<차 입>` → ` 차 입 ` → 정상 토큰화. 빈 문자열 치환 시 `<차><입>` → `차입` 으로 붙을 위험.

---

## 6. O5 — Embed phase skip

### 현재 상태

[PipelineOrchestrator.cs:102](src/LocalSynapse.Pipeline/Orchestration/PipelineOrchestrator.cs#L102):
```csharp
if (_embeddingService.IsReady)
{
    await RunEmbeddingPhaseAsync(ct);
}
```

BGE-M3 다운로드 완료 시 `IsReady = true` → 사용하지 않는 embedding 생성.

### 구현 옵션

#### Option A: const 플래그 (권장)

```csharp
/// <summary>Dense search 비활성 기간 동안 embedding 생성을 건너뛴다. M2에서 제거.</summary>
private const bool SkipEmbeddingPhase = true;

// line 102:
if (!SkipEmbeddingPhase && _embeddingService.IsReady)
```

**장점**: 0 아키텍처 영향, 1줄 추가 + 1줄 변경. 의도 명확. M2에서 const 제거로 복원.
**단점**: 하드코딩. 재컴파일 필요.

#### Option B: DI 파라미터

PipelineOrchestrator 생성자에 `bool enableEmbedding = false` 추가. DI에서 주입.

**장점**: 런타임 설정 가능.
**단점**: Interface 변경 필요할 수 있음 (IPipelineOrchestrator에 파라미터 없으면 OK). 현 시점 과설계.

#### 권장: Option A

Dense search가 M2에서 부활할 때 이 const를 제거하면 됨. 그때 Option B로 전환 가능.

---

## 7. 재사용 가능한 기존 코드/패턴

| 패턴 | 위치 | 재사용 방법 |
|------|------|------------|
| `ExtractionResult.Fail("TOO_LARGE", ...)` | PlainTextParser, HtmlParser | O1 xlsx 크기 상한에 동일 에러 코드 사용 |
| Size probe + early return | XlsxParser:19-20 | O1 상한 체크를 기존 probe 직후 배치 |
| SpeedDiagLog.Log | 모든 파서 | O3 garble 감지 시 로그 추가 |
| `Descendants<Run>()` | OpenXml SDK 표준 | O4a DOCX Run 기반 추출 |

---

## 8. 위험 요소 및 사전 확인

| # | 위험 | 심각도 | 완화 |
|---|------|--------|------|
| 1 | O1 텍스트만 추출 → 숫자 셀이 의미 있는 경우 (예: 전화번호가 숫자 셀) | 낮음 | SharedString에 저장된 전화번호는 유지됨. 순수 numeric 셀만 skip |
| 2 | O2 boost 변경 → 기존 검색 습관과 다른 결과 | 중간 | Option B (후처리만) 채택으로 영향 최소화. O6에서 검증 |
| 3 | O3 garble heuristic false positive | 낮음 | 40% 임계 + 50자 최소 → 코드/수식 PDF에서도 안전 |
| 4 | O4a Run 기반 추출 → 일부 텍스트 누락 | 낮음 | Run은 DOCX에서 가시 텍스트의 표준 컨테이너. SdtRun 등 예외 확인 필요 |
| 5 | O5 embed skip → M2 부활 시 backfill 필요 | 설계 | const 제거 + 기존 `EnumerateChunksMissingEmbedding` 로직이 자동 backfill |

### 사전 확인 필요

- [ ] `DocumentFormat.OpenXml.Wordprocessing.Run` using 추가 필요 여부 (DocxParser 기존 using 확인)
- [ ] PdfPig 현재 버전 확인 (csproj)
- [ ] O4a: `SdtRun`, `Hyperlink` 등 Run 외 가시 텍스트 요소 존재 여부

---

## 9. 실행 순서 권장

```
O1 (xlsx)  → 가장 큰 임팩트, 단독 빌드 검증
O5 (embed) → 인덱싱 시간 추가 단축
O2 (boost) → 검색 품질, 독립적
O4 (DOCX/HWP) → 저비용, 독립적
O3 (PDF)   → 가장 복잡한 heuristic
O6 (측정)  → 전체 적용 후 검증
```

---

## 변경 이력

| 날짜 | 변경 |
|------|------|
| 2026-04-16 | 초안 작성. M0-A/B/C 진단 결과 + Ryan 결정 (xlsx 텍스트만 + 10MB) 기반. |
