using System.Security.Cryptography;
using System.Text;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class CampaignQueueService(string databasePath)
{
    public async Task<Guid> QueueCampaignAsync(Guid campaignId, long accountId, bool confirmed, CancellationToken cancellationToken = default)
    {
        if (!confirmed) throw new InvalidOperationException("Queue confirmation is required.");
        var recipients = await new CampaignRepository(databasePath).ListRecipientsAsync(campaignId, cancellationToken);
        var eligible = recipients.Where(item => item.AccountId == accountId && item.Status == "Pending").ToArray();
        if (eligible.Length == 0) throw new InvalidOperationException("No pending campaign recipients are eligible.");
        var idempotencyKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"campaign-job:{campaignId:D}"))).ToLowerInvariant();
        var jobs = new JobRepository(databasePath);
        var jobId = await jobs.CreateAsync(accountId, $"Campaign:{campaignId:D}", eligible.Length, idempotencyKey, cancellationToken);
        var items = eligible.Select(recipient => new JobItem(jobId, accountId, recipient.TelegramUserId, "Queued", 0, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"job-item:{jobId:D}:{recipient.TelegramUserId}"))).ToLowerInvariant(), null, DateTimeOffset.UtcNow));
        await new JobItemRepository(databasePath).CreateAsync(items, cancellationToken);
        await jobs.UpdateStatusAsync(jobId, JobStatus.Queued, cancellationToken: cancellationToken);
        return jobId;
    }
}

