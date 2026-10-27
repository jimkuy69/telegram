using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibCampaignJobRunner(string databasePath)
{
    public async Task<JobStatus> RunAsync(ITdLibClient client, Guid jobId, long accountId, bool confirmed, TimeSpan interval, CancellationToken cancellationToken = default)
    {
        var job = await new JobRepository(databasePath).GetAsync(jobId, cancellationToken) ?? throw new InvalidOperationException("JOB_NOT_FOUND");
        if (job.AccountId != accountId) throw new InvalidOperationException("JOB_ACCOUNT_MISMATCH");
        var text = job.MessageText;
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("JOB_MESSAGE_EMPTY");
        if (!string.IsNullOrWhiteSpace(job.LinkUrl)) text = $"{text}\n\n{job.LinkUrl}";
        return await RunAsync(client, jobId, accountId, text, confirmed, interval, cancellationToken);
    }

    public async Task<JobStatus> RunAsync(ITdLibClient client, Guid jobId, long accountId, string text, bool confirmed, TimeSpan interval, CancellationToken cancellationToken = default)
    {
        if (!confirmed) throw new InvalidOperationException("CONFIRMATION_REQUIRED");
        var items = await new JobItemRepository(databasePath).ListAsync(jobId, cancellationToken);
        var contacts = (await new ContactRepository(databasePath).ListAsync(accountId, cancellationToken)).ToDictionary(item => item.TelegramUserId);
        var itemRepository = new JobItemRepository(databasePath);
        await new JobRepository(databasePath).UpdateStatusAsync(jobId, JobStatus.Running, cancellationToken: cancellationToken);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Status is "Completed" or "Cancelled") continue;
            if (!contacts.TryGetValue(item.TargetId, out var contact) || !contact.HasConsent || contact.IsOptedOut)
            {
                await itemRepository.UpdateStatusAsync(jobId, item.TargetId, "Skipped", "CONSENT_REQUIRED_OR_OPTED_OUT", cancellationToken: cancellationToken);
                continue;
            }
            try
            {
                await new TdLibMessageSendService(databasePath).SendTextAsync(client, contact, text, true, cancellationToken);
                await itemRepository.UpdateStatusAsync(jobId, item.TargetId, "Completed", cancellationToken: cancellationToken);
            }
            catch (Exception exception)
            {
                if (exception is TdLibFloodWaitException floodWait)
                {
                    var until = DateTimeOffset.UtcNow.AddSeconds(floodWait.Seconds);
                    await itemRepository.UpdateStatusAsync(jobId, item.TargetId, "WaitingForFloodWait", floodWait.Message, cancellationToken: cancellationToken);
                    await new JobRepository(databasePath).UpdateStatusAsync(jobId, JobStatus.WaitingForFloodWait, floodWait.Message, until, cancellationToken);
                    return JobStatus.WaitingForFloodWait;
                }
                await itemRepository.UpdateStatusAsync(jobId, item.TargetId, "Failed", "TDLIB_MESSAGE_SEND_FAILED", incrementRetry: true, cancellationToken: cancellationToken);
                await new JobRepository(databasePath).UpdateStatusAsync(jobId, JobStatus.PartiallyCompleted, exception.Message, cancellationToken: cancellationToken);
                return JobStatus.PartiallyCompleted;
            }
            if (interval > TimeSpan.Zero) await Task.Delay(interval, cancellationToken);
        }
        var finalStatus = (await new JobItemRepository(databasePath).ListAsync(jobId, cancellationToken)).Any(item => item.Status == "Failed") ? JobStatus.PartiallyCompleted : JobStatus.Completed;
        await new JobRepository(databasePath).UpdateStatusAsync(jobId, finalStatus, cancellationToken: cancellationToken);
        return finalStatus;
    }
}
