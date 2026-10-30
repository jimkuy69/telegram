using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ConsentRequestQueueServiceTests
{
    [Fact]
    public async Task PlansSeparateReachableNonConsentedContactsFromTheRest()
    {
        var path = NewPath("plan");
        try
        {
            await Configure(path);
            var accountId = await new AccountRepository(path).CreateAsync("Plan", "+66000000201");
            await SeedContactsAsync(path, accountId);

            var plan = await Service(path).PlanAsync(accountId, [200, 201, 202, 203, 204]);

            Assert.Equal(new[] { 200L }, plan.Eligible.Select(candidate => candidate.TelegramUserId));
            var reasons = plan.Excluded.ToDictionary(candidate => candidate.TelegramUserId, candidate => candidate.ReasonCode);
            Assert.Equal(4, reasons.Count);
            Assert.Equal(ConsentRequestPolicy.NoOwnNumberReason, reasons[201]);
            Assert.Equal(ConsentRequestPolicy.AlreadyConsentedReason, reasons[202]);
            Assert.Equal(ConsentRequestPolicy.OptedOutReason, reasons[203]);
            Assert.Equal(ConsentRequestQueueService.UnknownContactReason, reasons[204]);
            Assert.Equal(ConsentRequestSettings.Default.DailyLimit, plan.DailyLimit);
            Assert.Equal(ConsentRequestSettings.Default.CooldownDays, plan.CooldownDays);
            Assert.False(plan.KillSwitchEngaged);
            Assert.Equal("Somchai Jaidee", plan.Eligible[0].DisplayName);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task PlansNeverPromiseMoreRequestsThanTheRemainingDailyQuota()
    {
        var path = NewPath("quota");
        try
        {
            await Configure(path);
            await new ConsentRequestSettingsRepository(path).SetAsync(new ConsentRequestSettings(30, 2));
            var accountId = await new AccountRepository(path).CreateAsync("Quota", "+66000000202");
            await new ContactRepository(path).UpsertManyAsync([
                NonConsented(accountId, 210), NonConsented(accountId, 211), NonConsented(accountId, 212)]);
            await new ConsentRequestRepository(path).AppendAsync(new ConsentRequest(
                0, accountId, 999, ConsentRequestKinds.Requested, DateTimeOffset.UtcNow.AddMinutes(-30),
                null, null, null, new string('a', 64), null, ConsentRequestSources.JobRunner));

            var plan = await Service(path).PlanAsync(accountId, [210, 211, 212]);

            Assert.Equal(1, plan.RequestedToday);
            Assert.Equal(1, plan.RemainingToday);
            Assert.Single(plan.Eligible);
            Assert.Equal(2, plan.Excluded.Count);
            Assert.All(plan.Excluded, candidate => Assert.Equal(ConsentRequestPolicy.DailyLimitReason, candidate.ReasonCode));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task QueuesOnlyEligibleContactsAndLeavesTheJobAwaitingConfirmation()
    {
        var path = NewPath("queue");
        try
        {
            await Configure(path);
            var accountId = await new AccountRepository(path).CreateAsync("Queue", "+66000000203");
            await SeedContactsAsync(path, accountId);

            var result = await Service(path).QueueAsync(accountId, [200, 201, 202], "ขอความยินยอม", 5, confirmed: true);

            Assert.True(result.Created);
            var job = (await new JobRepository(path).GetAsync(result.JobId!.Value))!;
            Assert.Equal("ConsentRequest:5", job.TargetType);
            Assert.Equal(JobStatus.WaitingForConfirmation, job.Status);
            Assert.Equal("ขอความยินยอม", job.MessageText);
            var items = await new JobItemRepository(path).ListAsync(job.Id);
            var item = Assert.Single(items);
            Assert.Equal(200, item.TargetId);
            Assert.Equal("WaitingForConfirmation", item.Status);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task QueueingTheSameSelectionTwiceOnTheSameDayReusesTheJob()
    {
        var path = NewPath("idempotent");
        try
        {
            await Configure(path);
            var accountId = await new AccountRepository(path).CreateAsync("Repeat", "+66000000204");
            await SeedContactsAsync(path, accountId);
            var service = Service(path);

            var first = await service.QueueAsync(accountId, [200], "ขอความยินยอม", null, confirmed: true);
            var second = await service.QueueAsync(accountId, [200], "ขอความยินยอม", null, confirmed: true);

            Assert.Equal(first.JobId, second.JobId);
            var job = (await new JobRepository(path).GetAsync(first.JobId!.Value))!;
            Assert.Equal("ConsentRequest", job.TargetType);
            Assert.Single(await new JobItemRepository(path).ListAsync(job.Id));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task CreatesNoJobWhenNobodyCanBeAsked()
    {
        var path = NewPath("empty");
        try
        {
            await Configure(path);
            var accountId = await new AccountRepository(path).CreateAsync("None", "+66000000205");
            await new ContactRepository(path).UpsertManyAsync([
                new Contact(accountId, 220, "Done", "", null, "+66000000220", true, false, DateTimeOffset.UtcNow)]);

            var result = await Service(path).QueueAsync(accountId, [220], "ขอความยินยอม", null, confirmed: true);

            Assert.False(result.Created);
            Assert.Null(result.JobId);
            Assert.Empty(await new JobRepository(path).ListAsync(accountId));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task KillSwitchExcludesEveryoneAndBlocksTheQueue()
    {
        var path = NewPath("killswitch");
        try
        {
            await Configure(path);
            await new SendSafetyRepository(path).SetKillSwitchAsync(true);
            var accountId = await new AccountRepository(path).CreateAsync("Kill", "+66000000206");
            await SeedContactsAsync(path, accountId);

            var result = await Service(path).QueueAsync(accountId, [200], "ขอความยินยอม", null, confirmed: true);

            Assert.True(result.Plan.KillSwitchEngaged);
            Assert.False(result.Created);
            Assert.Equal(SendSafetyPolicy.KillSwitchReason, result.Plan.Excluded.Single().ReasonCode);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task TestModeKeepsOptInRequestsInsideTheAllowlist()
    {
        var path = NewPath("testmode");
        try
        {
            await ConfigureTestMode(path, 200);
            var accountId = await new AccountRepository(path).CreateAsync("TestMode", "+66000000208");
            await SeedContactsAsync(path, accountId);

            var result = await Service(path).QueueAsync(accountId, [200, 205], "ขอความยินยอม", null, confirmed: true);

            Assert.True(result.Plan.TestModeEnabled);
            Assert.Equal(new[] { 200L }, result.Plan.Eligible.Select(candidate => candidate.TelegramUserId));
            Assert.Equal(SendSafetyPolicy.TestModeReason, result.Plan.Excluded.Single().ReasonCode);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task QueueingWithoutConfirmationIsRefused()
    {
        var path = NewPath("unconfirmed");
        try
        {
            await Configure(path);
            var accountId = await new AccountRepository(path).CreateAsync("Unconfirmed", "+66000000207");
            await SeedContactsAsync(path, accountId);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await Service(path).QueueAsync(accountId, [200], "ขอความยินยอม", null, confirmed: false));

            Assert.Equal("CONSENT_REQUEST_CONFIRMATION_REQUIRED", error.Message);
        }
        finally { Cleanup(path); }
    }

    private static string NewPath(string label) =>
        Path.Combine(Path.GetTempPath(), $"tg-consent-queue-{label}-{Guid.NewGuid():N}.db");

    private static ConsentRequestQueueService Service(string path) => new(path);

    private static async Task Configure(string path)
    {
        await new SqliteDatabase(path).InitializeAsync();
        await new SendSafetyRepository(path).SaveAsync(new SendSafetySettings(false, new HashSet<long>(), false, null));
    }

    private static async Task ConfigureTestMode(string path, params long[] allowlist)
    {
        await new SqliteDatabase(path).InitializeAsync();
        await new SendSafetyRepository(path).SaveAsync(new SendSafetySettings(true, allowlist.ToHashSet(), false, null));
    }

    private static Contact NonConsented(long accountId, long telegramUserId) =>
        new(accountId, telegramUserId, "Name", "", null, $"+66000000{telegramUserId}", false, false, DateTimeOffset.UtcNow);

    private static Task SeedContactsAsync(string path, long accountId) =>
        new ContactRepository(path).UpsertManyAsync([
            new Contact(accountId, 200, "Somchai", "Jaidee", null, "+66000000200", false, false, DateTimeOffset.UtcNow),
            new Contact(accountId, 201, "No", "Phone", null, null, false, false, DateTimeOffset.UtcNow),
            new Contact(accountId, 202, "Yes", "Already", null, "+66000000202", true, false, DateTimeOffset.UtcNow),
            new Contact(accountId, 203, "Stop", "Messaging", null, "+66000000203", false, true, DateTimeOffset.UtcNow),
            new Contact(accountId, 205, "Not", "Allowlisted", null, "+66000000205", false, false, DateTimeOffset.UtcNow)]);

    private static void Cleanup(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(path)) File.Delete(path);
    }
}
