using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibMessageSendServiceTests
{
    [Fact]
    public async Task SendsOnlyAfterConsentAndLogsWithoutMessageText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-message-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var client = new FakeClient("{\"@type\":\"message\",\"id\":1}");
            var accountId = await new AccountRepository(path).CreateAsync("Messaging", "+66000000099");
            var recipient = new Contact(accountId, 123, "A", "B", null, null, true, false, DateTimeOffset.UtcNow);
            var response = await new TdLibMessageSendService(path).SendTextAsync(client, recipient, "private message", true);
            Assert.Contains("message", response);
            Assert.Equal("sendMessage", client.Function);
            var logs = await new ActivityLogRepository(path).ListAsync(accountId);
            Assert.Contains(logs, item => item.EventType == "MessageSent" && !item.Message.Contains("private message", StringComparison.Ordinal));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class FakeClient(string response) : TelegramControlStudio.Core.Telegram.ITdLibClient
    {
        public string? Function { get; private set; }
        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default) { Function = function; return Task.FromResult(response); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

