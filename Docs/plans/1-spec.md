# Phase 1 Spec — SQLite 데이터 액세스 Medium 리팩토링

> **상태**: v3 — diff-plan 리뷰 피드백 + xUnit fallback 실측 반영 완료
> **전제 문서**: [1-recon.md](1-recon.md)
> **범위**: Phase 1 Medium — 4개 핵심 결함 (R1/R3/R5/R8) + 최소 테스트 인프라
> **가드레일**: **"필요한 만큼의 규모 안에서 최적의 효과"** — 본 spec 범위 확장 금지. 확장 유혹 발생 시 Phase 2 이후로 이월.
> **작업 기간 목표**: 1.5~2주, 사업 트랙(WO-EM1/EM2, 마케팅) 병행 유지.

---

## 0. 목표와 비목표

### 목표
1. **R5** — `Bm25SearchService` 검색 hot path의 N+1 쿼리 제거. 검색 1회당 `CreateConnection()` 호출을 수백 개에서 1개로 축소.
2. **R3** — `SettingsStore`를 SQLite에서 JSON 파일로 전환하여 DB lock 경쟁 경로에서 완전히 이탈.
3. **R1** — `SqliteConnectionFactory`의 dead code 5종 제거 (혼란 유발 원천 차단).
4. **R8** — `FileRepository.UpsertFiles`의 거대 단일 tx를 75개 단위 sub-batch로 분할하여 스캔 중 UI 반응성 개선 + cross-process lock hold time 감소.
5. **테스트 인프라** — xUnit v3 테스트 프로젝트 1개 + 8개 회귀 방지 테스트.

### 비목표 (Phase 2 이후)
- SqliteWriteQueue / background worker
- 7 Repository async 전환
- Characterization test 전체 세트
- Concurrency smoke tests / two-process smoke harness
- MigrationService tx 분할 / 진행률 보고
- Multi-process mutex / broker / read-only mode
- R10 partial failure detection 강화
- IAppPaths 분리
- Golden master schema snapshot
- `BatchUpdateExtractStatus` 감지 개선
- `MigrationService.UpgradeFtsTokenizerIfNeeded` 거대 tx 분할

### 원칙
- **TDD (Gate 4)**: 각 결함에 대해 실패 테스트 → 최소 구현 → green → refactor 순서
- **테스트 삭제 금지**: Gate 2 통과를 위해 테스트를 수정할 때는 production 코드가 틀린 것
- **Step별 commit 원칙**: 각 Step은 독립 commit. 중간 build 실패 상태로 다음 Step 진입 금지
- **grep 기반 정답 소스**: 파일/라인 번호는 diff-plan에서 grep 출력으로 재고정

---

## 1. Step 분할 (확정)

| Step | 내용 | 예상 diff | 리스크 | commit |
|------|------|---------|--------|--------|
| **Step 0** | xUnit v3 테스트 프로젝트 신설 + `TempDbFixture` + 빈 `dotnet test` 통과 | +80 | 🟢 | 1 |
| **Step 1** | R1 dead code 제거 (`SqliteConnectionFactory`) | -40 | 🟢 | 1 |
| **Step 2** | R3 SettingsStore JSON 전환 + legacy 마이그레이션 + 테스트 3개 | +200 | 🟡 | 1 |
| **Step 3** | R5 N+1 제거 (`GetBoostBatch` + `ExecuteSearch` 리팩토링) + 테스트 3개 + golden master | +180 | 🟡 | 1 |
| **Step 4** | R8 UpsertFiles sub-batch 분할 + 테스트 3개 (T7/T8/T9 — W3 회귀 가드 포함) | +90 | 🟢 | 1 |

**총 5 commits**. Feature branch 권장하나 필수 아님 (main 직접 push 허용). Step 간 의존성:

```
Step 0 ──┬──> Step 1 (독립, 안전한 시작)
         ├──> Step 2 (SettingsStore, 독립)
         ├──> Step 3 (R5, Step 0 fixture 필요)
         └──> Step 4 (R8, Step 0 fixture 필요)
```

Step 1은 가장 격리되어 리스크 0 → 빠른 green으로 momentum 확보.
Step 2 먼저 → SettingsStore 마이그레이션이 가장 복잡하고 데이터 리스크 존재 → 일찍 검증 시간 확보.
Step 3은 golden master 준비가 필요하므로 Step 2 다음.
Step 4는 호출자 영향 없는 내부 변경 → 마무리.

---

## 2. Step 0 — 테스트 인프라

### 2.1 신규 파일

```
tests/
└── LocalSynapse.Core.Tests/
    ├── LocalSynapse.Core.Tests.csproj
    ├── TempDbFixture.cs
    └── TestData/
        └── (비어있음, Step 3에서 golden master 추가)
```

### 2.2 프로젝트 설정 (`LocalSynapse.Core.Tests.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <!-- xUnit v3 3.2.2 was attempted first (recommended by spec v1/v2 reviewer)
         but failed VSTest compatibility with `dotnet test` on net8.0:
         "System.InvalidOperationException: Test process did not return valid JSON (non-object)"
         at Xunit.v3.TestProcessLauncherAdapter.GetAssemblyInfo(...).
         Root cause: xUnit v3 uses Microsoft Testing Platform which the current
         VSTest adapter cannot bridge. v2 fallback verified working on diff-plan probe.
         DO NOT revert to v3 without verifying VSTest compatibility for target .NET version. -->
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\LocalSynapse.Core\LocalSynapse.Core.csproj" />
    <ProjectReference Include="..\..\src\LocalSynapse.Search\LocalSynapse.Search.csproj" />
  </ItemGroup>
</Project>
```

**NuGet 승인** (CLAUDE.md 기준): `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` 3개. **FluentAssertions 제외** (v8+ 상업 라이선스 연 $129.95, LocalSynapse Pro tier와 철학 충돌). 순정 xUnit `Assert.*` 사용.

**xUnit v3 fallback 실제 발생** (2026-04-11, diff-plan 단계 probe):
- 시도 1: `xunit.v3 3.2.2` + `xunit.runner.visualstudio 3.1.5` on net8.0 → `dotnet build` 성공, `dotnet test` 실패 (`TestProcessLauncherAdapter.GetAssemblyInfo` invalid JSON 에러)
- 시도 2 (채택): `xunit 2.9.3` + `xunit.runner.visualstudio 2.8.2` + `Microsoft.NET.Test.Sdk 17.12.0` → `dotnet test` 정상 동작 (실측: `통과! - 실패: 0, 통과: 1, 전체: 1, 기간: 25ms`)
- 근거: xUnit v3는 Microsoft Testing Platform 기반이며 VSTest adapter가 v3 프로토콜을 아직 완전 지원하지 않음
- **향후 v3 재시도 전제**: xunit.runner.visualstudio가 v3 호환 안정 버전을 출시하고, 빈 샘플 프로젝트에서 실측 검증 통과해야 함

### 2.3 `TempDbFixture.cs`

```csharp
using System.Diagnostics;
using LocalSynapse.Core.Database;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Repositories;

namespace LocalSynapse.Core.Tests;

/// <summary>
/// Per-test temporary file-based SQLite fixture.
/// Creates a fresh DB with migrations applied, cleans up on dispose.
/// Uses file DB (not :memory:) because WAL mode behavior differs.
/// </summary>
public sealed class TempDbFixture : IDisposable
{
    public string DataFolder { get; }
    public string DbPath { get; }
    public ISettingsStore Settings { get; }
    public SqliteConnectionFactory Factory { get; }

