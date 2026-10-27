using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class JobItemRepositoryTests
{
    [Fact]
    public async Task CreatesListsAndTransitionsRecipientItems()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telegram-job-items-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO accounts(display_name,phone_number,status,created_at) VALUES('test','+66000000000','Ready',$created_at);";
                command.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync();
            }

            var jobId = await new JobRepository(path).CreateAsync(1, "ConsentMessaging", 2, Guid.NewGuid().ToString("N"));
            var repository = new JobItemRepository(path);
            var items = new[]
            {
                new JobItem(jobId, 1, 101, JobStatus.WaitingForConfirmation.ToString(), 0, Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow),
                new JobItem(jobId, 1, 102, JobStatus.WaitingForConfirmation.ToString(), 0, Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow)
            };

            await repository.CreateAsync(items);
            Assert.Equal(2, (await repository.ListAsync(jobId)).Count);
            Assert.Equal(2, await repository.UpdateStatusForJobAsync(
                jobId,
                JobStatus.WaitingForConfirmation.ToString(),
                JobStatus.Queued.ToString()));
            Assert.All(await repository.ListAsync(jobId), item => Assert.Equal(JobStatus.Queued.ToString(), item.Status));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
