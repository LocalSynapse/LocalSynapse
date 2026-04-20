# LocalSynapse Roadmap — SSOT (Milestone 중심)

> **최종 업데이트**: 2026-04-20
> **역할**: 전체 로드맵의 **단일 진실 공급원 (Single Source of Truth)**.
> [PHASES.md](PHASES.md)는 2026-04-11 기준 감사 결함 중심 계획이었고 속도 진단(2026-04-14) + Ryan 후기 이후 outdated. 본 문서가 이를 대체하며, PHASES.md는 아카이브/참조용.
> **원칙**: 비전 절대 유지 · 측정이 추측을 이긴다 · 사용자 체감 우선 · 가정 금지

---

## 완료 현황 (커밋 확인, 2026-04-15 기준)

| 구 Phase | 커밋 | 내용 | 상태 |
|----------|------|------|------|
| Phase 0a/0b/0c | `e10414d` | BGE-M3 dispose + minSize 검증 / XAML Run OneWay / PRAGMA synchronous=NORMAL | ✅ done |
| Phase 1 Medium | `c30048c`~`86f1b3a` | dead code 제거, SettingsStore JSON, N+1 제거, UpsertFiles sub-batch, xunit 2.9.3 + 9 tests, 실측 -23.5% | ✅ done |
| v2.5.1 | `ab196f0` | DMG Sequoia 가이드 | ✅ done |
| v2.5.2 Hotfix | `ca552be` | P1-1~P1-11 전부 (Pause/Scan 버그, empty catch 6, `.mcpb` 거짓광고 제거, HwpParser 로깅) | ✅ done |
| Phase 2a | `d992610` | CI test gate + PR workflow + tag validation | ✅ done |
| Phase 2b Security | `88d4c4c` | A-10~A-18 (file oracle, zip/XML bomb, LIKE/FTS5 escape, stdin cap, error sanitize) | ✅ done, **A-11 SHA256 hash 값 pending** |
| Phase 2c | `f61431d` + `40af520` | Dense off (EmptyDenseSearch null object) + Click boost position weight | ✅ done |
| 속도 진단 v1.1 | `diag/speed-measurement` | 185 이벤트 측정, Extract 35 files/min 확인 | ✅ done |
| M0-H Hotfix | `8212552` | H3 Korean min-length guard (debounce 250ms + 2음절 최소) | ✅ done |
| M0-A 계측 | `ea0a6ee` + `3fe0695` | Extract phase instrumentation + analysis script | ✅ done |
| M0-B 덤프 도구 | `c22d697` | parser dump CLI + quality verification templates | ✅ done |
| M0-D-A 진단 | `docs/diagnostics/M0-A-analysis.md` | 6,512 EXTRACT_FILE 분석. xlsx 85.7% 병목 확인 | ✅ done |
| M0-D-B 진단 | `docs/diagnostics/M0-B-quality-report.md` | 80파일 8포맷 검증. 전체 4.3/5, CRITICAL 1 (PDF CMap) | ✅ done |
| M0-D-C 진단 | `docs/diagnostics/M0-C-bm25-analysis.md` | BM25 가중치/boost/쿼리파서 분석. filename 5.0x 과도 확인 | ✅ done |

**여전히 Pending / 미착수** (PHASES.md 기준):
- Phase 2 Step 4+5 (2d): FK 강화, SettingsStore thread safety, FTS 마이그 분할 — spec 있음, diff-plan 없음
- Phase 2 Step 6+7 (2e): MCP 안정화 + Parser 방어 — 문서 없음
- Phase 2 Step 9 (2f): MCP 백엔드 분리 재설계 (LocalSynapse.Mcp.Stdio 신규) — 문서 없음
- Phase 2.5 Stabilization: 33개 MAJOR+MINOR
- Phase 3 Long-term Hardening: SqliteWriteQueue(R2) / FTS external-content / cross-process broker(R13) / BenchmarkDotNet CI
- Phase 3b Localization (i18n)
- A-11 BGE-M3 SHA256 hash 실제 값 채우기

---

## Milestone 구조 (비전 단일 성공 기준)

