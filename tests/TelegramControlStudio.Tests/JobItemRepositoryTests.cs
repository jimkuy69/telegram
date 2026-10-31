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

    [Fact]
    public async Task CancelPendingCancelsQueuedAndWaitingItemsOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telegram-job-cancel-{Guid.NewGuid():N}.db");
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

            var jobId = await new JobRepository(path).CreateAsync(1, "ConsentMessaging", 3, Guid.NewGuid().ToString("N"));
            var repository = new JobItemRepository(path);
            await repository.CreateAsync(new[]
            {
                new JobItem(jobId, 1, 201, JobStatus.WaitingForConfirmation.ToString(), 0, Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow),
                new JobItem(jobId, 1, 202, JobStatus.Queued.ToString(), 0, Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow),
                new JobItem(jobId, 1, 203, JobStatus.Running.ToString(), 0, Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow)
            });

            Assert.Equal(2, await repository.CancelPendingAsync(jobId));

            var byTarget = (await repository.ListAsync(jobId)).ToDictionary(item => item.TargetId);
            Assert.Equal(JobStatus.Cancelled.ToString(), byTarget[201].Status);
            Assert.Equal(JobStatus.Cancelled.ToString(), byTarget[202].Status);
            Assert.Equal(JobStatus.Running.ToString(), byTarget[203].Status);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task CompletedTransitionStampsSentAtAndKeepsTheFirstOne()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telegram-job-sent-at-{Guid.NewGuid():N}.db");
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

            var jobId = await new JobRepository(path).CreateAsync(1, "Campaign:consented", 2, Guid.NewGuid().ToString("N"));
            var repository = new JobItemRepository(path);
            await repository.CreateAsync(new[]
            {
                new JobItem(jobId, 1, 401, JobStatus.Queued.ToString(), 0, Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow),
                new JobItem(jobId, 1, 402, JobStatus.Queued.ToString(), 0, Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow)
            });

            Assert.All(await repository.ListAsync(jobId), item => Assert.Null(item.SentAt));

            await repository.UpdateStatusAsync(jobId, 401, "Running");
            Assert.Null((await repository.ListAsync(jobId)).Single(item => item.TargetId == 401).SentAt);

            await repository.UpdateStatusAsync(jobId, 401, "Completed");
            await repository.UpdateStatusAsync(jobId, 402, "Skipped", "CONTACT_NOT_FOUND");
            var afterFirstSend = await repository.ListAsync(jobId);
            var sentAt = afterFirstSend.Single(item => item.TargetId == 401).SentAt;
            Assert.NotNull(sentAt);
            Assert.Null(afterFirstSend.Single(item => item.TargetId == 402).SentAt);

            await repository.UpdateStatusForJobAsync(jobId, "Completed", "Queued");
            await Task.Delay(25);
            await repository.UpdateStatusAsync(jobId, 401, "Completed");

            Assert.Equal(sentAt, (await repository.ListAsync(jobId)).Single(item => item.TargetId == 401).SentAt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
