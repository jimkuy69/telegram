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
            CREATE TABLE IF NOT EXISTS account_sessions (account_id INTEGER PRIMARY KEY, session_directory TEXT NOT NULL, authorization_state TEXT NOT NULL, is_authorized INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL, FOREIGN KEY(account_id) REFERENCES accounts(id));
            CREATE TABLE IF NOT EXISTS account_secrets (account_id INTEGER NOT NULL, secret_name TEXT NOT NULL, protected_value BLOB NOT NULL, updated_at TEXT NOT NULL, PRIMARY KEY(account_id, secret_name), FOREIGN KEY(account_id) REFERENCES accounts(id));
            CREATE TABLE IF NOT EXISTS jobs (id TEXT PRIMARY KEY, account_id INTEGER NOT NULL, target_type TEXT NOT NULL, target_count INTEGER NOT NULL, status TEXT NOT NULL, retry_count INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL, started_at TEXT, completed_at TEXT, flood_wait_until TEXT, last_error TEXT, idempotency_key TEXT NOT NULL UNIQUE, FOREIGN KEY(account_id) REFERENCES accounts(id));
            CREATE TABLE IF NOT EXISTS contacts (account_id INTEGER NOT NULL, telegram_user_id INTEGER NOT NULL, first_name TEXT NOT NULL, last_name TEXT NOT NULL DEFAULT '', username TEXT, phone_number TEXT, has_consent INTEGER NOT NULL DEFAULT 0, is_opted_out INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL, deleted_at TEXT, PRIMARY KEY(account_id, telegram_user_id), FOREIGN KEY(account_id) REFERENCES accounts(id));
            CREATE INDEX IF NOT EXISTS ix_contacts_account_consent ON contacts(account_id, has_consent, is_opted_out);
            CREATE TABLE IF NOT EXISTS activity_logs (id INTEGER PRIMARY KEY AUTOINCREMENT, account_id INTEGER NOT NULL, job_id TEXT, level TEXT NOT NULL, event_type TEXT NOT NULL, message TEXT NOT NULL, created_at TEXT NOT NULL, FOREIGN KEY(account_id) REFERENCES accounts(id));
            CREATE INDEX IF NOT EXISTS ix_activity_logs_account_created ON activity_logs(account_id, created_at);
            INSERT OR IGNORE INTO schema_migrations(version, applied_at)
            VALUES(1, CURRENT_TIMESTAMP);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