| M | 이름 | 단일 성공 기준 | 진입점 |
|---|------|----------------|--------|
| **M0** | 즉각 사용자 경험 회복 | Ryan이 일상에서 LocalSynapse를 다시 쓴다 | 속도 진단 v1.1 |
| **M1** | MCP 정상 출시 | Claude Desktop에서 GUI 없이 MCP 동작 | Phase 2 Step 9 설계 |
| **M2** | Dense 부활 | BM25 + Dense + RRF 통합 ranking, 응답 시간 임계 미위반 | M0 측정 틀 재활용 |
| **M3** | 검증 인프라 + 데이터 안전 | 회귀 자동 감지 + 고아 데이터 0 + opt-in telemetry | M0과 병렬 |

### ⚠️ 출시 정책
- **v2.6.0 출시 완료** (`c71d932`) — i18n + 검색 UX + 4섹션 Smart Notes. Dense 없이 BM25 단독 출시. Ryan 일상 사용 중.
- **다음 릴리즈**: v2.7.0 — M2 Dense 부활 포함 시 출시.
- **M3 베타 채널 도입 후** 외부 사용자에게 가시화.
- 사업 트랙(Apple Developer, Pro tier, 마케팅)은 M0 + M3 완료 후 재개.

---

## Milestone 0 — 즉각 사용자 경험 회복

**성공 기준 (5개 전부 충족)**:
1. 검색 응답 200ms 이내 (최대 500ms)
2. 같은 검색 → 같은 결과 (일관성 보장)
3. Top 결과 강조 UI (224건 압도 해소, Semantic 0건 숨김)
4. 사용자 언어 UI 메시지 (파이프라인 카운트 → 비전 메시지)
5. 글로벌 사용자 피드백 채널

### M0-H. Hotfix 단계

**H1. 검색 성능 P1 최적화** ✅ done (`efcbfa7`)
- [x] PRAGMA cache_size = -65536
- [x] BM25 LIMIT 1800 → 600 (MaxMaterializeRows const)
- [ ] ExecuteSearchAsync 재진입 방지 (Bug 3-b) — H1 커밋에 미포함, 별도 처리 필요

**H2. UI 신뢰 버그** ✅ done (`efcbfa7`)
- [x] Bug 1: BGE-M3 Installed badge + Install 버튼 상태 동기화 (IsModelInstalled observable)
- [x] Bug 2: Stepper subtitles "text chunking" / "semantic vectors" 추가
- [x] 다운로드 크기 표시 수정 (~580MB → ~2.3GB)

**H3. IME commit 빈도 대응** ✅ done (`8212552`)
- [x] DebounceMs 150→250ms
- [x] `IsKoreanQuery` Hangul Syllables 감지 + 1음절 쿼리 skip
- [x] 근거: Avalonia 11.2.3 TextBox.Text는 IME 조합 중 미갱신 확정 ([M0-H-blocker-research.md](M0-H-blocker-research.md))

**H4. Before/After 재측정**
- [ ] 동일 계측 빌드 재측정, P95 ≤ 150ms / Max < 500ms 확인

**H5. BGE-M3 SHA256 hash 값 채움** ✅ done (`efcbfa7`)
- [x] HuggingFace LFS oid에서 실제 hash 조회, `RequiredFiles` 배열에 반영
- [x] tokenizer.json + sentencepiece.bpe.model sha256sum 수동 검증 완료

### M0-D. 진단 단계 (Hotfix 이후)

**A. Extraction 속도 진단** ✅ done ([M0-A-analysis.md](../diagnostics/M0-A-analysis.md))
- [x] 파서별 시간 계측 — xlsx 85.7%, pdf 3.8%, xml 3.4%
- [x] 파일 크기 vs Extract 시간 상관 — >10MB 206개 avg 41,142ms
- [x] Top 병목: xlsx sheets 단계 (p95=982ms, max=7,393,627ms)
- [x] 1위 파일: 세미파이브 분개장 45.5MB = 7,394초 단독

