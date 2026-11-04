using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class GroupBroadcastQueueServiceTests
{
    [Fact]
    public async Task ConfirmedGroupBroadcastQueuesJobAndExcludesNonPostable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-grp-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Group", "+66000000444");
            var groups = new TelegramGroupRepository(path);
            await groups.UpsertAsync(Group(accountId, -1001, "Alpha", canSendMessages: true));
            await groups.UpsertAsync(Group(accountId, -1002, "Beta", canSendMessages: false));
            await groups.UpsertAsync(Group(accountId, -1003, "Gamma", canSendMessages: true));
            var selected = await groups.ListAsync(accountId);

            var result = await new GroupBroadcastQueueService(path).QueueAsync(
                accountId, selected, "ประกาศจากแอดมิน", "https://example.com/landing", true);

            Assert.Equal(2, result.Groups);
            Assert.Equal(1, result.NotPostableExcluded);
            var job = (await new JobRepository(path).GetAsync(result.JobId))!;
            Assert.Equal(JobTargetTypes.GroupBroadcast, job.TargetType);
            Assert.Equal(JobStatus.WaitingForConfirmation, job.Status);
            Assert.Equal("ประกาศจากแอดมิน", job.MessageText);
            Assert.Equal("https://example.com/landing", job.LinkUrl);
            var items = await new JobItemRepository(path).ListAsync(result.JobId);
            Assert.Equal(2, items.Count);
            Assert.All(items, item => Assert.Equal("Queued", item.Status));
            Assert.Contains(items, item => item.TargetId == -1001);
            Assert.DoesNotContain(items, item => item.TargetId == -1002);
            Assert.Contains(items, item => item.TargetId == -1003);
            Assert.Equal(2, items.Select(item => item.IdempotencyKey).Distinct().Count());
            Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.IdempotencyKey)));
            var logs = await new ActivityLogRepository(path).ListAsync(accountId);
            Assert.Contains(logs, log => log.EventType == "GroupBroadcastQueued" && log.JobId == result.JobId);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task GroupBroadcastDeduplicatesSelectedGroupsByChatId()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-grp-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Dup", "+66000000555");
            var duplicate = Group(accountId, -1001, "Alpha", canSendMessages: true);

            var result = await new GroupBroadcastQueueService(path).QueueAsync(
                accountId, [duplicate, duplicate], "Hello groups", null, true);

            Assert.Equal(1, result.Groups);
            var items = await new JobItemRepository(path).ListAsync(result.JobId);
            Assert.Single(items);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task UnconfirmedGroupBroadcastIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-grp-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("No", "+66000000666");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new GroupBroadcastQueueService(path).QueueAsync(accountId, [], "Hello", null, false));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task GroupBroadcastRejectsBlankMessageAndOnlyNonPostableSelection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-grp-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Blank", "+66000000777");
            await Assert.ThrowsAsync<ArgumentException>(() =>
                new GroupBroadcastQueueService(path).QueueAsync(accountId, [], "  ", null, true));
            var blocked = Group(accountId, -1009, "Locked", canSendMessages: false);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new GroupBroadcastQueueService(path).QueueAsync(accountId, [blocked], "Hello", null, true));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new GroupBroadcastQueueService(path).QueueAsync(accountId, [], "Hello", null, true));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static TelegramGroup Group(long accountId, long chatId, string title, bool canSendMessages) =>
        new(accountId, chatId, title, "Group", 10, true, canSendMessages, true, DateTimeOffset.UtcNow, "observed");
}
