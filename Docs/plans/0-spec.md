# Phase 0 Spec — Quick Wins

> **상태**: 초안 (사용자 상세화 대기)
> **전제 문서**: [0-recon.md](0-recon.md)
> **범위**: Phase 0a (BGE-M3 안정화) + Phase 0b (FormatException 수정) + Phase 0c (PRAGMA 보강)
> **commit 전략**: 단일 commit — 세 변경이 독립적이고 위험도 🟢

---

## 1. Phase 0a — BGE-M3 다운로드 안정화

### 1.1 목표
BGE-M3 모델 다운로드 과정에서 발생하는 두 가지 결함을 제거한다:
- (A) Dispose 순서 문제로 인한 `File.Move` 실패 (Windows 특화 간헐적 IOException)
- (B) Truncated 다운로드에 대한 무결성 검증 부재

### 1.2 요구사항

#### R1. Dispose 순서 보장
`File.Move`가 실행되기 **전에** `fileStream`이 확실히 dispose 되어야 한다. 현재 `using var` 선언은 foreach 블록 종료 시점에 dispose되어 `File.Move` 시점에 파일 핸들이 살아있다.

**구현 방식**: `stream`과 `fileStream`을 `using { ... }` 블록으로 감싸고, 블록 종료 직후에 크기 검증과 `File.Move`를 수행한다.

#### R2. 다운로드 파일 크기 검증 (하한 검증, 보수적 90% 임계치)
다운로드 완료 후 `.part` 파일의 크기가 **사전 정의된 최소 크기 이상**인지 검증한다.

- **검증 방식**: `new FileInfo(partPath).Length >= minSize`
- **minSize 값** — HuggingFace UI 표시값의 **약 90%**를 사용 (HF UI는 반올림되므로 실제 바이트가 표시값보다 작을 수 있음):

```csharp
private static readonly (string RelativePath, string Sha256, long MinSize)[] RequiredFiles =
{
    // HF UI 표시값은 반올림되므로 실제 바이트가 표시값보다 작을 수 있다.
    // MinSize는 truncated 다운로드(보통 0~80%)만 거르고 false positive를 피하기 위해
    // HF 표시값의 약 90%로 의도적으로 보수화. HF 원본 값으로 되돌리지 말 것.
    ("model.onnx",              "",           650_000),  // ~90% of HF ~725 kB
    ("model.onnx_data",         "", 2_040_000_000),      // ~90% of HF ~2.27 GB
    ("tokenizer.json",          "",    15_000_000),      // ~88% of HF ~17.1 MB
    ("sentencepiece.bpe.model", "",     4_500_000),      // ~89% of HF ~5.07 MB
};
```

- **상한 검증 없음**: HuggingFace가 비정상적으로 큰 파일을 보낼 가능성은 무시.
- **변수명**: 기존 튜플 필드 `Size` → `MinSize`로 변경하여 의미 명확화. **사용처는 다음 grep의 출력을 정답 소스로 사용** (라인 번호를 spec에 박지 않음):
  ```bash
  grep -n '\.Size\b\|RequiredFiles\b' src/LocalSynapse.Pipeline/Embedding/BgeM3Installer.cs
  ```
  foreach 내부의 positional deconstruction(`var (relativePath, _, expectedSize)`)은 필드명과 무관하게 동작하므로 영향 없음.

**근거 — 원본 코드의 위험 패턴**: 원본 `RequiredFiles`는 HF UI 표시값(`725 kB`, `2.27 GB`)을 그대로 바이트로 옮긴 값이다. HF UI는 반올림하여 표시하므로 실제 파일 크기가 `725_000`보다 작을 수 있고, 그러면 엄격한 `>=` 검증은 **기존 사용자 전원을 재다운로드 지옥에 빠뜨린다**. 90% 임계치는 정상 다운로드 vs truncated 다운로드(네트워크 중단 시 보통 0~80% 완료)를 충분히 구분하면서 false positive 여지를 제거한다.

⚠️ **회귀 방지**: 향후 누군가 "중간의 이상한 숫자가 왜 있지?"하며 HF UI 값으로 되돌리지 않도록 배열 상단에 근거 주석을 명시한다.

