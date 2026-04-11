# LocalSynapse — 작업 Phase 마스터 플랜

> 본 문서는 SQLite lock 근본 원인 분석, 포크(MaceGit-AI/LocalSynapseFork) 변경사항 검증, 적대적 리뷰, 자체 분석을 종합하여 도출한 작업 목록의 단일 진실 공급원(Single Source of Truth)이다.
>
> **목적**: 모든 작업을 phase 단위로 그룹핑하여 작업 누락/중복/순서 오류를 방지하고, 각 phase의 리스크와 의존성을 명시한다.
>
> **갱신 규칙**: phase가 시작/완료될 때마다 본 문서의 상태(STATE) 컬럼을 업데이트한다. 새로 발견된 작업은 phase에 편입하거나 새 phase로 추가한다.

---

## 분류 기준

| 차원 | 설명 |
|------|------|
| **위험도** | 🟢 낮음 (병렬 적용 가능) / 🟡 중간 (회귀 테스트 필요) / 🔴 높음 (단독 phase) |
| **상태** | `pending` / `in-progress` / `done` / `blocked` / `dropped` |
| **출처** | 포크 / 자체 분석 / 적대적 리뷰 / 사용자 요구 |

---

## Phase 0 — Quick Wins (즉시 적용 가능, 위험 최소)

병렬 적용 가능. 단일 commit으로 묶을 수 있음.

### Phase 0a 🟢 — BGE-M3 다운로드 안정화 [done]
- **출처**: 포크 `98fc14b` + 사용자 별도 개편 의향
- **변경**: `BgeM3Installer.cs`
  - `using` 블록으로 stream/fileStream dispose 순서 보장 (File.Move 실패 방지)
  - 다운로드 후 파일 크기 검증 (`fileStream.Length != expectedSize` → InvalidDataException + part 파일 삭제)
- **위험**: 없음 (정확성 수정)
- **의존성**: 없음

### Phase 0b 🟢 — SearchPage FormatException 수정 [done (부분: Run 1곳, defensive)]
- **출처**: 포크 `1cc3030` 부분 cherry-pick (Localization 제외)
- **변경**:
  - `Converters/CountToBoolConverter.cs` 신규
  - `SearchPage.axaml` 5곳, `DataSetupPage.axaml` 1곳의 `<Run Text="{Binding ..., StringFormat=...}">`에 `Mode=OneWay` 명시
- **위험**: 없음 — OneWay는 ViewModel→View 단방향만 허용
- **의존성**: 없음

### Phase 0c 🟢 — `CreateConnection()` PRAGMA 보강 [done]
- **출처**: 적대적 리뷰 R1
- **변경**: `SqliteConnectionFactory.CreateConnection()`의 PRAGMA에 `synchronous=NORMAL` 추가 (1줄)
- **위험**: 없음 — write 속도 향상만
- **의존성**: 없음

---

## Phase 1 — SQLite 데이터 액세스 아키텍처 대단위 리팩토링 🔴 [pending]

> **이 phase는 단일 큰 작업으로 진행한다.** Sub-phase로 쪼개지 않는다.
> 이유: SettingsStore, write 직렬화, N+1, multi-process race가 모두 같은 아키텍처 결함의 다른 증상이며,
> 부분 적용 시 새로운 race condition 또는 inconsistency가 발생할 수 있다.

### 진입 조건 (반드시 충족)
1. Phase 0 (0a, 0b, 0c) 완료 — 기반 안정화
2. **사전 재검증 필수** — 본 phase 시작 직전에 다음을 다시 수행:
   - 코드베이스가 본 문서 작성 시점과 동일한지 확인 (origin/main fast-forward)
   - 적대적 리뷰어 Agent로 본 phase의 diff-plan 독립 검증
   - 최적 아키텍처 옵션 (B vs C vs D) 재평가 — 그동안 새로 발견된 contention 경로가 있는지

### 알려진 문제 (해결 대상)

본 phase가 해결해야 하는 모든 결함을 한곳에 모음:

#### A. 아키텍처 결함
1. `SqliteConnectionFactory.ExecuteSerialized()` / `GetConnection()` / `_lock` — **dead code** (호출자 0개)
2. 모든 Repository가 `CreateConnection()`으로 매번 새 connection 생성 → write 직렬화 부재
3. `SettingsStore`가 factory를 우회하여 자체 raw `SqliteConnection` 생성 (PRAGMA 없음, busy_timeout 0)
4. SettingsStore와 SqliteConnectionFactory 간 잠재 순환 의존성 (SettingsStore.GetDatabasePath ↔ Factory(ISettingsStore))

