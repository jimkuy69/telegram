using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TelegramControlStudio.Infrastructure.Persistence;

/// <summary>
/// One-time codes that let a user sign in to the web host from the owner bot, without the
/// Telegram Login Widget. Only the hash of a code is stored, so a database copy cannot be replayed.
/// </summary>
public sealed class BotLoginCodeRepository(string databasePath)
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task<string> CreateAsync(long telegramUserId, TimeSpan? lifetime = null, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(lifetime ?? DefaultLifetime);
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var prune = connection.CreateCommand())
        {
            prune.CommandText = "DELETE FROM bot_login_codes WHERE expires_at < $cutoff;";
            prune.Parameters.AddWithValue("$cutoff", now.AddDays(-1).ToString("O"));
            await prune.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bot_login_codes(code_hash, telegram_user_id, created_at, expires_at, used_at)
            VALUES($hash, $user, $created_at, $expires_at, NULL);
            """;
        command.Parameters.AddWithValue("$hash", Hash(code));
        command.Parameters.AddWithValue("$user", telegramUserId);
        command.Parameters.AddWithValue("$created_at", now.ToString("O"));
        command.Parameters.AddWithValue("$expires_at", expiresAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return code;
    }

    /// <summary>Returns the owner of the code and burns it, or null when it is unknown, used, or expired.</summary>
    public async Task<long?> ConsumeAsync(string code, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var hash = Hash(code.Trim());

        long telegramUserId;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = (SqliteTransaction)transaction;
            select.CommandText = "SELECT telegram_user_id FROM bot_login_codes WHERE code_hash = $hash;";
            select.Parameters.AddWithValue("$hash", hash);
            if (await select.ExecuteScalarAsync(cancellationToken) is not long value) return null;
            telegramUserId = value;
        }

        await using var update = connection.CreateCommand();
        update.Transaction = (SqliteTransaction)transaction;
        update.CommandText = """
            UPDATE bot_login_codes SET used_at = $now
            WHERE code_hash = $hash AND used_at IS NULL AND expires_at > $now;
            """;
        update.Parameters.AddWithValue("$hash", hash);
        update.Parameters.AddWithValue("$now", now.ToString("O"));
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) return null;

        await transaction.CommitAsync(cancellationToken);
        return telegramUserId;
    }

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant())));
}
