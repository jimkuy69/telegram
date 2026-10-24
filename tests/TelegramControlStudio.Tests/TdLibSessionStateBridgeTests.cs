using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Core.Security;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibSessionStateBridgeTests
{
    [Fact]
    public async Task PersistsReadyStateWithoutCredentialData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telegram-bridge-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Test", "+66000000000");
            var machine = new AuthorizationStateMachine();
            var bridge = new TdLibSessionStateBridge(path, accountId, SessionPathPolicy.GetAccountSessionDirectory(accountId), machine);
            await bridge.ApplyAsync(AuthorizationState.Ready);
            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using var sessionCommand = connection.CreateCommand();
            sessionCommand.CommandText = "SELECT authorization_state, is_authorized FROM account_sessions WHERE account_id = $id;";
            sessionCommand.Parameters.AddWithValue("$id", accountId);
            await using var reader = await sessionCommand.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Ready", reader.GetString(0));
            Assert.Equal(1, reader.GetInt32(1));
            await using var accountCommand = connection.CreateCommand();
            accountCommand.CommandText = "SELECT status FROM accounts WHERE id = $id;";
            accountCommand.Parameters.AddWithValue("$id", accountId);
            Assert.Equal("Ready", await accountCommand.ExecuteScalarAsync());
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task RouterUpdatePersistsWaitCodeAndAttachIsIdempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telegram-bridge-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Test", "+66000000001");
            var machine = new AuthorizationStateMachine();
            var bridge = new TdLibSessionStateBridge(path, accountId, SessionPathPolicy.GetAccountSessionDirectory(accountId), machine);
            bridge.Attach(); bridge.Attach();
            new TdLibUpdateRouter(machine).Route("""{"@type":"updateAuthorizationState","authorization_state":{"@type":"authorizationStateWaitCode"}}""");
            await Task.Delay(100);
            var account = Assert.Single(await new AccountRepository(path).ListAsync());
            Assert.Equal("PendingAuthorization", account.Status);
            bridge.Detach(); bridge.Detach();
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}