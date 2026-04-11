using Microsoft.Data.Sqlite;
using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.Core.Database;

/// <summary>
/// SQLite connection factory. Manages a SINGLE long-lived connection.
/// SQLite is single-writer; sharing one connection with WAL mode
/// prevents "database is locked" errors from concurrent access.
/// </summary>
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

        // WAL mode for concurrent reads + busy timeout for safety
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000; PRAGMA synchronous=NORMAL;";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Get the shared connection. Do NOT dispose this — it lives for the app's lifetime.
    /// For write operations, use ExecuteSerialized() to prevent concurrent writes.
    /// </summary>
    public SqliteConnection GetConnection() => _connection;

    /// <summary>
    /// Execute a write operation with exclusive lock.
    /// Prevents concurrent writes which cause "database is locked" in SQLite.
    /// </summary>
    public T ExecuteSerialized<T>(Func<SqliteConnection, T> action)
    {
        lock (_lock)
        {
            return action(_connection);
        }
    }

    /// <summary>
    /// Execute a void write operation with exclusive lock.
    /// </summary>
    public void ExecuteSerialized(Action<SqliteConnection> action)
    {
        lock (_lock)
        {
            action(_connection);
        }
    }

    /// <summary>
    /// Legacy compatibility: creates a new connection for cases that need it
    /// (e.g., long-running reads concurrent with writes).
    /// Caller is responsible for disposing.
    /// </summary>
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