#### B. Connection 패턴 결함
5. `Bm25SearchService.ExecuteSearch()` 142줄 — reader 루프 안에서 `_clickService.GetBoost()` N+1 호출. 검색 1회당 1+200개 connection (R3)
6. `CreateConnection()`에 `synchronous=NORMAL` 미설정 (Phase 0c에서 임시 보강 예정 → 이 phase에서 통합)
7. 30초 busy_timeout이 batch operation에 비현실적 (R8) — 500개 batch × 30s = worst case 4시간

#### C. Transaction 결함
8. `FileRepository.UpsertFiles()` — 500개 batch (1500 SQL)를 단일 트랜잭션으로 묶음. 트랜잭션 점유 시간 길어 다른 write가 timeout
9. `FileRepository.DeleteByPaths()` — try-catch 없이 두 cmd 실행, 예외 시 tx dangling (R4)
10. `FileRepository.BatchUpdateExtractStatus()` — partial failure silent (R5)
11. `MigrationService.UpgradeFtsTokenizerIfNeeded()` — DROP 3 FTS + 전체 재적재 + CREATE 트리거를 단일 거대 트랜잭션. 대용량 DB에서 수분간 lock
12. `MigrationService.RunMigrations()` 직후 `StartAutoRunAsync` 호출 — 마이그 race 가능성 (R6)

#### D. 외부 결함
13. GUI + MCP 동시 실행 시 process 간 직렬화 zero (`_cycleLock`은 in-process only) (R7)

### 후보 아키텍처 옵션 (시작 직전 재평가 필수)

| 옵션 | 핵심 아이디어 | 장점 | 단점 |
|------|--------------|------|------|
| **B. 단일 write connection + lock 직렬화** | dead code인 `ExecuteSerialized` 부활. write는 single conn + lock, read는 그대로 새 conn | 중간 변경량. 기존 facade 활용 | batch tx 동안 다른 write 대기. 대용량 작업 시 UI 멈춤 |
| **C. SqliteWriteQueue (background worker)** | 모든 write를 queue에 enqueue. worker 1개가 순차 실행 | 완전 직렬화. async 자연스러움. 메트릭 가능 | 가장 큰 리팩토링. 동기 → async 변환 필요 |
| **D. Hybrid: B + N+1 제거 + SettingsStore 분리** | B의 단순함 + 가장 큰 hot bug만 추가 정리 | 실용적 균형 | 일부 결함 잔존 가능 |
| **E. Connection-per-operation + WAL 강화** | 모든 connection에 일관 PRAGMA + 짧은 트랜잭션 | 변경 최소 | 직렬화 보장 없음. multi-writer race 미해결 |

**시작 시점에 반드시 결정**: 옵션 B/C/D 중 무엇을 채택할지. 적대적 리뷰어에게 옵션 비교 검증을 요청한 후 결정.

### 해결해야 할 모든 항목 (체크리스트)

본 phase가 완료되었다고 선언하려면 다음이 **모두** 충족되어야 함:

#### 코드 변경
- [ ] `AppPaths.cs` / `IAppPaths.cs` 신규 — zero-dependency 경로 클래스
- [ ] `SqliteConnectionFactory(IAppPaths)` 생성자 변경 (순환 의존성 제거)
- [ ] `SettingsStore`를 SQLite → JSON 파일로 전환 + atomic write (temp + File.Move) + 기존 SQLite settings 테이블에서 마이그레이션 코드
- [ ] DI 등록 순서 조정: `IAppPaths` → `SqliteConnectionFactory` → `ISettingsStore`
- [ ] `SqliteConnectionFactory`의 dead code (`ExecuteSerialized`, `GetConnection`, `_lock`) 처리:
  - 옵션 B/D 채택 시: 부활시켜 모든 write 메서드가 사용
  - 옵션 C 채택 시: 삭제하고 `SqliteWriteQueue`로 대체
  - 옵션 E 채택 시: 삭제 후 PRAGMA 일관 적용
- [ ] 모든 Repository **write** 메서드 리팩토링 (직렬화 경로로 이동):
  - `FileRepository.UpsertFiles`, `UpdateExtractStatus`, `BatchUpdateExtractStatus`, `DeleteByPaths`
  - `ChunkRepository.UpsertChunks`, `DeleteByFileId`
  - `EmbeddingRepository.Insert*`, `Delete*`
  - `PipelineStampRepository.Stamp*`, `Update*`
  - `SearchClickService.RecordClick`, `OnNewSearch`
