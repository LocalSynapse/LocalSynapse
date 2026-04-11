# Phase 0 Recon — Quick Wins

> **대상 범위**: Phase 0a (BGE-M3 다운로드 안정화) + Phase 0b (SearchPage FormatException 수정) + Phase 0c (`CreateConnection()` PRAGMA 보강)
>
> **목표**: 세 작업을 단일 commit으로 묶을 수 있도록 영향 범위를 확정하고, PHASES.md 기재사항과 실제 코드의 차이를 식별한다.

---

## 1. Phase 0a — BGE-M3 다운로드 안정화

### 영향 파일
- [src/LocalSynapse.Pipeline/Embedding/BgeM3Installer.cs](src/LocalSynapse.Pipeline/Embedding/BgeM3Installer.cs) (유일)

### 현재 구현 분석 ([BgeM3Installer.cs:91-108](src/LocalSynapse.Pipeline/Embedding/BgeM3Installer.cs#L91-L108))
```csharp
using var response = await _httpClient.GetAsync(url, ...);
using var stream   = await response.Content.ReadAsStreamAsync(ct);
using var fileStream = new FileStream(partPath, FileMode.Create, ...);
// ... write loop ...
File.Move(partPath, targetPath, overwrite: true);   // ← fileStream이 아직 살아있음
```

**결함 1 — Dispose 순서**: `using var` 선언은 **enclosing scope 종료 시** 역순 dispose된다. 여기서 enclosing scope는 `foreach` body. `File.Move`는 dispose 전에 실행되므로 `fileStream`이 `partPath`의 파일 핸들을 **점유한 상태에서 Move 시도** → Windows에서 `IOException: The process cannot access the file`.
- 매 다운로드마다 문제가 되지는 않지만, OS 버퍼 플러시가 지연되는 상황에서 간헐적으로 실패.
- macOS/Linux는 handle이 있어도 rename 가능해서 재현율이 낮음. Windows에서만 치명.

**결함 2 — 무결성 미검증**: 다운로드된 파일 크기가 `expectedSize`와 일치하는지 검증하지 않음. HTTP 중간 절단 / 프록시 개입 / 디스크 Full 시 truncated 파일이 그대로 이름 변경되어 정상 파일처럼 보임.

**결함 3 — 실패 시 .part 파일 잔존**: 예외 발생 시 `partPath`가 디스크에 남아 다음 실행 때 혼란. (현재는 `File.Exists(targetPath)` 체크만 있음 → `.part`는 체크 안 함.)

### 재사용 가능 요소
- 이미 `partPath` 네이밍 컨벤션이 존재. 별도 유틸 필요 없음.
- `ModelInfo[].Models` 배열에 `SizeBytes`가 있으나 이는 전체 모델 크기. 파일별 크기는 `RequiredFiles` 튜플의 `Size` 필드 사용.

### 구현 옵션

| 옵션 | 설명 | 장점 | 단점 |
|------|------|------|------|
| **A1. using block 명시화 + 크기 검증** | stream/fileStream을 `using { ... }` 블록으로 감싸 `File.Move` 전에 확실히 dispose. 블록 종료 직후 `new FileInfo(partPath).Length != expectedSize` 검증. | 최소 변경. 의도 명확. | `using var`보다 들여쓰기 한 단계 증가. |
| **A2. 명시적 `await fileStream.DisposeAsync()` 호출** | `using var` 유지하고 Move 직전에 `await fileStream.DisposeAsync()` + `await stream.DisposeAsync()` 명시. | 기존 코드 구조 거의 유지. | `using var`와 명시 dispose 혼용은 어색. 검토 시 혼란. |
| **A3. 전체 로직을 private helper로 추출** | `DownloadFileAsync(url, targetPath, expectedSize, ct)` 분리. using scope를 함수 단위로 만들어 명확화. | 테스트 용이. 가독성 향상. | Quick win 범위를 넘어섬. Phase 1에 미룰 수 있음. |

**권장**: **A1**. PHASES.md 기재와 가장 일치, 변경 최소, 안전성 확보. `part` 파일 정리(`File.Delete(partPath)` on exception)까지 포함 검토.

