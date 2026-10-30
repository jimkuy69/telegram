using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using TelegramControlStudio.Infrastructure.Security;

namespace TelegramControlStudio.Infrastructure.Persistence;

/// <summary>
/// Stores application secrets (currently the Telegram bot token) with Windows DPAPI under the current user.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AppSecretRepository(string databasePath)
{
    private readonly DpapiSecretStore _store = new();
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task<string?> GetBotTokenAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT protected_value FROM app_secrets WHERE name = $name;";
        command.Parameters.AddWithValue("$name", SecretNames.BotToken);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not byte[] protectedValue) return null;
        return _store.Unprotect(protectedValue);
    }

    public async Task SaveBotTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_secrets(name, protected_value, updated_at) VALUES($name, $value, $updated_at)
            ON CONFLICT(name) DO UPDATE SET protected_value = excluded.protected_value, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$name", SecretNames.BotToken);
        command.Parameters.AddWithValue("$value", _store.Protect(token.Trim()));
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteBotTokenAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM app_secrets WHERE name = $name;";
        command.Parameters.AddWithValue("$name", SecretNames.BotToken);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static class SecretNames
    {
        public const string BotToken = "telegram_bot_token";
    }
}
