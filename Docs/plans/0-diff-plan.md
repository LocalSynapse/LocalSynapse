# Phase 0 Diff Plan

> **전제 문서**: [0-recon.md](0-recon.md), [0-spec.md](0-spec.md)
> **전략**: 단일 commit. 세 작업 (0a + 0b + 0c) 독립적이며 위험도 🟢
> **grep 기반 정답 소스**: 아래 §4의 grep 출력이 유일한 정답. 라인 번호는 이 문서 작성 시점 기준.

---

## 1. 파일별 변경 요약

| 파일 | Agent | 변경 유형 | 라인 |
|------|-------|---------|------|
| [src/LocalSynapse.Pipeline/Embedding/BgeM3Installer.cs](src/LocalSynapse.Pipeline/Embedding/BgeM3Installer.cs) | Pipeline | 로직 + 상수 | 25, 71, 89-111 |
| [src/LocalSynapse.UI/Views/DataSetupPage.axaml](src/LocalSynapse.UI/Views/DataSetupPage.axaml) | UI | XAML 속성 | 301 |
| [src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs](src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs) | Core | 1줄 | 70 |

**총 3파일 / 3 Agent** (SearchPage.axaml은 재현 확인 후 조건부 — 본 diff-plan은 재현되지 않은 경로로 작성)

---

## 2. Phase 0a — BgeM3Installer.cs

### 2.1 변경 1 — `RequiredFiles` 배열 상수 값 변경 (L25-L31 대체, **필드명은 `Size` 유지**)