### 검증 사항
- [ ] 크기 검증 실패 시 어떤 예외로 던질지 (`InvalidDataException` vs `IOException`) — PHASES.md는 `InvalidDataException` 권장
- [ ] 크기 검증 실패 시 `.part` 파일 삭제 여부 (정책: 삭제 → 다음 실행 시 재시도)
- [ ] `expectedSize`가 정확한지 — `RequiredFiles` 배열의 값은 대략치(예: `model.onnx` 725_000, 실제는 바이트 단위 정확도 확인 필요). **정확한 값이 아니면 크기 검증 자체가 false positive 원천.** ⚠️

### ⚠️ 리스크
`RequiredFiles` 튜플의 `Size` 필드는 주석/커밋 이력상 **대략치(round number)**로 보인다. 예: `"tokenizer.json", "", 17_000_000` — HuggingFace 실제 파일은 17_082_757 bytes일 수 있음. **엄격한 `==` 비교 시 현재 사용자 전원이 재다운로드 지옥에 빠진다.**
- 완화: `>= expectedSize * 0.95` 같은 tolerance 또는 `>= minSize` 범위 검증으로 전환. spec 단계에서 확정 필요.

---

## 2. Phase 0b — SearchPage FormatException 수정

### PHASES.md 기재 vs 실제 코드 — **중대한 불일치 발견** ⚠️

