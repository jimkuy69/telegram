using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibCampaignJobRunnerTests
{
    [Fact]
    public async Task CompletesOnlyConsentedPendingItemsAndDoesNotRepeatCompletedItems()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-job-runner-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await ConfigureSafetyAsync(path, testMode: false);
            var accountId = await new AccountRepository(path).CreateAsync("Runner", "+66000000102");
            await new ContactRepository(path).UpsertManyAsync([
                new Contact(accountId, 10, "Allowed", "", null, null, true, false, DateTimeOffset.UtcNow),
                new Contact(accountId, 11, "Denied", "", null, null, false, false, DateTimeOffset.UtcNow)]);
            var jobId = await new JobRepository(path).CreateAsync(accountId, "Campaign:test", 2, "runner-job");
            await new JobItemRepository(path).CreateAsync([
                new JobItem(jobId, accountId, 10, "Queued", 0, "item-10", null, DateTimeOffset.UtcNow),
                new JobItem(jobId, accountId, 11, "Queued", 0, "item-11", null, DateTimeOffset.UtcNow)]);
            var result = await new TdLibCampaignJobRunner(path).RunAsync(new FakeClient(), jobId, accountId, "hello", true, TimeSpan.Zero);
            Assert.Equal(JobStatus.Completed, result);
            var items = await new JobItemRepository(path).ListAsync(jobId);
            Assert.Equal("Completed", items.Single(item => item.TargetId == 10).Status);
            Assert.Equal("Skipped", items.Single(item => item.TargetId == 11).Status);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task FloodWaitStopsTheJobAndPersistsWaitingState()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-job-runner-flood-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await ConfigureSafetyAsync(path, testMode: false);
            var accountId = await new AccountRepository(path).CreateAsync("Flood", "+66000000103");
            await new ContactRepository(path).UpsertManyAsync([
                new Contact(accountId, 20, "Allowed", "", null, null, true, false, DateTimeOffset.UtcNow)]);
            var jobId = await new JobRepository(path).CreateAsync(accountId, "Campaign:flood", 1, "runner-flood");
            await new JobItemRepository(path).CreateAsync([
                new JobItem(jobId, accountId, 20, "Queued", 0, "item-20", null, DateTimeOffset.UtcNow)]);
            var result = await new TdLibCampaignJobRunner(path).RunAsync(
                new FakeClient("{\"@type\":\"error\",\"name\":\"FLOOD_WAIT_30\"}"),
                jobId, accountId, "hello", true, TimeSpan.Zero);
            var saved = (await new JobRepository(path).ListAsync(accountId)).Single();
            var item = (await new JobItemRepository(path).ListAsync(jobId)).Single();
            Assert.Equal(JobStatus.WaitingForFloodWait, result);
            Assert.Equal(JobStatus.WaitingForFloodWait, saved.Status);
            Assert.True(saved.FloodWaitUntil > DateTimeOffset.UtcNow);
            Assert.Equal("WaitingForFloodWait", item.Status);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task SendFailureMarksPartialCompletionAndIncrementsRetry()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-job-runner-failure-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await ConfigureSafetyAsync(path, testMode: false);
            var accountId = await new AccountRepository(path).CreateAsync("Failure", "+66000000104");
            await new ContactRepository(path).UpsertManyAsync([
                new Contact(accountId, 21, "Allowed", "", null, null, true, false, DateTimeOffset.UtcNow)]);
            var jobId = await new JobRepository(path).CreateAsync(accountId, "Campaign:failure", 1, "runner-failure");
            await new JobItemRepository(path).CreateAsync([
                new JobItem(jobId, accountId, 21, "Queued", 0, "item-21", null, DateTimeOffset.UtcNow)]);
            var result = await new TdLibCampaignJobRunner(path).RunAsync(
                new FakeClient("{\"@type\":\"error\",\"name\":\"USER_IS_BLOCKED\"}"),
                jobId, accountId, "hello", true, TimeSpan.Zero);
            var saved = (await new JobItemRepository(path).ListAsync(jobId)).Single();
            Assert.Equal(JobStatus.PartiallyCompleted, result);
            Assert.Equal("Failed", saved.Status);
            Assert.Equal(1, saved.RetryCount);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task TestModeSendsOnlyAllowlistedRecipients()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-job-runner-testmode-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await ConfigureSafetyAsync(path, testMode: true, allowlist: 30);
            var accountId = await new AccountRepository(path).CreateAsync("TestMode", "+66000000105");
            await new ContactRepository(path).UpsertManyAsync([
                new Contact(accountId, 30, "Allowed", "", null, null, true, false, DateTimeOffset.UtcNow),
                new Contact(accountId, 31, "Blocked", "", null, null, true, false, DateTimeOffset.UtcNow)]);
            var jobId = await new JobRepository(path).CreateAsync(accountId, "Campaign:testmode", 2, "runner-testmode");
            await new JobItemRepository(path).CreateAsync([
                new JobItem(jobId, accountId, 30, "Queued", 0, "item-30", null, DateTimeOffset.UtcNow),
                new JobItem(jobId, accountId, 31, "Queued", 0, "item-31", null, DateTimeOffset.UtcNow)]);
            var client = new FakeClient();
            var result = await new TdLibCampaignJobRunner(path).RunAsync(client, jobId, accountId, "hello", true, TimeSpan.Zero);
            var items = await new JobItemRepository(path).ListAsync(jobId);
            Assert.Equal(JobStatus.Completed, result);
            Assert.Equal(1, client.CallCount);
            Assert.Equal("Completed", items.Single(item => item.TargetId == 30).Status);
            var skipped = items.Single(item => item.TargetId == 31);
            Assert.Equal("Skipped", skipped.Status);
            Assert.Equal(SendSafetyPolicy.TestModeReason, skipped.LastError);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task TestModePausesJobWhenNobodyIsAllowlisted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-job-runner-testmode-empty-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("TestModeEmpty", "+66000000106");
            await new ContactRepository(path).UpsertManyAsync([
                new Contact(accountId, 32, "Allowed", "", null, null, true, false, DateTimeOffset.UtcNow)]);
            var jobId = await new JobRepository(path).CreateAsync(accountId, "Campaign:testmode-empty", 1, "runner-testmode-empty");
            await new JobItemRepository(path).CreateAsync([
                new JobItem(jobId, accountId, 32, "Queued", 0, "item-32", null, DateTimeOffset.UtcNow)]);
            var client = new FakeClient();
            var result = await new TdLibCampaignJobRunner(path).RunAsync(client, jobId, accountId, "hello", true, TimeSpan.Zero);
            var saved = (await new JobRepository(path).GetAsync(jobId))!;
            var item = (await new JobItemRepository(path).ListAsync(jobId)).Single();
            Assert.Equal(JobStatus.Paused, result);
            Assert.Equal(JobStatus.Paused, saved.Status);
            Assert.Equal(SendSafetyPolicy.TestModeReason, saved.LastError);
            Assert.Equal("Skipped", item.Status);
            Assert.Equal(0, client.CallCount);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task KillSwitchPausesJobWithoutSending()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-job-runner-killswitch-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await ConfigureSafetyAsync(path, testMode: false);
            await new SendSafetyRepository(path).SetKillSwitchAsync(true);
            var accountId = await new AccountRepository(path).CreateAsync("KillSwitch", "+66000000107");
            await new ContactRepository(path).UpsertManyAsync([
                new Contact(accountId, 33, "Allowed", "", null, null, true, false, DateTimeOffset.UtcNow)]);
            var jobId = await new JobRepository(path).CreateAsync(accountId, "Campaign:killswitch", 1, "runner-killswitch");
            await new JobItemRepository(path).CreateAsync([
                new JobItem(jobId, accountId, 33, "Queued", 0, "item-33", null, DateTimeOffset.UtcNow)]);
            var client = new FakeClient();
            var result = await new TdLibCampaignJobRunner(path).RunAsync(client, jobId, accountId, "hello", true, TimeSpan.Zero);
            var saved = (await new JobRepository(path).GetAsync(jobId))!;
            var item = (await new JobItemRepository(path).ListAsync(jobId)).Single();
            Assert.Equal(JobStatus.Paused, result);
            Assert.Equal(JobStatus.Paused, saved.Status);
            Assert.Equal(SendSafetyPolicy.KillSwitchReason, saved.LastError);
            Assert.Equal("Queued", item.Status);
            Assert.Equal(0, client.CallCount);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    private static Task ConfigureSafetyAsync(string path, bool testMode, params long[] allowlist) =>
        new SendSafetyRepository(path).SaveAsync(new SendSafetySettings(testMode, allowlist.ToHashSet(), false, null));

    private sealed class FakeClient(string response = "{\"@type\":\"message\"}") : ITdLibClient
    {
        public int CallCount { get; private set; }
        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(response);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