**B. 파서 품질 수동 검증** ✅ done ([M0-B-quality-report.md](../diagnostics/M0-B-quality-report.md))
- [x] 80파일 8포맷, 전체 4.3/5, 성공률 85%
- [x] CRITICAL: PDF CMap 인코딩 실패 (10건 중 1건)
- [x] MAJOR: DOCX numbering ID 누출, XLSX 대용량 성능
- [x] MINOR: HWP `<>` 태그, hidden sheet 추출, 대용량 md

**C. BM25 ranking 현황 분석** ✅ done ([M0-C-bm25-analysis.md](../diagnostics/M0-C-bm25-analysis.md))
- [x] FTS5 weight: text(1.0), filename(5.0), folder_path(0.5)
- [x] 후처리 filenameBoost 5.0x 이중 적용 → 100배 점수 차이 발생
- [x] NaturalQueryParser: 조사분리 40개 + 복합어 분해 + 쿼리확장
- [x] TextChunker offset 버그 없음 확인, FTS5 injection 방어 충분

**D. 검색 결과 순위 원칙** — RANK1에서 대부분 구현 완료. 잔여는 "검증 후 미세 조정"
- [x] filename boost 가산 전환 + 2.5x→0.5 additive (`ce7d805`)
- [x] recency 반감기 365일 + floor 0.3 (`ce7d805`)
- [x] folder_path weight 0.5→1.0 (`ce7d805`)
- [x] MatchSource [Flags] 컬럼별 BM25 판별 (`ce7d805`)
- [ ] 재측정(O6) 후 미세 조정 여부 판단 — Wave 2에서 R3과 통합

### M0-R. 검색 결과 품질 (RANK1/RANK2/RANK2-post 완료, 잔여 작업)

**R-RANK. 검색 랭킹 + UI 4섹션 + Smart Notes** ✅ done
- [x] RANK1: MatchSource [Flags], 컬럼별 BM25 점수, 가산 boost, recency (`ce7d805` + `e4b750a`)
- [x] RANK2: 4섹션 레이아웃 (Filename/Opened/Content/Folders) + 기본 Smart Notes (`96d46e3`)
- [x] RANK2-post: 19종 뱃지 색상 UI + i18n + 파일행 2줄 리디자인 (`c15e9d0` + `76420ec`)

**R1. 검색 결과 수 상한 상향** — Wave 1
- [ ] Filename max 10→20, Content max 20→50 (현재 합계 35 → 75)
- [ ] "더 보기" 접기 교착 버그 수정 (ShowMoreFilename 조건)

**R2. 검색 순위 미세 조정** — Wave 1 (R1과 동시)
- [ ] R1 상한 확대 후 낮은 관련성 파일 노출 여부 확인
- [ ] 필요 시 threshold 조정 또는 boost 파라미터 미세 튜닝

### M0-U. UI / 비전 가시화

**U1. 결과 UI 재설계** — 대부분 완료
- [x] 4섹션 구조 + Smart Notes 뱃지 (RANK2/RANK2-post)
- [x] Semantic 탭: 3탭 전체 제거, 4섹션으로 대체 (RANK2)
- [ ] Progressive rendering — **조건부**: O6 재측정에서 P95 > 500ms인 쿼리가 존재할 때만 착수. 0.2~0.4s면 불필요 (깜빡임 리스크)

**U2. 비전 메시지 + i18n** ✅ done
- [x] ILocalizationService + LocalizationRegistry + StringKeys (`3e663d4`)
- [x] TrExtension AXAML 마크업으로 전 페이지 다국어 (`3e663d4`)
- [x] ComboBox FilterOption 토큰/표시 분리 (`38c9b1f`)
- [x] 버전 v2.6.0 태깅 (`c71d932`)

**U3. 글로벌 사용자 피드백 채널** — Wave 2 (R3과 동시)
- [ ] Settings 페이지에 "Send Feedback" 버튼 (GitHub Issues 링크)
- [ ] 간단한 익명 submit path (GitHub issue API 또는 전용 폼)

### M0-O. 최적화 (진단 결과 기반) ✅ 코드 완료 (`9b4f4ed`), O6 측정 진행 중

