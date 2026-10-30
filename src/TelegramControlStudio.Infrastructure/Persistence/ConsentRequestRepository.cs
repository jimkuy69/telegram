using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

/// <summary>
/// Append-only store for consent evidence. Read side exists to answer the two questions the
/// request policy cannot answer from memory: has this person already been asked recently, and how
/// many people has this account been asked today.
/// </summary>
public sealed class ConsentRequestRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true }.ToString();

    private static readonly string[] KnownKinds =
    [
        ConsentRequestKinds.Requested,
        ConsentRequestKinds.Granted,
        ConsentRequestKinds.OptedOut,
        ConsentRequestKinds.Rejected
    ];

    public async Task<long> AppendAsync(ConsentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AccountId <= 0) throw new ArgumentOutOfRangeException(nameof(request));
        if (request.TelegramUserId <= 0) throw new ArgumentOutOfRangeException(nameof(request));
        if (Array.IndexOf(KnownKinds, request.Kind) < 0)
            throw new ArgumentException($"Unknown consent event '{request.Kind}'.", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source);
        if (request.Kind == ConsentRequestKinds.Requested && string.IsNullOrWhiteSpace(request.BodySha256))
            throw new ArgumentException("A request must record the hash of the text that was sent.", nameof(request));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO consent_requests
                (account_id, telegram_user_id, kind, occurred_at, message_id, chat_id, template_id, body_sha256, matched_keyword, source)
            VALUES
                ($account_id, $telegram_user_id, $kind, $occurred_at, $message_id, $chat_id, $template_id, $body_sha256, $matched_keyword, $source);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$account_id", request.AccountId);
        command.Parameters.AddWithValue("$telegram_user_id", request.TelegramUserId);
        command.Parameters.AddWithValue("$kind", request.Kind);
        command.Parameters.AddWithValue("$occurred_at", request.OccurredAt.ToString("O"));
        command.Parameters.AddWithValue("$message_id", (object?)request.MessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("$chat_id", (object?)request.ChatId ?? DBNull.Value);
        command.Parameters.AddWithValue("$template_id", (object?)request.TemplateId ?? DBNull.Value);
        command.Parameters.AddWithValue("$body_sha256", (object?)request.BodySha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$matched_keyword", (object?)request.MatchedKeyword ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", request.Source);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Consent event insert returned no id."));
    }

    public async Task<DateTimeOffset?> LastRequestedAtAsync(long accountId, long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT occurred_at FROM consent_requests
            WHERE account_id = $account_id AND telegram_user_id = $telegram_user_id AND kind = $requested
            ORDER BY occurred_at DESC, id DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$telegram_user_id", telegramUserId);
        command.Parameters.AddWithValue("$requested", ConsentRequestKinds.Requested);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string raw ? DateTimeOffset.Parse(raw, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    /// <summary>The newest event for one contact, or null when nothing has been recorded yet.</summary>
    public async Task<ConsentRequest?> LatestForContactAsync(long accountId, long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT " + Columns +
            " FROM consent_requests WHERE account_id = $account_id AND telegram_user_id = $telegram_user_id" +
            " ORDER BY occurred_at DESC, id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$telegram_user_id", telegramUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    /// <summary>The newest Requested event for one contact in one chat — the request a reply can answer.</summary>
    public async Task<ConsentRequest?> LatestRequestInChatAsync(long accountId, long telegramUserId, long chatId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT " + Columns +
            " FROM consent_requests WHERE account_id = $account_id AND telegram_user_id = $telegram_user_id" +
            " AND kind = $requested AND chat_id = $chat_id" +
            " ORDER BY occurred_at DESC, id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$telegram_user_id", telegramUserId);
        command.Parameters.AddWithValue("$requested", ConsentRequestKinds.Requested);
        command.Parameters.AddWithValue("$chat_id", chatId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    /// <summary>Requests recorded for this account since the given moment, used for the daily cap.</summary>
    public async Task<int> CountRequestedSinceAsync(long accountId, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM consent_requests
            WHERE account_id = $account_id AND kind = $requested AND occurred_at >= $since;
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$requested", ConsentRequestKinds.Requested);
        command.Parameters.AddWithValue("$since", since.ToString("O"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    /// <summary>The newest event per contact, so a list can show who is still awaiting a reply.</summary>
    public async Task<IReadOnlyDictionary<long, ConsentRequest>> LatestByContactAsync(long accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT " + Columns + " FROM consent_requests WHERE account_id = $account_id ORDER BY occurred_at, id;";
        command.Parameters.AddWithValue("$account_id", accountId);
        var latest = new Dictionary<long, ConsentRequest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = Read(reader);
            latest[row.TelegramUserId] = row;
        }
        return latest;
    }

    public async Task<IReadOnlyList<ConsentRequest>> ListAsync(long accountId, string? kind = null, int limit = 500, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT " + Columns + " FROM consent_requests WHERE account_id = $account_id AND ($kind IS NULL OR kind = $kind) ORDER BY occurred_at DESC, id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$kind", (object?)kind ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));
        var result = new List<ConsentRequest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    private const string Columns =
        "id, account_id, telegram_user_id, kind, occurred_at, message_id, chat_id, template_id, body_sha256, matched_keyword, source";

    private static ConsentRequest Read(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetInt64(1),
        reader.GetInt64(2),
        reader.GetString(3),
        DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture),
        reader.IsDBNull(5) ? null : reader.GetInt64(5),
        reader.IsDBNull(6) ? null : reader.GetInt64(6),
        reader.IsDBNull(7) ? null : reader.GetInt64(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.GetString(10));
}
