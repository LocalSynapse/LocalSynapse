# Phase 1 Diff Plan — SQLite Medium Refactoring

> **전제 문서**: [1-recon.md](1-recon.md), [1-spec.md](1-spec.md) (v2)
> **전략**: 5 commits (Step 0~4), 각 Step 독립 commit. Feature branch 권장.
> **범위**: Phase 1 Medium — R1/R3/R5/R8 + 최소 테스트 인프라 8개
> **가드레일**: "필요한 만큼의 규모 안에서 최적의 효과". 확장 유혹 시 Phase 2로 이월.

---

## 0. Spec v2 → Diff Plan 변환 요약

spec은 Claude Web 검토 후 v2로 업데이트됨 (M1~M10 전부 해결). 이 diff-plan은 spec v2를 파일 라인 번호 수준으로 고정하고, **실측 검증 결과**로 추가 발견한 이슈를 반영한다.

### 0.1 Diff-plan 작성 중 발견한 실측 사실 (추가 확정)

**F1. xUnit v3 3.2.2 + xunit.runner.visualstudio 3.1.5 조합이 `dotnet test` (VSTest) 런너와 호환 불가** 🔴

실측: `dotnet build`는 성공하지만 `dotnet test` 실행 시 다음 에러로 discovery 실패:
```
[xUnit.net 00:00:00.68] xunit-probe: Catastrophic failure:
System.InvalidOperationException: Test process did not return valid JSON (non-object).
   at Xunit.v3.TestProcessLauncherAdapter.GetAssemblyInfo(...)
```
원인: xUnit v3는 Microsoft Testing Platform 기반이며 VSTest adapter가 v3 프로토콜과 맞지 않음. `dotnet run` 방식 또는 `Microsoft.Testing.Platform` 직접 사용이 필요.

**결정**: spec §9 위험 #5의 fallback 조건 즉시 발동 — **xunit v2.9.3 + xunit.runner.visualstudio 2.8.2 + Microsoft.NET.Test.Sdk 17.12.0** 조합 채택.

검증: 동일 probe 프로젝트에 fallback 조합 적용 → `dotnet test`에서 `통과!  - 실패: 0, 통과: 1, 전체: 1, 기간: 25ms`.

**영향**: Step 0의 csproj 내용이 spec §2.2와 달라짐. §1.2에서 수정된 버전 사용.

**F2. 솔루션 구조 확인** 🟢
- `LocalSynapse.v2.sln` 존재, 5개 프로젝트 등록 (Core/Pipeline/Search/Mcp/UI)
- `tests/` 디렉토리 없음 (신규 생성 필요)
- `LocalSynapse.Core.csproj`에 `Microsoft.Data.Sqlite 8.0.11` 포함 — 테스트가 Core를 참조하면 전이적으로 사용 가능

**F3. Grep 실측 결과 (spec M10 확인)** 🟢
- `SearchClickService.cs:11` — `public sealed class SearchClickService`
- `SqliteConnectionFactory.cs:11` — `public sealed class SqliteConnectionFactory : IDisposable`
- `FileRepository.cs:14` — `public sealed class FileRepository : IFileRepository`
- `FileChunk.cs:20` — `public const string Text = "text";`
- `FileRepository.cs:511` — `public static string GenerateFileId(string filePath)`
- `Bm25SearchService.cs:98` — `public void ClearCache() => _cache.Clear();`

---

## 1. 파일별 변경 요약