    public TempDbFixture()
    {
        DataFolder = Path.Combine(
            Path.GetTempPath(),
            $"localsynapse-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DataFolder);

        // Step 2 이후: SettingsStore가 JSON 기반. Step 0~1 동안은 legacy SQLite 기반.
        // 어느 경우든 dataFolder 주입 방식은 동일.
        Settings = new SettingsStore(DataFolder);
        Factory = new SqliteConnectionFactory(Settings);

        DbPath = Settings.GetDatabasePath();

        var migration = new MigrationService(Factory);
        migration.RunMigrations();
    }

    public void Dispose()
    {
        // Step 1 이후 SqliteConnectionFactory는 IDisposable이 아니므로 Factory.Dispose() 호출 안 함.
        // Migration 중 열린 connection은 MigrationService의 using 패턴으로 이미 close 상태.
        // Step 0 시점에서는 fixture 사용 테스트가 0개이므로 미dispose가 실질 문제 없음.
        try
        {
            Directory.Delete(DataFolder, recursive: true);
        }
        catch (Exception ex)
        {
            // 테스트 정리 실패는 비치명적 — temp 폴더 누적은 OS가 cleanup.
            // SQLite WAL/SHM 파일이 OS-level lock으로 즉시 삭제 안 될 수 있음.
            // CLAUDE.md 규칙: 모든 catch는 로깅 필수 (빈 catch 금지).
            Debug.WriteLine($"[TempDbFixture] Failed to delete {DataFolder}: {ex.Message}");
        }
    }
}
```

**핵심 선택**:
- `IDisposable` (not `IAsyncDispose`) — 동기 dispose, xUnit 호환
- `Path.Combine(Path.GetTempPath(), guid)` — 테스트 격리
- `Directory.Delete(... recursive: true)` — SQLite WAL 파일 (`-wal`, `-shm`) 포함 정리
- **Production DI 재사용 안 함** — fixture가 직접 `SettingsStore`/`SqliteConnectionFactory`/`MigrationService`를 생성. DI 복잡도 회피. M5 해결.
- Step 2 이후에는 `SettingsStore`가 JSON 기반 → fixture도 해당 버전을 사용
- **Step 1 이후 `SqliteConnectionFactory`에 `IDisposable` 제거** → fixture도 `Factory.Dispose()` 호출하지 않음. catch 블록은 `Debug.WriteLine` 로깅 필수 (C1 수정)

### 2.4 Step 0 수락 기준

- `dotnet build LocalSynapse.v2.sln` 0 errors
- `dotnet test LocalSynapse.v2.sln` 실행 → 0 tests passed (테스트가 아직 없으므로)
- Solution 파일에 test project 등록 확인 (`dotnet sln list`에 노출)
- `tests/` 디렉토리 생성

---

## 3. Step 1 — R1 Dead Code 제거

### 3.1 대상

[SqliteConnectionFactory.cs](src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs) (135줄 → 약 95줄)

### 3.2 삭제 대상

1. `private readonly SqliteConnection _connection;` (L13)
2. `private readonly object _lock = new();` (L14)
3. `private bool _disposed;` (L15) — `Dispose()`가 `_connection` 참조를 잃으므로 함께 정리
4. 생성자 내부 `_connection = new SqliteConnection(...); _connection.Open(); PRAGMA ...` (L22-L28) — long-lived connection 생성 로직
5. `public SqliteConnection GetConnection()` (L35)
6. `public T ExecuteSerialized<T>(Func<SqliteConnection, T> action)` (L41-L47)
7. `public void ExecuteSerialized(Action<SqliteConnection> action)` (L52-L58)
8. `Dispose()` 내부의 `_connection.Dispose()` (L79)

### 3.3 유지 대상

- `public SqliteConnectionFactory(ISettingsStore settings)` 생성자 시그니처
- 생성자 내부 `settings.GetDatabasePath() + Directory.CreateDirectory` 로직
- `public SqliteConnection CreateConnection()` (Phase 0c PRAGMA + WAL 추가, §3.5)

### 3.4 결정 사항

1. **`IDisposable` 인터페이스 제거** — `_connection` 없이는 정리할 자원이 없다. DI 컨테이너가 singleton lifecycle 관리하므로 factory가 `IDisposable`일 필요 없음. ServiceProvider dispose 시 호출되지만 no-op으로 두는 것은 혼란. **완전 제거**.
2. **`sealed` 제거** (M7 해결) — test subclassing을 위해 필요. 향후 실수로 다시 `sealed`를 추가하지 않도록 클래스 XML 주석에 명시 (§3.5 코드 참조).
3. **`CreateConnection`에 `virtual` 추가** (M7 해결).

### 3.5 After 구조 (목표) — M6/M7 반영

```csharp
namespace LocalSynapse.Core.Database;

/// <summary>
/// SQLite connection factory. Creates a new connection per call with
/// consistent PRAGMAs (journal_mode=WAL, busy_timeout=30000, synchronous=NORMAL).
/// Callers are responsible for disposing connections.
///
/// Phase 1: SqliteWriteQueue-based serialization is NOT used. Cross-process
/// safety is provided by SQLite's file lock + busy_timeout=30000.
///
/// NOTE: `sealed` removed to allow test subclassing (see Phase 1 Step 0/3/4).
/// `CreateConnection` is `virtual` so test doubles can count calls via `override`.
/// </summary>
public class SqliteConnectionFactory
{
    private readonly string _dbPath;

    public SqliteConnectionFactory(ISettingsStore settings)
    {
        _dbPath = settings.GetDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
    }

    /// <summary>
    /// Creates a new SQLite connection with standard PRAGMAs applied.
    /// Caller is responsible for disposing via `using`.
    ///
    /// WAL mode is set on every connection. PRAGMA journal_mode=WAL is a no-op
    /// for databases already in WAL mode (SQLite checks current mode first),
    /// so this is safe and idempotent. Setting WAL here avoids order-dependency
    /// between SqliteConnectionFactory construction, SettingsStore initialization,
    /// and MigrationService.RunMigrations.
    /// </summary>
    public virtual SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "PRAGMA busy_timeout=30000;";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "PRAGMA synchronous=NORMAL;";
        cmd.ExecuteNonQuery();
        return conn;
    }
}
```

### 3.6 M6 — `journal_mode=WAL` 설정 시점 ✅ 해결 (옵션 b)

**결정: (b) `CreateConnection()` 매 호출마다 포함**.

초안은 (a) MigrationService 진입 시 설정을 권장했으나, Claude Web 검토에서 **순서 의존성 함정**이 발견되었다:

DI 생성 순서를 추적하면:
1. `ISettingsStore` 생성 → 생성자 내부에서 `LoadOrMigrate()` → `TryMigrateFromSqlite()` → **raw SqliteConnection 오픈** (WAL 미적용)
2. `SqliteConnectionFactory` 생성 — 단순 경로 저장
3. `MigrationService` 생성
4. `migration.RunMigrations()` — 여기서 (a)를 적용해도 이미 1단계에서 connection이 한 번 열렸음

즉 (a)는 "기존 WAL DB는 우연히 안전, 신규 DB는 migration 전까지 rollback journal 모드"라는 우연에 기댄 안전성이다. 특히 `TempDbFixture`의 빈 DB 테스트(T2)에서 journal mode 미확정 상태로 SQLite를 터치한다.

**(b)의 근거**:
- `PRAGMA journal_mode=WAL`은 이미 WAL인 DB에 호출해도 **no-op** (SQLite가 현재 모드 확인 후 재설정 안 함). 비용은 microsecond 단위
- 어느 connection이 먼저 열리든 **동일한 PRAGMA 보장** — 순서 의존성 0
- `SettingsStore.TryMigrateFromSqlite`의 raw connection은 read-only operation 1회라 WAL이든 rollback이든 무관 (read는 WAL 설정과 무관하게 동일 동작)
- `TempDbFixture`가 `Factory = new ...` 후 migration을 돌리면 migration의 첫 `CreateConnection`에서 WAL 자동 설정 — 테스트 격리 유리

**영향**: `MigrationService.cs`는 **수정하지 않음**. §8 Impact Scope에서 `MigrationService.cs` 제거 (§8 업데이트).

### 3.7 M7 — sealed 제거 + virtual (Step 1 범위 확장)

Step 1은 원래 "삭제만"이었지만 M7 해결을 위해 **`sealed` 제거 + `CreateConnection`에 `virtual` 추가**를 Step 1에 포함한다.

**근거**: Step 3 T5, Step 4 T7이 각각 `SearchClickService`와 `SqliteConnectionFactory`의 test double을 요구한다. C# `new` keyword는 **변수 선언 타입 기반 정적 디스패치**이므로, `Bm25SearchService` / `FileRepository` 내부에서 base 타입으로 선언된 필드 경유 호출은 파생 클래스의 `new` 메서드로 디스패치되지 **않는다**. `virtual` + `override`만 유효.

**Step 1 변경 추가**:
- `SqliteConnectionFactory`: `sealed` 제거 + `CreateConnection`에 `virtual` 추가 (§3.5 코드 반영됨)
- `SearchClickService`: `sealed` 제거 + `GetBoostBatch`에 `virtual` 추가 (Step 3에서 `GetBoostBatch` 신설 시 함께 적용)
- `FileRepository`: sealed 유지 (test에서 직접 subclass 안 함, counting은 factory 쪽에서 수행)

**주의**: `SearchClickService`의 `virtual GetBoostBatch`는 Step 3에서 신설되므로 Step 1에서는 `sealed` 제거만 수행. `virtual`은 Step 3에서 동시 도입.

### 3.7 Step 1 테스트

**Step 1은 테스트 없음** — 삭제만 하는 변경이라 빌드 통과가 곧 검증. R1은 "혼란 제거"가 목적이지 기능 추가가 아니므로 Gate 4 적용 예외. CLAUDE.md Gate 4는 "새 기능 추가 시" 조건이므로 기술적으로 부합.

### 3.8 Step 1 수락 기준

- Build 0 errors
- `grep -n 'ExecuteSerialized\|_connection\|_lock\s*=' src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs` 결과 0건
- 기존 호출자 0개(recon grep 결과)이므로 regression 위험 0
- `IDisposable` 제거 후 DI 컨테이너 빌드 성공 (`ServiceProvider.Dispose()` 시 no-op)

---

## 4. Step 2 — R3 SettingsStore JSON 전환

### 4.1 목표

`SettingsStore`가 SQLite를 우회하는 경로(PRAGMA 0, busy_timeout 0)를 제거한다. JSON 파일 기반으로 전환하여 SQLite lock 경쟁에서 완전 이탈.

### 4.2 관찰된 사용 키 전수 (grep 확정)

| 키 | 읽기 위치 | 쓰기 위치 | 주체 |
|---|---------|--------|------|
| `language` | [SettingsStore.cs:34](src/LocalSynapse.Core/Repositories/SettingsStore.cs#L34) | [SettingsStore.cs:40](src/LocalSynapse.Core/Repositories/SettingsStore.cs#L40) | SettingsStore |
| `fts_tokenizer_version` | [MigrationService.cs:402](src/LocalSynapse.Core/Database/MigrationService.cs#L402) | [MigrationService.cs:499](src/LocalSynapse.Core/Database/MigrationService.cs#L499) | **MigrationService (SQLite 직접 접근)** |

**M1 확정**: 2개 키뿐. `fts_tokenizer_version`은 `MigrationService`가 **SQLite `settings` 테이블에 직접** 접근하며 `SettingsStore`를 거치지 않는다 — **마이그레이션 대상 아님**.

즉 **JSON 마이그레이션 대상은 `language` 1개 키뿐**.

### 4.3 JSON 파일 경로

```
%LOCALAPPDATA%\LocalSynapse\settings.json          (Windows)
~/Library/Application Support/LocalSynapse/settings.json   (macOS)
~/.local/share/LocalSynapse/settings.json          (Linux)
```

`Environment.SpecialFolder.LocalApplicationData` + `"LocalSynapse"` 조합은 기존 `SettingsStore()` 생성자 ([SettingsStore.cs:17-19](src/LocalSynapse.Core/Repositories/SettingsStore.cs#L17-L19))와 동일. OS별 분기 불필요.

### 4.4 JSON 스키마

```json
{
  "version": 1,
  "language": "en"
}
```

- `version`: 향후 스키마 변경 대비. 현재는 `1` 고정.
- `language`: 유일한 키. 기본값 `"en"`.

**System.Text.Json 사용** (기존 NuGet 범위 내, 추가 승인 불필요).

### 4.5 Atomic Write 패턴

```csharp
private void WriteSettingsAtomic(SettingsFile settings)
{
    var json = JsonSerializer.Serialize(settings, SerializerOptions);
    var tempPath = _settingsPath + ".tmp";
    var backupPath = _settingsPath + ".bak";

    File.WriteAllText(tempPath, json);

    if (File.Exists(_settingsPath))
    {
        File.Replace(tempPath, _settingsPath, backupPath);
        try { File.Delete(backupPath); } catch { } // best-effort cleanup
    }
    else
    {
        File.Move(tempPath, _settingsPath);
    }
}
```

**핵심 선택**:
- `File.Replace`는 atomic rename을 제공 (Windows) 또는 `rename(2)` 호출 (Unix). partial write 안전.
- First-write 경로 (`target` 없음)는 `File.Replace` 사용 불가 → `File.Move` 분기.
- Backup 파일은 cleanup 실패 시 `.bak` 잔존 가능. 다음 run에서 무시됨 — 명시적 cleanup 시도 후 실패는 조용히 무시.

### 4.6 Legacy SQLite 마이그레이션

**첫 실행 시나리오** (JSON 파일 없음):
1. `_settingsPath` 존재 확인 → **있으면** JSON 로드
2. **없으면** SQLite `localsynapse.db`에서 `SELECT value FROM settings WHERE key='language'` 시도
3. SQLite 접근 실패 또는 행 없음 → `language = "en"` 기본값
4. 성공 시 해당 값으로 JSON 파일 생성
5. **SQLite 테이블은 삭제하지 않음** — 롤백 안전망

```csharp
private SettingsFile LoadOrMigrate()
{
    if (File.Exists(_settingsPath))
    {
        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<SettingsFile>(json, SerializerOptions)
                ?? new SettingsFile();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Debug.WriteLine($"[SettingsStore] Failed to read settings.json: {ex.Message}");
            // Preserve corrupt file for manual recovery.
            // Prevents legacy SQLite migration from silently overwriting
            // the user's most recent (but currently unreadable) preference.
            var corruptPath = _settingsPath + $".corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}";
            try { File.Move(_settingsPath, corruptPath); }
            catch (Exception moveEx) { Debug.WriteLine($"[SettingsStore] Corrupt backup failed: {moveEx.Message}"); }
            // Fall through to migration attempt
        }
    }

    var migrated = TryMigrateFromSqlite();
    WriteSettingsAtomic(migrated);
    return migrated;
}

