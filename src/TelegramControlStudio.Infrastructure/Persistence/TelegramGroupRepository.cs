using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class TelegramGroupRepository
{
    private readonly string _connectionString;
    public TelegramGroupRepository(string databasePath) => _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true }.ToString();

    public async Task<IReadOnlyList<TelegramGroup>> ListAsync(long accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT account_id, chat_id, title, kind, member_count, can_view_members, can_send_messages, can_invite_users, last_synced_at, observed_note FROM telegram_groups WHERE account_id = $account_id AND deleted_at IS NULL ORDER BY title";
        command.Parameters.AddWithValue("$account_id", accountId);
        var result = new List<TelegramGroup>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(Read(reader));
        return result;
    }

    public async Task UpsertAsync(TelegramGroup group, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO telegram_groups(account_id, chat_id, title, kind, member_count, can_view_members, can_send_messages, can_invite_users, last_synced_at, observed_note, deleted_at)
            VALUES($account_id, $chat_id, $title, $kind, $member_count, $can_view_members, $can_send_messages, $can_invite_users, $last_synced_at, $observed_note, NULL)
            ON CONFLICT(account_id, chat_id) DO UPDATE SET title=excluded.title, kind=excluded.kind, member_count=excluded.member_count, can_view_members=excluded.can_view_members, can_send_messages=excluded.can_send_messages, can_invite_users=excluded.can_invite_users, last_synced_at=excluded.last_synced_at, observed_note=excluded.observed_note, deleted_at=NULL;
            """;
        command.Parameters.AddWithValue("$account_id", group.AccountId); command.Parameters.AddWithValue("$chat_id", group.ChatId);
        command.Parameters.AddWithValue("$title", group.Title); command.Parameters.AddWithValue("$kind", group.Kind);
        command.Parameters.AddWithValue("$member_count", (object?)group.MemberCount ?? DBNull.Value);
        command.Parameters.AddWithValue("$can_view_members", group.CanViewMembers ? 1 : 0); command.Parameters.AddWithValue("$can_send_messages", group.CanSendMessages ? 1 : 0); command.Parameters.AddWithValue("$can_invite_users", group.CanInviteUsers ? 1 : 0);
        command.Parameters.AddWithValue("$last_synced_at", (object?)group.LastSyncedAt?.ToString("O") ?? DBNull.Value); command.Parameters.AddWithValue("$observed_note", group.ObservedNote);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Hides every group of the account that is not in <paramref name="keepChatIds"/>, so a chat the
    /// operator left does not linger in the list forever. Soft-deleting keeps the row history, but the
    /// member rows of a pruned chat are removed because they would otherwise resurface stale if the
    /// chat is joined again.
    /// </summary>
    public async Task<int> PruneAsync(long accountId, IReadOnlyCollection<long> keepChatIds, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var keepClause = keepChatIds.Count == 0
            ? "1 = 1"
            : $"chat_id NOT IN ({string.Join(", ", Enumerable.Range(0, keepChatIds.Count).Select(index => $"$keep{index}"))})";

        await using (var members = connection.CreateCommand())
        {
            members.Transaction = transaction;
            members.CommandText = $"""
                DELETE FROM group_members
                WHERE account_id = $account_id
                  AND chat_id IN (
                      SELECT chat_id FROM telegram_groups
                      WHERE account_id = $account_id AND deleted_at IS NULL AND {keepClause});
                """;
            BindAccountAndKeeps(members, accountId, keepChatIds);
            await members.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var groups = connection.CreateCommand())
        {
            groups.Transaction = transaction;
            groups.CommandText = $"""
                UPDATE telegram_groups
                SET deleted_at = $now
                WHERE account_id = $account_id AND deleted_at IS NULL AND {keepClause};
                """;
            BindAccountAndKeeps(groups, accountId, keepChatIds);
            groups.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            var pruned = await groups.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return pruned;
        }
    }

    private static void BindAccountAndKeeps(SqliteCommand command, long accountId, IReadOnlyCollection<long> keepChatIds)
    {
        command.Parameters.AddWithValue("$account_id", accountId);
        var index = 0;
        foreach (var chatId in keepChatIds) command.Parameters.AddWithValue($"$keep{index++}", chatId);
    }

    private static TelegramGroup Read(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4),
        reader.GetInt32(5) == 1, reader.GetInt32(6) == 1, reader.GetInt32(7) == 1,
        reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)), reader.GetString(9));
}

