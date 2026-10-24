using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class TemplateMediaRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true }.ToString();

    public async Task LinkAsync(TemplateMedia link, CancellationToken cancellationToken = default)
    {
        if (link.SortOrder < 0) throw new ArgumentOutOfRangeException(nameof(link.SortOrder));
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO template_media(template_id, media_file_id, account_id, sort_order)
            SELECT $template_id, $media_file_id, $account_id, $sort_order
            WHERE EXISTS (SELECT 1 FROM message_templates WHERE id = $template_id AND account_id = $account_id)
              AND EXISTS (SELECT 1 FROM media_files WHERE id = $media_file_id AND account_id = $account_id AND deleted_at IS NULL)
            ON CONFLICT(template_id, media_file_id) DO UPDATE SET sort_order = excluded.sort_order;
            """;
        command.Parameters.AddWithValue("$template_id", link.TemplateId);
        command.Parameters.AddWithValue("$media_file_id", link.MediaFileId);
        command.Parameters.AddWithValue("$account_id", link.AccountId);
        command.Parameters.AddWithValue("$sort_order", link.SortOrder);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) throw new InvalidOperationException("Template and media must belong to the same account and be active.");
    }
}

