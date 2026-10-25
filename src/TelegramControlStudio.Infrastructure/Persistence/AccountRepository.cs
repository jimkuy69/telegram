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
}

