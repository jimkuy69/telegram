using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class QuickSendQueueServiceTests
{
    [Fact]
    public async Task ConfirmedQuickSendQueuesJobAndAssertsConsentForSelectedBatch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-quick-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Quick", "+66000000111");
            var contacts = new ContactRepository(path);
            await contacts.UpsertManyAsync([
                new Contact(accountId, 501, "A", "", "a_user", "+66000000001", false, false, DateTimeOffset.UtcNow),
                new Contact(accountId, 502, "B", "", null, "+66000000002", false, true, DateTimeOffset.UtcNow),
                new Contact(accountId, 503, "C", "", null, "+66000000003", true, false, DateTimeOffset.UtcNow)
            ]);
            var selected = await contacts.ListAsync(accountId);

            var result = await new QuickSendQueueService(path).QueueAsync(accountId, "สวัสดีครับ", null, selected, true);

            Assert.Equal(2, result.Recipients);
            Assert.Equal(1, result.ConsentAsserted);
            Assert.Equal(1, result.OptedOutExcluded);
            var items = await new JobItemRepository(path).ListAsync(result.JobId);
            Assert.Equal(2, items.Count);
            Assert.All(items, item => Assert.Equal("Queued", item.Status));
            Assert.DoesNotContain(items, item => item.TargetId == 502);
            var job = (await new JobRepository(path).GetAsync(result.JobId))!;
            Assert.Equal("สวัสดีครับ", job.MessageText);
            Assert.Equal(QuickSendQueueService.TargetType, job.TargetType);
            Assert.Equal(JobStatus.WaitingForConfirmation, job.Status);
            var stored = await contacts.ListAsync(accountId);
            Assert.True(stored.Single(item => item.TelegramUserId == 501).HasConsent);
            Assert.True(stored.Single(item => item.TelegramUserId == 502).IsOptedOut);
            Assert.DoesNotContain(stored, item => item.TelegramUserId == 502 && item.HasConsent);
            var logs = await new ActivityLogRepository(path).ListAsync(accountId);
            Assert.Contains(logs, log => log.EventType == "QuickSendQueued" && log.JobId == result.JobId);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task UnconfirmedQuickSendIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-quick-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Quick", "+66000000222");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new QuickSendQueueService(path).QueueAsync(accountId, "Hello", null, [], false));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task QuickSendRejectsBlankMessageAndOnlyOptedOutSelection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-quick-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Quick", "+66000000333");
            await Assert.ThrowsAsync<ArgumentException>(() =>
                new QuickSendQueueService(path).QueueAsync(accountId, "  ", null, [], true));
            var optedOut = new Contact(accountId, 601, "D", "", null, null, false, true, DateTimeOffset.UtcNow);
            await new ContactRepository(path).UpsertAsync(optedOut);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new QuickSendQueueService(path).QueueAsync(accountId, "Hello", null, [optedOut], true));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
