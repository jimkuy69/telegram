using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Core.Reporting;

namespace TelegramControlStudio.Infrastructure.Persistence;

/// <summary>
/// Reads the rows behind <see cref="ResultReport"/>. Day bucketing, rates and reason grouping are
/// delegated to <see cref="ResultReportCalculator"/> so this class stays a pure query.
/// </summary>
public sealed class ResultReportRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task<ResultReport> LoadAsync(
        long accountId,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeZoneInfo zone,
        CancellationToken cancellationToken = default)
    {
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        if (from > to) throw new ArgumentException("Start must not be after end.", nameof(from));
        ArgumentNullException.ThrowIfNull(zone);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var items = await ReadItemsAsync(connection, accountId, from, to, cancellationToken);
        var consent = await ReadConsentAsync(connection, accountId, from, to, cancellationToken);
        var (campaignJobs, consentJobs) = await ReadJobCountsAsync(connection, accountId, from, to, cancellationToken);

        return ResultReportCalculator.Build(accountId, from, to, campaignJobs, consentJobs, items, consent, zone);
    }

    private static async Task<IReadOnlyList<ResultReportItem>> ReadItemsAsync(
        SqliteConnection connection, long accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT job_items.status,
                   job_items.sent_at,
                   job_items.updated_at,
                   job_items.last_error,
                   jobs.target_type
            FROM job_items
            INNER JOIN jobs ON jobs.id = job_items.job_id
            WHERE job_items.account_id = $account_id
              AND julianday(COALESCE(job_items.sent_at, job_items.updated_at)) >= julianday($from)
              AND julianday(COALESCE(job_items.sent_at, job_items.updated_at)) <= julianday($to)
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$from", from.ToString("O"));
        command.Parameters.AddWithValue("$to", to.ToString("O"));

        var result = new List<ResultReportItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var targetType = reader.IsDBNull(4) ? null : reader.GetString(4);
            result.Add(new ResultReportItem(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : DateTimeOffset.Parse(reader.GetString(1)),
                DateTimeOffset.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                JobTargetTypes.IsConsentRequest(targetType)));
        }
        return result;
    }

    private static async Task<ResultReportConsent> ReadConsentAsync(
        SqliteConnection connection, long accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT kind, COUNT(*)
            FROM consent_requests
            WHERE account_id = $account_id
              AND julianday(occurred_at) >= julianday($from)
              AND julianday(occurred_at) <= julianday($to)
            GROUP BY kind;
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$from", from.ToString("O"));
        command.Parameters.AddWithValue("$to", to.ToString("O"));

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            counts[reader.GetString(0)] = reader.GetInt32(1);

        return new ResultReportConsent(
            counts.GetValueOrDefault(ConsentRequestKinds.Requested),
            counts.GetValueOrDefault(ConsentRequestKinds.Granted),
            counts.GetValueOrDefault(ConsentRequestKinds.Rejected),
            counts.GetValueOrDefault(ConsentRequestKinds.OptedOut));
    }

    private static async Task<(int CampaignJobs, int ConsentJobs)> ReadJobCountsAsync(
        SqliteConnection connection, long accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT target_type, COUNT(*)
            FROM jobs
            WHERE account_id = $account_id
              AND julianday(created_at) >= julianday($from)
              AND julianday(created_at) <= julianday($to)
            GROUP BY target_type;
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$from", from.ToString("O"));
        command.Parameters.AddWithValue("$to", to.ToString("O"));

        var campaign = 0;
        var consent = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var count = reader.GetInt32(1);
            if (JobTargetTypes.IsConsentRequest(reader.GetString(0))) consent += count;
            else campaign += count;
        }
        return (campaign, consent);
    }
}