- [ ] `FileRepository.UpsertFiles` batch 분할 — 500개 → 100개 sub-batch + sub-batch 사이에 lock 해제
- [ ] `FileRepository.DeleteByPaths` tx nesting bug 수정 (try-catch + rollback)
- [ ] `FileRepository.BatchUpdateExtractStatus` partial failure 처리
- [ ] `Bm25SearchService.ExecuteSearch` N+1 제거 — reader materialize 후 `WHERE file_path IN (?, ?, ...)` batch 조회 1회로 통합
- [ ] `SearchClickService`에 `GetBoostBatch(query, paths)` 메서드 추가
- [ ] `MigrationService.UpgradeFtsTokenizerIfNeeded` 진행률 보고 + 단계별 분할 검토
- [ ] `MigrationService` 시작 race 방어 검증 (App.axaml.cs에서 동기 호출 순서 확인)
- [ ] `CreateConnection()`에 `synchronous=NORMAL` 통합 (Phase 0c와 통합)
- [ ] (옵션) Multi-process race 차단 — `App.axaml.cs`에서 named mutex/file lock으로 GUI 점유. MCP 모드는 read-only로 동작하거나 거부. **사용자 결정 필요** (GUI + MCP 동시 사용 허용 여부)

#### 검증
- [ ] 빌드: `dotnet build LocalSynapse.v2.sln` 0 errors, 0 warnings
- [ ] 회귀 시나리오 통과:
  - 스캔 진행 중 사용자 검색 + 결과 클릭 5회 → lock 에러 0건, 검색 응답 < 1초
  - 스캔 진행 중 Settings에서 언어 변경 → 즉시 반영, lock 에러 0건
  - 스캔 + Pipeline indexing 동시 진행 → stamps 정확 업데이트, 데이터 정합성
  - 빈 DB에서 첫 스캔 (60K 파일) → 완료까지 lock 에러 0건
  - 마이그레이션 (FTS 재구성)이 5분 이상 걸리는 큰 DB → 진행률 표시, 다른 write 대기열, 정상 처리
- [ ] 적대적 리뷰어 사후 검증 통과 (CONDITIONAL PASS 이상)

### 위험 및 완화

| 위험 | 완화 |
|------|------|
| 리팩토링 범위가 거대 — 회귀 가능성 | Loop Workflow `/recon` → `/spec` → `/diff-plan` (적대적 리뷰 포함) → `/execute` 강제 적용 |
| 기존 사용자 SQLite settings 데이터 손실 | 마이그레이션 코드 필수. 첫 실행 시 SQLite settings 테이블 → settings.json 이전 |
| Read 메서드가 lock 보유한 트랜잭션 동안 stale read | WAL 모드의 snapshot isolation으로 무관 (기존과 동일 동작) |
| MCP 사용자가 GUI 동시 실행 못하게 되면 불만 | 사용자 결정 필요. 옵션 1f (multi-process)는 사용자 승인 후 적용 |
| 마이그레이션 코드가 기존 데이터 손상 | 적대적 리뷰에서 검증 필수. backup 권장 |

### 의존성
- **선행**: Phase 0 (0a, 0b, 0c) 완료
- **후행**: Phase 2a (Localization), Phase 2b (메트릭) 가능

---

## Phase 2 — 기능 확장

### Phase 2a 🔴 — Localization (다국어 i18n) [pending]
- **출처**: 포크 `f3804b9` + `a829d9a`
- **변경**:
  - `LocalizationService.cs` (85줄) 신규
  - `Resources/Strings.{en,de,ko-KR}.axaml` (각 169줄)
  - 모든 ViewModels + Views의 사용자 텍스트를 `{DynamicResource ...}`로 변경
  - `SettingsViewModel`에 ChangeLanguage 명령
- **추가 작업 (포크에 없음)**:
  - 한국어 번역 검수 (포크는 독일어 화자 작성)
  - 기존 macOS 분기 텍스트(`SearchShortcutText`, `IndexedSummaryText`)를 LocalizationService와 통합 방안 결정
- **위험**: 매우 높음 — merge 충돌 다수, 거의 모든 axaml 변경
- **의존성**: Phase 1 완료 후 (lock 안정화 후 진행해야 분리된 검증 가능)
- **단독 phase로 분리 필수**

### Phase 2b 🟡 — Pipeline 메트릭 + 모니터링 [pending]
- **출처**: 적대적 리뷰 + 자체 분석
- **변경**:
  - 직렬화 경로(Phase 1 채택 옵션에 따라) 실행 시간 측정 → lock wait 감지
  - `MigrationService.UpgradeFtsTokenizerIfNeeded` 진행률 표시 (큰 DB에서 수분 멈춤 방지)
  - Debug.WriteLine 기반 lock 통계 출력 (디버깅용)
