using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class InviteLinkRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true
    }.ToString();

    public async Task<long> CreateAsync(InviteLink link, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(link.InviteUrl);
        if (link.UsageLimit is <= 0) throw new ArgumentOutOfRangeException(nameof(link.UsageLimit));
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO invite_links(account_id, chat_id, invite_url, expires_at, usage_limit, creates_join_request, created_at)
            VALUES($account_id, $chat_id, $invite_url, $expires_at, $usage_limit, $creates_join_request, $created_at);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$account_id", link.AccountId);
        command.Parameters.AddWithValue("$chat_id", link.ChatId);
        command.Parameters.AddWithValue("$invite_url", link.InviteUrl);
        command.Parameters.AddWithValue("$expires_at", link.ExpiresAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$usage_limit", link.UsageLimit ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$creates_join_request", link.CreatesJoinRequest ? 1 : 0);
        command.Parameters.AddWithValue("$created_at", link.CreatedAt.ToString("O"));
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Invite link insert did not return an ID."));
    }

    public async Task<IReadOnlyList<InviteLink>> ListAsync(long accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, account_id, chat_id, invite_url, expires_at, usage_limit, creates_join_request, created_at, revoked_at
            FROM invite_links WHERE account_id = $account_id ORDER BY created_at DESC;
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var links = new List<InviteLink>();
        while (await reader.ReadAsync(cancellationToken))
        {
            links.Add(new InviteLink(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.GetInt32(6) == 1,
                DateTimeOffset.Parse(reader.GetString(7)), reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8))));
        }
        return links;
    }

    public async Task<bool> MarkRevokedAsync(long accountId, string inviteUrl, DateTimeOffset revokedAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inviteUrl);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE invite_links SET revoked_at = $revoked_at WHERE account_id = $account_id AND invite_url = $invite_url AND revoked_at IS NULL;";
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$invite_url", inviteUrl);
        command.Parameters.AddWithValue("$revoked_at", revokedAt.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}