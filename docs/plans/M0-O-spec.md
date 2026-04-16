# M0-O Spec — 진단 기반 최적화 일괄 적용

> **작성일**: 2026-04-16
> **상태**: v2 확정 (리뷰 반영)
> **입력**: M0-O-recon.md + Ryan 결정사항 + Ryan 리뷰 (2026-04-16)
> **범위**: O1~O6 (6개 파일, ~25줄 변경)

---

## 1. 목표

M0-A/B/C 진단에서 발견된 병목과 품질 이슈를 일괄 해결하여:
- **인덱싱**: 171분 → ≤30분
- **검색 품질**: filename 과부스트 해소, 노이즈(DOCX ID, HWP 태그, PDF garble) 제거
- **불필요 작업 제거**: Dense search 비활성 상태에서 embedding 생성 중단

---

## 2. 요구사항 (확정)

### O1. xlsx 10MB 상한

**파일**: `src/LocalSynapse.Pipeline/Parsing/XlsxParser.cs`

| 항목 | 결정 |
|------|------|
| 크기 상한 | 10MB 초과 xlsx → **완전 skip** (`ExtractionResult.Fail("TOO_LARGE")`) |
| 상한 체크 시점 | `SpreadsheetDocument.Open` 이전 (open 비용 회피) |
| 에러 코드 | `"TOO_LARGE"` (PlainTextParser/HtmlParser 관례 동일) |
| ~~숫자 셀 제외~~ | **이번 Phase에서 제외** (리뷰 결과: 아래 근거 참조) |

**변경 상세**:

`Parse()` 메서드 — size probe 직후, Open 이전에 상한 체크 추가:
```
sizeBytes > 10 * 1024 * 1024 → Fail("TOO_LARGE", 상세 메시지)
```

**숫자 셀 제외를 보류한 근거**:
1. 속도 병목의 근본 원인은 셀 값 반환이 아니라 **대용량 시트 DOM 로드 + 셀 수 자체**. 숫자 셀을 빈 문자열로 바꿔도 DOM 순회 시간은 동일.
2. 셀 서식 "일반"의 숫자는 SharedString이 아닌 numeric 셀로 저장됨. 전화번호, 계정코드 등 검색 의미 있는 숫자도 제외될 위험.
3. 10MB 상한만으로 Top 3 파일(45.5MB, 15.7MB, 15.7MB) 제거. 나머지는 O6 재측정 후 판단.

**동작 변경**:
- 10MB 이하 xlsx: **기존과 동일** (모든 셀 추출)
- 10MB 초과 xlsx: 본문 인덱싱 안 함, 파일명 검색만 가능

**O6 fallback 조건** (목표 미달 시):
- 인덱싱 ≤30분 미달 → 상한 5MB로 하향 검토
- 여전히 미달 → SAX 스트리밍(OpenXmlReader) 전환을 별도 Phase로

### O2. Filename boost 감소

**파일**: `src/LocalSynapse.Search/Services/Bm25SearchService.cs`