- **위험**: 낮음 — 관찰 위주
- **의존성**: Phase 1 완료 후 의미 있음

---

## Phase 3 — 워크플로우 / 문서 / 사소한 작업

### Phase 3a 🟢 — Loop Workflow 본격 적용 [in-progress]
- **출처**: 사용자가 이미 적용한 가이드 (loop-workflow-setup-guide.md)
- **변경**: 본 문서 작성 자체가 첫 진입. SQLite 작업부터 `/recon` → `/spec` → `/diff-plan` (diff-reviewer 적대적 검증) → `/execute` 흐름 강제
- **위험**: 없음 — 프로세스 변경
- **의존성**: 없음

### Phase 3b 🟢 — DMG 다크모드 레이블 텍스트 색상 (선택) [dropped]
- **출처**: 이전 작업 미해결 항목
- **결정**: macOS Finder가 배경 밝기에 따라 자동 결정 — 직접 제어 불가. 적용 안 함, README/문서에만 기록
- **상태**: dropped

---

## 적용 안 함 (적용 불필요)

| 항목 | 사유 |
|------|------|
| 포크 `8c98817` SettingsStore unlock | C# `using var` 동작 오해, 의미 없음 (`using var` = `using {}` 동등) |
| 포크 `1180fbd` SQLite busy_timeout 추가 | Phase 1의 SettingsStore JSON 전환이 완전 대체 |
| 포크 `8a1dbaf`, `a1dbbfe` .gitignore | LocalSynapse .gitignore와 충돌, 불필요 |

---

## Phase 의존성 그래프

```
Phase 0a (BGE-M3) ──┐
Phase 0b (Format)   ├── 독립, 병렬 적용 가능, 단일 commit 가능
Phase 0c (PRAGMA)  ──┘
        │
        ▼
Phase 1 (SQLite 대단위 리팩토링) ─── 단독 phase, 시작 직전 재검증
        │
        ▼
Phase 2a (Localization) ─── 단독 phase
Phase 2b (메트릭) ───────── Phase 1 완료 후

Phase 3a (Loop Workflow) ─── 진행 중 (본 문서 작성 자체가 진입)
Phase 3b (DMG dark) ──────── dropped
```

---

## 권장 실행 순서

| 순서 | Phase | 상태 |
|------|-------|------|
| 1 | **0a + 0b + 0c** (단일 commit) | pending |
| 2 | **Phase 1 사전 재검증** (코드 fast-forward + 적대적 리뷰 + 옵션 B/C/D 결정) | pending |
| 3 | **Phase 1 본격 진행** (Loop Workflow: `/recon` → `/spec` → `/diff-plan` → `/execute`) | pending |
| 4 | **Phase 2b** (메트릭 — Phase 1 효과 측정) | pending |
| 5 | **Phase 2a** (Localization — 단독, 한국어 번역 검수) | pending |
| 6 | (Phase 1f — multi-process 차단, 사용자 결정 필요) | pending |

---

## 통계

| Phase | 작업 수 | 예상 commit 수 | 위험 |
|-------|---------|--------------|------|
| Phase 0 | 3 | 1 | 🟢 |
| Phase 1 | 1 (대단위) | 1~3 | 🔴 |
| Phase 2 | 2 | 2 | 🔴🟡 |
| Phase 3 | 2 | 0~1 | 🟢 |
| **합계** | **8개 작업** | **4~7 commits** | — |

---

## 변경 이력

| 날짜 | 변경 |
|------|------|
| 2026-04-11 | 초안 작성. SQLite 작업을 단일 대단위 phase로 통합. Phase 0/1/2/3 구조 확정. |
| 2026-04-11 | Phase 0 실행 완료 (0a/0b/0c). Loop Workflow 전체 적용 (recon→spec→diff-plan→review→execute). diff-reviewer BLOCK 판정 1회 후 수정 반영하여 통과. 단일 commit으로 병합. |

---

## 다음 액션

1. **Phase 0 즉시 진행 가능** — `0a + 0b + 0c` 단일 commit으로 적용
2. **Phase 1 진입 조건 검증 후 시작** — 본 문서를 다시 읽고, 코드베이스 fast-forward 확인, 적대적 리뷰어로 옵션 결정
3. **본 문서는 phase 진행 시마다 업데이트** — 상태(pending → in-progress → done) 갱신
