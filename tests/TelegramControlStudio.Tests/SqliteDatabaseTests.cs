using Microsoft.Data.Sqlite;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class SqliteDatabaseTests
{
    [Fact]
    public async Task InitializeRecordsSchemaVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telegram-studio-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT MAX(version) FROM schema_migrations;";
                Assert.Equal(5L, (long)(await command.ExecuteScalarAsync() ?? 0L));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
                File.Delete(path);
        }
    }
    [Fact]
    public async Task InitializeCreatesRequiredTablesAndEnablesForeignKeys()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telegram-studio-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();

            await using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys;";
                Assert.Equal(1L, (long)(await pragma.ExecuteScalarAsync() ?? 0L));
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN (
                    'accounts', 'account_sessions', 'account_secrets', 'jobs',
                    'job_items', 'contacts', 'telegram_groups', 'group_members',
                    'activity_logs', 'message_templates', 'campaigns',
                    'campaign_recipients', 'media_files', 'invite_links',
                    'join_requests', 'template_media', 'consent_requests',
                    'app_users', 'invites', 'app_settings', 'app_secrets'
                  );
                """;

            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));

            Assert.Equal(21, tables.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task InitializeAddsSentAtToALegacyJobItemsTable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telegram-studio-{Guid.NewGuid():N}.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE job_items (
                        job_id TEXT NOT NULL,
                        account_id INTEGER NOT NULL,
                        target_id INTEGER NOT NULL,
                        status TEXT NOT NULL,
                        retry_count INTEGER NOT NULL DEFAULT 0,
                        idempotency_key TEXT NOT NULL UNIQUE,
                        last_error TEXT,
                        updated_at TEXT NOT NULL,
                        PRIMARY KEY(job_id, target_id));
                    """;
                await command.ExecuteNonQueryAsync();
            }

            await new SqliteDatabase(path).InitializeAsync();

            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var pragma = connection.CreateCommand();
                pragma.CommandText = "PRAGMA table_info(job_items);";
                var columns = new List<string>();
                await using var reader = await pragma.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    columns.Add(reader.GetString(1));
                Assert.Contains("sent_at", columns, StringComparer.OrdinalIgnoreCase);

                await using var version = connection.CreateCommand();
                version.CommandText = "SELECT MAX(version) FROM schema_migrations;";
                Assert.Equal(5L, (long)(await version.ExecuteScalarAsync() ?? 0L));

                await using var index = connection.CreateCommand();
                index.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_job_items_account_sent';";
                Assert.Equal(1L, Convert.ToInt64(await index.ExecuteScalarAsync() ?? 0L));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
