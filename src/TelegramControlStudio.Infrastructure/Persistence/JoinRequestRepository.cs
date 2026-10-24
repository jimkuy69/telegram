using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class JoinRequestRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true }.ToString();

    public async Task<long> UpsertAsync(JoinRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Status);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO join_requests(account_id, chat_id, telegram_user_id, status, requested_at, decision_at, decision_reason)
            VALUES($account_id, $chat_id, $telegram_user_id, $status, $requested_at, $decision_at, $decision_reason)
            ON CONFLICT(account_id, chat_id, telegram_user_id) DO UPDATE SET
              status = excluded.status, decision_at = excluded.decision_at, decision_reason = excluded.decision_reason;
            SELECT id FROM join_requests WHERE account_id = $account_id AND chat_id = $chat_id AND telegram_user_id = $telegram_user_id;
            """;
        command.Parameters.AddWithValue("$account_id", request.AccountId);
        command.Parameters.AddWithValue("$chat_id", request.ChatId);
        command.Parameters.AddWithValue("$telegram_user_id", request.TelegramUserId);
        command.Parameters.AddWithValue("$status", request.Status);
        command.Parameters.AddWithValue("$requested_at", request.RequestedAt.ToString("O"));
        command.Parameters.AddWithValue("$decision_at", request.DecisionAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$decision_reason", (object?)request.DecisionReason ?? DBNull.Value);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Join request upsert did not return an ID."));
    }
}