| 항목 | 결정 |
|------|------|
| BM25 column weight (SQL) | **5.0 유지** (recall 보존) |
| 후처리 filenameBoost (C#) | **5.0 → 2.5** (precision 개선) |
| 변경 줄 | line 178: `? 5.0 : 1.0` → `? 2.5 : 1.0` |

**근거**: BM25 weight는 candidate set 진입에 영향 (recall). 후처리 boost는 최종 순위에 영향 (precision). 후처리만 줄여서 recall 손실 없이 "파일명 1단어 > 본문 수십 문장" 문제 해소.

### O3. PDF garble 감지

**파일**: `src/LocalSynapse.Pipeline/Parsing/PdfParser.cs`

| 항목 | 결정 |
|------|------|
| 감지 방식 | Heuristic: **알파벳+CJK 문자(letter) 비율 < 20%** (최소 50자) |
| garbled 페이지 | 해당 페이지 텍스트 제외 |
| 전체 garbled | `ExtractionResult.Fail("GARBLED_TEXT", 상세 메시지)` |
| 일부 garbled | 정상 페이지만 포함 + Debug.WriteLine 경고 |

**구현**:
```csharp
private static bool IsLikelyGarbled(string text)
{
    if (text.Length < 50) return false;
    var letterCount = text.Count(c => char.IsLetter(c)); // 알파벳 + CJK 포함
    return (double)letterCount / text.Length < 0.20;
}
```

**감지 기준 변경 근거** (리뷰 반영):
- 초안의 "특수문자 비율 > 40%" 방식은 마침표(.), 쉼표(,), 하이픈(-) 등 정상 문장부호를 포함하여 영문 기술 문서에서 false positive 위험.
- M0-B 실제 garble 패턴 `!"#$%!&'!()*+,-"'"&.*/*0123`의 특징: **알파벳/한글이 거의 없음**.
- `char.IsLetter()`는 Latin, Hangul, CJK Unified Ideographs 모두 포함 → 언어 무관 안전.

**추가 구현**:
- `ParseAsync` 내 페이지 순회에서 garble 체크 후 조건부 포함
- garbled 페이지 수를 PARSE_DETAIL 로그에 기록

### O4. DOCX ID 누출 + HWP `<>` 태그

#### O4a: DOCX

**파일**: `src/LocalSynapse.Pipeline/Parsing/DocxParser.cs`

| 항목 | 결정 |
|------|------|
| 추출 방식 | `para.InnerText` → `para.Descendants<Run>()` 기반 |
| 포함 범위 | **Run만** — `Descendants<Run>()`은 Hyperlink/SdtRun 내부 Run도 탐색하므로 안전 |
| TableCell | 동일하게 `Descendants<Run>()` 적용 |

**변경**:
- line 41: `var text = para.InnerText;` → `var text = string.Concat(para.Descendants<Run>().Select(r => r.InnerText));`
- line 50: `.Select(c => c.InnerText.Trim())` → `.Select(c => string.Concat(c.Descendants<Run>().Select(r => r.InnerText)).Trim())`

**근거**: `Descendants<Run>()`은 서브트리 전체를 순회하므로 Hyperlink, SdtRun(인라인 콘텐츠 컨트롤) 내부의 Run도 포함됨. NumberingProperties, BookmarkStart 등 비가시 요소만 확실히 제외.

**SdtBlock(블록 레벨) 참고**: body.ChildElements에서 SdtBlock은 Paragraph/Table과 동급이지만, 현재 코드에서 이미 무시됨 (기존 갭, O4a와 무관). 날짜 필드 등 SdtBlock 텍스트 누락은 M0-O 범위 밖.

#### O4b: HWP

**파일**: `src/LocalSynapse.Pipeline/Parsing/HwpParser.cs`

| 항목 | 결정 |
|------|------|
| 변경 위치 | `TryReadPrvText()` line 72 |
| 처리 방식 | `<` → 공백, `>` → 공백 치환 |

**변경**:
```
기존: return Encoding.Unicode.GetString(data).Trim('\0').Trim();
변경: return Encoding.Unicode.GetString(data).Trim('\0').Trim()
          .Replace("<", " ").Replace(">", " ");
```

**근거**: 빈 문자열 치환 시 `<차><입>` → `차입`으로 붙을 위험. 공백 치환이 안전.

### O5. Embed phase skip

**파일**: `src/LocalSynapse.Pipeline/Orchestration/PipelineOrchestrator.cs`

| 항목 | 결정 |
|------|------|
| 방식 | `private const bool SkipEmbeddingPhase = true;` |
| 적용 위치 | line 102 조건문에 const 추가 |
| 복원 | M2 (Dense 부활) 시 const 제거 또는 false 변경 |

**변경**:
```csharp
/// <summary>Dense search 비활성 기간 동안 embedding 생성을 건너뛴다. M2에서 제거.</summary>
private const bool SkipEmbeddingPhase = true;

// line 102 변경:
if (!SkipEmbeddingPhase && _embeddingService.IsReady)
```

**backfill 경로**: M2에서 const 제거 시, 기존 `EnumerateChunksMissingEmbedding` 로직이 자동으로 미생성 embedding을 처리.

### O6. 재측정 검증

| 항목 | 목표 | fallback |
|------|------|----------|
| 인덱싱 시간 | ≤30분 (baseline 171분) | 미달 시 상한 5MB 하향 또는 SAX 전환 Phase 신설 |
| xlsx 비중 | <20% (baseline 85.7%) | |
| 검색 P95 | ≤150ms (baseline 336ms) | |
| Embedding phase | ~0초 (skip) | |
| 검색 결과 품질 | filename 매치와 본문 매치 균형 확인 | 과교정 시 boost를 3.0으로 상향 조정 |

**O6 미달 시 단계적 대응**:
1. 인덱싱 30분 초과 → 상한 10MB → 5MB로 하향
2. 여전히 초과 → 숫자 셀 제외 재검토 (O1에서 보류한 항목)
3. 여전히 초과 → SAX 스트리밍(OpenXmlReader) 전환을 별도 Phase로

---

## 3. UI 변경

**없음**. 모든 변경은 백엔드(Pipeline, Search).

`"TOO_LARGE"` 및 `"GARBLED_TEXT"` 에러 코드는 기존 UI 파이프라인 상태 표시에서 error count 증가로 반영됨.

**Out of Scope (M0-U 이월)**: "이 파일은 크기 초과/텍스트 추출 불가로 인덱싱 제외됨" 같은 사용자 피드백 메시지. 현재는 error count 숫자만 올라갈 뿐 어떤 파일이 왜 제외됐는지 사용자가 알 수 없음. M0-U UI 재설계 시 DataSetupViewModel 상태 표시(M0-H H2)와 함께 처리.

---

## 4. DB 변경

**없음**. 스키마, 마이그레이션, 인덱스 변경 없음.

---

## 5. 제약 사항

- Interfaces/ 폴더 변경 금지 (CLAUDE.md)
- Models/ public properties 변경 금지
- 승인되지 않은 NuGet 패키지 추가 금지
- Pipeline → Search 참조 금지 (의존성 방향)

---

## 6. 실행 순서

```
O1 (xlsx) → O5 (embed) → O2 (boost) → O4 (DOCX/HWP) → O3 (PDF) → O6 (측정)
```

O1이 가장 큰 임팩트. O3이 가장 복잡 (heuristic 튜닝). 각 변경 후 `dotnet build` 확인.

---

## 7. 테스트 계획

| 변경 | 검증 방법 |
|------|----------|
| O1 10MB 상한 | >10MB xlsx → `"TOO_LARGE"` 반환 확인 |
| O1 10MB 이하 | ≤10MB xlsx → 기존과 동일하게 모든 셀 추출 확인 |
| O2 boost | 기존 검색 쿼리로 결과 순위 비교 (파일명 매치 순위 하락 확인) |
| O3 garble | CMap 실패 PDF → `"GARBLED_TEXT"` 반환 또는 garbled 페이지 제외 확인 |
| O4a DOCX | numbering 있는 DOCX → 숫자 ID 미포함 확인 |
| O4b HWP | PrvText에 `<>` 있는 HWP → 꺾쇠 미포함 확인 |
| O5 embed | 인덱싱 실행 → embed phase 로그 "skipped" 확인 |
| O6 전체 | 동일 데이터로 인덱싱 시간 ≤30분 확인 |

**Gate 검증**:
```
Gate 1: dotnet build LocalSynapse.v2.sln → 0 errors
Gate 2: dotnet test LocalSynapse.v2.sln → 0 failures
Gate 3: 변경 파일 6개, Agent 2개 (Pipeline, Search)
```

---

## 변경 이력

| 날짜 | 변경 |
|------|------|
| 2026-04-16 | 초안 작성. Ryan 결정: O2 후처리만 2.5x, O3 heuristic 포함, O4a Run만, O1 완전 skip. |
| 2026-04-16 | **v2 리뷰 반영**. 🔴O1 숫자 셀 제외 보류 (10MB 상한만 적용, O6 후 판단). 🔴O3 감지 기준 "letter 비율 < 20%"로 변경 (false positive 방지). 🟡O4a SdtRun 확인 → Descendants\<Run\>이 내부 Run 포함하므로 안전 확인. 🟡UI 피드백 M0-U 이월 명시. 🟡O6 fallback 조건 3단계 정의. |
