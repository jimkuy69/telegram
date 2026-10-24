using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class MediaRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task<long> CreateAsync(MediaFile media, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(media.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(media.LocalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(media.Sha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(media.MimeType);
        if (media.SizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(media.SizeBytes));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO media_files
                (account_id, file_name, local_path, sha256, mime_type, size_bytes, created_at)
            VALUES
                ($account_id, $file_name, $local_path, $sha256, $mime_type, $size_bytes, $created_at);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$account_id", media.AccountId);
        command.Parameters.AddWithValue("$file_name", media.FileName);
        command.Parameters.AddWithValue("$local_path", media.LocalPath);
        command.Parameters.AddWithValue("$sha256", media.Sha256);
        command.Parameters.AddWithValue("$mime_type", media.MimeType);
        command.Parameters.AddWithValue("$size_bytes", media.SizeBytes);
        command.Parameters.AddWithValue("$created_at", media.CreatedAt.ToString("O"));
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Media insert did not return an ID."));
    }

    public async Task<IReadOnlyList<MediaFile>> ListAsync(long accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, file_name, local_path, sha256, mime_type, size_bytes, created_at, deleted_at
            FROM media_files WHERE account_id = $account_id AND deleted_at IS NULL ORDER BY created_at DESC;
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        var result = new List<MediaFile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MediaFile(reader.GetInt64(0), accountId, reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5), DateTimeOffset.Parse(reader.GetString(6)), null));
        }
        return result;
    }

    public async Task<MediaFile?> FindBySha256Async(long accountId, string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, file_name, local_path, sha256, mime_type, size_bytes, created_at, deleted_at
            FROM media_files
            WHERE account_id = $account_id AND sha256 = $sha256 AND deleted_at IS NULL
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$sha256", sha256);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new MediaFile(reader.GetInt64(0), accountId, reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5), DateTimeOffset.Parse(reader.GetString(6)), reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)));
    }

    public async Task SoftDeleteAsync(long accountId, long mediaId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE media_files SET deleted_at = $deleted_at WHERE id = $id AND account_id = $account_id AND deleted_at IS NULL;";
        command.Parameters.AddWithValue("$deleted_at", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", mediaId);
        command.Parameters.AddWithValue("$account_id", accountId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}