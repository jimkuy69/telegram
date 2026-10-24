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
            CREATE TABLE IF NOT EXISTS telegram_groups (account_id INTEGER NOT NULL, chat_id INTEGER NOT NULL, title TEXT NOT NULL, kind TEXT NOT NULL, member_count INTEGER, can_view_members INTEGER NOT NULL DEFAULT 0, can_send_messages INTEGER NOT NULL DEFAULT 0, can_invite_users INTEGER NOT NULL DEFAULT 0, last_synced_at TEXT, observed_note TEXT NOT NULL DEFAULT '', deleted_at TEXT, PRIMARY KEY(account_id, chat_id), FOREIGN KEY(account_id) REFERENCES accounts(id));
            CREATE INDEX IF NOT EXISTS ix_telegram_groups_account_kind ON telegram_groups(account_id, kind);
            CREATE TABLE IF NOT EXISTS activity_logs (id INTEGER PRIMARY KEY AUTOINCREMENT, account_id INTEGER NOT NULL, job_id TEXT, level TEXT NOT NULL, event_type TEXT NOT NULL, message TEXT NOT NULL, created_at TEXT NOT NULL, FOREIGN KEY(account_id) REFERENCES accounts(id));
            CREATE INDEX IF NOT EXISTS ix_activity_logs_account_created ON activity_logs(account_id, created_at);
            CREATE TABLE IF NOT EXISTS message_templates (id INTEGER PRIMARY KEY AUTOINCREMENT, account_id INTEGER NOT NULL, name TEXT NOT NULL, language TEXT NOT NULL, body TEXT NOT NULL, link TEXT, version INTEGER NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, updated_at TEXT NOT NULL, FOREIGN KEY(account_id) REFERENCES accounts(id));
            CREATE UNIQUE INDEX IF NOT EXISTS ux_message_templates_account_name_version ON message_templates(account_id, name, version);
            CREATE TABLE IF NOT EXISTS campaigns (id TEXT PRIMARY KEY, account_id INTEGER NOT NULL, template_id INTEGER NOT NULL, name TEXT NOT NULL, consent_policy TEXT NOT NULL, created_at TEXT NOT NULL, FOREIGN KEY(account_id) REFERENCES accounts(id), FOREIGN KEY(template_id) REFERENCES message_templates(id));
            CREATE TABLE IF NOT EXISTS campaign_recipients (campaign_id TEXT NOT NULL, account_id INTEGER NOT NULL, telegram_user_id INTEGER NOT NULL, status TEXT NOT NULL, retry_count INTEGER NOT NULL DEFAULT 0, idempotency_key TEXT NOT NULL UNIQUE, last_error TEXT, PRIMARY KEY(campaign_id, telegram_user_id), FOREIGN KEY(campaign_id) REFERENCES campaigns(id), FOREIGN KEY(account_id) REFERENCES accounts(id));
            CREATE INDEX IF NOT EXISTS ix_campaign_recipients_status ON campaign_recipients(campaign_id, status);
            CREATE TABLE IF NOT EXISTS media_files (id INTEGER PRIMARY KEY AUTOINCREMENT, account_id INTEGER NOT NULL, file_name TEXT NOT NULL, local_path TEXT NOT NULL, sha256 TEXT NOT NULL, mime_type TEXT NOT NULL, size_bytes INTEGER NOT NULL, created_at TEXT NOT NULL, deleted_at TEXT, FOREIGN KEY(account_id) REFERENCES accounts(id));
            CREATE UNIQUE INDEX IF NOT EXISTS ux_media_files_account_sha256 ON media_files(account_id, sha256);
            CREATE TABLE IF NOT EXISTS invite_links (id INTEGER PRIMARY KEY AUTOINCREMENT, account_id INTEGER NOT NULL, chat_id INTEGER NOT NULL, invite_url TEXT NOT NULL, expires_at TEXT, usage_limit INTEGER, creates_join_request INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL, revoked_at TEXT, FOREIGN KEY(account_id) REFERENCES accounts(id));
            CREATE UNIQUE INDEX IF NOT EXISTS ux_invite_links_account_url ON invite_links(account_id, invite_url);
            CREATE TABLE IF NOT EXISTS join_requests (id INTEGER PRIMARY KEY AUTOINCREMENT, account_id INTEGER NOT NULL, chat_id INTEGER NOT NULL, telegram_user_id INTEGER NOT NULL, status TEXT NOT NULL, requested_at TEXT NOT NULL, decision_at TEXT, decision_reason TEXT, FOREIGN KEY(account_id) REFERENCES accounts(id));
            CREATE UNIQUE INDEX IF NOT EXISTS ux_join_requests_account_chat_user ON join_requests(account_id, chat_id, telegram_user_id);
            CREATE TABLE IF NOT EXISTS template_media (template_id INTEGER NOT NULL, media_file_id INTEGER NOT NULL, account_id INTEGER NOT NULL, sort_order INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(template_id, media_file_id), FOREIGN KEY(template_id) REFERENCES message_templates(id), FOREIGN KEY(media_file_id) REFERENCES media_files(id), FOREIGN KEY(account_id) REFERENCES accounts(id));
            CREATE INDEX IF NOT EXISTS ix_template_media_order ON template_media(template_id, sort_order);
            INSERT OR IGNORE INTO schema_migrations(version, applied_at)
            VALUES(1, CURRENT_TIMESTAMP);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

