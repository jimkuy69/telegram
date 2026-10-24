using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class JobRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task<Guid> CreateAsync(
        long accountId,
        string targetType,
        int targetCount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (accountId <= 0)
            throw new ArgumentOutOfRangeException(nameof(accountId));
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (targetCount < 0)
            throw new ArgumentOutOfRangeException(nameof(targetCount));

        var id = Guid.NewGuid();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO jobs
                (id, account_id, target_type, target_count, status, created_at, idempotency_key)
            VALUES
                ($id, $account_id, $target_type, $target_count, $status, $created_at, $idempotency_key);
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$target_type", targetType);
        command.Parameters.AddWithValue("$target_count", targetCount);
        command.Parameters.AddWithValue("$status", JobStatus.WaitingForConfirmation.ToString());
        command.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$idempotency_key", idempotencyKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    public async Task<bool> UpdateStatusAsync(
        Guid jobId,
        JobStatus status,
        string? lastError = null,
        DateTimeOffset? floodWaitUntil = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE jobs
            SET status = $status,
                last_error = $last_error,
                flood_wait_until = $flood_wait_until,
                started_at = CASE WHEN $status = 'Running' AND started_at IS NULL THEN $now ELSE started_at END,
                completed_at = CASE WHEN $status IN ('Completed', 'PartiallyCompleted', 'Failed', 'Cancelled') THEN $now ELSE completed_at END
            WHERE id = $id;
            """;
        var now = DateTimeOffset.UtcNow.ToString("O");
        command.Parameters.AddWithValue("$id", jobId.ToString());
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$last_error", (object?)lastError ?? DBNull.Value);
        command.Parameters.AddWithValue("$flood_wait_until", floodWaitUntil?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$now", now);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<Guid>> ListRecoverableAsync(
        long accountId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id
            FROM jobs
            WHERE account_id = $account_id
              AND status IN ('Running', 'WaitingForFloodWait')
            ORDER BY created_at;
            """;
        command.Parameters.AddWithValue("$account_id", accountId);

        var jobs = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (Guid.TryParse(reader.GetString(0), out var id))
                jobs.Add(id);
        return jobs;
    }

    public async Task<int> PauseRunningJobsAsync(
        long accountId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE jobs
            SET status = 'Paused',
                last_error = 'Paused during safe startup recovery'
            WHERE account_id = $account_id
              AND status = 'Running';
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> ResumeEligibleJobsAsync(
        long accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE jobs
            SET status = 'Queued', last_error = NULL, flood_wait_until = NULL
            WHERE account_id = $account_id
              AND (status = 'Paused' OR (status = 'WaitingForFloodWait' AND flood_wait_until IS NOT NULL AND flood_wait_until <= $now));
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> IncrementRetryAsync(
        Guid jobId,
        string error,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE jobs
            SET retry_count = retry_count + 1, last_error = $last_error, status = 'Queued'
            WHERE id = $id AND status NOT IN ('Completed', 'Cancelled', 'Failed');
            """;
        command.Parameters.AddWithValue("$id", jobId.ToString());
        command.Parameters.AddWithValue("$last_error", error);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}

