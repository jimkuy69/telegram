using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class JobItemRunnerTests
{
    [Fact]
    public async Task RunnerCompletesQueuedItems()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-runner-{Guid.NewGuid():N}.db");
        try
        {
            var (job, accountId) = await CreateJobAsync(path);
            var seen = 0;
            await new JobItemRunner(path).RunAsync(job, (_, _) => { seen++; return Task.CompletedTask; });
            var item = (await new JobItemRepository(path).ListAsync(job.Id)).Single();
            Assert.Equal(1, seen);
            Assert.Equal("Completed", item.Status);
            Assert.Equal(JobStatus.Completed, (await new JobRepository(path).ListAsync(accountId)).Single().Status);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task RunnerStopsAndPersistsFloodWait()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-runner-{Guid.NewGuid():N}.db");
        try
        {
            var (job, accountId) = await CreateJobAsync(path);
            await new JobItemRunner(path).RunAsync(job, (_, _) => throw new InvalidOperationException("FLOOD_WAIT_12"));
            var saved = (await new JobRepository(path).ListAsync(accountId)).Single();
            Assert.Equal(JobStatus.WaitingForFloodWait, saved.Status);
            Assert.Equal("Failed", (await new JobItemRepository(path).ListAsync(job.Id)).Single().Status);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task RetryableNetworkFailureReturnsItemToQueue()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-runner-{Guid.NewGuid():N}.db");
        try
        {
            var (job, accountId) = await CreateJobAsync(path);
            await new JobItemRunner(path).RunAsync(job, (_, _) => throw new InvalidOperationException("network timeout"));
            var savedJob = (await new JobRepository(path).ListAsync(accountId)).Single();
            var item = (await new JobItemRepository(path).ListAsync(job.Id)).Single();
            Assert.Equal(JobStatus.Queued, savedJob.Status);
            Assert.Equal("Queued", item.Status);
            Assert.Equal(1, item.RetryCount);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    private static async Task<(Job Job, long AccountId)> CreateJobAsync(string path)
    {
        await new SqliteDatabase(path).InitializeAsync();
        var accountId = await new AccountRepository(path).CreateAsync("Runner", "+66000000004");
        var jobs = new JobRepository(path);
        var jobId = await jobs.CreateAsync(accountId, "Message", 1, Guid.NewGuid().ToString("N"));
        await new JobItemRepository(path).CreateAsync([new JobItem(jobId, accountId, 123, "Queued", 0, Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow)]);
        return ((await jobs.ListAsync(accountId)).Single(), accountId);
    }
}

