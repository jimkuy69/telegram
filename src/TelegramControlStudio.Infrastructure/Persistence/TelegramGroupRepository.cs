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

    private static TelegramGroup Read(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4),
        reader.GetInt32(5) == 1, reader.GetInt32(6) == 1, reader.GetInt32(7) == 1,
        reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)), reader.GetString(9));
}

