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

    /// <summary>UI 언어 코드를 반환한다 (기본값: "en").</summary>
    public string GetLanguage() => _settings.Language ?? "en";

    /// <summary>UI 언어 코드를 저장한다.</summary>
    public void SetLanguage(string cultureName)
    {
        _settings.Language = cultureName;
        WriteSettingsAtomic(_settings);
    }

    /// <summary>앱 데이터 폴더 경로를 반환한다.</summary>
    public string GetDataFolder() => _dataFolder;

    /// <summary>로그 폴더 경로를 반환한다.</summary>
    public string GetLogFolder()
    {
        var folder = Path.Combine(_dataFolder, "logs");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>ONNX 모델 폴더 경로를 반환한다.</summary>
    public string GetModelFolder()
    {
        var folder = Path.Combine(_dataFolder, "models");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>SQLite 데이터베이스 파일 경로를 반환한다.</summary>
    public string GetDatabasePath() => _dbPath;

    /// <summary>사용자 지정 스캔 루트 폴더를 반환한다. 미설정 시 null.</summary>
    public string[]? GetScanRoots()
    {
        var roots = _settings.ScanRoots;
        return roots is { Length: > 0 } ? roots : null;
    }

    /// <summary>스캔 루트 폴더를 저장한다. null이면 기본 동작으로 복귀.</summary>
    public void SetScanRoots(string[]? roots)
    {
        _settings.ScanRoots = roots is { Length: > 0 } ? roots : null;
        WriteSettingsAtomic(_settings);
    }

    /// <summary>인덱싱 성능 모드를 반환한다 (기본값: "Cruise").</summary>
    public string GetPerformanceMode()
        => _settings.IndexingPerformanceMode ?? "Cruise";

    /// <summary>인덱싱 성능 모드를 저장한다.</summary>
    public void SetPerformanceMode(string mode)
    {
        _settings.IndexingPerformanceMode = mode;
        WriteSettingsAtomic(_settings);
    }

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
                // Prevents legacy SQLite migration from silently overwriting
                // the user's most recent (but currently unreadable) preference.
                var corruptPath = _settingsPath + $".corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}";
                try
                {
                    File.Move(_settingsPath, corruptPath);
                }
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
            try
            {
                File.Delete(backupPath);
            }
            catch (Exception delEx)
            {
                // backup 삭제 실패는 비치명적 — 다음 write 시 덮어쓰기됨
                Debug.WriteLine($"[SettingsStore] Backup cleanup failed: {delEx.Message}");
            }
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
    public string[]? ScanRoots { get; set; }
    public string? IndexingPerformanceMode { get; set; }
}
