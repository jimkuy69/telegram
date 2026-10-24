using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class ContactRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task<IReadOnlyList<Contact>> ListAsync(long accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT telegram_user_id, first_name, last_name, username, phone_number,
                   has_consent, is_opted_out, updated_at
            FROM contacts
            WHERE account_id = $account_id AND deleted_at IS NULL
            ORDER BY first_name, last_name;
            """;
        command.Parameters.AddWithValue("$account_id", accountId);

        var result = new List<Contact>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new Contact(
                accountId,
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt64(5) == 1,
                reader.GetInt64(6) == 1,
                DateTimeOffset.Parse(reader.GetString(7))));
        }

        return result;
    }

    public async Task UpsertAsync(Contact contact, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO contacts
                (account_id, telegram_user_id, first_name, last_name, username, phone_number,
                 has_consent, is_opted_out, updated_at, deleted_at)
            VALUES
                ($account_id, $telegram_user_id, $first_name, $last_name, $username, $phone_number,
                 $has_consent, $is_opted_out, $updated_at, NULL)
            ON CONFLICT(account_id, telegram_user_id) DO UPDATE SET
                first_name = excluded.first_name,
                last_name = excluded.last_name,
                username = excluded.username,
                phone_number = excluded.phone_number,
                has_consent = excluded.has_consent,
                is_opted_out = excluded.is_opted_out,
                updated_at = excluded.updated_at,
                deleted_at = NULL;
            """;
        command.Parameters.AddWithValue("$account_id", contact.AccountId);
        command.Parameters.AddWithValue("$telegram_user_id", contact.TelegramUserId);
        command.Parameters.AddWithValue("$first_name", contact.FirstName);
        command.Parameters.AddWithValue("$last_name", contact.LastName);
        command.Parameters.AddWithValue("$username", (object?)contact.Username ?? DBNull.Value);
        command.Parameters.AddWithValue("$phone_number", (object?)contact.PhoneNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$has_consent", contact.HasConsent ? 1 : 0);
        command.Parameters.AddWithValue("$is_opted_out", contact.IsOptedOut ? 1 : 0);
        command.Parameters.AddWithValue("$updated_at", contact.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertManyAsync(IEnumerable<Contact> contacts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO contacts
                    (account_id, telegram_user_id, first_name, last_name, username, phone_number,
                     has_consent, is_opted_out, updated_at, deleted_at)
                VALUES
                    ($account_id, $telegram_user_id, $first_name, $last_name, $username, $phone_number,
                     $has_consent, $is_opted_out, $updated_at, NULL)
                ON CONFLICT(account_id, telegram_user_id) DO UPDATE SET
                    first_name = excluded.first_name, last_name = excluded.last_name,
                    username = excluded.username, phone_number = excluded.phone_number,
                    has_consent = excluded.has_consent, is_opted_out = excluded.is_opted_out,
                    updated_at = excluded.updated_at, deleted_at = NULL;
                """;
            command.Parameters.AddWithValue("$account_id", contact.AccountId);
            command.Parameters.AddWithValue("$telegram_user_id", contact.TelegramUserId);
            command.Parameters.AddWithValue("$first_name", contact.FirstName);
            command.Parameters.AddWithValue("$last_name", contact.LastName);
            command.Parameters.AddWithValue("$username", (object?)contact.Username ?? DBNull.Value);
            command.Parameters.AddWithValue("$phone_number", (object?)contact.PhoneNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("$has_consent", contact.HasConsent ? 1 : 0);
            command.Parameters.AddWithValue("$is_opted_out", contact.IsOptedOut ? 1 : 0);
            command.Parameters.AddWithValue("$updated_at", contact.UpdatedAt.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}