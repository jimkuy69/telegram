using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class JobItemRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true }.ToString();
    public async Task CreateAsync(IEnumerable<JobItem> items, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var item in items)
        {
            await using var command = connection.CreateCommand(); command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "INSERT INTO job_items(job_id,account_id,target_id,status,retry_count,idempotency_key,last_error,updated_at) VALUES($job_id,$account_id,$target_id,$status,$retry_count,$key,$error,$updated_at)";
            command.Parameters.AddWithValue("$job_id", item.JobId.ToString("D")); command.Parameters.AddWithValue("$account_id", item.AccountId); command.Parameters.AddWithValue("$target_id", item.TargetId); command.Parameters.AddWithValue("$status", item.Status); command.Parameters.AddWithValue("$retry_count", item.RetryCount); command.Parameters.AddWithValue("$key", item.IdempotencyKey); command.Parameters.AddWithValue("$error", (object?)item.LastError ?? DBNull.Value); command.Parameters.AddWithValue("$updated_at", item.UpdatedAt.ToString("O")); await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
    public async Task<int> UpdateStatusForJobAsync(Guid jobId, string fromStatus, string toStatus, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromStatus);
        ArgumentException.ThrowIfNullOrWhiteSpace(toStatus);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE job_items SET status=$to_status, updated_at=$updated_at WHERE job_id=$job_id AND status=$from_status";
        command.Parameters.AddWithValue("$to_status", toStatus);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$job_id", jobId.ToString("D"));
        command.Parameters.AddWithValue("$from_status", fromStatus);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CancelPendingAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE job_items SET status=$cancelled, updated_at=$updated_at WHERE job_id=$job_id AND status IN ($queued, $waiting)";
        command.Parameters.AddWithValue("$cancelled", JobStatus.Cancelled.ToString());
        command.Parameters.AddWithValue("$queued", JobStatus.Queued.ToString());
        command.Parameters.AddWithValue("$waiting", JobStatus.WaitingForConfirmation.ToString());
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$job_id", jobId.ToString("D"));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<JobItem>> ListAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "SELECT job_id,account_id,target_id,status,retry_count,idempotency_key,last_error,updated_at FROM job_items WHERE job_id=$job_id ORDER BY target_id"; command.Parameters.AddWithValue("$job_id", jobId.ToString("D"));
        var result = new List<JobItem>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) result.Add(new JobItem(Guid.Parse(reader.GetString(0)), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3), reader.GetInt32(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), DateTimeOffset.Parse(reader.GetString(7)))); return result;
    }

    public async Task<bool> UpdateStatusAsync(Guid jobId, long targetId, string status, string? lastError = null, bool incrementRetry = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE job_items SET status=$status, retry_count=retry_count + $increment, last_error=$error, updated_at=$updated_at WHERE job_id=$job_id AND target_id=$target_id AND status NOT IN ('Completed','Cancelled')";
        command.Parameters.AddWithValue("$status", status); command.Parameters.AddWithValue("$increment", incrementRetry ? 1 : 0); command.Parameters.AddWithValue("$error", (object?)lastError ?? DBNull.Value); command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O")); command.Parameters.AddWithValue("$job_id", jobId.ToString("D")); command.Parameters.AddWithValue("$target_id", targetId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}

