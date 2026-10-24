using Microsoft.Data.Sqlite;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibJoinRequestSyncHandlerTests
{
    [Fact]
    public async Task SyncsJoinRequestUpdateToSqlite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telegram-join-sync-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Sync Test", "+66000000000");
            var handler = new TdLibJoinRequestSyncHandler(path, accountId);
            Assert.True(await handler.HandleAsync("""{"@type":"updateChatJoinRequest","chat_id":-1001,"user_id":99}"""));
            Assert.False(await handler.HandleAsync("""{"@type":"updateUser"}"""));
            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT status FROM join_requests WHERE account_id = $account_id;";
            command.Parameters.AddWithValue("$account_id", accountId);
            Assert.Equal("Pending", await command.ExecuteScalarAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

