using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class GroupMemberRepository
{
    private readonly string _connectionString;
    public GroupMemberRepository(string databasePath) => _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true }.ToString();

    public async Task<IReadOnlyList<GroupMember>> ListAsync(long accountId, long chatId, string? search = null, int offset = 0, int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT account_id, chat_id, telegram_user_id, display_name, username, status, synced_at, source_note FROM group_members WHERE account_id=$account_id AND chat_id=$chat_id AND ($search='' OR display_name LIKE $pattern OR username LIKE $pattern) ORDER BY display_name LIMIT $limit OFFSET $offset";
        command.Parameters.AddWithValue("$account_id", accountId); command.Parameters.AddWithValue("$chat_id", chatId); command.Parameters.AddWithValue("$search", search ?? string.Empty); command.Parameters.AddWithValue("$pattern", $"%{search ?? string.Empty}%"); command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500)); command.Parameters.AddWithValue("$offset", Math.Max(0, offset));
        var result = new List<GroupMember>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new GroupMember(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), DateTimeOffset.Parse(reader.GetString(6)), reader.GetString(7)));
        return result;
    }

    public async Task UpsertAsync(GroupMember member, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO group_members(account_id, chat_id, telegram_user_id, display_name, username, status, synced_at, source_note) VALUES($account_id,$chat_id,$user_id,$name,$username,$status,$synced_at,$note) ON CONFLICT(account_id,chat_id,telegram_user_id) DO UPDATE SET display_name=excluded.display_name, username=excluded.username, status=excluded.status, synced_at=excluded.synced_at, source_note=excluded.source_note";
        command.Parameters.AddWithValue("$account_id", member.AccountId); command.Parameters.AddWithValue("$chat_id", member.ChatId); command.Parameters.AddWithValue("$user_id", member.TelegramUserId); command.Parameters.AddWithValue("$name", member.DisplayName); command.Parameters.AddWithValue("$username", (object?)member.Username ?? DBNull.Value); command.Parameters.AddWithValue("$status", member.Status); command.Parameters.AddWithValue("$synced_at", member.SyncedAt.ToString("O")); command.Parameters.AddWithValue("$note", member.SourceNote); await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

