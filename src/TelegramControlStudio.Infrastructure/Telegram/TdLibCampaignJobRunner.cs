using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Safety;

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
        return await RunAsync(client, jobId, accountId, text, confirmed, interval, JobTargetTypes.PurposeOf(job.TargetType), cancellationToken);
    }

    public async Task<JobStatus> RunAsync(
        ITdLibClient client,
        Guid jobId,
        long accountId,
        string text,
        bool confirmed,
        TimeSpan interval,
        SendPurpose purpose = SendPurpose.Campaign,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed) throw new InvalidOperationException("CONFIRMATION_REQUIRED");
        var jobRepository = new JobRepository(databasePath);
        var itemRepository = new JobItemRepository(databasePath);
        var safetyRepository = new SendSafetyRepository(databasePath);
        var sender = new TdLibMessageSendService(databasePath);
        var emergencyToken = EmergencyStopSignal.Token;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, emergencyToken);
        var token = linkedCancellation.Token;

        var safety = await safetyRepository.GetAsync(token);
        if (safety.KillSwitchEngaged)
        {
            await LogAsync(accountId, jobId, "KillSwitchBlocked", "Job start blocked by kill switch.");
            await jobRepository.UpdateStatusAsync(jobId, JobStatus.Paused, SendSafetyPolicy.KillSwitchReason, cancellationToken: CancellationToken.None);
            return JobStatus.Paused;
        }

        var items = await itemRepository.ListAsync(jobId, token);
        var contacts = (await new ContactRepository(databasePath).ListAsync(accountId, token)).ToDictionary(item => item.TelegramUserId);
        await jobRepository.UpdateStatusAsync(jobId, JobStatus.Running, cancellationToken: token);

        try
        {
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                if (item.Status is "Completed" or "Cancelled") continue;

                // A pause or cancel raised from the queue UI must stop this loop even when it
                // was started by the auto runner, which owns no cancellation token of its own.
                var liveJob = await jobRepository.GetAsync(jobId, token);
                if (liveJob is { Status: not JobStatus.Running }) return liveJob.Status;

                if (!contacts.TryGetValue(item.TargetId, out var contact))
                {
                    await itemRepository.UpdateStatusAsync(jobId, item.TargetId, "Skipped", "CONTACT_NOT_FOUND", cancellationToken: token);
                    continue;
                }

                // An opt-in request replaces the campaign gate with the request gate; both are
                // evaluated before transmission so a blocked recipient is skipped without a network call.
                safety = await safetyRepository.GetAsync(token);
                var decision = purpose == SendPurpose.ConsentRequest
                    ? await EvaluateConsentRequestAsync(contact, safety, token)
                    : SendSafetyPolicy.Evaluate(safety, contact.TelegramUserId, contact.HasConsent, contact.IsOptedOut);
                if (!decision.CanSend)
                {
                    await itemRepository.UpdateStatusAsync(jobId, item.TargetId, "Skipped", decision.ReasonCode, cancellationToken: token);
                    if (decision.Verdict == SendSafetyVerdict.Block)
                    {
                        await PauseForKillSwitchAsync(jobRepository, accountId, jobId);
                        return JobStatus.Paused;
                    }
                    continue;
                }

                try
                {
                    if (purpose == SendPurpose.ConsentRequest)
                    {
                        await sender.SendConsentRequestAsync(client, contact, text, JobTargetTypes.TemplateIdOf(liveJob?.TargetType), token);
                    }
                    else
                    {
                        await sender.SendTextAsync(client, contact, text, true, token);
                    }
                    await itemRepository.UpdateStatusAsync(jobId, item.TargetId, "Completed", cancellationToken: token);
                }
                catch (SendSafetyBlockedException blocked) when (blocked.Verdict == SendSafetyVerdict.Block)
                {
                    await itemRepository.UpdateStatusAsync(jobId, item.TargetId, "Queued", blocked.ReasonCode, cancellationToken: CancellationToken.None);
                    await PauseForKillSwitchAsync(jobRepository, accountId, jobId);
                    return JobStatus.Paused;
                }
                catch (SendSafetyBlockedException blocked)
                {
                    await itemRepository.UpdateStatusAsync(jobId, item.TargetId, "Skipped", blocked.ReasonCode, cancellationToken: token);
                    continue;
                }
                catch (OperationCanceledException) when (emergencyToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    await itemRepository.UpdateStatusAsync(jobId, item.TargetId, "Queued", SendSafetyPolicy.KillSwitchReason, cancellationToken: CancellationToken.None);
                    await PauseForKillSwitchAsync(jobRepository, accountId, jobId);
                    return JobStatus.Paused;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (TdLibFloodWaitException floodWait)
                {
                    var until = DateTimeOffset.UtcNow.AddSeconds(floodWait.Seconds);
                    await itemRepository.UpdateStatusAsync(jobId, item.TargetId, "WaitingForFloodWait", floodWait.Message, cancellationToken: token);
                    await jobRepository.UpdateStatusAsync(jobId, JobStatus.WaitingForFloodWait, floodWait.Message, until, token);
                    return JobStatus.WaitingForFloodWait;
                }
                catch (Exception exception)
                {
                    await itemRepository.UpdateStatusAsync(jobId, item.TargetId, "Failed", "TDLIB_MESSAGE_SEND_FAILED", incrementRetry: true, cancellationToken: token);
                    await jobRepository.UpdateStatusAsync(jobId, JobStatus.PartiallyCompleted, exception.Message, cancellationToken: token);
                    return JobStatus.PartiallyCompleted;
                }
                if (interval > TimeSpan.Zero) await Task.Delay(interval, token);
            }
        }
        catch (OperationCanceledException) when (emergencyToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await itemRepository.UpdateStatusForJobAsync(jobId, "Running", "Queued", CancellationToken.None);
            await PauseForKillSwitchAsync(jobRepository, accountId, jobId);
            return JobStatus.Paused;
        }

        var remaining = await itemRepository.ListAsync(jobId, token);
        if (remaining.Count > 0 && remaining.All(item => item.Status is "Skipped" or "Cancelled") &&
            remaining.Any(item => item.Status == "Skipped" && item.LastError == SendSafetyPolicy.TestModeReason))
        {
            await jobRepository.UpdateStatusAsync(jobId, JobStatus.Paused, SendSafetyPolicy.TestModeReason, cancellationToken: CancellationToken.None);
            return JobStatus.Paused;
        }

        var finalStatus = remaining.Any(item => item.Status == "Failed") ? JobStatus.PartiallyCompleted : JobStatus.Completed;
        await jobRepository.UpdateStatusAsync(jobId, finalStatus, cancellationToken: token);
        return finalStatus;
    }

    private async Task<SendSafetyDecision> EvaluateConsentRequestAsync(Contact contact, SendSafetySettings safety, CancellationToken token)
    {
        var limits = await new ConsentRequestSettingsRepository(databasePath).GetAsync(token);
        var requests = new ConsentRequestRepository(databasePath);
        var now = DateTimeOffset.UtcNow;
        var eligibility = new ConsentRequestEligibility(
            contact.TelegramUserId,
            !string.IsNullOrWhiteSpace(contact.PhoneNumber),
            contact.HasConsent,
            contact.IsOptedOut,
            await requests.LastRequestedAtAsync(contact.AccountId, contact.TelegramUserId, token),
            await requests.CountRequestedSinceAsync(contact.AccountId, now.AddDays(-1), token),
            limits.CooldownDays,
            limits.DailyLimit,
            now);
        return SendSafetyPolicy.EvaluateConsentRequest(safety, eligibility);
    }

    private async Task PauseForKillSwitchAsync(JobRepository jobRepository, long accountId, Guid jobId)
    {
        await jobRepository.UpdateStatusAsync(jobId, JobStatus.Paused, SendSafetyPolicy.KillSwitchReason, cancellationToken: CancellationToken.None);
        await LogAsync(accountId, jobId, "KillSwitchPaused", "Job paused by kill switch.");
    }

    private Task LogAsync(long accountId, Guid jobId, string eventType, string message) =>
        new ActivityLogRepository(databasePath).AppendAsync(new ActivityLog(accountId, jobId, "Warning", eventType, message, DateTimeOffset.UtcNow));
}
