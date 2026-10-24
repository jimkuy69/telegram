using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class JobFailureHandlerTests
{
    [Fact]
    public async Task FloodWaitIsPersistedAsWaitingState()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-failure-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Failure", "+66000000003");
            var jobs = new JobRepository(path);
            var id = await jobs.CreateAsync(accountId, "Message", 1, Guid.NewGuid().ToString("N"));
            await jobs.UpdateStatusAsync(id, JobStatus.Running);
            var job = (await jobs.ListAsync(accountId)).Single();

            var decision = await new JobFailureHandler(path).HandleAsync(job, JobFailureKind.FloodWait, "429: FLOOD_WAIT_42");
            var saved = (await jobs.ListAsync(accountId)).Single();
            Assert.Equal(JobStatus.WaitingForFloodWait, decision.NextStatus);
            Assert.Equal(JobStatus.WaitingForFloodWait, saved.Status);
            Assert.InRange(saved.FloodWaitUntil!.Value, DateTimeOffset.UtcNow.AddSeconds(40), DateTimeOffset.UtcNow.AddSeconds(45));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

