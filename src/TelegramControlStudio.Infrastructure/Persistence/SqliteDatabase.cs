using Microsoft.Data.Sqlite;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class SqliteDatabase
{
    private readonly string _connectionString;

    public SqliteDatabase(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? throw new ArgumentException("Database path must include a directory.", nameof(databasePath)));
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS accounts (id INTEGER PRIMARY KEY AUTOINCREMENT, display_name TEXT NOT NULL, phone_number TEXT NOT NULL, status TEXT NOT NULL, created_at TEXT NOT NULL, last_sync_at TEXT);
            CREATE TABLE IF NOT EXISTS jobs (id TEXT PRIMARY KEY, account_id INTEGER NOT NULL, target_type TEXT NOT NULL, target_count INTEGER NOT NULL, status TEXT NOT NULL, retry_count INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL, started_at TEXT, completed_at TEXT, flood_wait_until TEXT, last_error TEXT, idempotency_key TEXT NOT NULL UNIQUE, FOREIGN KEY(account_id) REFERENCES accounts(id));
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
