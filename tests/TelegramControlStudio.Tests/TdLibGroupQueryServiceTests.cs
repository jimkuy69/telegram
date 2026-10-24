using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Telegram;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibGroupQueryServiceTests
{
    [Fact]
    public async Task UsesGetChatsThenGetChatAndPersistsObservedGroups()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"tg-groups-{Guid.NewGuid():N}.db");
        try
        {
            await new TelegramControlStudio.Infrastructure.Persistence.SqliteDatabase(databasePath).InitializeAsync();
            var accountId = await new TelegramControlStudio.Infrastructure.Persistence.AccountRepository(databasePath).CreateAsync("Test", "+66000000000");
            var client = new FakeClient(new Queue<string>([
                "{\"@type\":\"chats\",\"chat_ids\":[-10,20]}",
                "{\"id\":-10,\"title\":\"Team\",\"type\":{\"@type\":\"chatTypeSupergroup\"},\"member_count\":5}",
                "{\"id\":20,\"title\":\"Basic\",\"type\":{\"@type\":\"chatTypeBasicGroup\"}}"
            ]));
            var groups = await new TdLibGroupQueryService(client, databasePath).SyncMainChatsAsync(accountId);
            Assert.Equal(2, groups.Count); Assert.Equal(3, client.Functions.Count); Assert.Equal("getChats", client.Functions[0]); Assert.Equal("getChat", client.Functions[1]); Assert.Equal("getChat", client.Functions[2]);
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(databasePath)) File.Delete(databasePath); }
    }

    private sealed class FakeClient(Queue<string> responses) : ITdLibClient
    {
        public List<string> Functions { get; } = [];
        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default) { Functions.Add(function); return Task.FromResult(responses.Dequeue()); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

