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
}

