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

    [Fact]
    public async Task ConvertsTdlibFloodWaitErrorToTypedExceptionAndRedactedLog()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-flood-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var client = new FakeClient("{\"@type\":\"error\",\"code\":429,\"message\":\"FLOOD_WAIT_42\"}");
            var accountId = await new AccountRepository(path).CreateAsync("Flood", "+66000000098");
            var recipient = new Contact(accountId, 456, "A", "B", null, null, true, false, DateTimeOffset.UtcNow);

            var exception = await Assert.ThrowsAsync<TdLibFloodWaitException>(() =>
                new TdLibMessageSendService(path).SendTextAsync(client, recipient, "private message", true));

            Assert.Equal(42, exception.Seconds);
            var logs = await new ActivityLogRepository(path).ListAsync(accountId);
            Assert.Contains(logs, item => item.EventType == "FloodWait" && !item.Message.Contains("private message", StringComparison.Ordinal));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task DetectsFormattedTdlibErrorJson()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-formatted-error-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var client = new FakeClient("{\n  \"@type\": \"error\",\n  \"code\": 400,\n  \"message\": \"BAD_REQUEST\"\n}");
            var accountId = await new AccountRepository(path).CreateAsync("Formatted", "+66000000097");
            var recipient = new Contact(accountId, 789, "A", "B", null, null, true, false, DateTimeOffset.UtcNow);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new TdLibMessageSendService(path).SendTextAsync(client, recipient, "private message", true));
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
