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

    [Fact]
    public async Task ConsentRequestJobSendsOnlyToReachableNonConsentedContactsAndRecordsEvidence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-job-runner-consent-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await ConfigureSafetyAsync(path, testMode: false);
            var accountId = await new AccountRepository(path).CreateAsync("Consent", "+66000000108");
            await new ContactRepository(path).UpsertManyAsync([
                new Contact(accountId, 40, "New", "Number", null, "+66000000040", false, false, DateTimeOffset.UtcNow),
                new Contact(accountId, 41, "No", "Phone", null, null, false, false, DateTimeOffset.UtcNow),
                new Contact(accountId, 42, "Already", "Yes", null, "+66000000042", true, false, DateTimeOffset.UtcNow),
                new Contact(accountId, 43, "Said", "Stop", null, "+66000000043", false, true, DateTimeOffset.UtcNow)]);
            var jobId = await new JobRepository(path).CreateAsync(accountId, "ConsentRequest:7", 4, "runner-consent", "ขอความยินยอมได้ไหม");
            await new JobItemRepository(path).CreateAsync(new[] { 40L, 41L, 42L, 43L }.Select(userId =>
                new JobItem(jobId, accountId, userId, "Queued", 0, $"consent-item-{userId}", null, DateTimeOffset.UtcNow)));
            var client = new FakeClient("{\"@type\":\"message\",\"id\":987}");

            var result = await new TdLibCampaignJobRunner(path).RunAsync(client, jobId, accountId, true, TimeSpan.Zero);

            var items = await new JobItemRepository(path).ListAsync(jobId);
            var evidence = await new ConsentRequestRepository(path).ListAsync(accountId);
            Assert.Equal(JobStatus.Completed, result);
            Assert.Equal(1, client.CallCount);
            Assert.Equal("Completed", items.Single(item => item.TargetId == 40).Status);
            Assert.Equal(ConsentRequestPolicy.NoOwnNumberReason, items.Single(item => item.TargetId == 41).LastError);
            Assert.Equal(ConsentRequestPolicy.AlreadyConsentedReason, items.Single(item => item.TargetId == 42).LastError);
            Assert.Equal(ConsentRequestPolicy.OptedOutReason, items.Single(item => item.TargetId == 43).LastError);
            var request = Assert.Single(evidence);
            Assert.Equal(ConsentRequestKinds.Requested, request.Kind);
            Assert.Equal(40, request.TelegramUserId);
            Assert.Equal(987, request.MessageId);
            Assert.Equal(7, request.TemplateId);
            Assert.Equal(ConsentRequestSources.JobRunner, request.Source);
            Assert.False(string.IsNullOrWhiteSpace(request.BodySha256));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ConsentRequestJobSkipsContactsStillInsideTheCooldown()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-job-runner-consent-cooldown-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await ConfigureSafetyAsync(path, testMode: false);
            var accountId = await new AccountRepository(path).CreateAsync("Cooldown", "+66000000109");
            await new ContactRepository(path).UpsertManyAsync([
                new Contact(accountId, 44, "Asked", "Recently", null, "+66000000044", false, false, DateTimeOffset.UtcNow)]);
            await new ConsentRequestRepository(path).AppendAsync(new ConsentRequest(
                0, accountId, 44, ConsentRequestKinds.Requested, DateTimeOffset.UtcNow.AddDays(-5),
                null, null, null, "f".PadLeft(64, '0'), null, ConsentRequestSources.JobRunner));
            var jobId = await new JobRepository(path).CreateAsync(accountId, "ConsentRequest", 1, "runner-cooldown", "ขอความยินยอมได้ไหม");
            await new JobItemRepository(path).CreateAsync([
                new JobItem(jobId, accountId, 44, "Queued", 0, "consent-item-44", null, DateTimeOffset.UtcNow)]);
            var client = new FakeClient();

            var result = await new TdLibCampaignJobRunner(path).RunAsync(client, jobId, accountId, true, TimeSpan.Zero);

            var item = (await new JobItemRepository(path).ListAsync(jobId)).Single();
            Assert.Equal(JobStatus.Completed, result);
            Assert.Equal("Skipped", item.Status);
            Assert.Equal(ConsentRequestPolicy.CooldownReason, item.LastError);
            Assert.Equal(0, client.CallCount);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ConsentRequestJobStopsWhenTheDailyQuotaIsUsedUp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-job-runner-consent-quota-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await ConfigureSafetyAsync(path, testMode: false);
            await new ConsentRequestSettingsRepository(path).SetAsync(new ConsentRequestSettings(30, 1));
            var accountId = await new AccountRepository(path).CreateAsync("Quota", "+66000000110");
            await new ContactRepository(path).UpsertManyAsync([
                new Contact(accountId, 45, "First", "", null, "+66000000045", false, false, DateTimeOffset.UtcNow),
                new Contact(accountId, 46, "Second", "", null, "+66000000046", false, false, DateTimeOffset.UtcNow)]);
            var jobId = await new JobRepository(path).CreateAsync(accountId, "ConsentRequest", 2, "runner-quota", "ขอความยินยอมได้ไหม");
            await new JobItemRepository(path).CreateAsync(new[] { 45L, 46L }.Select(userId =>
                new JobItem(jobId, accountId, userId, "Queued", 0, $"consent-item-{userId}", null, DateTimeOffset.UtcNow)));
            var client = new FakeClient();

            var result = await new TdLibCampaignJobRunner(path).RunAsync(client, jobId, accountId, true, TimeSpan.Zero);

            var items = await new JobItemRepository(path).ListAsync(jobId);
            Assert.Equal(JobStatus.Completed, result);
            Assert.Equal(1, client.CallCount);
            Assert.Equal("Completed", items.Single(item => item.TargetId == 45).Status);
            Assert.Equal("Skipped", items.Single(item => item.TargetId == 46).Status);
            Assert.Equal(ConsentRequestPolicy.DailyLimitReason, items.Single(item => item.TargetId == 46).LastError);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ConsentRequestJobIsBlockedByTheKillSwitchLikeAnyOtherJob()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-job-runner-consent-kill-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await ConfigureSafetyAsync(path, testMode: false);
            await new SendSafetyRepository(path).SetKillSwitchAsync(true);
            var accountId = await new AccountRepository(path).CreateAsync("ConsentKill", "+66000000111");
            await new ContactRepository(path).UpsertManyAsync([
                new Contact(accountId, 47, "New", "", null, "+66000000047", false, false, DateTimeOffset.UtcNow)]);
            var jobId = await new JobRepository(path).CreateAsync(accountId, "ConsentRequest", 1, "runner-consent-kill", "ขอความยินยอมได้ไหม");
            await new JobItemRepository(path).CreateAsync([
                new JobItem(jobId, accountId, 47, "Queued", 0, "consent-item-47", null, DateTimeOffset.UtcNow)]);
            var client = new FakeClient();

            var result = await new TdLibCampaignJobRunner(path).RunAsync(client, jobId, accountId, true, TimeSpan.Zero);

            Assert.Equal(JobStatus.Paused, result);
            Assert.Equal(0, client.CallCount);
            Assert.Empty(await new ConsentRequestRepository(path).ListAsync(accountId));
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
