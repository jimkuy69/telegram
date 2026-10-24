using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class DashboardMetricsRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task<DashboardMetrics> LoadAsync(
        long accountId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        if (from.HasValue && to.HasValue && from > to)
            throw new ArgumentException("Start must not be after end.", nameof(from));
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var jobs = connection.CreateCommand();
        jobs.CommandText = """
            SELECT status, COUNT(*)
            FROM jobs
            WHERE account_id = $account_id
              AND ($from IS NULL OR julianday(created_at) >= julianday($from))
              AND ($to IS NULL OR julianday(created_at) <= julianday($to))
            GROUP BY status;
            """;
        jobs.Parameters.AddWithValue("$account_id", accountId);
        jobs.Parameters.AddWithValue("$from", from?.ToString("O") ?? (object)DBNull.Value);
        jobs.Parameters.AddWithValue("$to", to?.ToString("O") ?? (object)DBNull.Value);

        var counts = new Dictionary<JobStatus, int>();
        await using (var reader = await jobs.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                if (Enum.TryParse<JobStatus>(reader.GetString(0), out var status))
                    counts[status] = reader.GetInt32(1);
        }

        await using var errors = connection.CreateCommand();
        errors.CommandText = """
            SELECT event_type, COUNT(*)
            FROM activity_logs
            WHERE account_id = $account_id
              AND event_type IN ('PermissionError', 'PrivacyError', 'NetworkError', 'NetworkRetry')
              AND ($from IS NULL OR julianday(created_at) >= julianday($from))
              AND ($to IS NULL OR julianday(created_at) <= julianday($to))
            GROUP BY event_type;
            """;
        errors.Parameters.AddWithValue("$account_id", accountId);
        errors.Parameters.AddWithValue("$from", from?.ToString("O") ?? (object)DBNull.Value);
        errors.Parameters.AddWithValue("$to", to?.ToString("O") ?? (object)DBNull.Value);

        var errorCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var reader = await errors.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                errorCounts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return new DashboardMetrics(
            counts.Values.Sum(),
            counts.GetValueOrDefault(JobStatus.Completed),
            counts.GetValueOrDefault(JobStatus.Failed) + counts.GetValueOrDefault(JobStatus.PartiallyCompleted),
            counts.GetValueOrDefault(JobStatus.Queued) + counts.GetValueOrDefault(JobStatus.Running),
            counts.GetValueOrDefault(JobStatus.WaitingForFloodWait),
            errorCounts.GetValueOrDefault("PermissionError"),
            errorCounts.GetValueOrDefault("PrivacyError"),
            errorCounts.GetValueOrDefault("NetworkError") + errorCounts.GetValueOrDefault("NetworkRetry"));
    }
}

