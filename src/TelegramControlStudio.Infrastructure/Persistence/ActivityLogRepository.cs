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
}