private SettingsFile TryMigrateFromSqlite()
{
    var settings = new SettingsFile { Version = 1, Language = "en" };
    var dbPath = Path.Combine(_dataFolder, "localsynapse.db");
    if (!File.Exists(dbPath)) return settings;

    try
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = 'language'";
        var result = cmd.ExecuteScalar() as string;
        if (!string.IsNullOrEmpty(result))
        {
            settings.Language = result;
            Debug.WriteLine($"[SettingsStore] Migrated language='{result}' from legacy SQLite settings table");
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[SettingsStore] SQLite migration skipped: {ex.Message}");
        // Fall through — default "en"
    }

    return settings;
}
```

### 4.7 결정 사항 (사용자 확정)

- **GetBoost 처리**: Step 3에서 `GetBoost(query, path)` **삭제**, `GetBoostBatch`만 유지 (§5 참조)
- **Legacy SQLite settings 테이블**: **절대 삭제 안 함** — 롤백 안전망. `fts_tokenizer_version`도 MigrationService가 계속 사용
- **경로 메서드**: `ISettingsStore`에 `GetDataFolder/GetLogFolder/GetModelFolder/GetDatabasePath` **그대로 유지**. `IAppPaths` 분리 제외
- **Golden master**: Hardcoded 테스트 데이터 (§6 참조)

### 4.8 `SettingsStore` 최종 인터페이스 (불변)

```csharp
public interface ISettingsStore
{
    string GetLanguage();
    void SetLanguage(string cultureName);
    string GetDataFolder();
    string GetLogFolder();
    string GetModelFolder();
    string GetDatabasePath();
}
```

**시그니처 100% 불변**. 호출자 (PipelineEmbedding, SecurityViewModel, SettingsViewModel, BgeM3Installer, SqliteConnectionFactory, EmbeddingService) 영향 없음.

### 4.9 `GetDatabasePath()` 동작

JSON 전환 후에도 `GetDatabasePath()`는 여전히 `Path.Combine(_dataFolder, "localsynapse.db")`를 반환. `SqliteConnectionFactory`가 이 경로를 사용하여 WAL 모드 SQLite 접근. **settings.json은 별개 파일**.

### 4.10 Step 2 테스트 (3개)

#### T1. `SettingsStore_RoundTripsLanguage`
```csharp
[Fact]
public void SettingsStore_RoundTripsLanguage()
{
    using var temp = new TempDbFixture();
    var store = new SettingsStore(temp.DataFolder);

    Assert.Equal("en", store.GetLanguage()); // 기본값

    store.SetLanguage("ko-KR");
    Assert.Equal("ko-KR", store.GetLanguage());

    // 영속성 확인: 새 instance로 로드
    var store2 = new SettingsStore(temp.DataFolder);
    Assert.Equal("ko-KR", store2.GetLanguage());
}
```

#### T2. `SettingsStore_MigratesFromLegacySqlite_OnFirstRun`
```csharp
[Fact]
public void SettingsStore_MigratesFromLegacySqlite_OnFirstRun()
{
    using var temp = new TempDbFixture();
    // temp fixture는 이미 migration을 실행했으므로 settings 테이블 존재
    // 직접 legacy 키 삽입 (JSON 파일 아직 없음)
    using (var conn = new SqliteConnection($"Data Source={temp.DbPath}"))
    {
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO settings (key, value) VALUES ('language', 'ko-KR')";
        cmd.ExecuteNonQuery();
    }

    // settings.json이 없는 상태에서 새 SettingsStore 생성 → legacy 마이그레이션 발동
    var jsonPath = Path.Combine(temp.DataFolder, "settings.json");
    if (File.Exists(jsonPath)) File.Delete(jsonPath);

    var store = new SettingsStore(temp.DataFolder);
    Assert.Equal("ko-KR", store.GetLanguage());

    // SQLite 테이블은 삭제되지 않음 확인
    using var conn2 = new SqliteConnection($"Data Source={temp.DbPath}");
    conn2.Open();
    using var cmd2 = conn2.CreateCommand();
    cmd2.CommandText = "SELECT value FROM settings WHERE key = 'language'";
    Assert.Equal("ko-KR", cmd2.ExecuteScalar());
}
```

#### T3. `SettingsStore_CorruptJson_IsBackedUp_AndMigrationRerun`

**교체 근거**: 원본 T3 (`AtomicWrite_SurvivesPartialWriteSimulation`)는 단위 테스트로 실제 power failure를 시뮬레이션할 수 없고 `.tmp` 파일 잔존 상황만 검증했다 — "atomic이라는 단어의 절반만 테스트". 원본 atomic write 보장은 OS의 `File.Replace` / `rename(2)`에 위임되며 xUnit으로 검증 불가능.

**새 T3**는 spec v2 §4.6에서 추가된 `.corrupt.{timestamp}` 백업 로직을 직접 검증한다 — 이는 우리가 직접 작성한 코드이고, 회귀 가능성이 실존하며, 결정론적으로 검증 가능하다.

```csharp
[Fact]
public void SettingsStore_CorruptJson_IsBackedUp_AndMigrationRerun()
{
    using var temp = new TempDbFixture();
    var jsonPath = Path.Combine(temp.DataFolder, "settings.json");

    // 손상된 JSON 파일 작성
    File.WriteAllText(jsonPath, "{ invalid json");

    // Legacy SQLite에 값 삽입 (백업 후 migration이 이 값을 복구해야 함)
    using (var conn = new SqliteConnection($"Data Source={temp.DbPath}"))
    {
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT OR REPLACE INTO settings (key, value) VALUES ('language', 'de-DE')";
        cmd.ExecuteNonQuery();
    }

    var store = new SettingsStore(temp.DataFolder);
    Assert.Equal("de-DE", store.GetLanguage());

    // 손상된 파일이 백업됨 확인
    var corruptFiles = Directory.GetFiles(temp.DataFolder, "settings.json.corrupt.*");
    Assert.Single(corruptFiles);

    // 새 settings.json 생성됨 확인
    Assert.True(File.Exists(jsonPath));
}
```

### 4.11 Step 2 수락 기준

- Build 0 errors
- T1/T2/T3 green
- 아래 수동 마이그레이션 검증 7단계 절차 통과

### 4.12 수동 마이그레이션 검증 절차 (Ryan dev 머신, Step 2 완료 시 1회)

**사전 사항**: macOS 기준 경로는 `~/Library/Application Support/LocalSynapse`. Windows는 `%LOCALAPPDATA%\LocalSynapse`.

```bash
# macOS 예시 (LOCALSYNAPSE_DATA 환경변수로 치환)
export LOCALSYNAPSE_DATA="$HOME/Library/Application Support/LocalSynapse"
```

1. **사전 백업**:
   ```bash
   cp "$LOCALSYNAPSE_DATA/localsynapse.db" "$LOCALSYNAPSE_DATA/localsynapse.db.phase1-backup"
   ```

2. **현재 language 값 기록**:
   ```bash
   sqlite3 "$LOCALSYNAPSE_DATA/localsynapse.db" \
     "SELECT value FROM settings WHERE key='language';"
   ```
   결과를 노트에 기록 (예: `en` / `ko-KR`). **행이 없으면** 해당 사용자는 한 번도 언어를 변경하지 않은 상태 → 기본값 `en` 예상.

3. **settings.json 부재 확인**:
   ```bash
   ls "$LOCALSYNAPSE_DATA/settings.json" 2>/dev/null && echo "EXISTS" || echo "absent"
   ```
   Step 2 구현 전에는 `absent`가 나와야 정상.

4. **Step 2 빌드 설치 후 LocalSynapse 실행**:
   - GUI 정상 시작
   - 언어 선택 메뉴가 기록한 값과 동일하게 표시

5. **settings.json 생성 확인**:
   ```bash
   cat "$LOCALSYNAPSE_DATA/settings.json"
   ```
   내용에 `"language": "<기록한 값>"` 포함 확인 (단계 2에서 행 없었으면 `"en"`).

6. **SQLite 보존 확인**:
   ```bash
   sqlite3 "$LOCALSYNAPSE_DATA/localsynapse.db" \
     "SELECT value FROM settings WHERE key='language';"
   ```
   동일한 값이 여전히 존재 (삭제되지 않음).

7. **롤백 테스트**: LocalSynapse 종료 → `settings.json` 삭제 → 재실행 → 단계 2에서 기록한 값이 다시 나타나는지 확인 (legacy migration 재실행 경로 검증).
   ```bash
   rm "$LOCALSYNAPSE_DATA/settings.json"
   # LocalSynapse 재실행 후
   cat "$LOCALSYNAPSE_DATA/settings.json"
   ```

**검증 완료 시** Step 2 commit 가능. 실패 시 원인 분석 후 재시도, 데이터 손상 의심 시 `phase1-backup`에서 복구.

---

## 5. Step 3 — R5 N+1 제거

### 5.1 목표

`Bm25SearchService.ExecuteSearch`의 reader 루프 내부 `_clickService.GetBoost()` 호출을 제거한다. reader를 먼저 완전히 materialize → paths 수집 → `GetBoostBatch` **1회** 호출 → 점수 적용.

### 5.2 관찰된 현재 동작

[Bm25SearchService.cs:142](src/LocalSynapse.Search/Services/Bm25SearchService.cs#L142):
```csharp
var clickBoost = _clickService.GetBoost(originalQuery, r.GetString(2)); // path
```

`_clickService.GetBoost` → `SearchClickService.GetBoost` ([SearchClickService.cs:94](src/LocalSynapse.Search/Services/SearchClickService.cs#L94)) → `_connectionFactory.CreateConnection()` → PRAGMA 설정 → single row SELECT → dispose.

**LIMIT 계산**: `options.TopK * options.ChunksPerFile * 3`. `TopK=20, ChunksPerFile=4` 가정 시 240 rows → **240 connection**. 검색 1회당 240 connection.

### 5.3 새 API: `SearchClickService.GetBoostBatch`

```csharp
/// <summary>
/// 주어진 경로 목록 각각에 대한 click boost 점수를 반환한다 (0.0 ~ 1.0).
/// 단일 쿼리로 조회하여 N+1 문제를 회피한다.
/// `virtual`로 선언하여 test double이 `override`로 호출 횟수를 감시할 수 있다 (M7).
/// </summary>
/// <returns>
/// Dictionary<path, boost>. paths 중 click 기록이 없는 항목은 포함되지 않는다 (호출자는 TryGetValue 사용).
/// </returns>
public virtual Dictionary<string, double> GetBoostBatch(string query, IReadOnlyList<string> paths)
{
    if (paths.Count == 0) return new Dictionary<string, double>();

    var result = new Dictionary<string, double>(paths.Count);
    var normalizedQuery = query.ToLowerInvariant().Trim();

    using var conn = _connectionFactory.CreateConnection();
    using var cmd = conn.CreateCommand();

    // IN (?, ?, ...) batch query
    var placeholders = string.Join(", ", paths.Select((_, i) => $"$p{i}"));
    cmd.CommandText = $@"
        SELECT file_path, click_count, position, is_bounce
        FROM search_clicks
        WHERE query = $query AND file_path IN ({placeholders})";
    cmd.Parameters.AddWithValue("$query", normalizedQuery);
    for (int i = 0; i < paths.Count; i++)
        cmd.Parameters.AddWithValue($"$p{i}", paths[i]);

    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        var filePath = reader.GetString(0);
        var clickCount = reader.GetInt32(1);
        var position = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
        var isBounce = !reader.IsDBNull(3) && reader.GetInt32(3) == 1;

        if (isBounce) { result[filePath] = 0.0; continue; }

        // GetBoost와 동일 공식
        var baseBoost = Math.Min(1.0, clickCount * 0.1);
        var positionWeight = Math.Log2(position + 2);
        var boost = Math.Min(1.0, baseBoost * positionWeight);
        result[filePath] = boost;
    }

    return result;
}
```

**결정사항 (사용자 확정)**: 기존 `public double GetBoost(string query, string filePath)` 메서드 **삭제**. `Bm25SearchService`가 유일한 호출자이며 `GetBoostBatch`로 이전한다.

### 5.4 `Bm25SearchService.ExecuteSearch` 리팩토링

**Before** (N+1):
```csharp
var raw = new List<(Bm25Hit hit, double rawScore)>();
using var r = cmd.ExecuteReader();
while (r.Read())
{
    // ... bm25Score, recencyBoost, extBoost, filenameBoost 계산 ...
    var clickBoost = _clickService.GetBoost(originalQuery, r.GetString(2)); // ← N+1
    var finalScore = bm25Score * recencyBoost * extBoost * filenameBoost * (1.0 + clickBoost);
    raw.Add(...);
}
```

**After** (materialize → batch → score):
```csharp
// Phase 1: reader 먼저 완전 materialize (path 수집 포함)
var materialized = new List<(
    string fileId, string filename, string path, string extension,
    string folderPath, string? content, double bm25Score, string modifiedAt, bool isDirectory
)>();

