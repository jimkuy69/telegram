using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

/// <summary>
/// Persists the opt-in request limits in the shared app_settings table. Anything unreadable falls
/// back to the shipped defaults instead of silently widening the rules.
/// </summary>
public sealed class ConsentRequestSettingsRepository(string databasePath)
{
    private const string CooldownKey = "consent_request_cooldown_days";
    private const string DailyLimitKey = "consent_request_daily_limit";

    private readonly string _connectionString = new SqliteConnectionStringBuilder
    { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true }.ToString();

    public async Task<ConsentRequestSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var stored = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT key, value FROM app_settings WHERE key IN ($cooldown, $limit);";
            command.Parameters.AddWithValue("$cooldown", CooldownKey);
            command.Parameters.AddWithValue("$limit", DailyLimitKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                stored[reader.GetString(0)] = reader.GetString(1);
        }

        return new ConsentRequestSettings(
            ReadInt(stored, CooldownKey, ConsentRequestSettings.Default.CooldownDays),
            ReadInt(stored, DailyLimitKey, ConsentRequestSettings.Default.DailyLimit));
    }

    public async Task SetAsync(ConsentRequestSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.CooldownDays is < 0 or > 365)
            throw new ArgumentOutOfRangeException(nameof(settings), "Cooldown must be between 0 and 365 days.");
        if (settings.DailyLimit is < 0 or > 200)
            throw new ArgumentOutOfRangeException(nameof(settings), "Daily limit must be between 0 and 200 requests.");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await WriteAsync(connection, (SqliteTransaction)transaction, CooldownKey, settings.CooldownDays.ToString(), cancellationToken);
        await WriteAsync(connection, (SqliteTransaction)transaction, DailyLimitKey, settings.DailyLimit.ToString(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task WriteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO app_settings(key, value, updated_at) VALUES($key, $value, $updated_at)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> stored, string key, int fallback) =>
        stored.TryGetValue(key, out var raw) && int.TryParse(raw, out var parsed) && parsed >= 0 ? parsed : fallback;
}
