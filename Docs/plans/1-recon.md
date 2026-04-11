# Phase 1 Recon — SQLite 데이터 액세스 아키텍처 대단위 리팩토링

> **대상 범위**: PHASES.md Phase 1 전체 (13개 결함)
> **전제**: Phase 0 완료 (`e10414d`). Phase 0c의 `synchronous=NORMAL` 보강 포함됨.
> **목적**: (1) PHASES.md의 13개 결함 주장을 실제 코드로 재검증하여 **과장/사실을 분리**, (2) 아키텍처 옵션 B/C/D/E를 현재 상태 기반으로 재평가, (3) 실행 전 반드시 해결할 미결 사항을 나열.
>
> ⚠️ **중요**: 본 phase는 단일 대단위 작업이지만, spec/diff-plan 단계에서 **논리적 단위로 순서를 분리**하여 각 단계마다 빌드 통과를 보장해야 한다. 한 PR에 모두 들어가더라도 논리적 순서가 없으면 리뷰 불가능.

---

## 1. PHASES.md 13개 결함 실측 검증 — 사실 vs 과장

Phase 0 완료 후 현재 코드 기준으로 PHASES.md에 기재된 13개 결함을 전부 실측했다. **일부는 과장 또는 잘못된 주장**으로 판명되었으며, 정확한 문제 정의 없이 리팩토링을 시작하면 불필요한 변경이 발생할 수 있다.

### ✅ 실존 결함 (수정 필요)

