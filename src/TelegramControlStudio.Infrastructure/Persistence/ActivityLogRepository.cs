using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Security;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class ActivityLogRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task AppendAsync(ActivityLog entry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Level);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.EventType);
        var safeMessage = SecretRedactor.Redact(entry.Message);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO activity_logs(account_id, job_id, level, event_type, message, created_at)
            VALUES($account_id, $job_id, $level, $event_type, $message, $created_at);
            """;
        command.Parameters.AddWithValue("$account_id", entry.AccountId);
        command.Parameters.AddWithValue("$job_id", entry.JobId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$level", entry.Level);
        command.Parameters.AddWithValue("$event_type", entry.EventType);
        command.Parameters.AddWithValue("$message", safeMessage);
        command.Parameters.AddWithValue("$created_at", entry.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityLog>> ListAsync(
        long accountId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT account_id, job_id, level, event_type, message, created_at
            FROM activity_logs
            WHERE account_id = $account_id
              AND ($from IS NULL OR created_at >= $from)
              AND ($to IS NULL OR created_at <= $to)
            ORDER BY created_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$from", from?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$to", to?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        var entries = new List<ActivityLog>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new ActivityLog(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                SecretRedactor.Redact(reader.GetString(4)),
                DateTimeOffset.Parse(reader.GetString(5))));
        }
        return entries;
    }
}

