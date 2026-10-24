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
                command.CommandText = "SELECT version FROM schema_migrations;";
                Assert.Equal(1L, (long)(await command.ExecuteScalarAsync() ?? 0L));
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
