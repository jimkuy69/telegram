using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibConsentCampaignServiceTests
{
    [Fact]
    public async Task SendsOnlyConsentedRecipientsAndStopsAfterFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-campaign-send-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Campaign", "+66000000101");
            var recipients = new[]
            {
                new Contact(accountId, 1, "A", "", null, null, true, false, DateTimeOffset.UtcNow),
                new Contact(accountId, 2, "B", "", null, null, false, false, DateTimeOffset.UtcNow),
                new Contact(accountId, 3, "C", "", null, null, true, true, DateTimeOffset.UtcNow)
            };
            var client = new FakeClient();
            var result = await new TdLibConsentCampaignService(path).SendTextAsync(client, accountId, recipients, "hello", true, TimeSpan.Zero);
            Assert.Equal(1, result.Sent);
            Assert.Equal(2, result.Skipped);
            Assert.Equal([1L], client.ChatIds);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class FakeClient : ITdLibClient
    {
        public List<long> ChatIds { get; } = [];
        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            ChatIds.Add((long)parameters["chat_id"]!);
            return Task.FromResult("{\"@type\":\"message\"}");
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

