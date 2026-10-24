using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class CampaignRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task CreateAsync(
        Campaign campaign,
        IReadOnlyCollection<CampaignRecipient> recipients,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaign.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(campaign.ConsentPolicy);
        if (recipients.Any(recipient => recipient.CampaignId != campaign.Id || recipient.AccountId != campaign.AccountId))
        {
            throw new InvalidOperationException("Campaign recipients must belong to the campaign account.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var campaignCommand = connection.CreateCommand();
        campaignCommand.Transaction = (SqliteTransaction)transaction;
        campaignCommand.CommandText = """
            INSERT INTO campaigns (id, account_id, template_id, name, consent_policy, created_at)
            VALUES ($id, $account_id, $template_id, $name, $consent_policy, $created_at);
            """;
        campaignCommand.Parameters.AddWithValue("$id", campaign.Id.ToString("D"));
        campaignCommand.Parameters.AddWithValue("$account_id", campaign.AccountId);
        campaignCommand.Parameters.AddWithValue("$template_id", campaign.TemplateId);
        campaignCommand.Parameters.AddWithValue("$name", campaign.Name);
        campaignCommand.Parameters.AddWithValue("$consent_policy", campaign.ConsentPolicy);
        campaignCommand.Parameters.AddWithValue("$created_at", campaign.CreatedAt.ToString("O"));
        await campaignCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var recipient in recipients)
        {
            await using var recipientCommand = connection.CreateCommand();
            recipientCommand.Transaction = (SqliteTransaction)transaction;
            recipientCommand.CommandText = """
                INSERT INTO campaign_recipients
                    (campaign_id, account_id, telegram_user_id, status, retry_count, idempotency_key, last_error)
                VALUES
                    ($campaign_id, $account_id, $telegram_user_id, $status, $retry_count, $idempotency_key, $last_error);
                """;
            recipientCommand.Parameters.AddWithValue("$campaign_id", recipient.CampaignId.ToString("D"));
            recipientCommand.Parameters.AddWithValue("$account_id", recipient.AccountId);
            recipientCommand.Parameters.AddWithValue("$telegram_user_id", recipient.TelegramUserId);
            recipientCommand.Parameters.AddWithValue("$status", recipient.Status);
            recipientCommand.Parameters.AddWithValue("$retry_count", recipient.RetryCount);
            recipientCommand.Parameters.AddWithValue("$idempotency_key", recipient.IdempotencyKey);
            recipientCommand.Parameters.AddWithValue("$last_error", (object?)recipient.LastError ?? DBNull.Value);
            await recipientCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CampaignRecipient>> ListRecipientsAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT account_id, telegram_user_id, status, retry_count, idempotency_key, last_error
            FROM campaign_recipients
            WHERE campaign_id = $campaign_id
            ORDER BY telegram_user_id;
            """;
        command.Parameters.AddWithValue("$campaign_id", campaignId.ToString("D"));

        var recipients = new List<CampaignRecipient>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            recipients.Add(new CampaignRecipient(
                campaignId,
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return recipients;
    }
}