using (var r = cmd.ExecuteReader())
{
    while (r.Read())
    {
        materialized.Add((
            r.GetString(0),
            r.GetString(1),
            r.GetString(2),
            r.GetString(3),
            r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            Math.Abs(r.GetDouble(8)),
            r.GetString(6),
            !r.IsDBNull(7) && r.GetInt32(7) == 1
        ));
    }
} // reader + connection disposed here

// Phase 2: click boost batch lookup (단일 쿼리)
var paths = materialized.Select(m => m.path).ToList();
var clickBoosts = _clickService.GetBoostBatch(originalQuery, paths);

// Phase 3: 점수 계산
var raw = new List<(Bm25Hit hit, double rawScore)>();
foreach (var m in materialized)
{
    var recencyBoost = ComputeRecencyBoost(m.modifiedAt);
    var extBoost = ExtensionBoost.GetBoost(m.extension);
    var filenameBoost = meaningfulTokens.Length > 0 &&
        meaningfulTokens.Any(t => IsWordBoundaryMatch(m.filename, t)) ? 5.0 : 1.0;

    var clickBoost = clickBoosts.TryGetValue(m.path, out var cb) ? cb : 0.0;
    var finalScore = m.bm25Score * recencyBoost * extBoost * filenameBoost * (1.0 + clickBoost);

    raw.Add((new Bm25Hit
    {
        FileId = m.fileId,
        Filename = m.filename,
        Path = m.path,
        Extension = m.extension,
        FolderPath = m.folderPath,
        Content = m.content,
        Score = finalScore,
        MatchedTerms = meaningfulTokens.ToList(),
        ModifiedAt = m.modifiedAt,
        IsDirectory = m.isDirectory,
    }, finalScore));
}
```

**핵심**: reader를 `using` 블록으로 감싸서 Phase 2의 새 connection open 전에 기존 connection이 반드시 해제되도록 보장. 두 connection이 동시에 살아있는 시점 없음.

### 5.5 `SearchClickService` 정리

- `public double GetBoost(string query, string filePath)` **삭제**
- `RecordClick` / `OnNewSearch` / `GetBoostBatch`만 public 유지

### 5.6 Step 3 테스트 (3개)

#### T4. `GetBoostBatch_ReturnsBoostForAllRecordedPaths`
```csharp
[Fact]
public void GetBoostBatch_ReturnsBoostForAllRecordedPaths()
{
    using var temp = new TempDbFixture();
    var svc = new SearchClickService(temp.Factory);

    svc.RecordClick("test", "/a/file1.txt", 0);
    svc.RecordClick("test", "/a/file2.txt", 3);
    svc.RecordClick("test", "/a/file1.txt", 0); // 2nd click, count = 2

    var boosts = svc.GetBoostBatch("test",
        new[] { "/a/file1.txt", "/a/file2.txt", "/a/unseen.txt" });

    Assert.True(boosts.ContainsKey("/a/file1.txt"));
    Assert.True(boosts.ContainsKey("/a/file2.txt"));
    Assert.False(boosts.ContainsKey("/a/unseen.txt")); // 기록 없음

    Assert.True(boosts["/a/file1.txt"] > 0.0);
    Assert.True(boosts["/a/file2.txt"] > boosts["/a/file1.txt"]); // pos 3 > pos 0
}
```

#### T5. `ExecuteSearch_CallsGetBoostBatchOnce_NotPerResult`

**전제 (M7 해결 확정)**: `SearchClickService`는 `sealed` 제거 + `GetBoostBatch`가 `virtual`로 선언됨. test 서브클래스에서 `override` 사용.

```csharp
// 호출 횟수 관찰용 test double
private sealed class CountingSearchClickService : SearchClickService
{
    public int BatchCallCount { get; private set; }
    public CountingSearchClickService(SqliteConnectionFactory f) : base(f) { }