**HuggingFace API 실측 검증** (2026-04-11 조회):
```
GET https://huggingface.co/api/models/BAAI/bge-m3/tree/main/onnx
```
| 파일 | 원본 값 | HF API 실제 바이트 | 계획 MinSize | 여유 |
|------|--------|------------------|-------------|------|
| model.onnx              | 725_000 🔴      | **724_923**       | 650_000       | +74,923 |
| model.onnx_data         | 2_270_000_000 🔴 | **2_266_820_608** | 2_040_000_000 | +226 MB |
| tokenizer.json          | 17_000_000      | **17_082_821**    | 15_000_000    | +2.08 MB |
| sentencepiece.bpe.model | 5_000_000       | **5_069_051**     | 4_500_000     | +569 KB |

⚠️ **결정적 검증 결과**: 원본 `model.onnx = 725_000`은 실제 HF 파일(`724_923`)보다 **77바이트 더 크다**. 즉, 원본 값으로 엄격한 `>=` 검증을 하면 **100% 모든 다운로드가 실패**한다. 본 spec의 하향 조정은 단순한 예방이 아니라 **필수 수정**임이 증명됨. `model.onnx_data`도 원본이 실제보다 약 3.2 MB 크다.

#### R3. 검증 실패 시 동작
크기 검증 실패 시:
1. `.part` 파일을 `File.Delete(partPath)`로 삭제
2. `InvalidDataException`을 throw 한다
3. 메시지 예시: `$"Downloaded file {relativePath} is smaller than expected minimum size ({actualSize} < {minSize})"`
4. 다음 실행 시 해당 파일이 없으므로 자연스럽게 재다운로드

#### R4. 성공 경로 보장
검증 통과 시에만 `File.Move(partPath, targetPath, overwrite: true)` 실행.

