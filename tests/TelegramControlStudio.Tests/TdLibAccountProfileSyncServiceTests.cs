using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibAccountProfileSyncServiceTests
{
    [Fact]
    public async Task SyncsVerifiedProfileFromGetMe()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-profile-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accounts = new AccountRepository(path);
            var id = await accounts.CreateAsync("Pending", "+66000000001");
            var client = new FakeClient("{\"@type\":\"user\",\"first_name\":\"Ada\",\"last_name\":\"Lovelace\",\"phone_number\":\"+66000000002\"}");
            Assert.True(await new TdLibAccountProfileSyncService(client, accounts).SyncAsync(id));
            var saved = (await accounts.ListAsync()).Single();
            Assert.Equal("Ada Lovelace", saved.DisplayName);
            Assert.Equal("+66000000002", saved.PhoneNumber);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class FakeClient(string response) : ITdLibClient
    {
        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default) => Task.FromResult(response);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
