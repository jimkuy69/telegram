using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibContactSyncServiceTests
{
    [Fact]
    public async Task GetsContactIdsThenUsersAndPersistsReadOnlyContacts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-contact-sync-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Sync", "+66000000009");
            var client = new FakeClient(new Queue<string>([
                "{\"@type\":\"users\",\"user_ids\":[42,43,42]}",
                "{\"id\":42,\"first_name\":\"A\",\"last_name\":\"One\",\"usernames\":{\"editable_username\":\"alice\"},\"phone_number\":\"+66810000000\"}",
                "{\"id\":43,\"first_name\":\"B\",\"last_name\":\"Two\"}"
            ]));
            var contacts = await new TdLibContactSyncService(client, path).SyncAsync(accountId);

            Assert.Equal(2, contacts.Count);
            Assert.Equal(["getContacts", "getUser", "getUser"], client.Functions);
            var stored = await new ContactRepository(path).ListAsync(accountId);
            Assert.Equal(2, stored.Count);
            Assert.False(stored.Single(item => item.TelegramUserId == 42).HasConsent);
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class FakeClient(Queue<string> responses) : ITdLibClient
    {
        public List<string> Functions { get; } = [];
        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default) { Functions.Add(function); return Task.FromResult(responses.Dequeue()); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}