using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibContactSyncServiceTests
{
    [Fact]
    public void ParserAcceptsLegacyUsernameAndRejectsInvalidUser()
    {
        var contact = TdLibContactParser.ParseUser("{\"id\":77,\"first_name\":\"Legacy\",\"username\":\"legacy_user\"}", 9);
        Assert.NotNull(contact);
        Assert.Equal("legacy_user", contact!.Username);
        Assert.Null(TdLibContactParser.ParseUser("{\"id\":0}", 9));
        Assert.Empty(TdLibContactParser.ParseUserIds("{\"@type\":\"error\",\"code\":500}"));
    }

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
            Assert.Contains(await new ActivityLogRepository(path).ListAsync(accountId), log => log.EventType == "ContactsSyncedReadOnly" && log.Message.Contains("stored 2 contact(s)", StringComparison.Ordinal));
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task SkipsMalformedIndividualUserAndKeepsOtherContacts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-contact-sync-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Malformed", "+66000000010");
            var client = new FakeClient(new Queue<string>([
                "{\"user_ids\":[50,51]}",
                "not-json",
                "{\"id\":51,\"first_name\":\"Valid\"}"
            ]));

            var contacts = await new TdLibContactSyncService(client, path).SyncAsync(accountId);
            Assert.Single(contacts);
            Assert.Equal(51, contacts[0].TelegramUserId);
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