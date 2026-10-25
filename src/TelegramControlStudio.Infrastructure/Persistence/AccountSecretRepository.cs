using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Security;

namespace TelegramControlStudio.Infrastructure.Persistence;

[SupportedOSPlatform("windows")]
public sealed class AccountSecretRepository(string databasePath, DpapiSecretStore secretStore)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task SaveAsync(long accountId, string secretName, string secret, CancellationToken cancellationToken = default)
    {
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        ArgumentException.ThrowIfNullOrEmpty(secret);

        var protectedValue = secretStore.Protect(secret);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO account_secrets(account_id, secret_name, protected_value, updated_at)
            VALUES($account_id, $secret_name, $protected_value, $updated_at)
            ON CONFLICT(account_id, secret_name) DO UPDATE SET
                protected_value = excluded.protected_value,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$secret_name", secretName);
        command.Parameters.AddWithValue("$protected_value", protectedValue);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        CryptographicOperations.ZeroMemory(protectedValue);
    }

    public async Task<string?> GetAsync(long accountId, string secretName, CancellationToken cancellationToken = default)
    {
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT protected_value FROM account_secrets WHERE account_id = $account_id AND secret_name = $secret_name;";
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$secret_name", secretName);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not byte[] protectedValue) return null;
        try { return secretStore.Unprotect(protectedValue); }
        finally { CryptographicOperations.ZeroMemory(protectedValue); }
    }

    public async Task<bool> DeleteAsync(long accountId, string secretName, CancellationToken cancellationToken = default)
    {
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM account_secrets WHERE account_id = $account_id AND secret_name = $secret_name;";
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$secret_name", secretName);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}