**O1. xlsx 10MB 상한** ✅ done
- [x] 파일 크기 상한 10MB: >10MB xlsx → `TOO_LARGE` skip
- [x] 숫자 셀 제외: 보류 (리뷰 결과 — DOM 순회가 근본 원인, O6 후 재판단)

**O2. Filename boost 감소** ✅ done
- [x] 후처리 filenameBoost 5.0 → 2.5 (BM25 SQL weight 5.0 유지 — recall 보존)

**O3. PDF garble 감지** ✅ done
- [x] `IsLikelyGarbled` heuristic (letter 비율 < 20%)
- [x] 페이지별 필터링, 전체 garbled 시 `GARBLED_TEXT` Fail

**O4. DOCX ID 누출 + HWP `<>` 태그** ✅ done
- [x] DocxParser: `Descendants<Run>()` 기반 추출 (numbering ID 제거)
- [x] HwpParser: PrvText `<>` → 공백 치환

**O5. Embed phase 기본 skip** ✅ done
- [x] `SkipEmbeddingPhase = true` const (M2에서 제거)
- [x] M2 backfill: 기존 `EnumerateChunksMissingEmbedding` 자동 처리

**O6. 재측정 검증** 🔄 진행 중 (2026-04-16 17:31 시작)
- [ ] 인덱싱 ≤30분, xlsx TOO_LARGE skip 확인
- [ ] 검색 품질 (filename boost 변경 체감) — Ryan 수동 검증
- [ ] 미달 시 fallback: 5MB 하향 → 숫자 셀 재검토 → SAX

### M0-O 범위 밖 (후순위)

| 항목 | 이유 | 시점 |
|------|------|------|
| pdf error_PARSE_ERROR 34건 | 상세 분석 없이 대응 어려움 | M0-A2 필요 시 |
| pptx error 7건 | 비중 작음 | 후순위 |
| CJK trigram tokenizer | 구조적 변경, Dense와 묶어야 | M2 |
| OCR (스캔 PDF) | 외부 의존성 큼 | Milestone 4+ |
| Korean expansion 간소화 | A/B 측정 필요 | 검색 품질 Phase |
| folder_path / recency boost 조정 | M0-D Ryan 결정 필요 | M0-D |

### M0 실행 순서 (Wave 구조)

```
Wave 1: R1 (결과 수 상향) + R2 (순위 미세조정)        ≈ 1일
  → 사용자가 "파일이 안 나온다" 문제 즉시 해결
  → 상한과 순위를 동시 적용해야 품질 유지

Wave 2: R3/O6 (재측정) + U3 (피드백 채널)             ≈ 1일
  → 내부 측정(R3) + 외부 피드백(U3) 동시 확보
  → M0 성공 기준 5개 사실상 달성 시점

Wave 3: U1 Progressive rendering (조건부)              ≈ 0~1일
  → O6 결과 P95 > 500ms일 때만 착수
  → 0.2~0.4s면 skip (M0 완료 앞당김)

Wave 4: M3 검증 인프라 (Golden set + 회귀 CI)          ≈ 1주

Wave 5+: M1 MCP 출시, M2 Dense 부활                   ≈ 수주
```

**M0 완료 판정**: Wave 2 종료 시 성공 기준 5개 충족 여부 확인
1. 검색 응답 200ms 이내 — R3/O6 재측정으로 확인
2. 같은 검색 → 같은 결과 — RANK1에서 확보 (deterministic scoring)
3. Top 결과 강조 UI — RANK2+RANK2-post 4섹션+뱃지로 달성
4. 사용자 언어 UI 메시지 — Phase B i18n으로 달성
5. 글로벌 사용자 피드백 채널 — U3으로 달성

---

## Milestone 1 — MCP 정상 출시

**성공 기준**: Claude Desktop에서 LocalSynapse GUI 없이 MCP 동작 + UI+Stdio 동시 실행 안정

### M1-Design
- [ ] Phase 2 Step 9 recon/spec 작성 (현재 2f-*.md 없음)
- [ ] LocalSynapse.Mcp.Stdio 신규 프로젝트 설계 (UI 미참조)
- [ ] DI 분리 (`ServiceCollectionExtensions` 일부 이동)

