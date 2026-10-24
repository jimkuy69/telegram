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

    public async Task<bool> DecideAsync(
        long accountId,
        long chatId,
        long telegramUserId,
        string decision,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (decision is not ("Approved" or "Rejected")) throw new ArgumentException("Decision must be Approved or Rejected.", nameof(decision));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE join_requests
            SET status = $status, decision_at = $decision_at, decision_reason = $reason
            WHERE account_id = $account_id AND chat_id = $chat_id AND telegram_user_id = $telegram_user_id AND status = 'Pending';
            """;
        command.Parameters.AddWithValue("$status", decision);
        command.Parameters.AddWithValue("$decision_at", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$chat_id", chatId);
        command.Parameters.AddWithValue("$telegram_user_id", telegramUserId);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (changed)
        {
            await new ActivityLogRepository(databasePath).AppendAsync(new Core.Domain.ActivityLog(
                accountId, null, "Info", "JoinRequestDecision",
                $"Join request {decision} for chat {chatId}, user {telegramUserId}. Reason: {reason}",
                DateTimeOffset.UtcNow), cancellationToken);
        }
        return changed;
    }

    public async Task<IReadOnlyList<JoinRequest>> ListPendingAsync(long accountId, long? chatId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, chat_id, telegram_user_id, status, requested_at, decision_at, decision_reason
            FROM join_requests
            WHERE account_id = $account_id AND status = 'Pending' AND ($chat_id IS NULL OR chat_id = $chat_id)
            ORDER BY requested_at;
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$chat_id", chatId ?? (object)DBNull.Value);
        var result = new List<JoinRequest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new JoinRequest(reader.GetInt64(0), accountId, reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3), DateTimeOffset.Parse(reader.GetString(4)), reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)), reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return result;
    }
}

