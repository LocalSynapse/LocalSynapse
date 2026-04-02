using Microsoft.Data.Sqlite;
using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.Core.Repositories;

/// <summary>
/// 앱 설정 저장소. 파일 시스템 경로 + SQLite key-value 저장.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private readonly string _dataFolder;
    private readonly string _dbPath;
    private string? _connectionString;

    /// <summary>기본 데이터 폴더(%LOCALAPPDATA%/LocalSynapse)를 사용하는 생성자.</summary>
    public SettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalSynapse"))
    {
    }

    /// <summary>지정된 데이터 폴더를 사용하는 생성자.</summary>
    public SettingsStore(string dataFolder)
    {
        _dataFolder = dataFolder;
        _dbPath = Path.Combine(_dataFolder, "localsynapse.db");
        Directory.CreateDirectory(_dataFolder);
    }

    /// <summary>UI 언어 코드를 반환한다 (기본값: "en").</summary>
    public string GetLanguage()
    {
        return GetSetting("language") ?? "en";
    }

    /// <summary>UI 언어 코드를 저장한다.</summary>
    public void SetLanguage(string cultureName)
    {
        SetSetting("language", cultureName);
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

    private string? GetSetting(string key)
    {
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE key = $key";
            cmd.Parameters.AddWithValue("$key", key);
            return cmd.ExecuteScalar() as string;
        }
        catch (SqliteException)
        {
            // settings 테이블이 아직 없을 수 있음 (MigrationService 실행 전)
            return null;
        }
    }

    private void SetSetting(string key, string value)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        _connectionString ??= $"Data Source={_dbPath}";
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
