using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class InviteRepository(string databasePath)
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task<Invite> CreateAsync(
        AppUserRole role,
        string? note,
        DateTimeOffset? expiresAt,
        long? createdBy,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = NewCode();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO invites(code, role, note, created_by, created_at, expires_at, used_at, used_by, is_revoked)
                VALUES($code, $role, $note, $created_by, $created_at, $expires_at, NULL, NULL, 0);
                """;
            command.Parameters.AddWithValue("$code", code);
            command.Parameters.AddWithValue("$role", role.ToString());
            command.Parameters.AddWithValue("$note", note ?? string.Empty);
            command.Parameters.AddWithValue("$created_by", (object?)createdBy ?? DBNull.Value);
            command.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$expires_at", expiresAt?.ToString("O") ?? (object)DBNull.Value);
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
                return new Invite(code, role, note ?? string.Empty, createdBy, DateTimeOffset.UtcNow, expiresAt, null, null, false);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
            }
        }
        throw new InvalidOperationException("Could not allocate a unique invite code.");
    }

    public async Task<Invite?> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT code, role, note, created_by, created_at, expires_at, used_at, used_by, is_revoked
            FROM invites WHERE code = $code;
            """;
        command.Parameters.AddWithValue("$code", code.Trim().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadInvite(reader) : null;
    }

    public async Task<IReadOnlyList<Invite>> ListAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT code, role, note, created_by, created_at, expires_at, used_at, used_by, is_revoked
            FROM invites ORDER BY created_at DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
        var invites = new List<Invite>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) invites.Add(ReadInvite(reader));
        return invites;
    }

    public async Task<bool> RedeemAsync(string code, long telegramUserId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE invites
            SET used_at = $now, used_by = $used_by
            WHERE code = $code
              AND used_at IS NULL
              AND is_revoked = 0
              AND (expires_at IS NULL OR expires_at > $now);
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$used_by", telegramUserId);
        command.Parameters.AddWithValue("$code", code.Trim().ToUpperInvariant());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> RevokeAsync(string code, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE invites SET is_revoked = 1 WHERE code = $code AND used_at IS NULL AND is_revoked = 0;";
        command.Parameters.AddWithValue("$code", code.Trim().ToUpperInvariant());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static Invite ReadInvite(SqliteDataReader reader) => new(
        reader.GetString(0),
        Enum.TryParse<AppUserRole>(reader.GetString(1), out var role) ? role : AppUserRole.Friend,
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetInt64(3),
        DateTimeOffset.Parse(reader.GetString(4)),
        reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
        reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
        reader.IsDBNull(7) ? null : reader.GetInt64(7),
        reader.GetInt64(8) == 1);

    private static string NewCode()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return string.Create(8, bytes.ToArray(), static (chars, raw) =>
        {
            for (var i = 0; i < chars.Length; i++) chars[i] = Alphabet[raw[i] % Alphabet.Length];
        });
    }
}
