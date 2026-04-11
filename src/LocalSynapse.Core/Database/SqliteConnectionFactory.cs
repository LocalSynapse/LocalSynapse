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
/// NOTE: `sealed` removed intentionally to allow test subclassing
/// (see tests/LocalSynapse.Core.Tests/FileRepositoryTests for CountingConnectionFactory).
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
