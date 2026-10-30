using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class CampaignAutoRunnerTests
{
    [Fact]
    public async Task RunOnceSendsAConfirmedQueuedJobWithoutAnotherClick()
    {
        var scenario = await AutoRunScenario.CreateAsync(testMode: false);
        try
        {
            Assert.Equal(1, await scenario.Runner.RunOnceAsync());
            Assert.Equal(JobStatus.Completed, (await scenario.Jobs.GetAsync(scenario.JobId))!.Status);
            Assert.Equal("Completed", (await new JobItemRepository(scenario.DatabasePath).ListAsync(scenario.JobId)).Single().Status);
            Assert.Equal(1, scenario.Client.CallCount);
            Assert.Equal(0, scenario.Gate.Count);
        }
        finally { scenario.Cleanup(); }
    }

    [Fact]
    public async Task RunOnceResumesFloodWaitedJobOnlyAfterTheCooldown()
    {
        var scenario = await AutoRunScenario.CreateAsync(testMode: false);
        try
        {
            await scenario.Jobs.UpdateStatusAsync(scenario.JobId, JobStatus.WaitingForFloodWait, "FLOOD_WAIT_60", DateTimeOffset.UtcNow.AddMinutes(2));
            Assert.Equal(0, await scenario.Runner.RunOnceAsync());
            Assert.Equal(JobStatus.WaitingForFloodWait, (await scenario.Jobs.GetAsync(scenario.JobId))!.Status);
            Assert.Equal(0, scenario.Client.CallCount);

            await scenario.Jobs.UpdateStatusAsync(scenario.JobId, JobStatus.WaitingForFloodWait, "FLOOD_WAIT_60", DateTimeOffset.UtcNow.AddSeconds(-1));
            Assert.Equal(1, await scenario.Runner.RunOnceAsync());
            Assert.Equal(JobStatus.Completed, (await scenario.Jobs.GetAsync(scenario.JobId))!.Status);
        }
        finally { scenario.Cleanup(); }
    }

    [Fact]
    public async Task RunOnceLeavesPausedJobAlone()
    {
        var scenario = await AutoRunScenario.CreateAsync(testMode: false);
        try
        {
            await scenario.Jobs.UpdateStatusAsync(scenario.JobId, JobStatus.Paused, "Paused by user.");
            Assert.Equal(0, await scenario.Runner.RunOnceAsync());
            Assert.Equal(JobStatus.Paused, (await scenario.Jobs.GetAsync(scenario.JobId))!.Status);
            Assert.Equal(0, scenario.Client.CallCount);
        }
        finally { scenario.Cleanup(); }
    }

    [Fact]
    public async Task RunOnceDoesNothingWhileTheKillSwitchIsEngaged()
    {
        var scenario = await AutoRunScenario.CreateAsync(testMode: false);
        try
        {
            await new SendSafetyRepository(scenario.DatabasePath).SetKillSwitchAsync(true);
            Assert.Equal(0, await scenario.Runner.RunOnceAsync());
            Assert.Equal(JobStatus.Queued, (await scenario.Jobs.GetAsync(scenario.JobId))!.Status);
            Assert.Equal(0, scenario.Client.CallCount);
        }
        finally { scenario.Cleanup(); }
    }

    [Fact]
    public async Task RunOnceSkipsAJobAManualWindowIsAlreadyDriving()
    {
        var scenario = await AutoRunScenario.CreateAsync(testMode: false);
        try
        {
            Assert.True(scenario.Gate.TryBegin(scenario.JobId));
            Assert.Equal(0, await scenario.Runner.RunOnceAsync());
            Assert.Equal(JobStatus.Queued, (await scenario.Jobs.GetAsync(scenario.JobId))!.Status);
            Assert.Equal(0, scenario.Client.CallCount);
        }
        finally { scenario.Cleanup(); }
    }

    [Fact]
    public async Task TestModeStillGatesAutoRunsToTheAllowlist()
    {
        var scenario = await AutoRunScenario.CreateAsync(testMode: true);
        try
        {
            Assert.Equal(1, await scenario.Runner.RunOnceAsync());
            Assert.Equal(0, scenario.Client.CallCount);
            Assert.Equal(JobStatus.Paused, (await scenario.Jobs.GetAsync(scenario.JobId))!.Status);
            var item = (await new JobItemRepository(scenario.DatabasePath).ListAsync(scenario.JobId)).Single();
            Assert.Equal("Skipped", item.Status);
            Assert.Equal(SendSafetyPolicy.TestModeReason, item.LastError);
        }
        finally { scenario.Cleanup(); }
    }

    [Fact]
    public async Task TryClaimRejectsAJobThatIsAlreadyRunning()
    {
        var scenario = await AutoRunScenario.CreateAsync(testMode: false);
        try
        {
            Assert.True(await scenario.Jobs.TryClaimAsync(scenario.JobId, DateTimeOffset.UtcNow));
            Assert.False(await scenario.Jobs.TryClaimAsync(scenario.JobId, DateTimeOffset.UtcNow));
        }
        finally { scenario.Cleanup(); }
    }

    [Fact]
    public async Task PauseRaisedMidRunStopsTheRunnerBeforeTheNextRecipient()
    {
        var scenario = await AutoRunScenario.CreateAsync(testMode: false, recipients: new[] { 500L, 501L });
        try
        {
            var pausingClient = new PausingClient(scenario.DatabasePath, scenario.JobId);
            await scenario.Registry.RemoveAsync(scenario.AccountId);
            scenario.Registry.Register(scenario.AccountId, pausingClient);

            Assert.Equal(1, await scenario.Runner.RunOnceAsync());
            Assert.Equal(1, pausingClient.CallCount);
            Assert.Equal(JobStatus.Paused, (await scenario.Jobs.GetAsync(scenario.JobId))!.Status);
            var items = await new JobItemRepository(scenario.DatabasePath).ListAsync(scenario.JobId);
            Assert.Single(items, item => item.Status == "Completed");
            Assert.Single(items, item => item.Status == "Queued");
        }
        finally { scenario.Cleanup(); }
    }

    private sealed record AutoRunScenario(
        string DatabasePath,
        long AccountId,
        Guid JobId,
        TdLibAccountClientRegistry Registry,
        JobInFlightGate Gate,
        FakeClient Client,
        JobRepository Jobs,
        CampaignAutoRunner Runner)
    {
        public static async Task<AutoRunScenario> CreateAsync(bool testMode, long[]? recipients = null)
        {
            var targets = recipients ?? new[] { 500L };
            var databasePath = Path.Combine(Path.GetTempPath(), $"tg-auto-{Guid.NewGuid():N}.db");
            await new SqliteDatabase(databasePath).InitializeAsync();
            await new SendSafetyRepository(databasePath).SaveAsync(new SendSafetySettings(
                testMode, new HashSet<long>(), false, null));
            var accountId = await new AccountRepository(databasePath).CreateAsync("Auto", "+66000000201");
            await new ContactRepository(databasePath).UpsertManyAsync(targets.Select(target =>
                new Contact(accountId, target, "Consented", "", null, null, true, false, DateTimeOffset.UtcNow)));
            var jobs = new JobRepository(databasePath);
            var jobId = await jobs.CreateAsync(accountId, "ConsentMessaging", targets.Length, $"auto-{Guid.NewGuid():N}", "สวัสดีครับ", null);
            await new JobItemRepository(databasePath).CreateAsync(targets.Select(target =>
                new JobItem(jobId, accountId, target, "Queued", 0, $"item-{jobId:N}-{target}", null, DateTimeOffset.UtcNow)));
            await jobs.UpdateStatusAsync(jobId, JobStatus.Queued);

            var client = new FakeClient();
            var registry = new TdLibAccountClientRegistry();
            registry.Register(accountId, client);
            var gate = new JobInFlightGate();
            var runner = new CampaignAutoRunner(databasePath, registry, gate, TimeSpan.Zero);
            return new AutoRunScenario(databasePath, accountId, jobId, registry, gate, client, jobs, runner);
        }

        public void Cleanup()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(DatabasePath)) File.Delete(DatabasePath);
        }
    }

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

    /// <summary>Stands in for the queue window pressing Pause after the first transmission.</summary>
    private sealed class PausingClient(string databasePath, Guid jobId) : ITdLibClient
    {
        public int CallCount { get; private set; }

        public async Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            CallCount++;
            await new JobRepository(databasePath).UpdateStatusAsync(jobId, JobStatus.Paused, "Paused by user.");
            return "{\"@type\":\"message\"}";
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
