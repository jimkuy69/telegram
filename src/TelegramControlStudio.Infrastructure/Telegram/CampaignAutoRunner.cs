using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Core.Security;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

/// <summary>
/// Drives confirmed queue jobs for accounts whose TDLib session is connected in this process,
/// so a flood-wait or the next queued job continues without anyone watching the window.
/// Every job still runs through <see cref="TdLibCampaignJobRunner"/>, which re-checks the
/// send-safety policy per recipient and honours the emergency stop before transmitting.
/// </summary>
public sealed class CampaignAutoRunner(
    string databasePath,
    TdLibAccountClientRegistry clients,
    JobInFlightGate inFlight,
    TimeSpan? sendInterval = null)
{
    private readonly TimeSpan _sendInterval = sendInterval ?? TimeSpan.FromSeconds(2);

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if ((await new SendSafetyRepository(databasePath).GetAsync(cancellationToken)).KillSwitchEngaged) return 0;

        var started = 0;
        var jobs = new JobRepository(databasePath);
        foreach (var accountId in clients.AccountIds)
        {
            try
            {
                if (!clients.TryGet(accountId, out var client) || client is null) continue;
                foreach (var job in await jobs.ListRunnableAsync(accountId, DateTimeOffset.UtcNow, cancellationToken))
                {
                    if (!AutoRunPolicy.IsEligible(job, DateTimeOffset.UtcNow)) continue;
                    if (!inFlight.TryBegin(job.Id)) continue;
                    try
                    {
                        if (!await jobs.TryClaimAsync(job.Id, DateTimeOffset.UtcNow, cancellationToken)) continue;
                        started++;
                        await RunJobAsync(client, job, accountId, cancellationToken);
                    }
                    finally
                    {
                        inFlight.End(job.Id);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await LogAsync(accountId, null, "Error", "AutoRunTickFailed", exception.Message, CancellationToken.None);
            }
        }
        return started;
    }

    public async Task RunLoopAsync(TimeSpan tick, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await RunOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            try
            {
                await Task.Delay(tick, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunJobAsync(ITdLibClient client, Job job, long accountId, CancellationToken cancellationToken)
    {
        try
        {
            var status = await new TdLibCampaignJobRunner(databasePath)
                .RunAsync(client, job.Id, accountId, true, _sendInterval, cancellationToken);
            await LogAsync(accountId, job.Id, "Info", "AutoRunFinished", $"Auto runner finished job {job.Id} as {status}.", CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            await new JobRepository(databasePath)
                .UpdateStatusAsync(job.Id, JobStatus.Paused, "Auto runner stopped with the application.", cancellationToken: CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await new JobRepository(databasePath)
                .UpdateStatusAsync(job.Id, JobStatus.Failed, "AUTO_RUN_FAILED", cancellationToken: CancellationToken.None);
            await LogAsync(accountId, job.Id, "Error", "AutoRunFailed", exception.Message, CancellationToken.None);
        }
    }

    private Task LogAsync(long accountId, Guid? jobId, string level, string eventType, string message, CancellationToken cancellationToken) =>
        new ActivityLogRepository(databasePath).AppendAsync(
            new ActivityLog(accountId, jobId, level, eventType, SecretRedactor.Redact(message), DateTimeOffset.UtcNow), cancellationToken);
}