#### R1. `SqliteConnectionFactory` dead code (확인 ✅)
- [SqliteConnectionFactory.cs:14](src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs#L14) `_lock`
- [SqliteConnectionFactory.cs:35](src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs#L35) `GetConnection()`
- [SqliteConnectionFactory.cs:41,52](src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs#L41) `ExecuteSerialized<T>` / `ExecuteSerialized`
- **grep 검증**: `ExecuteSerialized|GetConnection\(`의 호출 결과는 **정의 자체 4줄뿐**. 호출자 0개. 완전한 dead code.
- 생성자에서 만든 `_connection` (long-lived)도 `GetConnection()`에서만 반환되는데 호출자 없음 → **_connection 자체도 dead**.

#### R2. 모든 Repository가 `CreateConnection()` 사용 (확인 ✅)
- `FileRepository`, `ChunkRepository`, `EmbeddingRepository`, `PipelineStampRepository`, `SearchClickService`, `Bm25SearchService`, `MigrationService` — 7개 모두 매 호출마다 `_connectionFactory.CreateConnection()` → open → dispose 패턴.
- **Write 직렬화 부재**: 공유 connection 없음, lock 없음. 동시 write는 SQLite의 file-level lock + `busy_timeout=30000`에만 의존.

#### R3. `SettingsStore`가 factory 우회 (확인 ✅)
- [SettingsStore.cs:94-100](src/LocalSynapse.Core/Repositories/SettingsStore.cs#L94-L100) `OpenConnection()` — raw `SqliteConnection` 생성.
- **PRAGMA 없음**: `busy_timeout` 0 (기본), `journal_mode` 설정 없음 (WAL은 DB-level이므로 유지되지만 새 connection에 PRAGMA 재적용 없음), `synchronous` FULL.
- Phase 0c의 `CreateConnection()` 보강은 SettingsStore에 **적용되지 않음**.
- `GetLanguage()`가 스캔 중 호출되면 busy_timeout 0으로 즉시 `SQLITE_BUSY` → UI 즉시 실패 가능.

#### R5. `Bm25SearchService.ExecuteSearch` N+1 (확인 ✅, 매우 심각)
- [Bm25SearchService.cs:142](src/LocalSynapse.Search/Services/Bm25SearchService.cs#L142) `_clickService.GetBoost(originalQuery, r.GetString(2))` — **reader 루프 안에서** 호출.
- `GetBoost()`는 [SearchClickService.cs:96](src/LocalSynapse.Search/Services/SearchClickService.cs#L96) 자체적으로 `_connectionFactory.CreateConnection()` 호출 → 매 결과마다 **새 connection open/PRAGMA/close**.
- LIMIT = `options.TopK * options.ChunksPerFile * 3` → 기본값에 따라 **수백 connection**. reader가 아직 열려 있는 connection 1개 + boost용 새 connection N개.
- **Hot path**: 이 경로는 사용자가 매 검색마다 실행. 현재 캐시 덕분에 30초 TTL 동안은 재실행 안 됨 ([Bm25SearchService.cs:32](src/LocalSynapse.Search/Services/Bm25SearchService.cs#L32)).

#### R7. 30초 `busy_timeout` batch 대비 비현실적 (사실 ✅)
- `CreateConnection()`에 `busy_timeout=30000` 설정됨.
- 다른 writer가 5분짜리 batch를 쥐고 있으면, 대기 중인 writer는 30초 후 `SqliteException: SQLITE_BUSY` throw. 이게 repo 호출자에게 그대로 전파됨 (catch 없음).
- **UI 블로킹**: 스캔 중 사용자 검색/설정 변경 시 즉시 에러.

#### R8. `FileRepository.UpsertFiles` 전체 batch 단일 tx (부분 확인 ⚠️)
- [FileRepository.cs:57-149](src/LocalSynapse.Core/Repositories/FileRepository.cs#L57-L149) — `IEnumerable<FileMetadata>` **전체**를 단일 tx로 묶음. 500개는 호출자가 결정하는 값.
- tx 안에서 `files` INSERT + `files_fts` DELETE + `files_fts` INSERT **3회** per file → 500개 × 3 = 1500 SQL.
- 한 tx 동안 다른 모든 writer는 busy_timeout 대기 → UI lag.
- 해결: 50~100 sub-batch 분할 + batch 사이 tx 해제.

#### R10. `FileRepository.BatchUpdateExtractStatus` partial failure silent (부분 확인 ⚠️)
- [FileRepository.cs:249-265](src/LocalSynapse.Core/Repositories/FileRepository.cs#L249-L265) — `using var tx`로 tx 시작, foreach 내부에서 ExecuteNonQuery, 마지막에 commit.
- **tx 자체는 자동 rollback** (C#이 using dispose 시 commit 안 된 tx rollback). 그러므로 "SQL 예외 시 일관성 깨진다"는 주장은 **과장**.
- 그러나 **0 rows affected 감지 안 됨**: 어떤 id가 존재하지 않아도 ExecuteNonQuery는 0 반환하고 성공 처리. 이게 silent partial failure.
- 해결: 영향 row 수 집계 + 기대치와 다르면 warning.

#### R11. `MigrationService.UpgradeFtsTokenizerIfNeeded` 거대 tx (확인 ✅)
- [MigrationService.cs:412-514](src/LocalSynapse.Core/Database/MigrationService.cs#L412-L514) — `using var tx` + 명시 `tx.Rollback()` 있음 (error handling 양호).
- 그러나 tx 안에서: FTS 3개 DROP + CREATE + 트리거 3개 DROP + CREATE + **`INSERT ... SELECT` 로 file_chunks 전체 재적재** + emails 재적재 + settings 기록.
- 대용량 DB (예: 100만 chunks)에서 수분~수십분 걸리는 거대 tx. 이 동안 다른 모든 writer는 대기 → app freeze.
- 진행률 표시 없음.

#### R13. GUI + MCP multi-process race (사실 ✅)
- `SqliteConnectionFactory._lock`은 in-process only. 별도 process (MCP stdio)는 WAL + `busy_timeout`만으로 serialize.
- WAL은 동시 reader 허용하지만 writer는 여전히 한 번에 하나. 두 process가 동시에 write하면 busy_timeout 대기 → race 없음, 성능만 저하.
- "race"는 과장이지만, 대용량 작업 시 상호 대기는 실존.

### ❌ 과장 또는 잘못된 주장 (수정 범위 축소 가능)

#### R4. SettingsStore ↔ SqliteConnectionFactory 순환 의존성 (❌ 과장)
- 실제 코드:
  - `SqliteConnectionFactory` 생성자 ([SqliteConnectionFactory.cs:17](src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs#L17)) — `ISettingsStore`를 받아 `settings.GetDatabasePath()` 1회 호출.
  - `SettingsStore` — `SqliteConnectionFactory`를 **전혀 참조하지 않음**. 자체 `_connectionString`으로 동작.
- **즉 순환은 없고 단방향 의존성**: `SqliteConnectionFactory → ISettingsStore` (경로 조회용).
- DI 등록 순서도 이미 `ISettingsStore` → `SqliteConnectionFactory` 순 ([ServiceCollectionExtensions.cs:30-31](src/LocalSynapse.UI/Services/DI/ServiceCollectionExtensions.cs#L30-L31)).
- "잠재적 순환 의존성"이란 표현은 **PHASES.md 작성자의 추측일 뿐**, 실제로는 문제 없음.
- 다만 **`IAppPaths` 분리가 여전히 유용**: SettingsStore가 SQLite 기반인 한, 경로 조회라는 단순 책임이 "SQLite 가용성"과 섞이는 구조는 부적절.

#### R6. `App.axaml.cs`의 마이그-auto run race (❌ 과장)
- [App.axaml.cs:38-55](src/LocalSynapse.UI/App.axaml.cs#L38-L55) — `migration.RunMigrations()` **동기** 호출 후 `Task.Run`으로 orchestrator 시작.
- `RunMigrations`는 완전 동기. 리턴 이후에 `Task.Run`이 실행되므로 race 없음.
- **수정 불필요**.

#### R9. `FileRepository.DeleteByPaths` tx dangling (❌ 과장)
- [FileRepository.cs:193-232](src/LocalSynapse.Core/Repositories/FileRepository.cs#L193-L232) — `using var tx = conn.BeginTransaction();` 패턴.
- 예외 발생 시 C# using이 tx를 dispose → uncommitted tx는 자동 rollback.
- **"dangling"은 틀림**. 단 명시적 `catch` 없어서 예외 메시지 로깅 부재 → 디버깅 어려움은 사실.
- 해결: try-catch + Debug.WriteLine 추가는 개선이지만 **정합성 문제는 아님**.

#### R12. `_cycleLock`은 in-process only (사실이지만 이미 존재 — 추가 작업 불필요)
- 이 항목은 "multi-process race" R13과 중복. R13으로 통합.

### 📊 요약 표

| # | PHASES.md 주장 | 실측 판정 | 심각도 | 수정 필요 |
|---|--------------|---------|--------|---------|
| R1 | dead code 3종 | ✅ 사실 | 🟡 혼란 유발 | 제거 또는 부활 |
| R2 | CreateConnection 전면 사용 | ✅ 사실 | 🔴 write 직렬화 부재 | 핵심 수정 |
| R3 | SettingsStore 우회 | ✅ 사실 | 🔴 PRAGMA/timeout 0 | 핵심 수정 |
| R4 | 순환 의존성 | ❌ 과장 | 🟢 | 불필요 (단 IAppPaths는 유익) |
| R5 | N+1 in ExecuteSearch | ✅ 사실 | 🔴 hot path | 핵심 수정 |
| R6 | PRAGMA synchronous | Phase 0c에서 해결 | — | 완료 |
| R7 | 30s busy_timeout 비현실 | ✅ 사실 | 🟡 대안: 직렬화 경로 | 아키텍처 변경 시 해결 |
| R8 | UpsertFiles 거대 batch tx | ✅ 사실 | 🟡 UI lag | sub-batch 분할 |
| R9 | DeleteByPaths dangling | ❌ 과장 | 🟢 | 로깅 추가만 |
| R10 | BatchUpdate partial silent | ⚠️ 부분 | 🟡 | 감지 추가 |
| R11 | 거대 FTS migration tx | ✅ 사실 | 🟡 app freeze | 진행률 + 단계 분할 |
| R12 | Migration race | ❌ 과장 | 🟢 | 불필요 |
| R13 | GUI+MCP multi-process | ✅ 사실 | 🟡 성능 저하 | 사용자 결정 필요 |

**결론**: 13개 중 **실제 핵심 결함은 R1/R2/R3/R5 (4개)**, **2차 결함은 R7/R8/R10/R11/R13 (5개)**, **과장은 R4/R6/R9/R12 (4개)**.

---

## 2. 아키텍처 옵션 재평가

PHASES.md가 제시한 옵션 B/C/D/E를 현재 코드 상태 + Phase 0 완료 상태 + 실측 결과 기준으로 재평가한다.

### 옵션 B. 단일 write connection + lock 직렬화
- **핵심**: dead code인 `_connection` + `_lock` + `ExecuteSerialized`를 **부활**. 모든 write는 공유 connection + lock. Read는 `CreateConnection()` 유지.
- **변경 규모**: 중간. 7개 Repository의 write 메서드를 `ExecuteSerialized` 경로로 이동.
- **장점**:
  - 기존 facade 활용 (dead code 부활이므로 새 인프라 최소)
  - Write 완전 직렬화 → N+1 경로의 `GetBoost()`가 Read이므로 아무 문제 없음
  - 동기 API 유지 가능 (기존 repo signature 불변)
- **단점**:
  - Batch tx 동안 다른 write는 대기 (lock 자체가 writer 대기 큐). UI 스레드에서 write 트리거 시 UI freeze 가능.
  - **단 WAL이므로 다른 reader는 무관**. 검색 중 스캔 진행 시 검색은 계속 가능.
  - `_connection` 1개로 모든 write를 직렬화하면 SettingsStore도 이 경로를 써야 함 → R3 해결 필요.

### 옵션 C. `SqliteWriteQueue` (background worker)
- **핵심**: 모든 write를 queue에 enqueue. worker thread 1개가 순차 dequeue + 실행.
- **변경 규모**: 가장 큼. 7개 Repository의 **모든 write 메서드**를 async로 변환. 기존 호출자도 await 필요 → 파급 범위 거대.
- **장점**:
  - 완전 직렬화
  - Async 자연스러움
  - Worker에서 메트릭/로깅 집중 가능
  - Writer가 background 스레드에 고정되므로 UI 스레드 완전 보호
- **단점**:
  - 가장 큰 리팩토링. 동기 → async 변환이 Pipeline/Scan 코드 전체에 파급
  - 에러 처리 복잡 (enqueue 시점에 throw인가, dequeue 완료 시점에 throw인가)
  - Transaction 경계가 모호 (UpsertFiles batch 전체를 한 enqueue로?)
  - **Phase 1 범위를 초과할 가능성**

### 옵션 D. Hybrid — B + N+1 제거 + SettingsStore 분리
- **핵심**: 옵션 B + 가장 hot한 결함(R5 N+1, R3 SettingsStore)을 추가로 정리. R8/R10/R11은 Phase 2로 유보.
- **변경 규모**: 중간 (B보다 약간 크지만 C보다 훨씬 작음).
- **장점**:
  - 실용적. 사용자가 체감하는 top 3 문제 (write race, search latency, settings lock)를 모두 해결
  - Phase 0의 기반 위에서 자연스러운 확장
  - R4는 과장이므로 `IAppPaths` 분리는 선택. SettingsStore의 책임만 **JSON 전환 or factory 사용**으로 단순화
- **단점**:
  - R8/R10/R11은 다음 phase 대기
  - 중간 상태 — "완전 해결"이 아니라 "80% 해결"

### 옵션 E. Connection-per-operation + WAL 강화
- **핵심**: 모든 connection에 일관 PRAGMA만 적용. Phase 0c가 이미 일부 수행.
- **변경 규모**: 최소.
- **장점**: 변경 최소.
- **단점**: Write 직렬화 보장 없음. R1/R2/R5 미해결. **핵심 결함 해결 안 됨**.
- **판정**: **Phase 0c로 이미 채택된 부분적 옵션**. Phase 1에서 확장할 가치 없음.

### 최종 결정: **Full C (SqliteWriteQueue + async)** ✅ (2026-04-11 사용자 확정)

사용자 목표가 "**견고하고 효과적인 아키텍처**"이므로 Hybrid의 부분 해결을 거부하고 Full C를 채택한다.

**Full C가 해결하는 것** (Hybrid 대비 추가):
- **R7 실효 해결**: writer 대기가 channel queue이므로 busy_timeout 30초 한계 자체가 무의미해짐
- **R8 자동 해결**: worker가 batch coalescing + 내부 sub-batch 실행 정책을 한 곳에서 관리
- **R10 자동 해결**: worker 1곳에서 partial failure 감지/로깅/재시도 정책
- **R11 자동 해결**: MigrationService도 WorkItem으로 enqueue → 진행률 콜백 표준화
- **R13 자동 해결**: worker 시작 시 file lock 획득 → multi-process 자연스럽게 차단
- **UI 스레드 절대 보호**: write 호출 = 즉시 channel enqueue + Task 반환. lock 대기 0.
- **메트릭 후킹 집중**: Phase 2b가 Phase 1에 자동 흡수 (worker 입/출구에서만 측정)
- **Backpressure 가능**: queue depth 상한 + 초과 시 정책 (reject/wait/drop)

**Full C 시그니처 변경** (Hybrid/C-Lite 대비 추가 파급):
- `IFileRepository.UpsertFile` → `UpsertFileAsync`
- `IFileRepository.UpsertFiles` → `UpsertFilesAsync`
- `IFileRepository.UpdateExtractStatus` → `UpdateExtractStatusAsync`
- `IFileRepository.BatchUpdateExtractStatus` → `BatchUpdateExtractStatusAsync`
- `IFileRepository.DeleteByPaths` → `DeleteByPathsAsync`
- `IChunkRepository.UpsertChunks` → `UpsertChunksAsync`
- `IChunkRepository.DeleteChunksForFile` → `DeleteChunksForFileAsync`
- `IPipelineStampRepository.Stamp*/Update*` → `*Async` (8개 메서드)
- `SearchClickService.RecordClick/OnNewSearch` → `*Async`
- `IMigrationService.RunMigrations` → `RunMigrationsAsync`

Read 메서드는 **시그니처 불변** — queue 우회 (WAL concurrent read 활용).

**호출자 파급 범위**: 위 async 변환으로 인한 `await` 추가 + caller signature async 전환. PipelineOrchestrator / FileScanner / ContentExtractor / App.axaml.cs가 주 영향권. spec 단계에서 호출 경로 전체 grep으로 확정.

---

## 3. 영향 파일 목록 (Phase 1 Medium 기준)

### 3.1 신규 파일

| Agent | 파일 | 설명 |
|-------|------|------|
| Tests | `tests/LocalSynapse.Core.Tests/LocalSynapse.Core.Tests.csproj` | xUnit v3 테스트 프로젝트 |
| Tests | `tests/LocalSynapse.Core.Tests/TempDbFixture.cs` | 임시 파일 DB + migration 자동 적용 fixture |
| Tests | `tests/LocalSynapse.Core.Tests/TestData/search-golden-master.json` | R5 검색 랭킹 golden master (Step 3에서 생성) |

### 3.2 수정 파일

| # | Agent | 파일 | 변경 유형 | 설명 | 예상 diff |
|---|-------|------|---------|------|---------|
| R1 | Core | [SqliteConnectionFactory.cs](src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs) | 삭제 | `_connection`, `_lock`, `GetConnection()`, `ExecuteSerialized<T>`, `ExecuteSerialized` 전부 삭제. `CreateConnection()`만 유지 (Phase 0c PRAGMA 포함) | -40줄 |
| R3 | Core | [SettingsStore.cs](src/LocalSynapse.Core/Repositories/SettingsStore.cs) | 재작성 | JSON 파일 기반으로 교체 + atomic write (`File.Replace`) + legacy SQLite 1회 마이그레이션. SQLite `settings` 테이블은 삭제하지 않음 (롤백 안전망) | +150줄 |
| R5 | Search | [Bm25SearchService.cs](src/LocalSynapse.Search/Services/Bm25SearchService.cs) | 리팩토링 | `ExecuteSearch`에서 reader를 먼저 materialize → paths 수집 → `GetBoostBatch` 1회 호출 → 점수 적용 | ~70줄 |
| R5 | Search | [SearchClickService.cs](src/LocalSynapse.Search/Services/SearchClickService.cs) | 추가 | `GetBoostBatch(string query, IReadOnlyList<string> paths)` 메서드 추가. 기존 `GetBoost(query, path)`는 유지 (spec 단계에서 grep으로 다른 호출자 확인 후 결정) | +30줄 |
| R8 | Core | [FileRepository.cs](src/LocalSynapse.Core/Repositories/FileRepository.cs) | 내부 구현 변경 | `UpsertFiles`가 내부에서 75개 단위 sub-batch로 분할, 각 sub-batch마다 별도 tx. 호출자 시그니처 불변 | ~30줄 |

**변경 없음** (Full C 기준에서 수정 대상이었던 파일들): `ChunkRepository.cs`, `EmbeddingRepository.cs`, `PipelineStampRepository.cs`, `MigrationService.cs`, `IMigrationService.cs`, 모든 `I*Repository.cs` 인터페이스, `App.axaml.cs`, `ServiceCollectionExtensions.cs`, `PipelineOrchestrator.cs`, `FileScanner.cs`, `ContentExtractor.cs`.

### 3.3 예상 규모 총합

| 항목 | 규모 |
|------|------|
| Production diff | ~320줄 (R5: 100, R3: 150, R1: -40, R8: 30, 기타 DI 조정) |
| Test diff | ~250줄 (fixture 50, 테스트 8개 × 평균 25줄) |
| 신규 파일 | 3개 (csproj, TempDbFixture.cs, golden master JSON) |
| 수정 파일 | 5개 |
| 작업 기간 | 1.5~2주 (솔로, 사업 트랙 일부 병행 가능) |
| Phase 0 대비 | 10~12배 (Phase 0 Full 대비 5~6배 축소) |

## 4. 재사용 가능한 기존 패턴 (Phase 1 Medium)

1. **`using var tx = conn.BeginTransaction()`** — 기존 Repository 전체 일관 패턴. R8 sub-batch 분할 시 각 sub-batch마다 동일 패턴 재사용.
2. **parameter pooling** (`pId = cmd.Parameters.Add(...)` 후 루프에서 `pId.Value = ...` 재사용) — `FileRepository.UpsertFiles`에 이미 적용됨. sub-batch 분할 시 outer loop만 추가하고 parameter 바인딩 코드는 그대로 유지.
3. **DI 등록 순서** — 이미 올바름. 변경 없음.
4. **`Path.Combine(Environment.GetFolderPath(SpecialFolder.LocalApplicationData), "LocalSynapse")`** — 기존 SettingsStore 생성자 ([SettingsStore.cs:17-19](src/LocalSynapse.Core/Repositories/SettingsStore.cs#L17-L19)) 패턴. JSON 전환 시 동일 경로 규칙 사용하여 macOS/Windows/Linux 일관성 유지.
5. **Phase 0c PRAGMA 패턴** (`busy_timeout=30000; synchronous=NORMAL;` 개별 `ExecuteNonQuery` 분리) — `CreateConnection()`에 이미 적용됨. R1에서 삭제 대상 아님, 그대로 유지.

**R1 dead code는 재사용하지 않음** — Phase 1 Medium은 SqliteWriteQueue를 도입하지 않으므로 `_connection`/`_lock`/`ExecuteSerialized`를 부활시킬 이유 없음. 혼란만 남김.

---

## 5. 위험 및 사전 확인 필요 사항

### 🔴 Blocker — 모두 해결됨 (2026-04-11)

#### D1. 아키텍처 옵션 최종 선택 ✅ 해결
- **결정: Phase 1 Medium** — 4개 핵심 결함만 수정. SqliteWriteQueue 전체 인프라는 제외.
- **경로**: R5 (N+1) + R3 (SettingsStore JSON) + R1 (dead code 제거) + R8 (sub-batch 분할)
- **근거**: 냉정한 재검증 결과 Full C는 (1) 실측 핵심 문제가 R5 하나뿐이고, (2) cross-process 요구로 in-process queue의 우아함이 절반 깨지며, (3) 솔로 개발자 사업 트랙(Pro tier, 마케팅)을 3~6주 정지시키고, (4) TDD 인프라 + 대규모 리팩토링 동시 수행은 위험 곱셈이며, (5) "한 번에 다 고치자"는 아키텍처 유혹에 해당한다고 판단.
- **원칙**: "필요한 만큼의 규모 안에서 최적의 효과". R2/R7/R10/R11/R13는 Phase 2 이후로 유예 — 실제 사용자 리포트로 실측되는 시점에 개별 phase로 처리.

#### D2. SettingsStore 전략 ✅ 해결
- **결정: (a) JSON 전환** — `%LOCALAPPDATA%\LocalSynapse\settings.json` (macOS: `~/Library/Application Support/LocalSynapse/settings.json`)
- **Atomic write**: temp file → `File.Replace(target, target, backup)`
- **마이그레이션**: 첫 실행 시 JSON 파일이 없고 SQLite `settings` 테이블에 키가 있으면 1회 읽어 JSON 생성. SQLite 테이블은 **삭제하지 않음** (롤백 안전망).
- **IAppPaths 분리는 제외** — JSON 전환으로 SettingsStore가 SQLite와 무관해지면 순환 이슈도 자동 소멸. 별도 인터페이스 도입은 "필요한 만큼" 원칙 위반.

#### D3. Multi-process (GUI + MCP) 동시 실행 ✅ 해결
- **결정: 허용 (a)** — 두 process 모두 full feature (인덱싱 포함). 제품의 dual-mode 포지셔닝과 일치.
- **메커니즘**: 각 process가 자체 `SqliteConnectionFactory` 사용. SQLite file lock + `busy_timeout=30000` (Phase 0c 적용됨)으로 자연 직렬화.
- **R8 sub-batch 분할이 lock hold time을 줄여** 상호 대기를 최소화. 이게 Phase 1 Medium에서 R13을 다루는 유일한 방법.
- **Cross-process broker, read-only mode, busy retry loop, try again UX는 모두 제외**. 현재 실측 경쟁 리포트 없음 → 실측 시점에 Phase 2로 분리.

#### D4. Step 분할 ✅ 해결
- **결정: 5 steps (Step 0~4), 각 1 commit, feature branch 권장하나 필수 아님**

| Step | 내용 | 리스크 |
|---|---|---|
| 0 | 테스트 프로젝트 신설 + `TempDbFixture` + 빈 `dotnet test` 통과 | 🟢 |
| 1 | R1 dead code 제거 | 🟢 |
| 2 | R3 SettingsStore JSON 전환 + 마이그레이션 + 테스트 3개 | 🟡 |
| 3 | R5 N+1 제거 + `GetBoostBatch` + 테스트 3개 + golden master | 🟡 |
| 4 | R8 UpsertFiles sub-batch 분할 + 테스트 2개 | 🟢 |

### 🟡 Major — spec/diff-plan 단계에서 확인

#### M1. 기존 SQLite `settings` 테이블 데이터 키 목록 확정
마이그레이션 대상 키 명시 필요. grep으로 `SettingsStore` 호출자 전수 조사. 현재 알려진 키: `language`, `fts_tokenizer_version`. 누락 여부 확인.

#### M2. R5 검색 랭킹 golden master 준비
테스트용 small corpus (10~20개 파일) + 고정 쿼리 3~5개. 리팩토링 전 결과를 JSON으로 저장. 리팩토링 후 동일성 assert. **Phase 1 Step 3 시작 전 준비**.

#### M3. R8 sub-batch 크기 결정
초기 값: 75 (50~100 범위의 중간). spec에서 상수로 고정. 추후 benchmark로 최적화 여지 남김. 현 단계에서 실측 기반 선택은 over-engineering.

#### M4. xUnit v3 vs v2 결정
v3가 async 시나리오에서 개선되었으나 Phase 1 Medium의 테스트는 대부분 동기 또는 단순 async라 v2도 충분. 기본 선택: **v3** (장기 유지보수 유리). spec에서 최종 확정.

#### M5. 테스트 프로젝트의 DI 구성
Production의 `ServiceCollectionExtensions`를 테스트에서 재사용할지, 아니면 테스트용 최소 DI를 별도 구축할지. `TempDbFixture`가 production DI를 부분 사용하는 방식 권장 — 테스트가 production 구성을 검증하는 효과.

### 🟢 Minor — 정보

#### I1. `gate-check.ps1` 부재
Phase 0과 동일. 수동 `dotnet build` + `dotnet test` 검증만 수행.

#### I2. 테스트 인프라는 Phase 1에서 **최소 규모로 구축**
신규 test project 1개, fixture 1개, 테스트 ~8개. Characterization / concurrency smoke / golden master (ranking만 해당) / schema snapshot은 **제외**. Phase 1.5 또는 Phase 2에서 필요 시 확장.

#### I3. 사업 트랙 병행 가능성
Phase 1 Medium은 1.5~2주 집중 작업. Email integration (WO-EM1/EM2), Korean community posts, 데모 GIF 등 다른 트랙과 일부 병행 가능. Phase 1 Full (3~6주) 대비 사업 임팩트 최소.

---

## 6. 테스트 전략 — Phase 1 Medium 최소 인프라

> **원칙**: "필요한 만큼의 규모 안에서 최적의 효과". Phase 1 Medium은 4개 결함만 다루므로 테스트도 해당 4개 결함의 회귀 방지에 집중.

### 6.1 원칙

1. **회귀 방지 우선**. 새 동작 검증보다 "기존 사용자 경험이 변경되지 않음" 증명이 우선.
2. **Integration > Unit for SQLite**. 임시 파일 DB 사용. `:memory:`는 WAL 동작이 다르므로 지양.
3. **Fresh DB per test**. `TempDbFixture`로 격리.
4. **Golden master는 R5 검색 랭킹에만**. 다른 곳에는 과도함.
5. **테스트 삭제 금지**. Gate 2 통과를 위해 테스트를 수정할 때는 production 코드가 틀린 것.

### 6.2 테스트 인프라 — Step 0

**신규 파일**:
- `tests/LocalSynapse.Core.Tests/LocalSynapse.Core.Tests.csproj` (xUnit v3)
- `tests/LocalSynapse.Core.Tests/TempDbFixture.cs`
- `tests/LocalSynapse.Core.Tests/TestData/search-golden-master.json` (Step 3에서 작성)

**`TempDbFixture`**:
```csharp
public sealed class TempDbFixture : IDisposable
{
    public string DbPath { get; }
    public SqliteConnectionFactory Factory { get; }
    public TempDbFixture()
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"localsynapse-test-{Guid.NewGuid():N}.db");
        Factory = new SqliteConnectionFactory(new TestSettingsStore(DbPath));
        var migration = new MigrationService(Factory);
        migration.RunMigrations();
    }
    public void Dispose() { Factory.Dispose(); File.Delete(DbPath); }
}
```

**NuGet 승인 필요**:
- `xunit.v3`
- `xunit.runner.visualstudio`
- `Microsoft.NET.Test.Sdk`

FluentAssertions는 **제외** (v8+ 상업 라이선스 연 $129.95 필요, LocalSynapse Pro tier와 충돌). 순정 xUnit `Assert.Equal` / `Assert.Throws<T>` / `Assert.True` 사용.

### 6.3 테스트 세트 — 총 8개

#### Step 2 (R3 SettingsStore)
1. `SettingsStore_RoundTripsLanguage` — JSON 기본 동작
2. `SettingsStore_MigratesFromLegacySqlite_OnFirstRun` — 1회 마이그레이션
3. `SettingsStore_AtomicWrite_SurvivesPartialWriteSimulation` — temp + Replace 패턴

#### Step 3 (R5 N+1 제거)
4. `GetBoostBatch_ReturnsBoostForAllPaths` — 새 메서드 정상 동작
5. `ExecuteSearch_CallsGetBoostOnce_NotPerResult` — N+1 회귀 방지 (수동 stub `SearchClickService`로 호출 횟수 assert)
6. `ExecuteSearch_ProducesSameRanking_AsGoldenMaster` — 랭킹 변경 없음

#### Step 4 (R8 sub-batch)
7. `UpsertFiles_ChunksInto75SizedSubBatches_ForLargeInputs` — sub-batch 동작
8. `UpsertFiles_TotalInsertedCount_MatchesInput` — 기능 회귀 방지

### 6.4 Golden master — R5 전용

**범위**: 검색 랭킹 1개만.

**준비 (Step 3 시작 전)**:
1. 테스트 corpus: 10~20개 작은 텍스트 파일 (hardcoded in test project)
2. 고정 쿼리: 3~5개
3. 리팩토링 전 현재 `ExecuteSearch` 결과를 JSON으로 dump → `search-golden-master.json` 저장
4. 리팩토링 후 동일 쿼리 → 결과와 JSON 비교

**제외**:
- Schema snapshot golden master (Phase 1에서 스키마 변경 없음)
- 전 repository characterization (Phase 1에서 repository 시그니처 불변, Full C 미채택)

### 6.5 Gate — 확장 없음

CLAUDE.md Gate 1~4 유지. Gate 5/6/7 **추가하지 않음**.
- Gate 1 (Build): `dotnet build` 0 errors
- Gate 2 (Tests): `dotnet test` 0 failures, 신규 테스트 8개 전부 green
- Gate 3 (Impact Scope): Core Agent 중심. Search Agent는 R5 경로만 (`Bm25SearchService`, `SearchClickService`)
- Gate 4 (TDD): R5/R3/R8 각 test를 먼저 red → 구현 → green 순서

### 6.6 위험

- **Golden master의 brittleness**: 검색 corpus나 tokenizer가 변경되면 JSON 업데이트 필요. Step 3에서 재생성 절차 문서화.
- **Sub-batch 테스트의 observability**: sub-batch 분할을 외부에서 관찰하려면 BeginTransaction 횟수를 세야 함. `SqliteConnectionFactory`를 test double로 감싸거나, 간단히 `UpsertFiles(200개)` 시 정확히 `ceil(200/75) = 3` tx가 발생했는지를 spy로 확인.
- **Mock 부담**: 테스트 5에서 stub `SearchClickService`를 수동 작성해야 함 (순정 xUnit은 mock 기능 없음). 10~15줄 추가.

## 7. 다음 단계

**`/spec 1` 실행 권장**.

Spec 단계에서 확정할 사항 (M1~M5만 — D1~D4는 모두 해결됨):
- **M1**: SettingsStore 마이그레이션 대상 키 전수 (grep 결과)
- **M2**: R5 golden master corpus/쿼리 확정
- **M3**: R8 sub-batch 크기 상수 (75)
- **M4**: xUnit v3 최종 확정 + 패키지 버전
- **M5**: 테스트 DI 구성 방식

Spec 확정 후 `/diff-plan 1` → diff-reviewer 적대적 검증 → `/execute 1` Step 0~4 순차 실행.

### ⚠️ 원칙 재확인

**"필요한 만큼의 규모 안에서 최적의 효과"**. 이 문장은 Phase 1 recon/spec/diff-plan/execute 전반에 걸친 **가드레일**이다.

- Phase 1 Medium 범위(R1/R3/R5/R8 + 8 tests)를 spec/diff-plan/execute 단계에서 **확장하지 말 것**.
- 확장 유혹이 생기면 해당 항목은 Phase 2 이후로 이월.
- "어차피 이 김에 고치자" = 엔지니어링 실패 전형 패턴.

### Phase 2 이후 후보 (실측 리포트 발생 시 개별 phase로)

- **R2/R7 write 직렬화** — SqliteWriteQueue 또는 경량 lock. 실측 트리거: 스캔 중 UI freeze 또는 `SQLITE_BUSY` 에러 리포트
- **R10 partial failure detection** — BatchUpdate 영향 row 수 집계. 실측 트리거: 데이터 불일치 리포트
- **R11 MigrationService tx 분할 + 진행률** — FTS tokenizer upgrade 시 UI freeze 리포트
- **R13 cross-process broker** — GUI+MCP 동시 실행 시 상호 대기 리포트
- **7 repository characterization 확장** — R2 착수 시 안전망으로 선행 구축
- **Concurrency smoke harness** — R2 착수 시 동시
- **Two-process smoke harness** — R13 착수 시

### 사업 트랙 병행 유지

Phase 1 Medium은 1.5~2주 추정. 이 기간 동안:
- Email integration (WO-EM1/EM2) 작업 **계속**
- Korean community posts, 데모 GIF 제작 병행 가능
- Pro tier launch 일정 영향 최소

Phase 1 Full (3~6주)로 복귀하지 말 것. 사업 타이밍이 허용하지 않음.

---

## 8. 변경 이력

| 날짜 | 변경 |
|------|------|
| 2026-04-11 | 초안 작성. 13개 결함 실측 검증 (4개 과장 식별). 옵션 B/C/D/E 재평가. D1~D4 blocker 식별. |
| 2026-04-11 | 사용자 결정: **Full C (SqliteWriteQueue + async 전면화)** 채택. TDD 기반 안정성 프레임워크 추가 (§6). 테스트 인프라 Phase 1 Step 0로 격상. Gate 5/6/7 추가. 영향 파일 목록 Full C 기준으로 재작성 (~15~20 파일). |
| 2026-04-11 | 냉정한 재검증 결과 **Phase 1 Medium으로 축소**. Full C (SqliteWriteQueue + 7 repo async + cross-process broker)는 실측 문제 부재 + 사업 트랙 정지 리스크로 Phase 2 이후 유예. **4개 결함(R1/R3/R5/R8) + 최소 테스트 인프라(8 tests)**로 최종 확정. D1~D4 전부 해결. Step 0~4 확정. FluentAssertions 제외(라이선스). "필요한 만큼의 규모" 원칙을 §5/§7에 가드레일로 명시. §3 파일 목록 Medium 기준 재작성 (수정 5 + 신규 3 파일). §6 테스트 전략을 Full C TDD → Medium 최소 인프라로 교체 (characterization/concurrency smoke/two-process harness 전부 제외). |
