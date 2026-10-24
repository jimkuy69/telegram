using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class JobRecoveryTests
{
    [Fact]
    public async Task RunningJobsArePausedForSafeStartupRecovery()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-recovery-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Recovery", "+66000000001");
            var jobs = new JobRepository(path);
            var jobId = await jobs.CreateAsync(accountId, "Message", 1, Guid.NewGuid().ToString("N"));
            await jobs.UpdateStatusAsync(jobId, JobStatus.Running);

            Assert.Equal(1, await jobs.PauseRunningJobsAsync(accountId));
            var job = (await jobs.ListAsync(accountId)).Single();
            Assert.Equal(JobStatus.Paused, job.Status);
            Assert.Equal("Paused during safe startup recovery", job.LastError);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExpiredFloodWaitJobsBecomeQueued()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-recovery-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Recovery", "+66000000002");
            var jobs = new JobRepository(path);
            var jobId = await jobs.CreateAsync(accountId, "Message", 1, Guid.NewGuid().ToString("N"));
            await jobs.UpdateStatusAsync(jobId, JobStatus.WaitingForFloodWait, floodWaitUntil: DateTimeOffset.UtcNow.AddMinutes(-1));

            Assert.Equal(1, await jobs.ResumeEligibleJobsAsync(accountId, DateTimeOffset.UtcNow));
            var job = (await jobs.ListAsync(accountId)).Single();
            Assert.Equal(JobStatus.Queued, job.Status);
            Assert.Null(job.FloodWaitUntil);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