    public override Dictionary<string, double> GetBoostBatch(
        string query, IReadOnlyList<string> paths)
    {
        BatchCallCount++;
        return base.GetBoostBatch(query, paths);
    }
}

[Fact]
public void ExecuteSearch_CallsGetBoostBatchOnce_NotPerResult()
{
    using var temp = new TempDbFixture();
    SeedSearchCorpus(temp); // §5.7 참조

    var counter = new CountingSearchClickService(temp.Factory);
    var svc = new Bm25SearchService(temp.Factory, counter);

    svc.ClearCache();
    var results = svc.Search("report", new SearchOptions { TopK = 10, ChunksPerFile = 4 });

    Assert.True(results.Count > 0);
    Assert.Equal(1, counter.BatchCallCount);
}
```

**왜 `new` 아닌 `override`**: C# `new` keyword는 **변수 선언 타입 기반 정적 디스패치**이므로, `Bm25SearchService._clickService` 필드가 `SearchClickService` 타입으로 선언되어 있으면 `new` 메서드는 호출되지 않고 base 메서드로 디스패치된다. 런타임 타입이 `CountingSearchClickService`여도 무관. 즉 `new`로 작성하면 테스트가 컴파일되고 실행되지만 `BatchCallCount`가 0으로 나와 테스트가 아무것도 검증하지 못한다.

**`override`를 쓰려면 base method가 `virtual`이어야 한다** → Step 1에서 `SearchClickService` `sealed` 제거 + Step 3에서 `GetBoostBatch`에 `virtual` 추가 (§3.7 참조).

#### T6. `ExecuteSearch_ProducesSameRanking_AsGoldenMaster`
```csharp
[Fact]
public void ExecuteSearch_ProducesSameRanking_AsGoldenMaster()
{
    using var temp = new TempDbFixture();
    SeedSearchCorpus(temp);

    var clickSvc = new SearchClickService(temp.Factory);
    var bm25 = new Bm25SearchService(temp.Factory, clickSvc);

    var queries = new[] { "report", "budget 2024", "plan" };
    var goldenPath = Path.Combine(
        AppContext.BaseDirectory, "TestData", "search-golden-master.json");
    var golden = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
        File.ReadAllText(goldenPath))!;

    foreach (var q in queries)
    {
        bm25.ClearCache();
        var hits = bm25.Search(q, new SearchOptions { TopK = 10, ChunksPerFile = 4 });
        var ranking = hits.Select(h => h.Path).ToArray();
        Assert.Equal(golden[q], ranking); // 정확한 순서 일치
    }
}
```

### 5.7 Golden Master Corpus (Hardcoded)

**결정사항 (사용자 확정)**: Hardcoded 테스트 데이터.

```csharp
private static void SeedSearchCorpus(TempDbFixture temp)
{
    var fileRepo = new FileRepository(temp.Factory);
    var chunkRepo = new ChunkRepository(temp.Factory);

    // 10개 테스트 파일 (확장자/이름/내용 다양성)
    var files = new[]
    {
        ("/corpus/budget-2024-report.docx",     "budget report for fiscal year 2024 quarterly analysis"),
        ("/corpus/project-plan-q1.docx",        "project plan document q1 milestones deliverables"),
        ("/corpus/meeting-notes-jan.txt",       "meeting notes january team sync budget discussion"),
        ("/corpus/annual-report-2023.pdf",      "annual report 2023 revenue growth key achievements"),
        ("/corpus/roadmap-vision.md",           "product roadmap vision long-term strategic goals"),
        ("/corpus/budget-proposal.xlsx",        "budget proposal draft spending estimates proposal"),
        ("/corpus/readme.md",                   "readme overview installation quickstart guide"),
        ("/corpus/explanation.txt",             "explanation details context reasoning background"),
        ("/corpus/finance-summary.docx",        "finance summary expenses revenue profit margin"),
        ("/corpus/plan-template.docx",          "plan template sections checklist empty fields"),
    };

    var metadataList = files.Select((t, i) => new FileMetadata
    {
        Id = "",  // UpsertFiles 내부에서 GenerateFileId(file.Path)로 덮어씀
        Path = t.Item1,
        Filename = System.IO.Path.GetFileName(t.Item1),
        Extension = System.IO.Path.GetExtension(t.Item1),
        SizeBytes = 1000,
        ModifiedAt = DateTime.UtcNow.AddDays(-i).ToString("o"),
        IndexedAt = DateTime.UtcNow.ToString("o"),  // UpsertFiles가 batch 공통값으로 재설정 (W3)
        FolderPath = "/corpus",
        MtimeMs = DateTimeOffset.UtcNow.AddDays(-i).ToUnixTimeMilliseconds(),
        IsDirectory = false,
        ExtractStatus = ExtractStatuses.Success,
    }).ToList();
    fileRepo.UpsertFiles(metadataList);

    // Chunks: 파일당 chunk 1개 (간단화)
    var chunks = files.Select((t, i) => new FileChunk
    {
        Id = $"chunk-{i}",
        FileId = FileRepository.GenerateFileId(t.Item1),
        ChunkIndex = 0,
        Text = t.Item2,
        SourceType = ChunkSourceTypes.Text,  // 실측: src/LocalSynapse.Core/Models/FileChunk.cs:20 상수 = "text"
        ContentHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(t.Item2))),
        CreatedAt = DateTime.UtcNow.ToString("o"),
    }).ToList();
    chunkRepo.UpsertChunks(chunks);
}
```

**Golden master 생성 절차 (Step 3 시작 전 1회)**:
1. 위 seed 함수로 DB 구성
2. 리팩토링 **전** 현재 `Bm25SearchService.Search(query)`를 실행
3. 각 쿼리의 path 배열을 `search-golden-master.json`으로 dump
4. 리팩토링 후 동일한 결과인지 assert

JSON 형식:
```json
{
  "report": [
    "/corpus/budget-2024-report.docx",
    "/corpus/annual-report-2023.pdf",
    "..."
  ],
  "budget 2024": [ "..." ],
  "plan": [ "..." ]
}
```

**Golden master 생성은 `.staging.json` 패턴 사용** (덮어쓰기 방지):

```csharp
[Fact(Skip = "Manual-only — remove '.staging' suffix to promote to real golden master")]
public void GenerateGoldenMaster_Staging()
{
    using var temp = new TempDbFixture();
    SeedSearchCorpus(temp);
    var clickSvc = new SearchClickService(temp.Factory);
    var bm25 = new Bm25SearchService(temp.Factory, clickSvc);

    var result = new Dictionary<string, string[]>();
    foreach (var q in new[] { "report", "budget 2024", "plan" })
    {
        bm25.ClearCache();
        var hits = bm25.Search(q, new SearchOptions { TopK = 10, ChunksPerFile = 4 });
        result[q] = hits.Select(h => h.Path).ToArray();
    }

    var stagingPath = Path.Combine(
        AppContext.BaseDirectory, "TestData", "search-golden-master.staging.json");
    File.WriteAllText(stagingPath,
        JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
}
```

**워크플로우** (xUnit v2 `[Fact(Skip="...")]`는 `--filter`로도 스킵되므로 주석 처리만 유효):
1. `GenerateGoldenMaster_Staging` 메서드의 `[Fact(Skip=...)]` 속성을 **임시로 주석 처리**
2. `dotnet test --filter "FullyQualifiedName~GenerateGoldenMaster_Staging"` 실행
3. `tests/LocalSynapse.Core.Tests/bin/.../TestData/search-golden-master.staging.json` 생성 확인
4. 파일을 repo 디렉토리 (`tests/LocalSynapse.Core.Tests/TestData/`)로 복사
5. 내용 검토 후 수동 rename: `mv search-golden-master.staging.json search-golden-master.json`
6. `[Fact(Skip=...)]` 속성 **복원**
7. `dotnet build` 다시 돌려서 output의 `TestData/search-golden-master.json`이 올바르게 복사되는지 확인
8. 단일 commit: golden master JSON + Skip 복원 + (Step 3 다른 변경)

**장점**: `.staging.json`과 실제 파일이 분리되어 있으므로 생성기가 실수로 promoted master를 덮어쓰지 않는다. promote는 수동 rename 1회만 필요.

**중요 (xUnit Skip 우회 불가)**: xUnit v2 `[Fact(Skip="...")]`는 `--filter`로도 무시되지 않는다. Skip 속성이 붙어있는 한 테스트는 실행되지 않는다. 유일한 우회 방법은 속성을 주석 처리하고 rebuild하는 것. `--filter`로 강제 실행은 불가능하며, 이 오해는 이전 spec draft에 있었던 실수를 수정한 것이다.

### 5.8 Step 3 수락 기준

- Build 0 errors
- T4/T5/T6 green
- `golden master` 파일이 `TestData/search-golden-master.json`에 존재 (csproj에서 `CopyToOutputDirectory=PreserveNewest` 필요)
- **Benchmark 의무 기록** — §5.9 참조

### 5.9 Benchmark (의무)

**근거**: R5는 Phase 1의 유일한 **hot path 실측 문제**이며 recon §1에서 "매우 심각" 🔴로 분류된 유일한 성능 결함. Phase 1 Medium 전체의 정당성이 R5 개선에 상당 부분 걸려 있다. 측정값이 없으면 Phase 1이 실제로 의미 있었는지 증명할 방법이 없고, Phase 2 (SqliteWriteQueue) justification의 근거 데이터도 부재하게 된다.

**산출물**: `Docs/plans/1-benchmark.md`

**측정 내용**:
- **Before** (Step 3 시작 **직전** commit): `Bm25SearchService.Search("report")` 10회 평균 latency (매회 `ClearCache` 호출)
- **After** (Step 3 완료 commit): 동일 쿼리 10회 평균 latency
- 사용 corpus: §5.7 `SeedSearchCorpus`
- 측정 코드: test project의 `[Fact(Skip = "benchmark")] public void MeasureExecuteSearchLatency()` — 수동 실행

**측정 코드 예시**:
```csharp
[Fact(Skip = "Manual benchmark — run explicitly to measure")]
public void MeasureExecuteSearchLatency()
{
    using var temp = new TempDbFixture();
    SeedSearchCorpus(temp);
    var clickSvc = new SearchClickService(temp.Factory);
    var bm25 = new Bm25SearchService(temp.Factory, clickSvc);

    var sw = new System.Diagnostics.Stopwatch();
    var durations = new List<long>();

    // Warmup (1회)
    bm25.ClearCache();
    bm25.Search("report", new SearchOptions { TopK = 10, ChunksPerFile = 4 });

    // Measure
    for (int i = 0; i < 10; i++)
    {
        bm25.ClearCache();
        sw.Restart();
        bm25.Search("report", new SearchOptions { TopK = 10, ChunksPerFile = 4 });
        sw.Stop();
        durations.Add(sw.ElapsedMilliseconds);
    }

    var avg = durations.Average();
    Console.WriteLine($"[Benchmark] Bm25SearchService.Search('report') avg: {avg:F2}ms");
    Console.WriteLine($"[Benchmark] Individual runs: [{string.Join(", ", durations)}]");
    // 결과를 수동으로 Docs/plans/1-benchmark.md에 기록
}
```

**보고 형식** (`Docs/plans/1-benchmark.md`):
```markdown
# Phase 1 Benchmark — R5 N+1 Removal

| Metric | Before (commit SHA) | After (commit SHA) | Improvement |
|--------|--------------------|--------------------|-------------|
| Search('report') avg | Xms | Yms | Z% |
| Search('budget 2024') avg | Xms | Yms | Z% |
| Search('plan') avg | Xms | Yms | Z% |
| CreateConnection calls/search | ~240 | 1 | -99.6% |

## Corpus
10개 hardcoded 파일 (§5.7 SeedSearchCorpus)

## 환경
- OS: macOS Darwin 24.4.0
- .NET: 8.0.419
- 측정 코드: tests/LocalSynapse.Core.Tests/BenchmarkTests.cs

## 분석
(예상 개선 30% 이상. 미만이면 N+1이 실제 병목이 아니었다는 증거. 이 경우 Phase 2 우선순위 재검토.)
```

**결과는 Phase 1 완료 커밋 메시지에 포함**. 예상 개선: **최소 30%**. 미만이면 N+1가 실제 병목이 아니었다는 증거이며, Phase 2 우선순위를 재검토한다 (SqliteWriteQueue가 R5 기반 정당성을 상실).

---

## 6. Step 4 — R8 UpsertFiles Sub-Batch 분할

### 6.1 목표

`FileRepository.UpsertFiles`가 입력 전체를 단일 tx로 처리하는 현재 구조를 `SubBatchSize = 75` 단위로 분할. 호출자 시그니처 불변.

### 6.2 Sub-Batch 크기

**M3 확정**: `private const int UpsertSubBatchSize = 75;`

- `FileScanner.BatchSize = 500` ([FileScanner.cs:22](src/LocalSynapse.Pipeline/Scanning/FileScanner.cs#L22))이므로 500개 호출당 `ceil(500/75) = 7` sub-tx.
- 각 sub-tx는 약 225 SQL (files insert 75 + fts delete 75 + fts insert 75).
- Lock hold time은 기존의 ~1/7로 감소 → 다른 writer 대기 시간 동일 배수 감소.

### 6.3 리팩토링 패턴

**Before** ([FileRepository.cs:57-149](src/LocalSynapse.Core/Repositories/FileRepository.cs#L57-L149)):
```csharp
public int UpsertFiles(IEnumerable<FileMetadata> files)
{
    using var conn = _connectionFactory.CreateConnection();
    using var tx = conn.BeginTransaction();

    // ... setup cmd, parameters ...

    foreach (var file in files) { ... }

    tx.Commit();
    return count;
}
```

**After**:
```csharp
private const int UpsertSubBatchSize = 75;

public int UpsertFiles(IEnumerable<FileMetadata> files)
{
    // materialize once to allow chunking
    var fileList = files as IReadOnlyList<FileMetadata> ?? files.ToList();

    // W3 회귀 방지: indexedAt을 메서드 진입 시점에 1회 계산하여 모든 sub-batch가
    // 동일한 값을 사용하도록 한다. 원본 코드(L62)는 단일 tx 내부에서 모든 파일이
    // 동일한 indexedAt을 가졌는데, sub-batch 분할 시 sub-batch마다 재계산하면
    // 같은 "batch"로 scan된 파일들이 다른 timestamp를 가지게 되어 recency ranking
    // 동작이 변한다. 이는 외부에서 관찰 가능한 회귀이므로 T8 regression guard로 검증한다.
    var indexedAt = DateTime.UtcNow.ToString("o");
    var totalCount = 0;

    for (int offset = 0; offset < fileList.Count; offset += UpsertSubBatchSize)
    {
        var take = Math.Min(UpsertSubBatchSize, fileList.Count - offset);
        var subBatch = new List<FileMetadata>(take);
        for (int i = 0; i < take; i++)
            subBatch.Add(fileList[offset + i]);

        totalCount += UpsertFilesSingleTransaction(subBatch, indexedAt);
    }

    return totalCount;
}

private int UpsertFilesSingleTransaction(IReadOnlyList<FileMetadata> files, string indexedAt)
{
    using var conn = _connectionFactory.CreateConnection();
    using var tx = conn.BeginTransaction();

    // ... 기존 L65-L148 로직 (cmd, pId 등 parameter pooling + foreach),
    //     단 L62의 `var indexedAt = DateTime.UtcNow.ToString("o");`는 삭제
    //     (outer 메서드에서 파라미터로 받음)

    tx.Commit();
    return count;
}
```

**핵심**:
- **`indexedAt`을 outer 메서드에서 1회 계산 + 파라미터 전달** → W3 회귀 방지
- 기존 parameter pooling, FTS delete/insert 로직 **그대로 내부 메서드로 이동**
- Outer loop는 chunking + `indexedAt` 계산 담당
- `files`가 lazy enumerable일 경우 1회 materialize — 이중 enumeration 방지
- Sub-batch 추출은 명시적 index 슬라이싱 (LINQ `Skip/Take` 대신 alloc 최소화)

### 6.4 호출자 영향

[FileScanner.cs:155,178](src/LocalSynapse.Pipeline/Scanning/FileScanner.cs#L155) 두 곳 — 시그니처 불변이므로 **수정 없음**.

### 6.5 Step 4 테스트 (2개)

#### T7. `UpsertFiles_ChunksInto75SizedSubBatches`

**전제 (M7 해결 확정)**: `SqliteConnectionFactory`는 Step 1에서 `sealed` 제거 + `CreateConnection`이 `virtual`로 선언됨. test 서브클래스에서 `override` 사용.

```csharp
// Test helper — tx 1회 = connection 1회 (기존 Repository 패턴 고정)
private sealed class CountingConnectionFactory : SqliteConnectionFactory
{
    public int ConnectionCount;
    public CountingConnectionFactory(ISettingsStore s) : base(s) { }

    public override SqliteConnection CreateConnection()
    {
        Interlocked.Increment(ref ConnectionCount);
        return base.CreateConnection();
    }
}

[Fact]
public void UpsertFiles_ChunksInto75SizedSubBatches()
{
    using var temp = new TempDbFixture();
    var counter = new CountingConnectionFactory(temp.Settings);

    // Migration을 counter 경유로 다시 돌리면 ConnectionCount가 오염된다.
    // temp fixture가 이미 migration을 수행했으므로 (기본 factory 사용),
    // counter는 repository 호출부터 세기 시작하도록 ConnectionCount = 0으로 리셋.
    counter.ConnectionCount = 0;

    var repo = new FileRepository(counter);

    // 200 files → ceil(200/75) = 3 sub-batches
    var files = Enumerable.Range(0, 200)
        .Select(i => CreateTestFile($"/test/file{i}.txt"))
        .ToList();

    repo.UpsertFiles(files);

    Assert.Equal(3, counter.ConnectionCount); // 3 sub-batches = 3 connections
}
```

**주의 (fixture 공유 이슈)**: `TempDbFixture`는 내부에서 기본 `SqliteConnectionFactory`를 생성하고 migration을 돌린다. 테스트가 `CountingConnectionFactory`를 별도로 만들더라도 **둘 다 같은 `_dbPath`를 가리키므로 data share**는 유지된다 — 즉 fixture가 migration으로 생성한 테이블을 counter-factory도 사용할 수 있다. 이는 의도된 동작. counter는 단지 `CreateConnection` 호출 횟수를 감시할 뿐.

**`override` 근거**: T5와 동일 (§5.6). `new` keyword 사용 시 `FileRepository._connectionFactory` 필드가 base 타입으로 선언되어 있어 counter의 `new` 메서드가 호출되지 않는다.

#### T8. `UpsertFiles_TotalInsertedCount_MatchesInput`
```csharp
[Fact]
public void UpsertFiles_TotalInsertedCount_MatchesInput()
{
    using var temp = new TempDbFixture();
    var repo = new FileRepository(temp.Factory);

    var files = Enumerable.Range(0, 200)
        .Select(i => TestHelpers.CreateTestFile($"/test/file{i}.txt"))
        .ToList();

    var count = repo.UpsertFiles(files);

    Assert.Equal(200, count);

    // DB verify
    var (totalFiles, _, _) = repo.CountScanStampTotals();
    Assert.Equal(200, totalFiles);
}
```

#### T9. `UpsertFiles_AssignsSameIndexedAtToAllFilesInBatch` (W3 회귀 가드)

**배경**: 원본 `UpsertFiles` ([FileRepository.cs:62](src/LocalSynapse.Core/Repositories/FileRepository.cs#L62))는 단일 tx 내부에서 모든 파일이 **동일한 `indexedAt`** 값을 공유했다. Sub-batch 분할 시 각 sub-batch마다 `DateTime.UtcNow`를 재계산하면, 같은 "batch"로 scan된 파일들이 micro/millisecond 단위로 다른 timestamp를 가지게 되어 recency ranking 경계 케이스에서 동작이 달라진다. 이를 영구 가드한다.

```csharp
[Fact]
public void UpsertFiles_AssignsSameIndexedAtToAllFilesInBatch()
{
    using var temp = new TempDbFixture();
    var repo = new FileRepository(temp.Factory);

    // 200 files → 3 sub-batches, but should share indexedAt
    var files = Enumerable.Range(0, 200)
        .Select(i => TestHelpers.CreateTestFile($"/test/file{i}.txt"))
        .ToList();

    repo.UpsertFiles(files);

    // 모든 파일의 indexed_at이 동일한 값인지 SQL로 확인
    using var conn = temp.Factory.CreateConnection();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(DISTINCT indexed_at) FROM files";
    var distinctCount = (long)cmd.ExecuteScalar()!;

    Assert.Equal(1, distinctCount);
}
```

**총 테스트 수**: T1~T9 = **9개**. spec §1 Step 분할의 "Step 4: R8 UpsertFiles sub-batch 분할 + 테스트 2개"에서 **3개**로 증가.

### 6.6 Step 4 수락 기준

- Build 0 errors
- T7/T8 green
- 기존 스캔 동작 수동 확인 (선택): LocalSynapse 실행 → 작은 폴더 스캔 → 에러 없이 완료

---

## 7. 미결 사항 (diff-plan 전 확정 필요)

### M1. ✅ 해결 — SettingsStore 마이그레이션 키 = `language` 1개뿐
`fts_tokenizer_version`은 `MigrationService`가 SQLite에 직접 접근하므로 대상 아님.

### M2. ✅ 해결 — Golden master = hardcoded 10 파일 corpus (§5.7)

### M3. ✅ 해결 — Sub-batch 크기 = 75 (§6.2)

### M4. ✅ 해결 — xUnit v3 사용 (§2.2). 버전은 diff-plan에서 `dotnet list package` 최신 조회 후 확정

### M5. ✅ 해결 — `TempDbFixture`가 production DI 재사용하지 않고 직접 객체 생성 (§2.3). DI 복잡도 회피

### M6. ✅ 해결 — `journal_mode=WAL`는 `CreateConnection()` 매 호출마다 설정 (옵션 b)
Claude Web 검토에서 (a) MigrationService 진입 시 설정의 순서 의존성 함정이 발견되어 (b)로 변경. §3.5 코드와 §3.6 참조. 이미 WAL인 DB는 재설정 no-op이며, 비용은 microsecond. 순서 의존성 0. `MigrationService.cs` 수정 불필요.

### M7. ✅ 해결 — `sealed` 제거 + `virtual` 추가 (옵션 A)
**Production 변경**:
- `SqliteConnectionFactory`: Step 1에서 `sealed` 제거 + `CreateConnection`에 `virtual` 추가
- `SearchClickService`: Step 1에서 `sealed` 제거, Step 3에서 `GetBoostBatch`에 `virtual` 추가 (신설 메서드)

**Test 변경**: T5 `CountingSearchClickService`와 T7 `CountingConnectionFactory`는 **`new` 아닌 `override`** 사용. `new` keyword는 변수 선언 타입 기반 정적 디스패치라 production 필드 경유 호출 시 파생 메서드로 디스패치되지 않아 테스트가 silently fail한다. §5.6/§6.5 코드에 반영됨.

**확인된 현재 상태** (grep 결과):
- `SearchClickService.cs:11` — `public sealed class SearchClickService` → Step 1에서 `sealed` 제거
- `SqliteConnectionFactory.cs:11` — `public sealed class SqliteConnectionFactory : IDisposable` → Step 1에서 `sealed` + `IDisposable` 둘 다 제거 + `CreateConnection`에 `virtual`

### M8. ✅ 해결 — `ClearCache()` 확인 완료
grep 결과 `Bm25SearchService.cs:98 — public void ClearCache() => _cache.Clear();`. 테스트에서 직접 호출 가능. **수정 불필요**.

### M9. ✅ 해결 — `.staging.json` 패턴 (옵션 2)
§5.7 참조. 생성기는 `search-golden-master.staging.json`에 쓰고, T6은 `search-golden-master.json`만 읽음. 수동 promote로 덮어쓰기 방지.

### M10. 🟢 실측 확인 완료 — SourceType / GenerateFileId
- `ChunkSourceTypes.Text = "text"` ([FileChunk.cs:20](src/LocalSynapse.Core/Models/FileChunk.cs#L20)) — §5.7 seed 코드에서 상수 사용
- `FileRepository.GenerateFileId`는 `public static` ([FileRepository.cs:511](src/LocalSynapse.Core/Repositories/FileRepository.cs#L511)) — §5.7 seed 코드 유효

---

## 8. 수락 기준 (Gate)

| Gate | 기준 | 수단 |
|------|------|------|
| 1. Build | 0 errors, warning count ≤ Phase 0 수준 | `dotnet build LocalSynapse.v2.sln` |
| 2. Tests | 8개 신규 테스트 전부 green, 0 failures | `dotnet test LocalSynapse.v2.sln` |
| 3. Impact Scope | Core + Search + Tests. 아래 파일 외 수정 금지 | `git diff --stat` |
| 4. TDD | Step 2/3/4 각 테스트는 먼저 red → 구현 → green 순서 | execute 단계에서 수동 확인 |

**Impact Scope 허용 파일**:
- Core: `SqliteConnectionFactory.cs`, `SettingsStore.cs`, `FileRepository.cs`
- Search: `Bm25SearchService.cs`, `SearchClickService.cs`
- Tests: `tests/LocalSynapse.Core.Tests/*` 전체 신규
- Solution: `LocalSynapse.v2.sln` (test project 추가)

**Impact Scope 금지 파일** (M6 옵션 b로 MigrationService 제거됨):
- `MigrationService.cs` — M6 옵션 b 채택으로 수정 불필요
- `ChunkRepository.cs`, `EmbeddingRepository.cs`, `PipelineStampRepository.cs`
- `ISettingsStore.cs` (시그니처 불변)
- `I*Repository.cs` 전체
- `App.axaml.cs`
- `ServiceCollectionExtensions.cs` (test 관련 제외)
- `PipelineOrchestrator.cs`, `FileScanner.cs`, `ContentExtractor.cs`
- 모든 UI 파일

---

## 9. 위험 및 완화

| # | 위험 | 완화 |
|---|------|------|
| 1 | Golden master brittleness — corpus 변경 시 JSON 재생성 필요 | §5.7 생성 절차 문서화, `[Fact(Skip="manual")]` 메서드로 재생성 자동화 |
| 2 | `SearchClickService`/`SqliteConnectionFactory` sealed 제거가 기존 철학과 충돌 | M7에서 명시적 결정. 테스트 가능성을 위한 최소 변경으로 정당화 |
| 3 | 마이그레이션 실패 시 `language` 값 손실 | SQLite 테이블 삭제하지 않음. 첫 run에서 JSON 생성 실패해도 다음 run에서 재시도 가능 |
| 4 | T3 atomic write 테스트가 power failure 시뮬레이션 불가 | spec에 명시 (§4.10), 불완전 검증임을 인정 |
| 5 | xUnit v3 runner VSTest 호환 실패 — **실제 발생** (diff-plan probe 2026-04-11) | Fallback 적용: `xunit 2.9.3` + `xunit.runner.visualstudio 2.8.2` + `Microsoft.NET.Test.Sdk 17.12.0`. 실측 검증 완료 (§2.2 주석 참조) |
| 6 | Phase 1 Medium 실행 중 범위 확장 유혹 | §0 비목표 목록 + "필요한 만큼" 원칙 + execute 단계에서 범위 초과 발견 시 즉시 Phase 2로 이월 |
| 7 | SQLite WAL 파일 (`-wal`, `-shm`)가 테스트 정리에서 누락 | `Directory.Delete(recursive:true)` 사용, TempDbFixture |
| 8 | R8 sub-batch 분할이 스캔 진행률 보고 빈도 증가시켜 UI 스팸 | FileScanner가 progress를 UpsertFiles 호출 **밖**에서 보고하므로 영향 없음. 확인 필요 |
| 9 | Step 3 리팩토링 후 검색 랭킹이 미묘하게 달라짐 (부동소수점 누적 오차) | golden master는 path 배열만 비교, score 값 비교 안 함 — 순서만 고정 |
| 10 | JSON corruption + legacy migration 조합 시 사용자 preference 손실 | §4.6 LoadOrMigrate가 손상된 JSON을 `.corrupt.{timestamp}` 로 백업 후 migration 진행. 사용자 수동 복구 경로 확보 |
| 11 | Step 1에서 `sealed` 제거가 JIT inlining 손실 가능 | 영향 메서드 (`CreateConnection`, `GetBoostBatch`)는 호출 빈도 낮아 성능 영향 미미. Benchmark §5.9로 실측 가능하면 확인 |

---

## 10. 변경 이력

| 날짜 | 변경 |
|------|------|
| 2026-04-11 | 초안 작성. recon의 D1~D4 확정 사항 반영. M1~M5 grep/사용자 답변으로 해결. M6~M9 diff-plan으로 이월. 5 step 분할 확정. Hardcoded golden master corpus 설계. T1~T8 테스트 spec 작성. |
| 2026-04-11 | **v2 — Claude Web 검토 피드백 반영**. M7 new→override 버그 수정 (§5.6 T5, §6.5 T7) + `SearchClickService`/`SqliteConnectionFactory`에서 `sealed` 제거 + `virtual` 추가 (Step 1 범위 확장). M6 옵션 a→b 변경 (`CreateConnection`에 `PRAGMA journal_mode=WAL` 추가, 순서 의존성 해결, §3.5/§3.6). xUnit v3 버전 3.2.2로 수정 (§2.2). §4.6 손상된 JSON `.corrupt.{timestamp}` 백업 로직 추가. §4.12 수동 마이그레이션 검증 7단계 절차 구체화. §5.7 golden master `.staging.json` 패턴으로 변경 (덮어쓰기 방지). §5.9 Benchmark 의무화 + `Docs/plans/1-benchmark.md` 산출물 지정. §5.7 `SourceType = ChunkSourceTypes.Text` 상수 사용. §8 Impact scope에서 `MigrationService.cs` 제거 (M6 옵션 b 반영). 위험 #10/#11 추가. M8/M9/M10 해결. 실측 확인: `ClearCache` public, `GenerateFileId` public static, `SourceType = "text"`, 3개 sealed class 확인. |
| 2026-04-11 | **v3.1 — xUnit fallback 실측 반영**. diff-plan probe 단계에서 `xunit.v3 3.2.2`가 `dotnet test` VSTest adapter와 비호환 확인 (`TestProcessLauncherAdapter` JSON 에러). Fallback 조합 `xunit 2.9.3` + `xunit.runner.visualstudio 2.8.2` + `Microsoft.NET.Test.Sdk 17.12.0`으로 전환, 실측 검증 완료. §2.2 csproj 코드블록 + 주석 업데이트, §9 위험 #5를 "실제 발생"으로 갱신. 회귀 방지 주석(§2.2)이 향후 v3 복귀 유혹 차단. |
| 2026-04-11 | **v3.2 — T3 교체 승인**. `SettingsStore_AtomicWrite_SurvivesPartialWriteSimulation` (power failure 단위 테스트 불가, 검증 가치 약함)을 `SettingsStore_CorruptJson_IsBackedUp_AndMigrationRerun` (§4.6 백업 로직 직접 검증)로 교체 (§4.10). diff-plan 리뷰 단계에서 발견 후 사용자 승인. Loop Workflow Hard Rule 준수: diff-plan에서 단독 결정하지 않고 사용자 소급 승인 후 spec 업데이트. |
| 2026-04-11 | **v3.3 — SeedSearchCorpus required 필드 수정** (§5.7). `FileMetadata`의 `required` 필드인 `Id`, `IndexedAt`, `FolderPath`가 초안 코드에서 누락되어 컴파일 불가 상태. diff-plan 리뷰에서 발견. `Id = ""` (UpsertFiles가 GenerateFileId로 덮어씀), `IndexedAt = now`, `FolderPath = "/corpus"`로 보강. |
| 2026-04-11 | **v3.4 — W3 회귀 방지: `UpsertFiles` `indexedAt` 파라미터 전달** (§6.3). 원본 [FileRepository.cs:62](src/LocalSynapse.Core/Repositories/FileRepository.cs#L62)는 `UpsertFiles` 메서드 진입 시 `var indexedAt = DateTime.UtcNow.ToString("o")` 1회 계산하여 모든 파일이 동일 값 공유. Sub-batch 분할 시 naïve 구현은 각 sub-batch마다 재계산하여 timestamp 상이 → recency ranking 경계 케이스에서 회귀. 수정: outer `UpsertFiles`에서 1회 계산 후 `UpsertFilesSingleTransaction(files, indexedAt)`으로 파라미터 전달. T9 `UpsertFiles_AssignsSameIndexedAtToAllFilesInBatch` 회귀 가드 추가 (§6.5). Step 4 테스트 수 2→3. W4 golden master 워크플로우 `--filter` 오해 수정 (§5.7) — `[Fact(Skip=...)]`는 `--filter`로 우회 불가, 주석 처리만 유효. |
| 2026-04-11 | **v3.5 — 2차 diff-reviewer 피드백 반영**. W-NEW-1/W-NEW-2: §2.3 `TempDbFixture` 전체 동기화 — `Debug.WriteLine` catch (C1 수정), `IDisposable` 제거 반영 (Step 1 이후 `Factory.Dispose()` 호출 안 함), `using System.Diagnostics; using LocalSynapse.Core.Repositories;` 추가. I-NEW-2: §1 Step 4 예상 diff "+80" → "+90"로 갱신 (T9 추가 반영). diff-plan v2도 동일 사항 반영. 2차 리뷰 최종 판정: PASS. |
