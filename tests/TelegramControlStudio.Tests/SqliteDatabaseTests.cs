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
                Assert.Equal(3L, (long)(await command.ExecuteScalarAsync() ?? 0L));
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
                    'join_requests', 'template_media',
                    'app_users', 'invites', 'app_settings', 'app_secrets'
                  );
                """;

            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));

            Assert.Equal(20, tables.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
                File.Delete(path);
        }
    }

}
