using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class AppUserRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM app_users;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<AppUser>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT telegram_user_id, first_name, last_name, username, role, is_blocked, created_at, last_login_at
            FROM app_users
            ORDER BY role, first_name, telegram_user_id;
            """;
        var users = new List<AppUser>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (TryRead(reader) is { } user) users.Add(user);
        return users;
    }

    public async Task<AppUser?> GetAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT telegram_user_id, first_name, last_name, username, role, is_blocked, created_at, last_login_at
            FROM app_users WHERE telegram_user_id = $id;
            """;
        command.Parameters.AddWithValue("$id", telegramUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? TryRead(reader) : null;
    }

    public async Task UpsertAsync(AppUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_users
                (telegram_user_id, first_name, last_name, username, role, is_blocked, created_at, last_login_at)
            VALUES ($id, $first, $last, $username, $role, $blocked, $created, $login)
            ON CONFLICT(telegram_user_id) DO UPDATE SET
                first_name = excluded.first_name,
                last_name = excluded.last_name,
                username = excluded.username,
                role = excluded.role,
                is_blocked = excluded.is_blocked,
                last_login_at = excluded.last_login_at;
            """;
        command.Parameters.AddWithValue("$id", user.TelegramUserId);
        command.Parameters.AddWithValue("$first", user.FirstName);
        command.Parameters.AddWithValue("$last", user.LastName);
        command.Parameters.AddWithValue("$username", (object?)user.Username ?? DBNull.Value);
        command.Parameters.AddWithValue("$role", user.Role.ToString());
        command.Parameters.AddWithValue("$blocked", user.IsBlocked ? 1 : 0);
        command.Parameters.AddWithValue("$created", user.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$login", user.LastLoginAt?.ToString("O") ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TouchLoginAsync(long telegramUserId, string firstName, string lastName, string? username, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE app_users
            SET first_name = $first, last_name = $last, username = $username, last_login_at = $now
            WHERE telegram_user_id = $id;
            """;
        command.Parameters.AddWithValue("$first", firstName);
        command.Parameters.AddWithValue("$last", lastName);
        command.Parameters.AddWithValue("$username", (object?)username ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$id", telegramUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> SetBlockedAsync(long telegramUserId, bool blocked, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE app_users SET is_blocked = $blocked WHERE telegram_user_id = $id;";
        command.Parameters.AddWithValue("$blocked", blocked ? 1 : 0);
        command.Parameters.AddWithValue("$id", telegramUserId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static AppUser? TryRead(SqliteDataReader reader)
    {
        if (!Enum.TryParse<AppUserRole>(reader.GetString(4), out var role)) return null;
        return new AppUser(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            role,
            reader.GetInt64(5) == 1,
            DateTimeOffset.Parse(reader.GetString(6)),
            reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)));
    }
}