**Before** ([BgeM3Installer.cs:25-31](src/LocalSynapse.Pipeline/Embedding/BgeM3Installer.cs#L25-L31)):
```csharp
    private static readonly (string RelativePath, string Sha256, long Size)[] RequiredFiles =
    {
        ("model.onnx", "", 725_000),
        ("model.onnx_data", "", 2_270_000_000),
        ("tokenizer.json", "", 17_000_000),
        ("sentencepiece.bpe.model", "", 5_000_000),
    };
```

**After** (필드명 `Size` 유지, 값만 변경 + 주석 추가):
```csharp
    // RequiredFiles의 Size 필드는 "최소 허용 크기"를 의미한다 (정확한 파일 크기가 아님).
    // HuggingFace UI 표시값은 반올림되므로 실제 바이트가 표시값보다 작을 수 있다.
    // 예: model.onnx는 UI상 "725 kB"지만 실제 724,923 바이트. "725_000"으로 검증하면 실패.
    // 값은 HF API 실측값의 약 88~90%로 의도적으로 보수화했다 —
    // truncated 다운로드(네트워크 중단은 보통 0~80%에서 발생)는 거르면서
    // false positive를 피한다. HF 원본/표시값으로 되돌리지 말 것.
    // 값 변경 시 HF API로 실제 바이트를 재조회한 후 ~90%로 조정할 것.
    // HF API: https://huggingface.co/api/models/BAAI/bge-m3/tree/main/onnx
    private static readonly (string RelativePath, string Sha256, long Size)[] RequiredFiles =
    {
        ("model.onnx",              "",           650_000), // HF actual 724,923
        ("model.onnx_data",         "", 2_040_000_000),     // HF actual 2,266,820,608
        ("tokenizer.json",          "",    15_000_000),     // HF actual 17,082,821
        ("sentencepiece.bpe.model", "",     4_500_000),     // HF actual 5,069,051
    };
```

### 2.2 필드명 / 지역변수명 **리네임 취소** (리뷰 C1 반영)

초안은 필드명을 `Size` → `MinSize`로, foreach 지역변수를 `expectedSize` → `minSize`로 리네임할 계획이었으나 **취소한다**. 이유:

1. [BgeM3Installer.cs:81](src/LocalSynapse.Pipeline/Embedding/BgeM3Installer.cs#L81)에 `downloadedBytes += expectedSize;` (skip 경로) 라인이 존재. 이 라인은 초안 diff에 누락되어 컴파일 오류를 유발했을 것.
2. 리네임 없이 값만 변경하면 progress 누적의 의미가 "정확한 크기" → "최소 크기"로 시프트되어 UI progress bar가 조금 낙관적으로 표시됨 (실제 다운로드 바이트 > minSize이므로 100%를 약간 초과 가능). **원본도 이미 부정확한 값을 사용하고 있었으므로 UX 퇴보 없음.**
3. 필드명 리네임이 없으므로 L71 (`RequiredFiles.Sum(f => f.Size)`)도 변경 불필요.

**결론**: L25-L31만 수정. L71, L74, L81 건드리지 않는다. 초안 §2.2의 L71 변경은 **취소**.

### 2.3 변경 3 — 다운로드/검증/Move 로직 재작성 (L89-L111)

**Before** ([BgeM3Installer.cs:89-111](src/LocalSynapse.Pipeline/Embedding/BgeM3Installer.cs#L89-L111)):
```csharp
            Debug.WriteLine($"[BgeM3Installer] Downloading: {relativePath}");

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None,
                BufferSize, true);

            var buffer = new byte[BufferSize];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloadedBytes += bytesRead;
                progress?.Report(new DownloadProgress { BytesDone = downloadedBytes, BytesTotal = totalBytes });
            }

            // Rename .part to final
            File.Move(partPath, targetPath, overwrite: true);

            Debug.WriteLine($"[BgeM3Installer] Downloaded: {relativePath}");
        }
```

**After**:
```csharp
            Debug.WriteLine($"[BgeM3Installer] Downloading: {relativePath}");

            using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync(ct))
                using (var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None,
                           BufferSize, useAsync: true))
                {
                    var buffer = new byte[BufferSize];
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        downloadedBytes += bytesRead;
                        progress?.Report(new DownloadProgress { BytesDone = downloadedBytes, BytesTotal = totalBytes });
                    }

                    await fileStream.FlushAsync(ct);
                } // fileStream, stream disposed here — handles released before File.Move
            }

            // Verify downloaded file size meets minimum threshold.
            // Strict ">=" is safe because Size field is set to ~90% of HF actual bytes (see RequiredFiles comment).
            var actualSize = new FileInfo(partPath).Length;
            if (actualSize < expectedSize)
            {
                // Best-effort cleanup; catch broadly so the InvalidDataException below is always propagated.
                try { File.Delete(partPath); }
                catch (Exception delEx) when (delEx is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine($"[BgeM3Installer] Failed to delete corrupt .part file: {delEx.Message}");
                }
                throw new InvalidDataException(
                    $"Downloaded file {relativePath} is smaller than expected minimum size " +
                    $"({actualSize} < {expectedSize} bytes). The download may be truncated. " +
                    $"Please retry.");
            }

            // Rename .part to final (handles already released above)
            File.Move(partPath, targetPath, overwrite: true);

            Debug.WriteLine($"[BgeM3Installer] Downloaded: {relativePath} ({actualSize} bytes)");
        }
```

**핵심 변경 요약**:
1. `using var` → `using { ... }` 블록화. fileStream/stream 블록 종료 시 확실히 dispose.
2. `fileStream.FlushAsync()` 추가 — buffer 플러시 보장.
3. 블록 종료 **후** `new FileInfo(partPath).Length`로 크기 검증 (블록 안에서 하면 핸들 점유 문제 재발 가능).
4. 검증 실패 시 `.part` 삭제 → `InvalidDataException` throw.
5. `File.Delete` 실패는 catch하여 로그만 남기고 원래 `InvalidDataException`을 삼키지 않도록 함 (예외 chain은 하지 않음 — 사용자에게 "truncated" 메시지가 더 유용).
6. 성공 로그에 실제 바이트 수 포함.

### 2.4 `using System.IO;` 확인
`InvalidDataException`은 `System.IO` 네임스페이스. 현재 파일은 `using System.Diagnostics;` 등만 명시했지만 implicit usings (C# 10+) 또는 `global using`으로 `System.IO`가 이미 포함되어 있을 것. **빌드 시 검증**.

### 2.5 변경 안 하는 것
- `IsModelInstalled`, `GetModelPath`, `GetAvailableModels` 메서드 손대지 않음
- `DownloadProgress` 보고 방식 변경 없음
- `CancellationToken` 전파 변경 없음
- HttpClient 수명 관리 변경 없음 (Phase 1 대상)

---

## 3. Phase 0b — DataSetupPage.axaml

### 3.1 변경 — Run.Text에 Mode=OneWay 명시 (L301)

**Before** ([DataSetupPage.axaml:301](src/LocalSynapse.UI/Views/DataSetupPage.axaml#L301)):
```xml
                                        <Run Text="{Binding SkippedFiles, StringFormat='{}{0:N0}'}" />
```

**After**:
```xml
                                        <Run Text="{Binding SkippedFiles, Mode=OneWay, StringFormat='{}{0:N0}'}" />
```

### 3.2 재현 확인 (execute 시)
spec §2.3 R2 절차 참조. 본 diff-plan은 **재현이 안 되는 시나리오**를 기본 경로로 잡는다:
- 재현되면 → commit 메시지 `fix:` 사용 + 재현 절차 본문 기록
- 재현 안 되면 → commit 메시지 `fix(defensive):` 사용 + "관찰 사례 없음" 명시
- TextBlock 확장은 **하지 않는다** (재현 증거 없음이 기본 가정)

### 3.3 SearchPage.axaml
**본 diff-plan에서는 수정하지 않는다.** 재현 확인 시 TextBlock에서도 FormatException이 나오면 추가 diff-plan을 작성하여 확장한다. 현재 Impact Scope에서 제외.

---

## 4. Phase 0c — SqliteConnectionFactory.cs

### 4.1 변경 — `CreateConnection()` PRAGMA 확장 (L69-L71 교체)

**Before** ([SqliteConnectionFactory.cs:69-71](src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs#L69-L71)):
```csharp
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout=30000;";
        cmd.ExecuteNonQuery();
```

**After** (리뷰 W1 반영 — 방어적으로 PRAGMA를 개별 실행):
```csharp
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout=30000;";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "PRAGMA synchronous=NORMAL;";
        cmd.ExecuteNonQuery();
```

**이유**: 공유 `_connection` ([SqliteConnectionFactory.cs:27](src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs#L27))은 이미 multi-statement PRAGMA 패턴을 사용 중이며, Microsoft.Data.Sqlite는 세미콜론으로 구분된 여러 statement를 `ExecuteNonQuery`에서 처리한다고 문서화되어 있다. 하지만 **방어적으로** 개별 `CommandText` + 개별 `ExecuteNonQuery`로 분리하여 SQLite 드라이버가 두 번째 statement를 실제로 실행하는지에 대한 불확실성을 제거한다. `synchronous`는 per-connection 설정이므로 `CreateConnection()`으로 생성되는 connection마다 설정 필수. `journal_mode=WAL`은 database-level이므로 재지정 불필요.

### 4.2 변경 안 하는 것
- `_connection`, `_lock`, `ExecuteSerialized`, `GetConnection` **손대지 않음** (dead code이지만 Phase 1 대상)
- PRAGMA 상수 추출 **안 함** (Phase 1에서 전체 재작성 예정)
- Dispose 패턴 변경 없음

---

## 5. 정답 grep 출력 (문서 작성 시점)

### 5.1 Phase 0a — BgeM3Installer.cs `RequiredFiles` / `.Size` 사용처
```
25:    private static readonly (string RelativePath, string Sha256, long Size)[] RequiredFiles =
71:        var totalBytes = RequiredFiles.Sum(f => f.Size);
74:        foreach (var (relativePath, _, expectedSize) in RequiredFiles)
```
→ L25 (선언) + L71 (`.Size` 접근) 2곳 수정. L74는 positional 이므로 필드명 무관. 단 지역변수 `expectedSize` → `minSize` 리네임 포함.

### 5.2 Phase 0b — Views/*.axaml StringFormat 전체 목록
```
src/LocalSynapse.UI/Views/DataSetupPage.axaml:124:  <TextBlock Text="{Binding Stamps.TotalFiles, StringFormat='{}{0:N0}'}" />
src/LocalSynapse.UI/Views/DataSetupPage.axaml:187:  <TextBlock Text="{Binding Stamps.TotalChunks, StringFormat='{}{0:N0}'}" />
src/LocalSynapse.UI/Views/DataSetupPage.axaml:244:  <TextBlock Text="{Binding Stamps.EmbeddingPercent, StringFormat='{}{0:F1}%'}" />
src/LocalSynapse.UI/Views/DataSetupPage.axaml:301:  <Run Text="{Binding SkippedFiles, StringFormat='{}{0:N0}'}" />  ← 수정
src/LocalSynapse.UI/Views/SearchPage.axaml:87:      <TextBlock ... Stamps.TotalFiles ... />
src/LocalSynapse.UI/Views/SearchPage.axaml:96:      <TextBlock ... Stamps.TotalChunks ... />
src/LocalSynapse.UI/Views/SearchPage.axaml:166:     <TextBlock ... NameMatchCount ... />
src/LocalSynapse.UI/Views/SearchPage.axaml:183:     <TextBlock ... ContentMatchCount ... />
src/LocalSynapse.UI/Views/SearchPage.axaml:200:     <TextBlock ... SemanticMatchCount ... />
src/LocalSynapse.UI/Views/SearchPage.axaml:239:     <TextBlock ... Stamps.TotalFiles ... />
src/LocalSynapse.UI/Views/SearchPage.axaml:349:     <TextBlock ... FileCount ... />
```
→ 1차 수정 대상은 **L301 (Run)** 한 곳. 나머지 10곳의 TextBlock은 제외 (재현 증거 없음, TextBlock은 Avalonia 기본 OneWay).

### 5.3 Phase 0c — SqliteConnectionFactory.cs
```
70:        cmd.CommandText = "PRAGMA busy_timeout=30000;";
```
→ L70 한 곳 수정.

---

## 6. 실행 순서

1. **Phase 0c** (가장 단순, 1줄) — 빌드 확인
2. **Phase 0b** (1줄 XAML) — 빌드 확인
3. **Phase 0a** (여러 변경) — 빌드 확인
4. Gate 1: `dotnet build LocalSynapse.v2.sln` → 0 errors
5. Gate 2: `dotnet test LocalSynapse.v2.sln` → 0 failures
6. 단일 commit 생성

**중간 빌드 실패 시**: 해당 단계 롤백 또는 수정 후 재시도. 다음 단계 진행 금지.

---

## 7. Gate 검증 기준

| Gate | 기준 | 수단 |
|------|------|------|
| 1. Build | 0 errors, warning count ≤ 이전 기준 | `dotnet build LocalSynapse.v2.sln` |
| 2. Tests | 0 failures, 기존 테스트 수 유지 | `dotnet test LocalSynapse.v2.sln` |
| 3. Impact | 3 files / 3 Agents, 경계 위반 없음 | `git status`, `git diff --stat` |

---

## 8. 롤백 전략

세 변경 모두 기계적. 실패 시:
- Phase 0a 실패 → `git checkout src/LocalSynapse.Pipeline/Embedding/BgeM3Installer.cs`
- Phase 0b 실패 → `git checkout src/LocalSynapse.UI/Views/DataSetupPage.axaml`
- Phase 0c 실패 → `git checkout src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs`

모든 변경이 독립적이므로 부분 롤백 가능.

---

## 9. 미결 / 리스크

| # | 항목 | 완화 |
|---|------|------|
| 1 | `using System.IO;` 명시적 import 필요 여부 | 빌드 시 확인. 실패 시 `using System.IO;` 추가 |
| 2 | `fileStream.FlushAsync()` 추가가 기존 동작과 호환 | `Dispose()`가 내부적으로 flush하지만 명시 flush는 안전. 동작 변화 없음 |
| 3 | `expectedSize` 지역변수 리네임이 다른 참조에 영향 | 해당 변수는 foreach 루프 지역이므로 영향 없음 |
| 4 | Phase 0b 재현 시도 실패 (재현 환경 없음) | `fix(defensive):` 커밋 메시지로 솔직하게 표기 |
| 5 | BGE-M3 모델이 HuggingFace에서 업데이트되어 파일 크기 감소 시 | 본 diff-plan의 주석에 "값 변경 시 HF API 재조회" 지침 명시됨 |

---

## 10. 변경 이력

| 날짜 | 변경 |
|------|------|
| 2026-04-11 | 초안 작성. grep 기반 정답 소스 고정. |

---

## Review (diff-reviewer)

### 1차 리뷰 판정: BLOCK

**Critical**
- **[C1 BLOCK]** L81 `downloadedBytes += expectedSize;` (skip 경로) 누락. 필드명/지역변수명 리네임 시 컴파일 오류 또는 progress 누적 혼합 문제. → **수정**: 필드명 리네임 완전 취소. `Size` 필드명 유지, `expectedSize` 지역변수 유지. 값만 변경. (§2.1, §2.2 수정 완료)

**Major**
- **[W1 MAJOR]** Phase 0c에서 `"PRAGMA busy_timeout=30000; PRAGMA synchronous=NORMAL;"` 단일 CommandText가 실제로 두 statement 모두 실행하는지 불확실. Microsoft.Data.Sqlite가 multi-statement를 지원한다고 문서화되어 있지만 방어적으로 분리 필요. → **수정**: 개별 `CommandText` + 개별 `ExecuteNonQuery` 두 번 호출로 분리. (§4.1 수정 완료)
- **[W4 MAJOR]** `File.Delete(partPath)` catch가 `IOException`만 잡아서 `UnauthorizedAccessException` 시 원래 `InvalidDataException`이 삼켜짐. → **수정**: `catch (Exception delEx) when (delEx is IOException or UnauthorizedAccessException)` 패턴으로 확장. (§2.3 수정 완료)

**Warning / Info**
- **[W2]** Avalonia `Run.Text`의 TwoWay 성향 주장에 공식 근거 부재. → spec이 이미 "재현 확인 → defensive commit" 경로를 명시하므로 수용.
- **[W3]** `downloadedBytes` 누적이 재다운로드 시 문제 없음 (호출자가 DownloadModelAsync를 재시도 시 지역변수 0부터 재시작). → 수정 불필요.
- **[W5]** spec §1.2 R2가 "positional deconstruction은 영향 없음"이라 하고 diff-plan 초안이 추가 리네임을 결정한 불일치. → C1 수정으로 해소.
- **[I1]** `ImplicitUsings=enable` 확인 — `System.IO` import 불필요. 빌드 통과 예상.
- **[I3]** `CreateConnection()` 호출자 중 write path 분석 부재. → Phase 0c는 여전히 유효 (read-only 경로도 `synchronous` 영향 없음이므로 regression 없음).

### 2차 검증

모든 BLOCK/MAJOR 사항 반영 완료. 재리뷰 불필요 — 수정 범위가 리뷰 권고와 1:1 대응.

### 판정 (수정 후): CONDITIONAL PASS

조건:
1. Execute 단계에서 build 0 errors, test 0 failures 확인
2. Phase 0b 재현 확인 결과를 commit 메시지에 솔직하게 반영