### M1-Infra
- [ ] 멀티 RID CI (win-x64 / osx-arm64 / osx-x64 / linux-x64)
- [ ] 진짜 `.mcpb` zip + 런처 스크립트
- [ ] `server.json` 크로스 플랫폼 광고 복원

### M1-Safety (구 Phase 2 Step 6 + Phase 1 유예 R13)
- [ ] MCP newline framing / response size cap
- [ ] `ListIndexedFilesTool` N+1 제거
- [ ] topK/limit validation
- [ ] **Cross-process SQLite 공유 실측** — UI + Stdio 동시 실행 시 WAL race 검증 (named mutex 또는 파일 lock 필요 여부 결정)

### M1-Verify
- [ ] Claude Desktop 연결 end-to-end (GUI 없음 상태)
- [ ] UI + Stdio 동시 실행 시 DB 무결성 확인

---

## Milestone 2 — Dense 부활

**성공 기준**: BM25 + Dense + RRF 통합, M0 응답 시간 임계 미위반

### M2-Core
- [ ] DenseSearchService 재구현 — streaming embedding fetch (메모리 폭발 없이)
- [ ] HybridSearchService BM25 + Dense 활성화, EmptyDenseSearch null object 교체
- [ ] RrfFusion 정확성 검증 (구 Phase 2 Step 2)
- [ ] Embedding thread safety (구 Phase 2 Step 2)

### M2-Pipeline
- [ ] Embedding pipeline 재개 + 기존 chunks backfill 경로 (M0-O에서 skip했던 것 복구)
- [ ] `needs_embedding` flag 또는 backfill queue (M0에서 embed skip 설계 시 준비)

### M2-Budget
- [ ] 응답 시간 예산 관리: Dense 추가 시 +100~300ms 예상 → P95 500ms 유지 설계
- [ ] 짧은 쿼리는 BM25 only, 긴 쿼리/자연어만 Dense 실행 옵션 검토
- [ ] M0 계측 틀로 Before/After 측정

### M2-Release
- [ ] v2.7.0 출시 (Dense 포함, v2.6.0은 BM25 단독 출시 완료)

---

## Milestone 3 — 검증 인프라 + 데이터 안전 (M0과 병렬)

**성공 기준**: 회귀 자동 감지 + 데이터 무결성 + 외부 사용자 가시화 준비

### M3-Tests
- [ ] Golden set 복구 + 자동 회귀 감지 CI (Phase 2c 이후 재생성 필요)
- [ ] Integration.Tests sln 등록 (현재 untracked 상태)
- [ ] BenchmarkDotNet + 성능 regression CI (구 Phase 3) — H4/M2 계측 자동화

### M3-Data (구 Phase 2 Step 4+5에서 핵심만)
- [ ] A-40: PRAGMA foreign_keys = ON
- [ ] A-41: embeddings.file_id FK + ON DELETE CASCADE
- [ ] A-43: SettingsStore `_settings` lock 보호
- [ ] A-45: TryMigrateFromSqlite busy_timeout PRAGMA
- [ ] A-50: CreateFtsTables 후 fts_tokenizer_version 즉시 삽입 (fresh DB rebuild 경로 차단)
- [ ] A-51: UpgradeFtsTokenizerIfNeeded 3단계 분할 + chunked insert

### M3-Telemetry
- [ ] `diag/speed-measurement` 브랜치를 opt-in telemetry로 진화
- [ ] 100% offline 원칙 유지 (opt-in + 로컬 저장 + 사용자 수동 업로드)
- [ ] 수집 항목: P95/Max, Extract files/min, 파서별 시간, 쿼리 언어 분포

### M3-Beta
- [ ] 다운로드 폼에 "Join beta?" 체크박스
- [ ] 베타 릴리즈 트랙 별도 (v2.6.0-beta.N)
- [ ] 피드백 채널 (M0 U3와 연계)

### M3-Minor (선별 편입)
- [ ] A-44: WriteSettingsAtomic backup null
- [ ] Phase 2.5 UI/Threading: RefreshTimer race, async void handlers, Dispose race (M0 U1/U2 작업 중 발견 시 처리)

