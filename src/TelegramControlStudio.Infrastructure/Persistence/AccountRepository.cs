using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class AccountRepository(string databasePath)
{
    public string DatabasePath { get; } = Path.GetFullPath(databasePath);
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath),
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task<long> CreateAsync(
        string displayName,
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO accounts(display_name, phone_number, status, created_at)
            VALUES($display_name, $phone_number, 'PendingAuthorization', $created_at);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$display_name", displayName);
        command.Parameters.AddWithValue("$phone_number", phoneNumber);
        command.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Account insert did not return an ID."));
    }

    public async Task SaveSessionAsync(AccountSession session, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO account_sessions(account_id, session_directory, authorization_state, is_authorized, updated_at)
            VALUES($account_id, $session_directory, $authorization_state, $is_authorized, $updated_at)
            ON CONFLICT(account_id) DO UPDATE SET
                session_directory = excluded.session_directory,
                authorization_state = excluded.authorization_state,
                is_authorized = excluded.is_authorized,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$account_id", session.AccountId);
        command.Parameters.AddWithValue("$session_directory", session.SessionDirectory);
        command.Parameters.AddWithValue("$authorization_state", session.AuthorizationState);
        command.Parameters.AddWithValue("$is_authorized", session.IsAuthorized ? 1 : 0);
        command.Parameters.AddWithValue("$updated_at", session.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, display_name, phone_number, status, created_at, last_sync_at
            FROM accounts
            ORDER BY created_at DESC;
            """;

        var accounts = new List<Account>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add(new Account(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5))));
        }

        return accounts;
    }

    public async Task<bool> UpdateStatusAsync(long accountId, string status, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE accounts SET status = $status, last_sync_at = $updated_at WHERE id = $account_id;";
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>
    /// Account-scoped tables in the order their rows can be removed: children before the parents they
    /// reference, so the delete never leans on cascades the schema does not declare.
    /// </summary>
    private static readonly string[] OwnedTables =
    [
        "job_items", "campaign_recipients", "template_media", "campaigns", "jobs", "message_templates",
        "media_files", "consent_requests", "contacts", "group_members", "telegram_groups", "invite_links",
        "join_requests", "activity_logs", "account_secrets", "account_sessions"
    ];

    public async Task<IReadOnlyDictionary<string, int>> CountOwnedRowsAsync(long accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var counts = new Dictionary<string, int>();
        foreach (var table in OwnedTables) counts[table] = await CountAsync(connection, table, accountId, cancellationToken);
        return counts;
    }

    /// <summary>Removes the account and every row that belongs to it. Returns null when the account does not exist.</summary>
    public async Task<AccountDeletion?> DeleteAsync(long accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        try
        {
            await using (var lookup = connection.CreateCommand())
            {
                lookup.Transaction = transaction;
                lookup.CommandText = "SELECT display_name FROM accounts WHERE id = $account_id;";
                lookup.Parameters.AddWithValue("$account_id", accountId);
                var displayName = await lookup.ExecuteScalarAsync(cancellationToken) as string;
                if (displayName is null) return null;

                var deleted = new Dictionary<string, int>();
                foreach (var table in OwnedTables)
                {
                    deleted[table] = await CountAsync(connection, table, accountId, cancellationToken);
                    if (deleted[table] == 0) continue;
                    await using var purge = connection.CreateCommand();
                    purge.Transaction = transaction;
                    purge.CommandText = $"DELETE FROM {table} WHERE account_id = $account_id;";
                    purge.Parameters.AddWithValue("$account_id", accountId);
                    await purge.ExecuteNonQueryAsync(cancellationToken);
                }

                await using var remove = connection.CreateCommand();
                remove.Transaction = transaction;
                remove.CommandText = "DELETE FROM accounts WHERE id = $account_id;";
                remove.Parameters.AddWithValue("$account_id", accountId);
                if (await remove.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException($"Account {accountId} disappeared while it was being deleted.");

                transaction.Commit();
                return new AccountDeletion(accountId, displayName, deleted);
            }
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string table, long accountId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE account_id = $account_id;";
        command.Parameters.AddWithValue("$account_id", accountId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    public async Task<bool> UpdateProfileAsync(long accountId, string displayName, string phoneNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE accounts SET display_name=$display_name, phone_number=$phone_number, last_sync_at=$updated_at WHERE id=$account_id";
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$display_name", displayName);
        command.Parameters.AddWithValue("$phone_number", phoneNumber);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