#### R5. 기존 동작 유지
- 이미 `targetPath`에 파일이 존재하면 건너뛰는 로직 ([BgeM3Installer.cs:79-84](src/LocalSynapse.Pipeline/Embedding/BgeM3Installer.cs#L79-L84)) 그대로 유지
- `IProgress<DownloadProgress>` 보고 방식 그대로 유지
- `CancellationToken` 전파 그대로 유지

### 1.3 비기능 요구사항
- **단위 테스트**: 추가하지 않음. Phase 0는 quick win 성격. 본격적 테스트는 Pipeline Agent 리팩토링 시점에 추가.
- **로깅**: `Debug.WriteLine` 기존 패턴 유지. 검증 실패 시 로그 1줄 추가.

### 1.4 영향 파일
- [src/LocalSynapse.Pipeline/Embedding/BgeM3Installer.cs](src/LocalSynapse.Pipeline/Embedding/BgeM3Installer.cs) (유일)

### 1.5 영향 Agent
Pipeline Agent 단독. 다른 Agent 영향 없음.

---

## 2. Phase 0b — FormatException 수정

### 2.1 목표
Avalonia 바인딩 중 `<Run Text="{Binding ..., StringFormat=...}">` 조합에서 발생하는 FormatException을 제거한다. Run.Text는 Avalonia에서 TwoWay 성향이 있어 StringFormat 역변환 경로에서 예외가 터진다.

### 2.2 핵심 원칙 — 재현 우선, 범위 최소화

초기 구상은 "SearchPage/DataSetup의 모든 StringFormat 바인딩 8~9곳에 방어적으로 `Mode=OneWay`를 명시"였으나 다음 이유로 **Run 1곳 수정**을 1차 범위로 확정한다:

1. **TextBlock.Text는 Avalonia에서 기본 OneWay** — 기본값을 재기재하는 것은 안티패턴. diff 크기와 리뷰 부담만 증가.
2. **TextBlock FormatException 관찰 사례 없음** — "방어적 일관성" 논리는 매력적이지만 실증 근거 부재.
3. **Run은 다름** — Run.Text는 Avalonia 일부 버전에서 StringFormat과 충돌 이력이 있어 명시적 `Mode=OneWay`가 의미 있음.

**확장 조건**: 실행 단계에서 TextBlock 바인딩 중 FormatException이 재현되는 사례가 **하나라도 확인**되면 해당 바인딩만 수정 범위에 추가한다. 재현되지 않으면 Run 1곳만 수정하고 종료.

### 2.3 요구사항

#### R1. 1차 수정 대상 (확정)
- [DataSetupPage.axaml:301](src/LocalSynapse.UI/Views/DataSetupPage.axaml#L301)
  ```xml
  <!-- Before -->
  <Run Text="{Binding SkippedFiles, StringFormat='{}{0:N0}'}" />

  <!-- After -->
  <Run Text="{Binding SkippedFiles, Mode=OneWay, StringFormat='{}{0:N0}'}" />
  ```

#### R2. 재현 확인 절차 (execute 단계에서 수행)
Run 1곳 수정 전/후로 다음을 수행하여 실제 버그임을 증명:

1. **사전**: 깨끗한 DB 준비 + 테스트용 폴더(이미지, zip 등 skip 대상 포함)
2. **실행**: LocalSynapse 실행 → Data setup 페이지 진입 → 해당 폴더 스캔 시작
3. **관찰**: 스캔 진행 중 SkippedFiles가 0 → N으로 증가할 때
   - **Avalonia.Data 카테고리 로그** 확인 (Avalonia 바인딩 예외는 `Debug.WriteLine`이 아니라 Avalonia 자체 로거를 통해 해당 카테고리로만 emit되며, 기본적으로 silently swallowed됨)
   - 필요 시 `LogToTrace()`가 debug 빌드에 활성화되어 있는지 사전 확인
   - UI에서 "N files skipped" 텍스트가 정상 렌더링되는지 (렌더 실패 = 바인딩 예외 증거)
4. **기록**: 관찰 결과를 commit 메시지 본문에 솔직하게 기재:
   - 재현됨: `"fix: Avalonia Run.Text + StringFormat FormatException (재현: DataSetup 스캔 시 SkippedFiles 증가 시점)"`
   - 재현 안 됨: `"fix(defensive): Avalonia Run.Text + StringFormat FormatException 예방 (방어적 수정, 관찰 사례 없음)"`

#### R3. 2차 확장 대상 (조건부)
재현 확인 중 TextBlock 바인딩에서 FormatException이 나오면 해당 바인딩에만 `Mode=OneWay` 추가. diff-plan 단계에서 정확한 수정 대상 목록은 다음 **단일 grep 명령**의 출력을 정답 소스로 삼는다:

```bash
grep -nE 'StringFormat' src/LocalSynapse.UI/Views/SearchPage.axaml src/LocalSynapse.UI/Views/DataSetupPage.axaml
```

diff-plan은 이 grep 출력을 해당 문서에 그대로 붙여넣어 라인 번호와 개수를 **단일 정답**으로 고정한다. recon/spec의 "5곳/7곳/8곳/9곳" 숫자 논쟁은 무시한다 (recon 작성 이후 파일이 바뀌었을 수 있으므로 재grep 필수).

#### R4. 추가하지 않는 것
- `Converters/CountToBoolConverter.cs` **추가하지 않음** — 현재 코드베이스에 용도 없음
- TextBlock 바인딩 **방어적 일괄 수정 안 함** (R3의 재현 조건 충족 시에만)
- 새로운 Converter 추가 없음
- 로직 변경 없음 (순수 XAML 속성 추가)

### 2.4 영향 파일
- [src/LocalSynapse.UI/Views/SearchPage.axaml](src/LocalSynapse.UI/Views/SearchPage.axaml)
- [src/LocalSynapse.UI/Views/DataSetupPage.axaml](src/LocalSynapse.UI/Views/DataSetupPage.axaml)

### 2.5 영향 Agent
UI Agent 단독.

---

## 3. Phase 0c — `CreateConnection()` PRAGMA 보강

### 3.1 목표
`SqliteConnectionFactory.CreateConnection()`으로 생성되는 모든 connection에 `synchronous=NORMAL` PRAGMA를 적용하여, 공유 `_connection`과 PRAGMA 일관성을 확보하고 write 성능을 개선한다.

### 3.2 요구사항

#### R1. PRAGMA 문자열 확장
[SqliteConnectionFactory.cs:70](src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs#L70)의 PRAGMA CommandText를 수정:

```csharp
// Before
cmd.CommandText = "PRAGMA busy_timeout=30000;";

// After
cmd.CommandText = "PRAGMA busy_timeout=30000; PRAGMA synchronous=NORMAL;";
```

#### R2. 다른 변경 없음
- `journal_mode=WAL`는 database-level 영구 설정이며 공유 `_connection` 초기화 시 이미 적용됨 → 재지정 불필요
- `ExecuteSerialized`, `GetConnection` 등 dead code는 **건드리지 않음** (Phase 1 대상)
- PRAGMA 상수 추출 리팩토링 **하지 않음** (Phase 1에서 전체 재작성 예정이므로 중복 작업)

#### R3. 회귀 방지
- `CreateConnection()` 호출자 7개 (Bm25SearchService, SearchClickService, MigrationService, FileRepository, PipelineStampRepository, EmbeddingRepository, ChunkRepository) 동작 변경 없음
- 기존 테스트 전체 통과

### 3.3 영향 파일
- [src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs](src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs) (유일, 1줄 수정)

### 3.4 영향 Agent
Core Agent 단독.

---

## 4. 전체 수락 기준 (Acceptance Criteria)

### 4.1 Gate 1 — Build
- `dotnet build LocalSynapse.v2.sln` — 0 errors
- 새로운 warning 없음 (기존 warning count 이상 증가 금지)

### 4.2 Gate 2 — Tests
- `dotnet test LocalSynapse.v2.sln` — 0 failures
- 기존 테스트 수 유지 (Phase 0에서는 신규 테스트 추가 없음)

### 4.3 Gate 3 — Impact Scope
- Pipeline Agent: `BgeM3Installer.cs` (필수, 1 file)
- UI Agent: `DataSetupPage.axaml` (필수, 1 file) + `SearchPage.axaml` (§2.3 R3 재현 조건 충족 시에만)
- Core Agent: `SqliteConnectionFactory.cs` (필수, 1 file)
- **각 Agent 경계 위반 없음**. 3~4개 파일, 3개 Agent.

### 4.4 기능 검증 (수동)
- [ ] BGE-M3 다운로드가 Windows에서 간헐적 IOException 없이 완료
- [ ] 부분 다운로드 시뮬레이션 (네트워크 중단) → `.part` 파일이 자동 정리되고 재실행 시 재다운로드
- [ ] Data setup 진행 중 SkippedFiles 표시에서 FormatException 없음
- [ ] Search page 렌더링 변화 없음

---

## 5. 미결 사항 / 남은 리스크

### 5.1 Phase 0b — 재현 확인 결과 피드백 루프
§2.3 R2의 재현 확인은 execute 단계에서 실제 수행되어야 하며, 결과가 commit 메시지에 솔직하게 반영되어야 한다. "재현 안 됨 → 방어적 수정"도 정당하지만 반드시 명시할 것.

### 5.2 Phase 0a — `minSize` 값 검증 (완료 ✅)
HuggingFace API (`/api/models/BAAI/bge-m3/tree/main/onnx`)로 실제 바이트 수를 조회하여 본 spec §1.2 R2 표에 반영 완료. 모든 minSize 값이 실제 파일 크기 대비 87~90% 범위에 있어 truncated 다운로드 감지와 false positive 방지 balance 확인됨.

### 5.3 Phase 0a — HuggingFace 파일 업데이트 시 대응
BGE-M3 모델 파일이 HuggingFace에서 업데이트되어 크기가 현재 값보다 작아지면 검증이 실패할 수 있다. 발생 시 minSize 값 재조정 필요 (현재는 Known Risk로 기록만).

---

## 6. 범위 외 (Out of Scope)

- Phase 1의 모든 SQLite 아키텍처 변경 사항 (dead code 제거, SettingsStore JSON 전환, N+1 제거 등)
- `CountToBoolConverter` 추가
- BgeM3Installer 단위 테스트 인프라 구축
- Progress 보고 방식 변경
- Localization / i18n
- PRAGMA 상수 리팩토링

---

## 7. 변경 이력

| 날짜 | 변경 |
|------|------|
| 2026-04-11 | 초안 작성. 사용자 결정 반영 (하한 검증 / InvalidDataException + .part 삭제 / 테스트 없음). |
| 2026-04-11 | 사용자 피드백 반영. Phase 0a: minSize를 HF UI 값 90%로 보수화. Phase 0b: Run 1곳만 1차 수정 + 재현 확인 절차 추가 + TextBlock은 조건부 확장. diff-plan은 grep 출력을 단일 정답 소스로 사용. |