---

## Milestone 4+ (후순위 — 현재 trigger 없음)

- **Localization i18n** (구 Phase 3b): Ryan 한국어 화자라 자체 사용에는 급하지 않음. 외부 사용자 확보 후 재평가
- **Phase 2 Step 9 외 MCP 고급 기능**: M1 완료 후 trigger 발생 시
- **Phase 2.5 Stabilization 잔여**: data layer / release/build / MCP/search 세부 정리 — 증거 기반 개별 편입
- **Phase 3 Long-term Hardening 중 cross-process broker**: M1-Safety에서 필요하면 승격, 아니면 계속 후순위
- **A-42 GenerateFileId 16→32 hex**: collision 확률 ~3×10⁻⁸ — 실제 발생 전까지 보류

## 드롭 (근거 확보 후)

| 항목 | 드롭 근거 |
|------|----------|
| **SqliteWriteQueue (R2)** | Phase 1 Medium에서 -23.5% 달성. 속도 진단에서 DB write는 병목 아님. 증거 없이 유지할 필요 없음 |
| **Phase 1 Full C 원본 계획** | Phase 1 Medium으로 superseded (이미 기록됨) |
| **Phase 3b DMG dark mode** | Finder 자동 결정, 직접 제어 불가 (이미 dropped 기록됨) |
| **`async void` 전면 async 변환** | Phase 1 Medium에서 실측 검증 결과 불필요 판정 |

---

## 진행 원칙

1. **M0 선착순 처리** — Ryan dogfooding 복귀가 모든 것의 전제. M0 체크박스 5개 전부 ✅될 때까지 M1/M2 착수 금지. M3는 M0과 병렬 가능.
2. **Loop 워크플로우 적용** — 각 작업 묶음은 `/recon` → `/spec` → `/diff-plan` → `/execute`. Phase 번호는 M{N}-{subkey} 체계로 (예: `M0-H1`, `M1-Design`).
3. **긴 대화 리스크 최소화**:
   - Milestone 단위로 대화창 분리 권장
   - 새 대화창 첫 메시지: `"LocalSynapse Project. 시작 전 확인: 1) 진행 중 M?-?, 2) 직전 완료, 3) current-work-plan.md 확인 후 시작"`
   - 본 파일이 single source of truth. 진행 상태는 이 파일의 체크박스 업데이트로 기록.
4. **측정 우선** — 새 가정은 `diag/speed-measurement`로 계측 확인 후 진입.
5. **스코프 외 발견 시** — Milestone 아래 subkey로 편입 제안. 본 파일에 반영 후 실행.

---

## 변경 이력

| 날짜 | 변경 |
|------|------|
| 2026-04-14 | 초안 (Phase 0-A/0-B 명칭으로 혼란, Hotfix + 진단 A~D 중심) |
| 2026-04-15 | **전면 재작성**. Milestone 0~3 체계 도입, PHASES.md의 완료/pending 작업 전체 재분류, SSOT로 승격. |
| 2026-04-16 | **M0-D 진단 완료 반영** (A/B/C 전부 done). M0-H H3 완료 반영. **M0-O 구체화**: xlsx 텍스트만 추출 + 10MB 상한 (Ryan 확정), filename boost 감소, PDF CMap, DOCX/HWP 노이즈 수정, embed skip. 우선순위 6단계로 정리. 범위 밖 항목 명시. |
| 2026-04-20 | **RANK1/RANK2/RANK2-post/Phase B 완료 반영**. M0-D-D를 "검증 후 미세 조정"으로 재정의 (RANK1에서 구현 완료). M0-R 섹션 신설 (R1 결과 수 상향 + R2 순위 미세조정). M0-U 재구성: U1 대부분 완료, U2 i18n 완료, U3 Wave 2로 승격. **Wave 실행 계획 추가**: Wave 1(R1+R2) → Wave 2(R3+U3, M0 완료 판정) → Wave 3(조건부 Progressive) → Wave 4(M3) → Wave 5+(M1/M2). R4 Progressive rendering을 O6 결과 조건부로 전환. |
