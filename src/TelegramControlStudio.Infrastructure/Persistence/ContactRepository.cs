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
}
