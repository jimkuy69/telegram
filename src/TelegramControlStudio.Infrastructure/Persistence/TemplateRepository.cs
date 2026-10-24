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

    public async Task<IReadOnlyList<MessageTemplate>> ListAsync(
        long accountId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, language, body, link, version, is_active, updated_at
            FROM message_templates
            WHERE account_id = $account_id
              AND ($active_only = 0 OR is_active = 1)
            ORDER BY updated_at DESC;
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$active_only", activeOnly ? 1 : 0);

        var templates = new List<MessageTemplate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            templates.Add(new MessageTemplate(
                reader.GetInt64(0),
                accountId,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt64(6) == 1,
                DateTimeOffset.Parse(reader.GetString(7))));
        }
        return templates;
    }

    public async Task<bool> DeactivateAsync(long accountId, long templateId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE message_templates SET is_active = 0 WHERE account_id = $account_id AND id = $id AND is_active = 1;";
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$id", templateId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<long> CreateNextVersionAsync(MessageTemplate template, CancellationToken cancellationToken = default)
    {
        var active = await ListAsync(template.AccountId, activeOnly: false, cancellationToken);
        var nextVersion = active.Where(item => item.Name == template.Name).Select(item => item.Version).DefaultIfEmpty(0).Max() + 1;
        return await CreateAsync(template with { Id = 0, Version = nextVersion, IsActive = true, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);
    }
}

