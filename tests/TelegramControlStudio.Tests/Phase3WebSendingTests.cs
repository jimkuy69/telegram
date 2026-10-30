using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

[SupportedOSPlatform("windows")]
public sealed class TdLibSessionLockTests
{
    [Fact]
    public void SecondAcquireFromAnotherThreadFailsWhileHeldAndSucceedsAfterRelease()
    {
        const long accountId = 990_001;

        var first = TdLibSessionLock.TryAcquire(accountId, TimeSpan.Zero);
        Assert.NotNull(first);
        try
        {
            // A Mutex is recursive for its owning thread, and the thread pool may reuse that
            // very thread — so the contender must be a dedicated, distinct OS thread.
            TdLibSessionLock? contender = null;
            var thread = new Thread(() => { contender = TdLibSessionLock.TryAcquire(accountId, TimeSpan.Zero); });
            thread.Start();
            thread.Join();
            Assert.Null(contender);
        }
        finally
        {
            first!.Dispose();
        }

        var second = TdLibSessionLock.TryAcquire(accountId, TimeSpan.Zero);
        Assert.NotNull(second);
        second!.Dispose();
    }

    [Fact]
    public void MutexNameIsStablePerAccount()
    {
        Assert.Equal(TdLibSessionLock.MutexName(42), TdLibSessionLock.MutexName(42));
        Assert.NotEqual(TdLibSessionLock.MutexName(42), TdLibSessionLock.MutexName(43));
    }
}

public sealed class ResumeFloodWaitedJobsTests
{
    [Fact]
    public async Task ResumesOnlyDueFloodWaitedJobs()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await SeedAccountAsync(path);
            var jobs = new JobRepository(path);

            var due = await jobs.CreateAsync(1, "test", 1, "fw-due");
            var future = await jobs.CreateAsync(1, "test", 1, "fw-future");
            var paused = await jobs.CreateAsync(1, "test", 1, "paused");
            var waiting = await jobs.CreateAsync(1, "test", 1, "waiting");

            var now = DateTimeOffset.UtcNow;
            await jobs.UpdateStatusAsync(due, JobStatus.WaitingForFloodWait, "flood", now.AddMinutes(-1));
            await jobs.UpdateStatusAsync(future, JobStatus.WaitingForFloodWait, "flood", now.AddMinutes(30));
            await jobs.UpdateStatusAsync(paused, JobStatus.Paused, SendSafetyPolicy.KillSwitchReason);
            await jobs.UpdateStatusAsync(waiting, JobStatus.WaitingForConfirmation);

            var resumed = await jobs.ResumeFloodWaitedJobsAsync(1, now);

            Assert.Equal(1, resumed);
            Assert.Equal(JobStatus.Queued, (await jobs.GetAsync(due))!.Status);
            Assert.Equal(JobStatus.WaitingForFloodWait, (await jobs.GetAsync(future))!.Status);
            Assert.Equal(JobStatus.Paused, (await jobs.GetAsync(paused))!.Status);
            Assert.Equal(JobStatus.WaitingForConfirmation, (await jobs.GetAsync(waiting))!.Status);

            Assert.Equal(0, await jobs.ResumeFloodWaitedJobsAsync(1, now));
            Assert.Equal(1, await jobs.ResumeFloodWaitedJobsAsync(1, now.AddMinutes(31)));
            Assert.Equal(JobStatus.Queued, (await jobs.GetAsync(future))!.Status);
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    private static async Task SeedAccountAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO accounts(id,display_name,phone_number,status,created_at) VALUES(1,'test','+66000000000','Ready',$created_at);";
        command.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class WebSendingToggleTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("true ", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void ParsesEnabledSetting(string? value, bool expected) =>
        Assert.Equal(expected, WebSendSettings.IsEnabled(value));
}
