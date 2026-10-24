using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class TemplateRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task<long> CreateAsync(
        MessageTemplate template,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(template.Body);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO message_templates
                (account_id, name, language, body, link, version, is_active, updated_at)
            VALUES
                ($account_id, $name, $language, $body, $link, $version, $is_active, $updated_at);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$account_id", template.AccountId);
        command.Parameters.AddWithValue("$name", template.Name);
        command.Parameters.AddWithValue("$language", template.Language);
        command.Parameters.AddWithValue("$body", template.Body);
        command.Parameters.AddWithValue("$link", (object?)template.Link ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", template.Version);
        command.Parameters.AddWithValue("$is_active", template.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$updated_at", template.UpdatedAt.ToString("O"));
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Template insert did not return an ID."));
    }
}
