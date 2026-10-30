using System.Runtime.Versioning;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Core.Security;
using TelegramControlStudio.Infrastructure.Hosting;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.Web.Services;

/// <summary>
/// Web-first sending: while the "web_sending_enabled" setting is on, this host owns
/// the TDLib sessions of Ready accounts, runs Queued jobs through the standard
/// campaign runner (consent, test mode and kill switch enforced per recipient), and
/// holds each session until the toggle is turned off. The desktop app is blocked by
/// <see cref="TdLibSessionLock"/> for as long as the toggle stays on.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WebSendRunnerService(
    StudioHost studio,
    ILogger<WebSendRunnerService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(2);

    private readonly Dictionary<long, HeldSession> _held = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Web send runner tick failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await DisconnectAllAsync();
    }

    private async Task TickAsync(CancellationToken token)
    {
        var settings = new AppSettingsRepository(studio.DatabasePath);
        var enabled = WebSendSettings.IsEnabled(await settings.GetAsync(WebSendSettings.EnabledKey, token));
        var status = enabled ? await RunEnabledPassAsync(token) : "disabled";

        if (!enabled && _held.Count > 0)
            await DisconnectAllAsync();

        await settings.SetAsync(WebSendSettings.LastActivityKey, $"{DateTimeOffset.UtcNow:O} | {status}", CancellationToken.None);
    }

    private async Task<string> RunEnabledPassAsync(CancellationToken token)
    {
        var accounts = await new AccountRepository(studio.DatabasePath).ListAsync(token);
        var now = DateTimeOffset.UtcNow;
        var parts = new List<string>();

        foreach (var account in accounts)
        {
            if (!string.Equals(account.Status, "Ready", StringComparison.OrdinalIgnoreCase))
            {
                await ReleaseIfHeldAsync(account.Id);
                continue;
            }

            var jobs = new JobRepository(studio.DatabasePath);
            var resumed = await jobs.ResumeFloodWaitedJobsAsync(account.Id, now, token);

            if (!_held.TryGetValue(account.Id, out var held))
            {
                // First pass after enabling: clean crash-leftover Running state, then connect.
                await jobs.PauseRunningJobsAsync(account.Id, token);
                held = await ConnectAsync(account, token);
                if (held is null)
                {
                    parts.Add($"account {account.Id}: unavailable");
                    continue;
                }

                _held[account.Id] = held;
            }

            held.LastActivity = now;
            while (await NextQueuedJobAsync(account.Id, token) is { } job)
            {
                var result = await new TdLibCampaignJobRunner(studio.DatabasePath)
                    .RunAsync(held.Client, job.Id, account.Id, confirmed: true, SendInterval, token);
                held.LastActivity = DateTimeOffset.UtcNow;
                parts.Add($"account {account.Id} job {job.Id:N8}: {result}");
            }

            if (resumed > 0)
                parts.Add($"account {account.Id}: resumed {resumed} flood-waited job(s)");
        }

        foreach (var removedAccountId in _held.Keys.Except(accounts.Select(account => account.Id)).ToArray())
            await ReleaseIfHeldAsync(removedAccountId);

        return parts.Count == 0 ? "idle" : string.Join("; ", parts);
    }

    private async Task<Job?> NextQueuedJobAsync(long accountId, CancellationToken token)
    {
        var jobs = await new JobRepository(studio.DatabasePath).ListAsync(accountId, token);
        return jobs.FirstOrDefault(job => job.Status == JobStatus.Queued);
    }

    private async Task<HeldSession?> ConnectAsync(Account account, CancellationToken token)
    {
        var configurations = new TdLibConfigurationRepository(studio.DatabasePath);
        var configuration = await configurations.GetAsync(account.Id, token);
        var native = await configurations.GetNativeLocationAsync(account.Id, token);
        if (configuration is null || native is null)
        {
            logger.LogWarning("Web sending: account {AccountId} has no TDLib configuration.", account.Id);
            return null;
        }

        var sessionLock = TdLibSessionLock.TryAcquire(account.Id, TimeSpan.Zero);
        if (sessionLock is null)
        {
            logger.LogWarning("Web sending: TDLib session of account {AccountId} is held by another process.", account.Id);
            return null;
        }

        var bootstrapper = new TdLibSessionBootstrapper(studio.TdLibClients, new AccountRepository(studio.DatabasePath));
        try
        {
            await bootstrapper.ConnectAsync(
                account.Id,
                native.Value.Directory,
                native.Value.Sha256,
                configuration.Value.ApiId,
                configuration.Value.ApiHash,
                SessionPathPolicy.GetAccountSessionDirectory(account.Id),
                token);

            if (!await WaitUntilReadyAsync(bootstrapper, account.Id, token))
            {
                logger.LogWarning(
                    "Web sending: account {AccountId} did not reach Ready; complete the Telegram sign-in in the desktop app first.",
                    account.Id);
                await studio.TdLibClients.RemoveAsync(account.Id);
                sessionLock.Dispose();
                return null;
            }

            if (!studio.TdLibClients.TryGet(account.Id, out var client) || client is null)
            {
                sessionLock.Dispose();
                return null;
            }

            logger.LogInformation("Web sending: connected TDLib session for account {AccountId}.", account.Id);
            return new HeldSession(client, sessionLock);
        }
        catch
        {
            await studio.TdLibClients.RemoveAsync(account.Id);
            sessionLock.Dispose();
            throw;
        }
    }

    private static async Task<bool> WaitUntilReadyAsync(TdLibSessionBootstrapper bootstrapper, long accountId, CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow + ReadyTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (bootstrapper.TryGetStateMachine(accountId, out var machine) && machine is not null)
            {
                if (machine.IsReady)
                    return true;
                if (machine.Current is AuthorizationState.Closed or AuthorizationState.Error or AuthorizationState.Expired)
                    return false;
            }

            await Task.Delay(500, token);
        }

        return false;
    }

    private async Task DisconnectAllAsync()
    {
        foreach (var accountId in _held.Keys.ToArray())
            await ReleaseIfHeldAsync(accountId);
    }

    private async Task ReleaseIfHeldAsync(long accountId)
    {
        if (!_held.Remove(accountId, out var held))
            return;

        await studio.TdLibClients.RemoveAsync(accountId);
        held.Lock.Dispose();
        logger.LogInformation("Web sending: released TDLib session of account {AccountId}.", accountId);
    }

    private sealed class HeldSession(ITdLibClient client, TdLibSessionLock sessionLock)
    {
        public ITdLibClient Client { get; } = client;
        public TdLibSessionLock Lock { get; } = sessionLock;
        public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.UtcNow;
    }
}