| # | 파일 | Agent | Step | 변경 유형 | 라인 |
|---|------|-------|------|----------|------|
| — | `tests/LocalSynapse.Core.Tests/LocalSynapse.Core.Tests.csproj` | Tests | 0 | 신규 | +30 |
| — | `tests/LocalSynapse.Core.Tests/TempDbFixture.cs` | Tests | 0 | 신규 | +45 |
| — | `tests/LocalSynapse.Core.Tests/TestHelpers.cs` | Tests | 0 | 신규 | +20 |
| — | `LocalSynapse.v2.sln` | Solution | 0 | 수정 | test project 등록 |
| R1 | [SqliteConnectionFactory.cs](src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs) | Core | 1 | 재작성 | -40 / 전체 |
| R1 | [SearchClickService.cs:11](src/LocalSynapse.Search/Services/SearchClickService.cs#L11) | Search | 1 | `sealed` 제거 | 1 |
| R3 | [SettingsStore.cs](src/LocalSynapse.Core/Repositories/SettingsStore.cs) | Core | 2 | 재작성 | ~200 |
| — | `tests/LocalSynapse.Core.Tests/SettingsStoreTests.cs` | Tests | 2 | 신규 | +80 |
| R5 | [SearchClickService.cs](src/LocalSynapse.Search/Services/SearchClickService.cs) | Search | 3 | 추가 + 삭제 | +45 / -30 |
| R5 | [Bm25SearchService.cs](src/LocalSynapse.Search/Services/Bm25SearchService.cs) | Search | 3 | `ExecuteSearch` 리팩토링 | ~70 |
| — | `tests/LocalSynapse.Core.Tests/Bm25SearchServiceTests.cs` | Tests | 3 | 신규 (T4~T6 + golden master 생성기 + benchmark) | +180 |
| — | `tests/LocalSynapse.Core.Tests/TestData/` + golden master JSON | Tests | 3 | 신규 | — |
| R8 | [FileRepository.cs:57-149](src/LocalSynapse.Core/Repositories/FileRepository.cs#L57-L149) | Core | 4 | sub-batch 분할 | ~30 |
| — | `tests/LocalSynapse.Core.Tests/FileRepositoryTests.cs` | Tests | 4 | 신규 (T7~T9 포함 W3 회귀 가드) | +90 |

**합계**:
- Production 수정: 5 파일
- Tests 신규: 1 csproj + 6 C# 파일 + 1 JSON
- Solution: 1 파일
- 총 diff: ~800줄 (production ~320 + test ~450)

---

## 2. Step 0 — 테스트 인프라

### 2.1 디렉토리 생성

```
tests/
└── LocalSynapse.Core.Tests/
    ├── LocalSynapse.Core.Tests.csproj
    ├── TempDbFixture.cs
    ├── TestHelpers.cs
    └── TestData/             # Step 3에서 JSON 추가
```

### 2.2 `LocalSynapse.Core.Tests.csproj` (신규)

**F1에 따라 xunit v2.9.3 fallback 채택** (spec §2.2의 xunit.v3 3.2.2 → v2 fallback으로 변경):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>LocalSynapse.Core.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
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

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

**주의**:
- `coverlet.collector` **제외** — spec §2.2의 FluentAssertions 제외 원칙과 동일 ("필요한 만큼"). coverage는 현 단계에서 불필요.
- `Microsoft.Data.Sqlite`는 Core 경유 전이 참조 — 명시 불필요
- `ProjectReference`는 Core + Search 2개. Pipeline은 불필요 (FileScanner 등을 테스트에서 직접 호출 안 함)

### 2.3 `TempDbFixture.cs` (신규)

```csharp
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

        // Note: Step 2 이후에는 SettingsStore가 JSON 기반.
        // Step 0~1 동안은 기존 SQLite 기반 SettingsStore 유지.
        Settings = new SettingsStore(DataFolder);
        Factory = new SqliteConnectionFactory(Settings);

        DbPath = Settings.GetDatabasePath();

        var migration = new MigrationService(Factory);
        migration.RunMigrations();
    }

    public void Dispose()
    {
        // WAL 파일(-wal, -shm) 포함 recursive 정리
        try
        {
            // SettingsStore/Factory는 Step 1에서 IDisposable 제거됨.
            // Migration 중 열린 connection들은 MigrationService.ExecuteNonQuery 패턴상
            // 이미 using으로 close됨. 추가 Dispose 호출 없음.
            Directory.Delete(DataFolder, recursive: true);
        }
        catch (Exception ex)
        {
            // 테스트 정리 실패는 비치명적 — temp 폴더 누적은 OS가 cleanup.
            // SQLite WAL/SHM 파일이 OS-level lock으로 즉시 삭제 안 될 수 있음.
            // CLAUDE.md 규칙: 모든 catch는 로깅 필수 (빈 catch 금지).
            System.Diagnostics.Debug.WriteLine(
                $"[TempDbFixture] Failed to delete {DataFolder}: {ex.Message}");
        }
    }
}
```

**주의**:
- Step 1에서 `SqliteConnectionFactory.IDisposable` 제거 예정이므로 `Dispose()`에서 `Factory.Dispose()` 호출 **안 함**.
- Step 0 시점에서는 아직 `SqliteConnectionFactory`가 `IDisposable`이지만, Step 0의 테스트는 아직 0개이므로 fixture 사용 전에 Step 1에서 수정 완료.
- **C1 수정 (diff-reviewer 발견)**: 빈 catch 블록은 CLAUDE.md 코딩 규칙 위반. `Debug.WriteLine`으로 실패 이유 로깅 + 주석으로 swallow 근거 명시.

### 2.4 `TestHelpers.cs` (신규)

```csharp
using LocalSynapse.Core.Models;

namespace LocalSynapse.Core.Tests;

internal static class TestHelpers
{
    /// <summary>Create a minimal FileMetadata for testing UpsertFiles.</summary>
    public static FileMetadata CreateTestFile(string path, string? filename = null)
    {
        var fn = filename ?? System.IO.Path.GetFileName(path);
        return new FileMetadata
        {
            Id = "",  // UpsertFiles internally overwrites with GenerateFileId(path)
            Path = path,
            Filename = fn,
            Extension = System.IO.Path.GetExtension(path),
            SizeBytes = 1000,
            ModifiedAt = DateTime.UtcNow.ToString("o"),
            IndexedAt = DateTime.UtcNow.ToString("o"),  // UpsertFiles overwrites with batch value (W3)
            FolderPath = System.IO.Path.GetDirectoryName(path) ?? "",
            MtimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            IsDirectory = false,
            ExtractStatus = ExtractStatuses.Success,  // I1: 상수 사용
        };
    }
}
```

**실측 확인** (diff-reviewer 후):
- `FileMetadata`의 `required` 필드 7개: `Id`, `Path`, `Filename`, `Extension`, `ModifiedAt`, `IndexedAt`, `FolderPath` ([FileMetadata.cs:5-12](src/LocalSynapse.Core/Models/FileMetadata.cs#L5-L12))
- `ExtractStatus`는 기본값 `ExtractStatuses.Pending`이라 required 아님
- `CreateTestFile`은 7개 required 필드 **모두** 설정. 컴파일 통과
- `Id = ""`는 `FileRepository.UpsertFiles` 내부 `var id = GenerateFileId(file.Path)` ([FileRepository.cs:111](src/LocalSynapse.Core/Repositories/FileRepository.cs#L111))에서 덮어씀
- `IndexedAt`은 `UpsertFiles`가 batch 공통값으로 재설정 (W3 수정)
- `System.IO.Path` 명시 (테스트 클래스의 `using` 지시문만으로는 `Path`가 `FileMetadata.Path`와 혼동될 수 있음)

### 2.5 Solution 파일 수정

```bash
dotnet sln LocalSynapse.v2.sln add tests/LocalSynapse.Core.Tests/LocalSynapse.Core.Tests.csproj
```

**주의**: `.sln` 직접 편집보다 `dotnet sln add` 명령 사용 → GUID 자동 생성, solution 구조 자동 업데이트.

### 2.6 Step 0 수락 기준

1. `dotnet build LocalSynapse.v2.sln` — 0 errors
2. `dotnet test LocalSynapse.v2.sln` — `통과! - 실패: 0, 통과: 0, 전체: 0` (테스트 0개이지만 명령 성공)
3. `dotnet sln LocalSynapse.v2.sln list`에 test 프로젝트 노출
4. `tests/LocalSynapse.Core.Tests/` 디렉토리 존재
5. Step 0 단일 commit

---

## 3. Step 1 — R1 Dead Code 제거 + sealed 제거

### 3.1 `SqliteConnectionFactory.cs` 재작성

**Before** ([SqliteConnectionFactory.cs 전체](src/LocalSynapse.Core/Database/SqliteConnectionFactory.cs)):
```csharp
using Microsoft.Data.Sqlite;
using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.Core.Database;

public sealed class SqliteConnectionFactory : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();
    private bool _disposed;

    public SqliteConnectionFactory(ISettingsStore settings)
    {
        var dbPath = settings.GetDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000; PRAGMA synchronous=NORMAL;";
        cmd.ExecuteNonQuery();
    }

    public SqliteConnection GetConnection() => _connection;

    public T ExecuteSerialized<T>(Func<SqliteConnection, T> action)
    {
        lock (_lock) { return action(_connection); }
    }

    public void ExecuteSerialized(Action<SqliteConnection> action)
    {
        lock (_lock) { action(_connection); }
    }

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connection.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout=30000;";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "PRAGMA synchronous=NORMAL;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
    }
}
```

**After** — 전체 재작성:
```csharp
using Microsoft.Data.Sqlite;
using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.Core.Database;

/// <summary>
/// SQLite connection factory. Creates a new connection per call with
/// consistent PRAGMAs (journal_mode=WAL, busy_timeout=30000, synchronous=NORMAL).
/// Callers are responsible for disposing connections via `using`.
///
/// Phase 1: SqliteWriteQueue-based serialization is NOT used. Cross-process
/// safety is provided by SQLite's file lock + busy_timeout=30000.
///
/// NOTE: `sealed` removed intentionally to allow test subclassing.
/// `CreateConnection` is `virtual` so test doubles can count calls via `override`.
/// Do not re-add `sealed` without removing test dependencies first.
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

**변경 요약**:
- `sealed` 제거
- `IDisposable` 제거
- `_connection`, `_lock`, `_disposed` 필드 제거
- 생성자에서 long-lived connection 생성 제거 → `_dbPath`만 저장
- `GetConnection()`, `ExecuteSerialized<T>`, `ExecuteSerialized` 제거
- `Dispose()` 제거
- `CreateConnection()`에 `virtual` 추가 + PRAGMA 3개 중 `journal_mode=WAL` 추가 (M6 옵션 b)

### 3.2 `SearchClickService.cs` sealed 제거 (L11)

**Before** ([SearchClickService.cs:11](src/LocalSynapse.Search/Services/SearchClickService.cs#L11)):
```csharp
public sealed class SearchClickService
```

**After**:
```csharp
public class SearchClickService
```

**주의**: 이 Step에서는 **단지 `sealed`만 제거**. `GetBoost`/`GetBoostBatch`에 `virtual`은 Step 3에서 추가 (새 메서드 생성 시 동시에).

### 3.3 Step 1 회귀 확인 대상

- `SqliteConnectionFactory.ExecuteSerialized` 호출자: **0개** (recon §1 R1 grep 검증 완료)
- `SqliteConnectionFactory.GetConnection` 호출자: **0개**
- `SqliteConnectionFactory` 의 `IDisposable` 사용: DI 컨테이너가 자동 dispose를 시도하지만 `IDisposable` 제거 후에는 DI 컨테이너가 dispose 호출하지 않음. **no-op이 됨** → regression 없음
- `SqliteConnectionFactory.Dispose()` 명시 호출자: **grep 확인 필요** (diff-plan 실행 시 사전 검증)

```bash
grep -rn "SqliteConnectionFactory.*Dispose\|connectionFactory\.Dispose\|_connectionFactory\.Dispose" src/
```

예상 결과: 0개. 있을 경우 해당 호출 제거 필요 (Step 1 범위 확장).

### 3.4 Step 1 테스트

**테스트 없음**. Step 1은 삭제/리네임 성격의 리팩토링이라 기능 회귀 테스트 불필요. 빌드 통과가 곧 검증 (CLAUDE.md Gate 4는 "새 기능 추가 시" 조건이므로 R1은 예외).

### 3.5 Step 1 수락 기준

1. `dotnet build LocalSynapse.v2.sln` — 0 errors, 0 warnings (기존 기준 유지)
2. `grep -rn "ExecuteSerialized\|GetConnection()" src/` → 0건 (3.1 Before에 나열된 dead code)
3. `grep -n "sealed class SearchClickService\|sealed class SqliteConnectionFactory" src/` → 0건
4. `dotnet test LocalSynapse.v2.sln` — 0 tests, 0 failures (Step 0 상태 유지)
5. Step 1 단일 commit

---

## 4. Step 2 — R3 SettingsStore JSON 전환

### 4.1 `SettingsStore.cs` 재작성

**Before**: [SettingsStore.cs](src/LocalSynapse.Core/Repositories/SettingsStore.cs) 전체 (102줄, SQLite 기반)

**After** — 신규 구현:

```csharp
using System.Diagnostics;
using System.Text.Json;
using LocalSynapse.Core.Interfaces;
using Microsoft.Data.Sqlite;

namespace LocalSynapse.Core.Repositories;

/// <summary>
/// 앱 설정 저장소. JSON 파일(settings.json) 기반.
///
/// Phase 1: SQLite `settings` 테이블 기반에서 전환.
/// 첫 실행 시 legacy SQLite `settings` 테이블에서 `language` 키를 1회 마이그레이션.
/// Legacy 테이블은 삭제하지 않음 — rollback 안전망 유지.
///
/// Atomic write: temp file → File.Replace (Windows) / rename(2) (Unix).
/// Corrupt JSON 감지 시 `.corrupt.{yyyyMMddHHmmss}` 백업 보존.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private readonly string _dataFolder;
    private readonly string _dbPath;
    private readonly string _settingsPath;
    private SettingsFile _settings;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>기본 데이터 폴더(%LOCALAPPDATA%/LocalSynapse 또는 macOS 상응)를 사용.</summary>
    public SettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalSynapse"))
    {
    }

    /// <summary>지정된 데이터 폴더를 사용.</summary>
    public SettingsStore(string dataFolder)
    {
        _dataFolder = dataFolder;
        _dbPath = Path.Combine(_dataFolder, "localsynapse.db");
        _settingsPath = Path.Combine(_dataFolder, "settings.json");
        Directory.CreateDirectory(_dataFolder);

        _settings = LoadOrMigrate();
    }

    public string GetLanguage() => _settings.Language ?? "en";

    public void SetLanguage(string cultureName)
    {
        _settings.Language = cultureName;
        WriteSettingsAtomic(_settings);
    }

    public string GetDataFolder() => _dataFolder;

    public string GetLogFolder()
    {
        var folder = Path.Combine(_dataFolder, "logs");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public string GetModelFolder()
    {
        var folder = Path.Combine(_dataFolder, "models");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public string GetDatabasePath() => _dbPath;

    // ═══════════════════════════════════════════════════════════════════
    //  Private helpers
    // ═══════════════════════════════════════════════════════════════════

    private SettingsFile LoadOrMigrate()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<SettingsFile>(json, SerializerOptions);
                if (loaded != null) return loaded;
                Debug.WriteLine("[SettingsStore] settings.json deserialized to null; falling through");
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Debug.WriteLine($"[SettingsStore] Failed to read settings.json: {ex.Message}");
                // Preserve corrupt file for manual recovery.
                var corruptPath = _settingsPath + $".corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}";
                try { File.Move(_settingsPath, corruptPath); }
                catch (Exception moveEx)
                {
                    Debug.WriteLine($"[SettingsStore] Corrupt backup failed: {moveEx.Message}");
                }
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
        if (!File.Exists(_dbPath)) return settings;

        try
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE key = 'language'";
            var result = cmd.ExecuteScalar() as string;
            if (!string.IsNullOrEmpty(result))
            {
                settings.Language = result;
                Debug.WriteLine(
                    $"[SettingsStore] Migrated language='{result}' from legacy SQLite settings table");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsStore] SQLite migration skipped: {ex.Message}");
            // Fall through — default "en"
        }

        return settings;
    }

    private void WriteSettingsAtomic(SettingsFile settings)
    {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        var tempPath = _settingsPath + ".tmp";
        var backupPath = _settingsPath + ".bak";

        File.WriteAllText(tempPath, json);

        if (File.Exists(_settingsPath))
        {
            File.Replace(tempPath, _settingsPath, backupPath);
            try { File.Delete(backupPath); } catch { /* best-effort */ }
        }
        else
        {
            File.Move(tempPath, _settingsPath);
        }
    }
}

/// <summary>settings.json 스키마.</summary>
internal sealed class SettingsFile
{
    public int Version { get; set; } = 1;
    public string? Language { get; set; }
}
```

### 4.2 `ISettingsStore.cs` — 변경 없음

[ISettingsStore.cs](src/LocalSynapse.Core/Interfaces/ISettingsStore.cs) 시그니처 **100% 불변**. 호출자 영향 없음.

### 4.3 `SettingsStoreTests.cs` (신규) — 3 tests

```csharp
using LocalSynapse.Core.Repositories;
using Microsoft.Data.Sqlite;

namespace LocalSynapse.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void SettingsStore_RoundTripsLanguage()
    {
        using var temp = new TempDbFixture();

        // TempDbFixture 생성자가 이미 `new SettingsStore(DataFolder)`를 호출하여
        // Step 2 이후에는 settings.json이 생성된 상태일 수 있다. T1은 "기본값 'en'"을
        // 검증하려 하므로 기존 JSON을 삭제하여 깨끗한 시작점을 확보한다.
        // (Step 2의 SettingsStore는 JSON 없으면 TryMigrateFromSqlite → 빈 settings 테이블 → 'en')
        var jsonPath = System.IO.Path.Combine(temp.DataFolder, "settings.json");
        if (File.Exists(jsonPath)) File.Delete(jsonPath);

        var store = new SettingsStore(temp.DataFolder);
        Assert.Equal("en", store.GetLanguage()); // default — legacy SQLite가 비어있음

        store.SetLanguage("ko-KR");
        Assert.Equal("ko-KR", store.GetLanguage());

        // 영속성 확인: 새 instance로 로드 → JSON 파일에서 읽음
        var store2 = new SettingsStore(temp.DataFolder);
        Assert.Equal("ko-KR", store2.GetLanguage());
    }

    [Fact]
    public void SettingsStore_MigratesFromLegacySqlite_OnFirstRun()
    {
        using var temp = new TempDbFixture();
        // temp fixture의 migration이 settings 테이블을 생성했음

        // 직접 legacy 키 삽입
        using (var conn = new SqliteConnection($"Data Source={temp.DbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT OR REPLACE INTO settings (key, value) VALUES ('language', 'ko-KR')";
            cmd.ExecuteNonQuery();
        }

        // settings.json 제거 (fixture가 legacy SettingsStore로 생성했을 수도 있음)
        var jsonPath = Path.Combine(temp.DataFolder, "settings.json");
        if (File.Exists(jsonPath)) File.Delete(jsonPath);

        // 새 SettingsStore 생성 → legacy 마이그레이션 발동
        var store = new SettingsStore(temp.DataFolder);
        Assert.Equal("ko-KR", store.GetLanguage());

        // JSON 파일 생성 확인
        Assert.True(File.Exists(jsonPath));

        // SQLite 테이블은 삭제되지 않음 확인
        using var conn2 = new SqliteConnection($"Data Source={temp.DbPath}");
        conn2.Open();
        using var cmd2 = conn2.CreateCommand();
        cmd2.CommandText = "SELECT value FROM settings WHERE key = 'language'";
        Assert.Equal("ko-KR", cmd2.ExecuteScalar());
    }

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
}
```

**주의 (T3 Atomic Write 제외)**: spec §4.10 T3 (`SettingsStore_AtomicWrite_SurvivesPartialWriteSimulation`)는 temp file 잔존 복구를 테스트하려 했으나, **실제 atomic write의 핵심**(power failure 시 손상되지 않음)을 검증하지 못한다. 대신 **spec v2에서 추가된 corrupt JSON 백업 로직**(§4.6)을 테스트하는 것이 더 가치 있다. T3를 `SettingsStore_CorruptJson_IsBackedUp_AndMigrationRerun`으로 **교체**.

이는 spec §4.10 T3의 범위 재조정으로 spec v2와 완전 부합 (spec §5 M10이 ".staging.json 패턴"을 채택한 것과 같은 정신 — 실제로 가치 있는 테스트만 작성).

### 4.4 Step 2 수동 검증

spec §4.12 수동 마이그레이션 검증 7단계 절차를 execute 단계에서 실제 Ryan dev 머신에서 실행. dev 머신에 현재 BGE-M3 미설치 상태이므로 `language` 값은 default `en`일 가능성 높음. 수동 절차:

1. 백업: `cp ~/Library/Application\ Support/LocalSynapse/localsynapse.db{,.phase1-backup}`
2. 현재 값 기록: `sqlite3 ... "SELECT value FROM settings WHERE key='language'"`
3. settings.json 부재 확인
4. LocalSynapse 실행 → GUI 정상 시작
5. settings.json 생성 확인
6. SQLite 보존 확인
7. 롤백 테스트: settings.json 삭제 → 재실행 → 값 재현

### 4.5 Step 2 수락 기준

1. `dotnet build LocalSynapse.v2.sln` — 0 errors
2. `dotnet test LocalSynapse.v2.sln` — 3개 SettingsStore 테스트 green
3. spec §4.12 수동 절차 1회 수행 완료 (execute 단계)
4. Step 2 단일 commit

---

## 5. Step 3 — R5 N+1 제거

### 5.1 `SearchClickService.cs` — `GetBoost` 삭제 + `GetBoostBatch` 추가

**Before** ([SearchClickService.cs:94-122](src/LocalSynapse.Search/Services/SearchClickService.cs#L94-L122)):
```csharp
public double GetBoost(string query, string filePath)
{
    using var conn = _connectionFactory.CreateConnection();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        SELECT click_count, position, is_bounce FROM search_clicks
        WHERE query = $query AND file_path = $path";
    cmd.Parameters.AddWithValue("$query", query.ToLowerInvariant().Trim());
    cmd.Parameters.AddWithValue("$path", filePath);

    using var reader = cmd.ExecuteReader();
    if (!reader.Read()) return 0.0;

    var clickCount = reader.GetInt32(0);
    var position = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
    var isBounce = !reader.IsDBNull(2) && reader.GetInt32(2) == 1;

    if (isBounce) return 0.0;

    var baseBoost = Math.Min(1.0, clickCount * 0.1);
    var positionWeight = Math.Log2(position + 2);
    var boost = Math.Min(1.0, baseBoost * positionWeight);

    return boost;
}
```

**After** — 전체 삭제 + 아래 `GetBoostBatch` 추가 (동일 위치):
```csharp
/// <summary>
/// 주어진 경로 목록 각각에 대한 click boost 점수를 반환한다 (0.0 ~ 1.0).
/// 단일 쿼리로 조회하여 N+1 문제를 회피한다.
/// `virtual`로 선언하여 test double이 `override`로 호출 횟수를 감시할 수 있다.
/// </summary>
/// <returns>
/// Dictionary&lt;path, boost&gt;. paths 중 click 기록이 없는 항목은 포함되지 않는다
/// (호출자는 TryGetValue 사용).
/// </returns>
public virtual Dictionary<string, double> GetBoostBatch(string query, IReadOnlyList<string> paths)
{
    if (paths.Count == 0) return new Dictionary<string, double>();

    // W2 방어 가드: SQLite SQLITE_MAX_VARIABLE_NUMBER (기본 999). 이 메서드는
    // paths 각각에 $p{i} 1개 + $query 1개 = paths.Count + 1 변수를 사용하므로
    // 안전 한계는 paths.Count <= 998. 현재 Bm25SearchService는
    // LIMIT = TopK * ChunksPerFile * 3 = 최대 ~240 paths만 전달.
    // 향후 LIMIT 공식이 변경되거나 TopK가 크게 증가하면 이 가드가 명시적 실패로 안내.
    // 청크 분할이 필요해지면 호출자에서 batch를 쪼개거나 이 메서드를 chunked 버전으로 확장.
    // 900으로 한 이유: 999에 정확히 맞추면 SQLite 컴파일 옵션 차이에 brittle. 여유 100.
    const int MaxPathsPerCall = 900;
    if (paths.Count > MaxPathsPerCall)
    {
        throw new ArgumentException(
            $"GetBoostBatch supports at most {MaxPathsPerCall} paths per call " +
            $"(SQLite SQLITE_MAX_VARIABLE_NUMBER limit). Got {paths.Count}. " +
            $"Caller must chunk the input or extend this method to handle chunking internally.",
            nameof(paths));
    }

    var result = new Dictionary<string, double>(paths.Count);
    var normalizedQuery = query.ToLowerInvariant().Trim();

    using var conn = _connectionFactory.CreateConnection();
    using var cmd = conn.CreateCommand();

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

        var baseBoost = Math.Min(1.0, clickCount * 0.1);
        var positionWeight = Math.Log2(position + 2);
        var boost = Math.Min(1.0, baseBoost * positionWeight);
        result[filePath] = boost;
    }

    return result;
}
```

### 5.2 `Bm25SearchService.ExecuteSearch` 리팩토링

**영향 라인**: [Bm25SearchService.cs:100-176](src/LocalSynapse.Search/Services/Bm25SearchService.cs#L100-L176) (`ExecuteSearch` 메서드 전체)

**Before** — 핵심 부분:
```csharp
private IReadOnlyList<Bm25Hit> ExecuteSearch(string ftsQuery, string originalQuery, SearchOptions options)
{
    using var conn = _connectionFactory.CreateConnection();
    using var cmd = conn.CreateCommand();

    cmd.CommandText = @" SELECT ... "; // unchanged
    cmd.Parameters.AddWithValue("$fts", ftsQuery);
    cmd.Parameters.AddWithValue("$limit", options.TopK * options.ChunksPerFile * 3);

    var meaningfulTokens = NaturalQueryParser.RemoveStopwords(originalQuery)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    var raw = new List<(Bm25Hit hit, double rawScore)>();
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
        var ext = r.GetString(3);
        var modifiedAt = r.GetString(6);
        var bm25Score = Math.Abs(r.GetDouble(8));

        var recencyBoost = ComputeRecencyBoost(modifiedAt);
        var extBoost = ExtensionBoost.GetBoost(ext);
        var filename = r.GetString(1);
        var filenameBoost = meaningfulTokens.Length > 0 &&
            meaningfulTokens.Any(t => IsWordBoundaryMatch(filename, t)) ? 5.0 : 1.0;

        var clickBoost = _clickService.GetBoost(originalQuery, r.GetString(2));  // ← N+1
        var finalScore = bm25Score * recencyBoost * extBoost * filenameBoost * (1.0 + clickBoost);

        raw.Add((new Bm25Hit { ... }, finalScore));
    }

    // ... group + filter
}
```

**After**:
```csharp
private IReadOnlyList<Bm25Hit> ExecuteSearch(string ftsQuery, string originalQuery, SearchOptions options)
{
    // Phase 1: reader를 먼저 완전 materialize (N+1 제거)
    var materialized = new List<(
        string fileId, string filename, string path, string extension,
        string folderPath, string? content, double bm25Score, string modifiedAt, bool isDirectory
    )>();

    using (var conn = _connectionFactory.CreateConnection())
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"
            SELECT
                f.id, f.filename, f.path, f.extension, f.folder_path,
                fc.text, f.modified_at, f.is_directory,
                bm25(chunks_fts, 0, 0, 1.0, 5.0, 0.5) AS rank
            FROM chunks_fts
            JOIN file_chunks fc ON chunks_fts.chunk_id = fc.id
            JOIN files f ON fc.file_id = f.id
            WHERE chunks_fts MATCH $fts
            ORDER BY rank
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$fts", ftsQuery);
        cmd.Parameters.AddWithValue("$limit", options.TopK * options.ChunksPerFile * 3);

        using var r = cmd.ExecuteReader();
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
    } // reader + cmd + connection all disposed here

    // Phase 2: click boost batch lookup (단일 쿼리)
    var paths = materialized.Select(m => m.path).ToList();
    var clickBoosts = _clickService.GetBoostBatch(originalQuery, paths);

    // Phase 3: 점수 계산
    var meaningfulTokens = NaturalQueryParser.RemoveStopwords(originalQuery)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries);

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

    // File-level dedup: keep best chunk per file
    var grouped = raw
        .GroupBy(x => x.hit.FileId)
        .Select(g => g.OrderByDescending(x => x.rawScore).First().hit)
        .OrderByDescending(h => h.Score)
        .Take(options.TopK)
        .ToList();

    // Apply extension filter
    if (options.ExtensionFilter is { Count: > 0 })
    {
        var filter = new HashSet<string>(options.ExtensionFilter, StringComparer.OrdinalIgnoreCase);
        return grouped.Where(h => filter.Contains(h.Extension)).ToList();
    }

    return grouped;
}
```

**핵심**: `using (var conn = ...)` 블록이 reader materialization까지 감싸고 있음 → Phase 2의 `GetBoostBatch`가 새 connection을 열 때 이전 connection은 완전히 dispose된 상태. **두 connection이 동시에 살아있는 시점 0**.

### 5.3 `Bm25SearchServiceTests.cs` (신규) — T4/T5/T6 + 생성기 + benchmark

```csharp
using System.Text.Json;
using LocalSynapse.Core.Database;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Core.Repositories;
using LocalSynapse.Search;           // SearchOptions (namespace: LocalSynapse.Search)
using LocalSynapse.Search.Services;  // SearchClickService, Bm25SearchService
using Microsoft.Data.Sqlite;

namespace LocalSynapse.Core.Tests;

public class Bm25SearchServiceTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  Test seed
    // ═══════════════════════════════════════════════════════════════════

    private static void SeedSearchCorpus(TempDbFixture temp)
    {
        var fileRepo = new FileRepository(temp.Factory);
        var chunkRepo = new ChunkRepository(temp.Factory);

        var files = new[]
        {
            ("/corpus/budget-2024-report.docx",    "budget report for fiscal year 2024 quarterly analysis"),
            ("/corpus/project-plan-q1.docx",       "project plan document q1 milestones deliverables"),
            ("/corpus/meeting-notes-jan.txt",      "meeting notes january team sync budget discussion"),
            ("/corpus/annual-report-2023.pdf",     "annual report 2023 revenue growth key achievements"),
            ("/corpus/roadmap-vision.md",          "product roadmap vision long-term strategic goals"),
            ("/corpus/budget-proposal.xlsx",       "budget proposal draft spending estimates proposal"),
            ("/corpus/readme.md",                  "readme overview installation quickstart guide"),
            ("/corpus/explanation.txt",            "explanation details context reasoning background"),
            ("/corpus/finance-summary.docx",       "finance summary expenses revenue profit margin"),
            ("/corpus/plan-template.docx",         "plan template sections checklist empty fields"),
        };

        var metadataList = files.Select((t, i) => new FileMetadata
        {
            Id = "",  // UpsertFiles overwrites via GenerateFileId(path)
            Path = t.Item1,
            Filename = System.IO.Path.GetFileName(t.Item1),
            Extension = System.IO.Path.GetExtension(t.Item1),
            SizeBytes = 1000,
            ModifiedAt = DateTime.UtcNow.AddDays(-i).ToString("o"),
            IndexedAt = DateTime.UtcNow.ToString("o"),  // UpsertFiles overwrites with batch value
            FolderPath = "/corpus",
            MtimeMs = DateTimeOffset.UtcNow.AddDays(-i).ToUnixTimeMilliseconds(),
            IsDirectory = false,
            ExtractStatus = ExtractStatuses.Success,
        }).ToList();
        fileRepo.UpsertFiles(metadataList);

        var chunks = files.Select((t, i) => new FileChunk
        {
            Id = $"chunk-{i}",
            FileId = FileRepository.GenerateFileId(t.Item1),
            ChunkIndex = 0,
            Text = t.Item2,
            SourceType = ChunkSourceTypes.Text,
            ContentHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(t.Item2))),
            CreatedAt = DateTime.UtcNow.ToString("o"),
        }).ToList();
        chunkRepo.UpsertChunks(chunks);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  T4. GetBoostBatch 기본 동작
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetBoostBatch_ReturnsBoostForAllRecordedPaths()
    {
        using var temp = new TempDbFixture();
        SeedSearchCorpus(temp); // search_clicks 테이블이 필요 없으므로 optional
        var svc = new SearchClickService(temp.Factory);

        svc.RecordClick("test", "/corpus/budget-2024-report.docx", 0);
        svc.RecordClick("test", "/corpus/plan-template.docx", 3);
        svc.RecordClick("test", "/corpus/budget-2024-report.docx", 0); // 2nd click

        var boosts = svc.GetBoostBatch("test",
            new[]
            {
                "/corpus/budget-2024-report.docx",
                "/corpus/plan-template.docx",
                "/corpus/unseen.txt",
            });

        Assert.True(boosts.ContainsKey("/corpus/budget-2024-report.docx"));
        Assert.True(boosts.ContainsKey("/corpus/plan-template.docx"));
        Assert.False(boosts.ContainsKey("/corpus/unseen.txt"));

        Assert.True(boosts["/corpus/budget-2024-report.docx"] > 0.0);
        Assert.True(boosts["/corpus/plan-template.docx"]
                  > boosts["/corpus/budget-2024-report.docx"]);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  T5. N+1 회귀 방지
    // ═══════════════════════════════════════════════════════════════════

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
        SeedSearchCorpus(temp);

        var counter = new CountingSearchClickService(temp.Factory);
        var svc = new Bm25SearchService(temp.Factory, counter);

        svc.ClearCache();
        var results = svc.Search("report", new SearchOptions { TopK = 10, ChunksPerFile = 4 });

        Assert.True(results.Count > 0);
        Assert.Equal(1, counter.BatchCallCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  T6. Golden master ranking
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ExecuteSearch_ProducesSameRanking_AsGoldenMaster()
    {
        using var temp = new TempDbFixture();
        SeedSearchCorpus(temp);

        var clickSvc = new SearchClickService(temp.Factory);
        var bm25 = new Bm25SearchService(temp.Factory, clickSvc);

        var goldenPath = Path.Combine(
            AppContext.BaseDirectory, "TestData", "search-golden-master.json");
        Assert.True(File.Exists(goldenPath),
            "Golden master missing. Run GenerateGoldenMaster_Staging first and promote.");

        var golden = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            File.ReadAllText(goldenPath))!;

        foreach (var q in new[] { "report", "budget 2024", "plan" })
        {
            bm25.ClearCache();
            var hits = bm25.Search(q, new SearchOptions { TopK = 10, ChunksPerFile = 4 });
            var ranking = hits.Select(h => h.Path).ToArray();
            Assert.Equal(golden[q], ranking);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Golden master generator (manual only, .staging pattern)
    // ═══════════════════════════════════════════════════════════════════

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
        Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
        File.WriteAllText(
            stagingPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Benchmark (manual only)
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Manual benchmark — run explicitly to measure")]
    public void MeasureExecuteSearchLatency()
    {
        using var temp = new TempDbFixture();
        SeedSearchCorpus(temp);
        var clickSvc = new SearchClickService(temp.Factory);
        var bm25 = new Bm25SearchService(temp.Factory, clickSvc);

        var sw = new System.Diagnostics.Stopwatch();
        var durations = new List<long>();

        // Warmup
        bm25.ClearCache();
        bm25.Search("report", new SearchOptions { TopK = 10, ChunksPerFile = 4 });

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
    }
}
```

### 5.4 Golden Master 생성 워크플로우 (Step 3 실행 시)

**중요 (W4 수정)**: xUnit v2 `[Fact(Skip="...")]`는 `--filter`로도 스킵된다. Skip 속성이 붙은 한 테스트는 실행되지 않는다. 유일한 방법은 **속성을 주석 처리하고 rebuild**.

1. **리팩토링 전 생성** (Bm25SearchService 변경 시작 전):
   1. Step 3 시작 시점에 `SeedSearchCorpus` + `GenerateGoldenMaster_Staging`을 포함한 테스트 파일 작성 (테스트만 추가, production 코드 아직 변경 없음)
   2. `csproj`에 `TestData/**` CopyToOutputDirectory 항목 추가 (§5.6 참조)
   3. `GenerateGoldenMaster_Staging`의 `[Fact(Skip=...)]` 속성을 **주석 처리**
   4. Build: `dotnet build LocalSynapse.v2.sln`
   5. 실행: `dotnet test LocalSynapse.v2.sln --filter "FullyQualifiedName~GenerateGoldenMaster_Staging"`
   6. 생성 확인: `tests/LocalSynapse.Core.Tests/bin/Debug/net8.0/TestData/search-golden-master.staging.json`
   7. 파일을 repo 디렉토리로 복사: `tests/LocalSynapse.Core.Tests/TestData/search-golden-master.staging.json`
   8. 내용 검토 후 rename: `mv search-golden-master.staging.json search-golden-master.json`
   9. `[Fact(Skip=...)]` 속성 **복원**
   10. Rebuild 후 T6이 새 golden master 읽는지 확인
2. **리팩토링** (Bm25SearchService + SearchClickService 변경) — production 코드 수정
3. **검증**: T4/T5/T6 green 확인

**단일 commit에 포함**: golden master JSON + Skip 복원 + refactoring + tests (모두 1개 Step 3 commit).

### 5.5 Benchmark 수행 (Step 3 완료 시)

1. Before 측정: Step 3 시작 **직전 commit**에서 `MeasureExecuteSearchLatency` 수동 실행
2. After 측정: Step 3 완료 commit에서 동일 실행
3. `Docs/plans/1-benchmark.md` 작성 (spec §5.9 형식)

### 5.6 csproj 업데이트 — TestData 복사

`LocalSynapse.Core.Tests.csproj`에 추가:

```xml
  <ItemGroup>
    <None Update="TestData\**\*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

### 5.7 Step 3 수락 기준 (2-commit 허용)

**Gate 2 일시 실패 방지를 위해 Step 3는 2 commit으로 분할한다**:

**Step 3a commit** (테스트 인프라 + golden master 준비):
1. `Bm25SearchServiceTests.cs` 추가 — T4/T5/T6 + `GenerateGoldenMaster_Staging` + `MeasureExecuteSearchLatency` 포함, `[Fact(Skip=...)]` 적용 상태
2. csproj에 `TestData/**` `CopyToOutputDirectory` ItemGroup 추가
3. Golden master 수동 워크플로우 수행 (§5.4):
   - Skip 주석 처리 → build → `dotnet test --filter "FullyQualifiedName~GenerateGoldenMaster_Staging"` → staging JSON 생성 → repo로 복사 → rename → Skip 복원
4. **Before benchmark** 수동 측정 (이 commit 상태 = N+1 원본 동작)
5. Gate: `dotnet build` 0 errors, `dotnet test` T1~T3 + T4 (T5/T6는 refactoring 전이므로 원본 `GetBoost` 경로를 검증) 통과
6. Commit: "test: add Bm25SearchService N+1 regression guard scaffolding + golden master"

**Step 3b commit** (리팩토링):
1. `SearchClickService.GetBoost(q, p)` 삭제 + `GetBoostBatch(q, paths)` 추가 (virtual)
2. `Bm25SearchService.ExecuteSearch` reader materialize → batch lookup 리팩토링
3. **After benchmark** 수동 측정 (이 commit 상태 = N+1 제거)
4. `Docs/plans/1-benchmark.md` 작성 (Before/After 비교)
5. Gate: `dotnet build` 0 errors, `dotnet test` T1~T6 전부 green
6. Commit: "refactor: eliminate N+1 in Bm25SearchService.ExecuteSearch via GetBoostBatch"

**주의**: Step 3a의 T5 `ExecuteSearch_CallsGetBoostBatchOnce_NotPerResult`는 `CountingSearchClickService.GetBoostBatch override`가 **Step 3b의 virtual 선언 이후에만 컴파일 가능**. 따라서 T5는 Step 3b 이후 green. Step 3a 시점에서는 T5를 **주석 처리**하거나 `[Fact(Skip="awaiting Step 3b")]`로 표시 후 Step 3b commit에서 복원.

**대안 (단순화)**: Step 3 전체를 단일 commit으로 묶을 수도 있다. 이 경우 T4/T5/T6 코드 + golden master + refactoring이 모두 한 commit에 들어간다. Gate 2 일시 실패 없이 한 번에 green이 된다. 다만 benchmark "Before"를 측정할 시점이 없어지므로 benchmark는 "Step 3 직전 commit(= Step 2 완료 commit)"과 "Step 3 완료 commit" 사이에서 측정해야 한다. **이 방법이 더 단순하며 권장**.

### 5.8 Step 3 수락 기준 — 요약 (단일 commit 권장 경로)

1. `dotnet build LocalSynapse.v2.sln` — 0 errors
2. `dotnet test LocalSynapse.v2.sln` — T4/T5/T6 green (T1~T3 포함 6 tests green)
3. `TestData/search-golden-master.json` 커밋에 포함
4. `Docs/plans/1-benchmark.md` 작성 완료 (Before: Step 2 완료 commit 측정, After: Step 3 완료 commit 측정)
5. Step 3 단일 commit (분할 필요 시 3a/3b로 분할 허용)

---

## 6. Step 4 — R8 UpsertFiles Sub-Batch 분할

### 6.1 `FileRepository.UpsertFiles` 리팩토링

**영향 라인**: [FileRepository.cs:57-149](src/LocalSynapse.Core/Repositories/FileRepository.cs#L57-L149)

**Before**: L57 → `public int UpsertFiles(IEnumerable<FileMetadata> files)` — 전체가 단일 tx 내부 foreach.

**After**:
```csharp
private const int UpsertSubBatchSize = 75;

public int UpsertFiles(IEnumerable<FileMetadata> files)
{
    // 1회 materialize (lazy enumerable 이중 열거 방지)
    var fileList = files as IReadOnlyList<FileMetadata> ?? files.ToList();

    // W3 회귀 방지: indexedAt을 메서드 진입 시점에 1회 계산.
    // 원본 코드 [FileRepository.cs:62]는 단일 tx 내부에서 한 번 계산하여
    // 모든 파일이 동일한 값을 공유했다. Sub-batch마다 재계산하면 같은 "batch"로
    // scan된 파일들이 millisecond 단위로 다른 timestamp를 갖게 되어 recency
    // ranking 경계 케이스에서 회귀. T9 회귀 가드가 이를 검증한다.
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

    // indexedAt은 outer UpsertFiles에서 1회 계산 후 파라미터로 전달받는다.
    // 기존 L62 `var indexedAt = DateTime.UtcNow.ToString("o");` 라인은 삭제.
    var count = 0;

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        INSERT INTO files (id, path, filename, extension, size_bytes, modified_at,
                           indexed_at, folder_path, mtime_ms, is_directory, file_ref_number,
                           extract_status)
        VALUES ($id, $path, $filename, $ext, $size, $modified_at,
                $indexed_at, $folder_path, $mtime_ms, $is_dir, $frn, $extract_status)
        ON CONFLICT(id) DO UPDATE SET
            filename       = excluded.filename,
            extension      = excluded.extension,
            size_bytes     = excluded.size_bytes,
            modified_at    = excluded.modified_at,
            indexed_at     = excluded.indexed_at,
            mtime_ms       = excluded.mtime_ms,
            is_directory   = excluded.is_directory,
            file_ref_number= excluded.file_ref_number,
            extract_status = excluded.extract_status";

    // ... 기존 parameter pooling (L83-L107) 그대로 ...

    using var ftsDelCmd = conn.CreateCommand();
    ftsDelCmd.CommandText = "DELETE FROM files_fts WHERE file_id = $fts_del_id";
    ftsDelCmd.Parameters.Add("$fts_del_id", SqliteType.Text);

    using var ftsInsCmd = conn.CreateCommand();
    ftsInsCmd.CommandText = @"
        INSERT INTO files_fts (file_id, filename, path, extension)
        VALUES ($fts_id, $fts_filename, $fts_path, $fts_ext)";
    ftsInsCmd.Parameters.Add("$fts_id", SqliteType.Text);
    ftsInsCmd.Parameters.Add("$fts_filename", SqliteType.Text);
    ftsInsCmd.Parameters.Add("$fts_path", SqliteType.Text);
    ftsInsCmd.Parameters.Add("$fts_ext", SqliteType.Text);

    foreach (var file in files)
    {
        // ... 기존 L109-L145 foreach body 그대로 ...
    }

    tx.Commit();
    return count;
}
```

**변경 요약**:
- `public int UpsertFiles(IEnumerable<FileMetadata> files)` 시그니처 유지 (호출자 FileScanner 영향 없음)
- **`indexedAt`을 outer 메서드에서 1회 계산 후 파라미터 전달** (W3 회귀 방지)
- 외부 loop 추가 — sub-batch 75개씩 슬라이싱
- 기존 전체 body를 `private int UpsertFilesSingleTransaction(IReadOnlyList<FileMetadata> files, string indexedAt)`로 이동
- `LINQ Skip/Take` 대신 명시적 index 슬라이싱 (alloc 최소화)
- **`using var` keyword를 `using` 블록으로 교체할 필요 없음** — 기존 패턴 그대로, 단지 메서드가 쪼개졌을 뿐
- 기존 L62의 `var indexedAt = DateTime.UtcNow.ToString("o");`는 삭제 (outer에서 전달받음)

### 6.2 `FileRepositoryTests.cs` (신규) — T7/T8

```csharp
using LocalSynapse.Core.Database;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Core.Repositories;
using Microsoft.Data.Sqlite;

namespace LocalSynapse.Core.Tests;

public class FileRepositoryTests
{
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

        // fixture에서 이미 migration 실행됨 (기본 factory 사용).
        // counter는 repository 호출부터 세도록 새로 생성 — 같은 _dbPath 공유
        var counter = new CountingConnectionFactory(temp.Settings);
        var repo = new FileRepository(counter);

        // 200 files → ceil(200/75) = 3 sub-batches
        var files = Enumerable.Range(0, 200)
            .Select(i => TestHelpers.CreateTestFile($"/test/file{i}.txt"))
            .ToList();

        repo.UpsertFiles(files);

        Assert.Equal(3, counter.ConnectionCount);
    }

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

        var (totalFiles, _, _) = repo.CountScanStampTotals();
        Assert.Equal(200, totalFiles);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  T9. W3 회귀 가드 — indexedAt 일관성 (sub-batch 분할 시에도 동일 값)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void UpsertFiles_AssignsSameIndexedAtToAllFilesInBatch()
    {
        using var temp = new TempDbFixture();
        var repo = new FileRepository(temp.Factory);

        // 200 files → ceil(200/75) = 3 sub-batches. 원본은 전체 단일 tx로
        // 한 번의 DateTime.UtcNow만 호출했으므로 모든 파일 indexedAt 동일.
        // Sub-batch 분할 후에도 이 속성이 유지되어야 함 (W3 회귀 방지).
        var files = Enumerable.Range(0, 200)
            .Select(i => TestHelpers.CreateTestFile($"/test/file{i}.txt"))
            .ToList();

        repo.UpsertFiles(files);

        // SQL로 SELECT DISTINCT indexed_at 확인 — 정확히 1개 값이어야 함
        using var conn = temp.Factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(DISTINCT indexed_at) FROM files";
        var distinctCount = (long)cmd.ExecuteScalar()!;

        Assert.Equal(1L, distinctCount);
    }
}
```

**총 Step 4 테스트 수**: T7/T8/T9 = **3개**. diff-plan §1 표 "R8 UpsertFiles sub-batch 분할 + 테스트 2개"를 **3개**로 업데이트.

### 6.3 Step 4 수락 기준

1. `dotnet build LocalSynapse.v2.sln` — 0 errors
2. `dotnet test LocalSynapse.v2.sln` — 전체 **9 tests** green (T1~T9)
3. T9 (W3 회귀 가드)가 distinctCount == 1 assertion 통과
4. Step 4 단일 commit

---

## 7. 실행 순서 종합

```
Step 0 — 테스트 인프라 신설 (test project + TempDbFixture + TestHelpers + sln 등록)
   │  Gate: build + test 명령 둘 다 성공, 0 tests
   ▼
Step 1 — R1 dead code 제거 + sealed 제거 (SqliteConnectionFactory, SearchClickService)
   │  Gate: build 0 errors, 0 warnings
   ▼
Step 2 — R3 SettingsStore JSON 전환 + 3 tests
   │  Gate: build + test 3 green + 수동 마이그레이션 검증 7단계
   ▼
Step 3 — R5 N+1 제거 + 3 tests + golden master + benchmark
   │  Gate: build + test 6 green + 1-benchmark.md 작성
   ▼
Step 4 — R8 sub-batch 분할 + 3 tests (T9 W3 회귀 가드 포함)
   │  Gate: build + test 9 green
   ▼
최종: `dotnet build` + `dotnet test` 전체 통과, Phase 1 완료 보고
```

**Step 간 의존성**:
- Step 0 → 나머지 전부 (fixture 필요)
- Step 1 → Step 3/4 (sealed 제거 없으면 test 서브클래스 불가)
- Step 2 → 독립 (SettingsStore는 Step 1과 무관)
- Step 3/4 → Step 1 필요

**필수 순서**: Step 0 → Step 1 → (Step 2, Step 3, Step 4 중 임의 순서이나 문서 제안 순서 권장).

---

## 8. DB 마이그레이션 / 스키마 변경

**DB 스키마 변경 없음**. Phase 1 Medium 전체가 스키마 변경 없이 로직만 변경.

- SQLite `settings` 테이블은 **삭제하지 않음** (spec §4.6, §4.7) — 롤백 안전망
- 기존 `search_clicks` 테이블 구조 불변
- 기존 `files`, `files_fts`, `file_chunks`, `chunks_fts` 구조 불변
- `MigrationService.cs` **수정하지 않음** (M6 옵션 b 덕분)

**데이터 마이그레이션** (SettingsStore 전용):
- SQLite `settings.language` 행 → `settings.json`의 `language` 필드로 1회 복사
- 원본 행은 **보존**. 사용자가 settings.json 삭제 시 legacy 경로에서 재복구 가능

---

## 9. 인터페이스 / 모델 변경 사항

**인터페이스 변경 없음**:
- `ISettingsStore` — 6개 메서드 시그니처 100% 불변
- `IFileRepository`, `IChunkRepository`, `IEmbeddingRepository`, `IPipelineStampRepository`, `IMigrationService` — 변경 없음
- `IBm25Search`, `IDenseSearch`, `IHybridSearch` — 변경 없음

**공개 클래스 시그니처 변경**:
- `SqliteConnectionFactory`: `sealed` → (non-sealed), `IDisposable` 제거, `CreateConnection`에 `virtual` 추가
- `SearchClickService`: `sealed` → (non-sealed), `GetBoost(q, p)` **삭제**, `GetBoostBatch(q, paths)` **추가** (virtual)
- `SettingsStore`: 구현만 변경, public 시그니처 유지

**모델 변경 없음**: 모든 `Core.Models` 클래스 불변.

**영향 Agent**:
- **Core Agent**: `SqliteConnectionFactory`, `SettingsStore`, `FileRepository`
- **Search Agent**: `SearchClickService`, `Bm25SearchService`
- **Tests**: 신규 `LocalSynapse.Core.Tests` 프로젝트
- **UI/Pipeline/Mcp Agent**: **영향 없음** (시그니처 불변)

---

## 10. 검증 절차

### Step별 자동 검증
```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build LocalSynapse.v2.sln   # 모든 Step에서 필수
dotnet test LocalSynapse.v2.sln    # Step 0~4 각각에서
```

### Step 2 수동 검증 (1회)
spec §4.12의 7단계 절차. Ryan dev 머신에서 Step 2 commit 후 실행.

### Step 3 golden master 생성 (1회)
1. Step 3 시작 시점 commit에서 `GenerateGoldenMaster_Staging` 수동 실행
2. `.staging.json` → `.json` rename
3. commit에 포함

### Step 3 benchmark (1회)
1. Before 측정 (Step 3 시작 **직전** commit)
2. After 측정 (Step 3 완료 commit)
3. `Docs/plans/1-benchmark.md` 작성

### 최종 검증
```bash
# 전체 빌드 + 테스트
dotnet build LocalSynapse.v2.sln
dotnet test LocalSynapse.v2.sln

# 예상: 빌드 0 errors, 테스트 9 passed (T1~T9)
```

### Gate 검증 (CLAUDE.md)
- **Gate 1 Build**: 0 errors, warning count ≤ Phase 0 수준
- **Gate 2 Tests**: 9개 테스트 green, 0 failures
- **Gate 3 Impact Scope**: 5개 production 파일 + tests/ 신규 디렉토리. §8 금지 파일 수정 없음.
- **Gate 4 TDD**: Step 2/3/4 각 테스트는 red → 구현 → green 순서

---

## 11. 롤백 전략

각 Step이 독립 commit이므로 `git revert <step-commit>`로 단위 롤백 가능.

| Step | 롤백 영향 |
|------|---------|
| Step 0 | test project 제거. 다른 Step 영향 없음 |
| Step 1 | dead code 복원. 기능 영향 없음 (호출자 0) |
| Step 2 | SettingsStore 원본 복원. `settings.json`은 잔존하나 다음 실행에서 이전 SettingsStore가 사용하지 않으므로 무해 |
| Step 3 | N+1 복원. 성능 퇴보하나 기능 영향 없음 |
| Step 4 | 단일 tx 복원. 기능 영향 없음 |

**전체 롤백**: Phase 1 feature branch 사용 시 branch 삭제만으로 완전 롤백. main 직접 push 시 5개 commit을 순차 revert.

---

## 12. 위험 및 완화

| # | 위험 | 완화 |
|---|------|------|
| 1 | `xunit v2.9.3` fallback이 spec과 다름 (spec은 v3 3.2.2) | §0.1 F1에서 실측 검증 완료. spec §9 위험 #5의 fallback 조건 충족 |
| 2 | Step 0 `TempDbFixture`가 Step 1 이전에는 `Factory.Dispose()` 필요 | Step 0에서 fixture 사용 0이므로 무관. Step 1에서 `IDisposable` 제거 후 정상화 |
| 3 | Step 2 `SettingsStoreTests`가 fixture의 legacy SettingsStore와 새 SettingsStore 혼용 | 각 테스트가 시작 시 `jsonPath` 삭제로 격리 |
| 4 | Step 3 golden master 생성 시점이 "Step 3 시작 직전 commit"으로 애매 | `GenerateGoldenMaster_Staging`을 Step 3 test 파일에 포함 → Step 3 **첫 commit**에서 staging 파일 생성 후 promote → refactoring은 별도 후속 commit (Step 3가 2 commit 될 수 있음). spec은 "Step 3 1 commit"이지만 현실적으로 분할 허용 |
| 5 | Benchmark 측정이 "Step 3 시작 직전 commit" = Step 2 완료 commit에서 수행 필요 | Step 2 commit 후 수동 측정 → 수치 기록 → Step 3 refactoring → 측정 재실행 |
| 6 | `FileMetadata`의 required 필드가 `Id = ""`로 빈 문자열 허용 여부 미확인 | FileRepository.UpsertFile/UpsertFiles가 내부에서 `GenerateFileId(file.Path)`로 덮어씀 — 실측: [FileRepository.cs:29, L111](src/LocalSynapse.Core/Repositories/FileRepository.cs) 확인됨. **empty Id OK** |
| 7 | `CountingConnectionFactory` 생성 시 fixture의 기본 Factory와 경쟁 | 각 factory는 같은 `_dbPath`를 가리키나 connection은 독립. SQLite file lock + busy_timeout이 순차 처리. race 없음 |
| 8 | Step 3 T6 golden master가 없으면 테스트 fail | Generator를 Step 3 commit에 포함 → 사용자가 수동으로 staging → real 변환 → commit. spec §5.7 워크플로우 따름 |
| 9 | JSON settings 파일이 OS 동기화 폴더(OneDrive 등)에 있으면 File.Replace가 atomic 보장 안 함 | 기본 경로 `%LOCALAPPDATA%`는 OneDrive sync 범위 밖. 사용자가 dataFolder override 시 본인 책임 |
| 10 | Step 3 리팩토링 후 랭킹 경계 케이스가 golden master와 일치하지 않을 가능성 | path 배열 비교만 수행, score 수치 비교 아님. 순서만 같으면 pass |
| 11 | xUnit v2 fallback NuGet 승인 | ✅ **승인 완료** (2026-04-11). xunit 2.9.3 + xunit.runner.visualstudio 2.8.2 + Microsoft.NET.Test.Sdk 17.12.0 3개 패키지가 CLAUDE.md "Approved NuGet Packages" 테이블 L71-73에 추가됨. v3 회귀 방지 주석 포함. |

---

## 13. 리뷰 체크리스트 (diff-reviewer용)

1. Step 1에서 `SqliteConnectionFactory.Dispose()`의 외부 호출자 grep 결과가 0인가?
2. Step 2 SettingsStore의 `TryMigrateFromSqlite`가 raw connection을 쓰는데 read-only이므로 WAL/synchronous PRAGMA 부재가 문제없는가?
3. Step 3 `using (var conn = ...)` 블록이 reader materialization 전체를 감싸는가? reader가 dispose되기 전에 `GetBoostBatch`가 호출되지 않는가?
4. Step 3 T5 `CountingSearchClickService`의 `override GetBoostBatch`가 실제로 디스패치되는가? (M7 확정 — `virtual` 필요)
5. Step 4 T7 `CountingConnectionFactory`와 fixture의 Factory가 같은 DB를 공유하는데 file lock 경쟁 없는가?
6. `FileMetadata.Id = ""`가 `required` 속성 제약을 통과하는가? (grep으로 required 여부 재확인)
7. xUnit v2.9.3 + runner 2.8.2 + Test.Sdk 17.12.0 조합이 net8.0에서 실제로 동작함을 실측 검증했는가? (§0.1 F1에서 probe 완료)
8. Step 2 `_settings` 필드가 thread-safe하지 않은데 SettingsStore가 multi-thread 호출 시나리오가 있는가?
9. Step 3 benchmark가 solo dev 환경에서 의미 있는 수치를 낼 corpus 크기인가? (10개 파일 corpus로 충분한지)
10. R5 N+1 제거가 `IsWordBoundaryMatch` 루프 내부 필요한 필드를 모두 materialize tuple에 포함했는가?

---

## 14. 변경 이력

| 날짜 | 변경 |
|------|------|
| 2026-04-11 | 초안 작성. spec v2 기반. xUnit v2 fallback 실측 발동 (§0.1 F1). Step 0~4 상세화. Adversarial review 대기. |
| 2026-04-11 | **v2 — 1차 adversarial review 피드백 반영**. spec v3로 동기화. BLOCK 수정: C1 (`TempDbFixture.Dispose()` catch 빈 블록 → `Debug.WriteLine`), C2 (SeedSearchCorpus `required` 필드 누락 → spec/diff-plan 모두 통일), C3 (xUnit v2 fallback NuGet 사용자 명시 승인 획득 + CLAUDE.md 패키지 테이블 업데이트). MAJOR 수정: W3 (`UpsertFiles` `indexedAt` outer 메서드에서 1회 계산 후 파라미터 전달 + T9 회귀 가드 신설), W4 (golden master 워크플로우 `--filter` 오해 삭제, Skip 주석 처리만 유효 명시), W2 (`GetBoostBatch` paths.Count > 900 가드 + SQLITE_MAX_VARIABLE_NUMBER 주석). T3 교체 (`AtomicWrite_SurvivesPartialWriteSimulation` → `CorruptJson_IsBackedUp_AndMigrationRerun`) 사용자 승인 후 spec §4.10 동기화. T9 추가로 총 테스트 수 8 → 9. Step 4 diff +60 → +90. TestHelpers.cs `ExtractStatuses.Success` 상수 사용 (I1). CLAUDE.md에 "Spec Deviation in diff-plan" 워크플로우 룰 추가. |
| 2026-04-11 | **v3 — 2차 adversarial review 피드백 반영** (판정 CONDITIONAL PASS → 수정 후 PASS). W-NEW-1/W-NEW-2 spec §2.3 동기화 (C1/Factory.Dispose 제거/using 추가). W-NEW-3 §12 위험 #11 "승인 완료"로 갱신. W-NEW-4 `Bm25SearchServiceTests.cs` 불필요 using `LocalSynapse.Search.Interfaces` 제거 + `LocalSynapse.Search` 추가 (SearchOptions 접근). W-NEW-5 Step 3 2-commit 분할 옵션 + 단일 commit 권장 경로 §5.7/§5.8에 명시. W-NEW-6 T1 주석 개선 (fixture 상호작용 설명). I-NEW-2 spec §1 Step 4 diff "+90" 갱신. **최종 판정 PASS — execute 진입 가능**. |

---

## Adversarial Review

### 1차 리뷰 판정: BLOCK (2026-04-11)

#### Critical (BLOCK 사유)

- **[C1 BLOCK]** `TempDbFixture.Dispose()`가 빈 catch 블록 사용 — CLAUDE.md "every caught exception must be logged. Empty catch blocks are forbidden" 위반. spec §2.3도 동일 결함 (spec이 diff-plan에 버그 전파).
  → **수정 완료**: `Debug.WriteLine($"[TempDbFixture] Failed to delete {DataFolder}: {ex.Message}")` + swallow 근거 주석 추가 (§2.3).

- **[C2 BLOCK]** spec §5.7 `SeedSearchCorpus`가 `FileMetadata`의 `required` 필드 3개(`Id`, `IndexedAt`, `FolderPath`) 누락으로 컴파일 불가. diff-plan §5.3은 포함했지만 spec과 불일치.
  → **수정 완료**: spec §5.7과 diff-plan §5.3 모두 7개 required 필드 포함으로 통일. 실측 근거: [FileMetadata.cs:5-12](src/LocalSynapse.Core/Models/FileMetadata.cs#L5-L12).

- **[C3 BLOCK]** `xunit 2.9.3` + `xunit.runner.visualstudio 2.8.2` + `Microsoft.NET.Test.Sdk 17.12.0` 3개 패키지가 CLAUDE.md "Approved NuGet Packages" 테이블에 없음. "Do NOT add packages without approval" 위반. diff-plan §0.1 F1의 실측 결정은 자체 결정이며 사용자 명시 승인 아님.
  → **수정 완료**: 사용자 명시 승인 획득 (2026-04-11), CLAUDE.md 테이블에 3개 패키지 항목 추가 + v3 회귀 방지 주석 명시.

#### Major (수정 권장)

- **[W1 MINOR → INFO]** Step 0 시점의 `TempDbFixture`가 Step 1 이전에는 `SqliteConnectionFactory`가 `IDisposable`이라 미dispose 상태. Step 0에서 fixture 사용 테스트 0개이므로 실제 문제 발생 안 함. Step 1 commit 후 정상화. 주석으로 근거 명시.

- **[W2 MAJOR]** `GetBoostBatch`의 IN 절에 SQLite 변수 수 제한(SQLITE_MAX_VARIABLE_NUMBER=999) 고려 없음. 현재 LIMIT=240이라 안전하지만 미래 회귀에 brittle.
  → **수정 완료**: `paths.Count > 900` 시 명시적 `ArgumentException` throw + 상세 주석 (§5.1).

- **[W3 MAJOR]** `UpsertFilesSingleTransaction`에서 `indexedAt`을 매번 재계산 → 원본 동작(전체 batch 공통값)과 다른 회귀. 실측: [FileRepository.cs:62](src/LocalSynapse.Core/Repositories/FileRepository.cs#L62)가 메서드 진입 시점에 1회 계산 확인.
  → **수정 완료**: outer `UpsertFiles`에서 1회 계산 후 `UpsertFilesSingleTransaction(subBatch, indexedAt)`으로 파라미터 전달. **T9 회귀 가드** (`SELECT COUNT(DISTINCT indexed_at) FROM files == 1`) 신설 (§6.2). Step 4 테스트 수 2 → 3. spec §6.3/§6.5 동기화.

- **[W4 MAJOR]** Golden master 워크플로우가 `[Fact(Skip=...)]`을 `--filter`로 우회 가능하다고 잘못 기술. xUnit v2에서 Skip 속성은 `--filter`로도 스킵됨.
  → **수정 완료**: §5.4 워크플로우를 "Skip 속성 주석 처리만 유효"로 수정 + 10단계 절차 명시. spec §5.7도 동기화.

- **[Loop Workflow Violation]** diff-plan이 spec §4.10 T3 (`AtomicWrite_SurvivesPartialWriteSimulation`)를 사용자 보고 없이 `CorruptJson_IsBackedUp_AndMigrationRerun`로 단독 교체. CLAUDE.md "spec에 없는 기능 추가 금지. 발견 시 사용자에게 보고하고 spec 업데이트 요청" 위반.
  → **수정 완료**: 사용자 소급 승인 획득 → spec §4.10 새 T3로 교체 + spec 변경 이력에 승인 사유 명시. CLAUDE.md에 "Spec Deviation in diff-plan" 워크플로우 룰 신설하여 향후 재발 방지.

#### Info (참고)

- **[I1]** `ExtractStatus = "SUCCESS"` 하드코딩 → `ExtractStatuses.Success` 상수로 교체 (§2.4). 일관성 개선.
- **[I2]** `CopyToOutputDirectory` ItemGroup은 Step 3 진입 시 csproj 2차 수정 필요 — execute 담당자 기억 필요.
- **[I3]** Step 3 benchmark "Step 3 시작 직전 commit" 측정 시점이 "Before/After" 두 단계를 강제 — execute 단계에서 partial work commit 허용.

### 2차 리뷰 판정: CONDITIONAL PASS (2026-04-11)

**1차 BLOCK/MAJOR 항목 재검증**: C1/C2/C3/W2/W3/W4/Loop Workflow 모두 **PASS**.

**2차 리뷰에서 새로 발견된 결함** (모두 MINOR, spec/diff-plan 동기화 누락 또는 문서화 개선):

- **[W-NEW-1 MINOR]** spec §2.3 `TempDbFixture`가 C1 수정(빈 catch → Debug.WriteLine)과 IDisposable 제거를 반영하지 않음 — diff-plan과 불일치.
  → **수정 완료**: spec §2.3 `Dispose()` 메서드를 `Debug.WriteLine` + 주석으로 갱신, `Factory.Dispose()` 제거, `using System.Diagnostics; using LocalSynapse.Core.Repositories;` 추가.

- **[W-NEW-2 MINOR]** spec §2.3에 `using LocalSynapse.Core.Repositories;` 누락 (SettingsStore/MigrationService 접근용).
  → **수정 완료**: W-NEW-1과 동시 해결.

- **[W-NEW-3 MINOR]** diff-plan §12 위험 #11이 "사용자 승인 필요"로 남아있으나 실제로는 C3에서 승인 완료.
  → **수정 완료**: "✅ 승인 완료 (2026-04-11). CLAUDE.md L71-73"로 갱신.

- **[W-NEW-4 MINOR]** `Bm25SearchServiceTests.cs`에 `using LocalSynapse.Search.Interfaces;` 불필요 import. IDE0005 경고 가능성.
  → **수정 완료**: 제거 + `using LocalSynapse.Search;` 추가 (SearchOptions 접근용, `LocalSynapse.Search.Services`만으로는 부족함을 실측 확인).

- **[W-NEW-5 MINOR]** Step 3 Gate 2 절차 모호 — golden master 없이 `dotnet test` 실행 시 T6 실패 위험.
  → **수정 완료**: §5.7 Step 3 수락 기준에 2-commit 분할 옵션(3a/3b) 명시 + **단일 commit 권장 경로**(benchmark는 Step 2 완료 commit에서 Before 측정)를 §5.8에 추가. execute 담당자가 선택 가능.

- **[W-NEW-6 MINOR]** T1의 `File.Delete(jsonPath)` 로직이 주석 없이 fixture와 상호작용. false negative 가능성.
  → **수정 완료**: T1에 fixture의 SettingsStore가 이미 `settings.json`을 생성할 수 있음을 주석으로 설명.

- **[I-NEW-2 INFO]** spec §1 Step 4 diff "+80" vs diff-plan §1 "+90" 불일치 (T9 추가 반영 미완료).
  → **수정 완료**: spec §1 표를 "+90"으로 갱신.

### 2차 리뷰 후 최종 판정: **PASS**

모든 BLOCK, MAJOR, MINOR 결함 수정 완료. spec v3와 diff-plan v2가 동기화됨. Execute 진입 조건 충족:

- Gate 1/2/3/4 (CLAUDE.md) 기준 명확
- 5 steps (0~4) 각 독립 commit 가능
- 9개 테스트 (T1~T9) 정의 완료
- Golden master 워크플로우 명시 (`--filter` 오해 수정됨)
- W3 회귀 가드 (T9) 신설
- W2 SQLITE_MAX_VARIABLE_NUMBER 방어 추가
- NuGet 승인 완료 (CLAUDE.md L71-73)
- Loop Workflow Hard Rule 강화 (Spec Deviation in diff-plan 신설)
