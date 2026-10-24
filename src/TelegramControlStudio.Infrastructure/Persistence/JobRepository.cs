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
}