| 항목 | PHASES.md 기재 | 실제 코드 |
|------|---------------|----------|
| SearchPage.axaml의 `<Run Text=...>` | **5곳** | **0곳** (grep 결과 없음) |
| DataSetupPage.axaml의 `<Run Text=...>` | 1곳 | 1곳 ([DataSetupPage.axaml:301](src/LocalSynapse.UI/Views/DataSetupPage.axaml#L301)) ✅ |
| `CountToBoolConverter.cs` 신규 | 필요 | 기존에 **어떤 변환기도 Count를 쓰지 않음** — 용도 불명 |

SearchPage에서 StringFormat을 쓰는 위치는 전부 `<Run>`이 아니라 `<TextBlock Text="..." />`이며, 발견된 곳은 총 5:
- [SearchPage.axaml:87](src/LocalSynapse.UI/Views/SearchPage.axaml#L87) `Stamps.TotalFiles` `{0:N0} files indexed`
- [SearchPage.axaml:96](src/LocalSynapse.UI/Views/SearchPage.axaml#L96) `Stamps.TotalChunks` `{0:N0} chunks`
- [SearchPage.axaml:166](src/LocalSynapse.UI/Views/SearchPage.axaml#L166) `NameMatchCount` `({0})`
- [SearchPage.axaml:183](src/LocalSynapse.UI/Views/SearchPage.axaml#L183) `ContentMatchCount` `({0})`
- [SearchPage.axaml:200](src/LocalSynapse.UI/Views/SearchPage.axaml#L200) `SemanticMatchCount` `({0})`
- [SearchPage.axaml:239](src/LocalSynapse.UI/Views/SearchPage.axaml#L239) `Stamps.TotalFiles` `{0:N0} files indexed` (중복)
- [SearchPage.axaml:349](src/LocalSynapse.UI/Views/SearchPage.axaml#L349) `FileCount` `{0} files`

DataSetupPage의 StringFormat 사용:
- [DataSetupPage.axaml:124,187,244](src/LocalSynapse.UI/Views/DataSetupPage.axaml#L124) — `<TextBlock Text=...>` (3곳)
- [DataSetupPage.axaml:301](src/LocalSynapse.UI/Views/DataSetupPage.axaml#L301) — `<Run Text=...>` (1곳)

### Avalonia 바인딩 기본 모드 분석
- `TextBlock.Text`: Avalonia에서 기본 `OneWay` → `StringFormat` + 숫자 소스 조합이 이론적으로 문제없음
- `Run.Text`: Avalonia에서 기본 **TwoWay**에 가까운 동작을 보이며, `StringFormat` 사용 시 **back-conversion에서 FormatException** 발생 (알려진 버그)

즉 **실제로 FormatException이 터지는 곳은 [DataSetupPage.axaml:301](src/LocalSynapse.UI/Views/DataSetupPage.axaml#L301) 한 곳**이며, PHASES.md의 "5곳"은 문서 작성자가 TextBlock과 Run을 혼동했을 가능성이 높음.

### 확인 필요 사항 (spec 단계에서 사용자 결정)
1. **실제 런타임에서 FormatException이 SearchPage에서 발생하는가?** — 증거 수집 필요. 발생 사례 로그 또는 재현 절차.
2. **TextBlock에도 `Mode=OneWay`를 명시해야 하는가?** — 기본값과 동일하므로 no-op에 가깝지만, 방어적 일관성 차원에서 추가 가능.
3. **`CountToBoolConverter`는 정말 필요한가?** — 현재 코드베이스에 `Count > 0 → bool` 변환이 필요한 곳이 없다면 포크의 cruft. 삭제 고려.

### 구현 옵션

| 옵션 | 설명 | 장점 | 단점 |
|------|------|------|------|
| **B1. Run만 수정 (minimal)** | [DataSetupPage.axaml:301](src/LocalSynapse.UI/Views/DataSetupPage.axaml#L301) 한 곳에 `Mode=OneWay` 추가. 끝. | 실제 문제 해결. 파일 1개 수정. | 포크의 5곳 가정과 다름 → 사용자 기대와 불일치 가능. |
| **B2. Run + 모든 TextBlock에 방어적 Mode=OneWay 명시** | 위 + SearchPage/DataSetupPage의 TextBlock StringFormat 7곳에도 `Mode=OneWay` 추가. | 방어적. 향후 유사 버그 예방. | 장황. 기본값 명시는 불필요하다는 철학과 상충. |
| **B3. CountToBoolConverter 포함 전체 cherry-pick** | 포크 `1cc3030`의 모든 변경을 그대로 적용 (CountToBoolConverter.cs 포함). | 포크와 완전 동기화. | 용도 없는 변환기 추가 → dead code 유입. |

**권장**: **B1** + 실제 FormatException 증거 여부에 따라 B2로 확장 고려. CountToBoolConverter는 용도 증명 전까지 추가하지 않음. spec 단계에서 사용자에게 확인.

---

## 3. Phase 0c — `CreateConnection()` PRAGMA 보강

### 현재 코드 ([SqliteConnectionFactory.cs:65-73](src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs#L65-L73))
```csharp
public SqliteConnection CreateConnection()
{
    var conn = new SqliteConnection(_connection.ConnectionString);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "PRAGMA busy_timeout=30000;";
    cmd.ExecuteNonQuery();
    return conn;
}
```

### 추가할 변경
```csharp
cmd.CommandText = "PRAGMA busy_timeout=30000; PRAGMA synchronous=NORMAL;";
```

### 주요 발견: 공유 connection과 `CreateConnection()`의 PRAGMA 불일치
공유 `_connection` ([SqliteConnectionFactory.cs:27](src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs#L27))는 `journal_mode=WAL; busy_timeout=30000; synchronous=NORMAL` 3개 모두 설정.
`CreateConnection()`은 `busy_timeout`만 설정. **`synchronous`는 per-connection 설정**이므로 `CreateConnection()`으로 생성된 connection은 기본 `synchronous=FULL`로 동작 → write 속도 저하.

`journal_mode=WAL`는 database 단위 영구 설정이므로 재지정 불필요 (공유 connection이 이미 설정).

### CreateConnection 호출자 (grep 결과)
- `src/LocalSynapse.Search/Services/Bm25SearchService.cs`
- `src/LocalSynapse.Search/Services/SearchClickService.cs`
- `src/LocalSynapse.Core/Database/MigrationService.cs`
- `src/LocalSynapse.Core/Repositories/FileRepository.cs`
- `src/LocalSynapse.Core/Repositories/PipelineStampRepository.cs`
- `src/LocalSynapse.Core/Repositories/EmbeddingRepository.cs`
- `src/LocalSynapse.Core/Repositories/ChunkRepository.cs`

7개 Repository/Service 모두 `CreateConnection()`을 사용 → **전체 write 경로가 `synchronous=FULL`로 동작 중**. Phase 1 전체 리팩토링 대상이지만, 이 1줄 변경만으로도 상당한 개선 기대.

### 구현 옵션

| 옵션 | 설명 | 장점 | 단점 |
|------|------|------|------|
| **C1. PRAGMA 1줄 추가 (PHASES.md 기재)** | `busy_timeout` 라인 뒤에 `PRAGMA synchronous=NORMAL;` 추가 (단일 CommandText로 묶음). | 최소 변경. 위험 없음. | 없음. |
| **C2. 공유 connection PRAGMA와 일관화 상수 도입** | `private const string ConnectionPragmas = "..."`로 추출하여 두 곳에서 재사용. | DRY. 향후 유지보수성 향상. | Quick win 범위 초과. Phase 1에서 어차피 재작성 예정이므로 중복 작업 가능성. |

**권장**: **C1**. Phase 1에서 전체 connection 아키텍처 재작성이 예정되어 있으므로 상수 추출은 Phase 1에서 수행.

### 리스크
- **없음**. `synchronous=NORMAL`은 WAL 모드에서 권장 설정이며, crash safety 측면에서 FULL과 차이는 체크포인트 타이밍 정도. write 성능 향상만 있음.

---

## 4. 공통 확인 사항

### 통합 commit 전략
세 변경이 서로 독립적이며 각각 위험도 🟢 → **단일 commit** 가능. 단 커밋 메시지에는 세 변경을 개별 bullet로 명시.

### Gate 체크리스트 (CLAUDE.md)
- Gate 1 (Build): 모든 변경이 axaml + C#이므로 `dotnet build LocalSynapse.v2.sln` 필수
- Gate 2 (Tests): 기존 테스트 유지. BgeM3Installer 크기 검증은 실제 다운로드 없이 테스트하기 어려움 → 단위 테스트 추가 여부 spec에서 결정
- Gate 3 (Impact Scope): 모든 변경이 각 Agent 내부 — violation 없음
  - Phase 0a: Pipeline Agent
  - Phase 0b: UI Agent
  - Phase 0c: Core Agent

### 의존성 그래프
세 작업 간 의존성 **없음**. 병렬 작성 가능.

---

## 5. 리스크 요약 및 사전 확인 필요 사항

| # | 항목 | Phase | 심각도 | 사용자 결정 필요? |
|---|------|-------|--------|------------------|
| 1 | `RequiredFiles` 크기 값이 대략치 → 엄격 `==` 검증 시 false positive 대량 발생 | 0a | 🔴 | ✅ tolerance 정책 |
| 2 | `InvalidDataException` vs `IOException` 선택 | 0a | 🟢 | ✅ |
| 3 | 실패 시 `.part` 파일 정리 정책 | 0a | 🟡 | ✅ |
| 4 | PHASES.md "SearchPage 5곳" 기재 vs 실제 Run 0곳 — 범위 확정 | 0b | 🟡 | ✅ |
| 5 | `CountToBoolConverter` 실제 필요 여부 | 0b | 🟢 | ✅ 삭제 vs 유지 |
| 6 | 방어적으로 TextBlock에도 Mode=OneWay 추가할지 | 0b | 🟢 | ✅ |
| 7 | Phase 0c는 **기계적 1줄 수정** — 추가 고려사항 없음 | 0c | 🟢 | ❌ |

---

## 6. 다음 단계

**다음: `/spec 0` 실행하세요.**

Spec 단계에서 결정할 핵심 사항:
1. **Phase 0a**: 크기 검증 tolerance (엄격 `==` / `>= min` / `>= 0.95 * expected`) — RequiredFiles 값의 정확도 결정에 의존
2. **Phase 0a**: 예외 타입, `.part` 정리 정책
3. **Phase 0b**: SearchPage TextBlock 수정 범위 — **실제 FormatException 발생 증거가 있는지** 사용자 확인
4. **Phase 0b**: `CountToBoolConverter` 추가 여부 (권장: 추가하지 않음)
5. **Phase 0a**: 단위 테스트 추가 여부 (네트워크 없이 테스트 가능한 경계 분리)
