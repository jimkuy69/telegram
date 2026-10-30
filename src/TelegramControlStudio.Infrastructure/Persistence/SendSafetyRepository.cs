using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Safety;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class SendSafetyRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task<SendSafetySettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT test_mode_enabled, allowlist_user_ids, kill_switch, updated_at
            FROM send_safety_settings
            WHERE id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return SendSafetySettings.SafeDefault;
        return new SendSafetySettings(
            reader.GetInt64(0) != 0,
            SendSafetyPolicy.ParseAllowlist(reader.GetString(1)),
            reader.GetInt64(2) != 0,
            DateTimeOffset.TryParse(reader.GetString(3), out var updatedAt) ? updatedAt : null);
    }

    public async Task SaveAsync(SendSafetySettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO send_safety_settings(id, test_mode_enabled, allowlist_user_ids, kill_switch, updated_at)
            VALUES(1, $test_mode, $allowlist, $kill_switch, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                test_mode_enabled = excluded.test_mode_enabled,
                allowlist_user_ids = excluded.allowlist_user_ids,
                kill_switch = excluded.kill_switch,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$test_mode", settings.TestModeEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$allowlist", SendSafetyPolicy.FormatAllowlist(settings.AllowlistUserIds));
        command.Parameters.AddWithValue("$kill_switch", settings.KillSwitchEngaged ? 1 : 0);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SendSafetySettings> UpdateAsync(
        Func<SendSafetySettings, SendSafetySettings> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        var current = await GetAsync(cancellationToken);
        var next = mutate(current) with { UpdatedAt = DateTimeOffset.UtcNow };
        await SaveAsync(next, cancellationToken);
        return next;
    }

    public Task<SendSafetySettings> SetKillSwitchAsync(bool engaged, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings with { KillSwitchEngaged = engaged }, cancellationToken);

    public Task<SendSafetySettings> SetTestModeAsync(bool enabled, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings with { TestModeEnabled = enabled }, cancellationToken);

    public Task<SendSafetySettings> SaveAllowlistAsync(IEnumerable<long> userIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var allowlist = userIds.Where(id => id > 0).ToHashSet();
        return UpdateAsync(settings => settings with { AllowlistUserIds = allowlist }, cancellationToken);
    }
}
