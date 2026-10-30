using TelegramControlStudio.Infrastructure.Hosting;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.Web.Services;

/// <summary>Long-polls the Telegram Bot API and routes commands to <see cref="BotCommandRouter"/>.</summary>
public sealed class BotPollingService(
    StudioHost studio,
    TelegramBotApiClient client,
    ILogger<BotPollingService> logger) : BackgroundService
{
    private long _offset;
    private bool _initialized;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!BotSecrets.IsSupported)
        {
            logger.LogWarning("Bot polling is disabled: DPAPI secret storage is unavailable on this platform.");
            return;
        }

        var router = new BotCommandRouter(studio.DatabasePath);
        while (!stoppingToken.IsCancellationRequested)
        {
            string? token = null;
            try
            {
                token = await BotSecrets.GetTokenAsync(studio.DatabasePath, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read the bot token from the database.");
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                await DelayAsync(TimeSpan.FromSeconds(10), stoppingToken);
                continue;
            }

            try
            {
                if (!_initialized)
                {
                    var backlog = await client.GetUpdatesAsync(token, -1, 0, stoppingToken);
                    _offset = backlog.Count > 0 ? backlog[^1].UpdateId + 1 : 0;
                    _initialized = true;
                }

                var updates = await client.GetUpdatesAsync(token, _offset, 25, stoppingToken);
                foreach (var update in updates)
                {
                    _offset = Math.Max(_offset, update.UpdateId + 1);
                    if (update.Message is not { } message) continue;

                    try
                    {
                        var reply = await router.HandleAsync(message, stoppingToken);
                        if (!string.IsNullOrWhiteSpace(reply))
                            await client.SendMessageAsync(token, message.ChatId, reply, stoppingToken);
                    }
                    catch (BotApiException ex)
                    {
                        logger.LogWarning(ex, "Bot rejected a reply for update {UpdateId}.", update.UpdateId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Bot command handling failed for update {UpdateId}.", update.UpdateId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Bot polling failed; retrying in 5 seconds.");
                await DelayAsync(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
