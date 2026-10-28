using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed record CampaignSendResult(int Sent, int Skipped, int Failed, IReadOnlyList<long> FailedRecipients);

public sealed class TdLibConsentCampaignService(string databasePath)
{
    public async Task<CampaignSendResult> SendTextAsync(
        ITdLibClient client,
        long accountId,
        IEnumerable<Contact> recipients,
        string text,
        bool confirmed,
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(recipients);
        if (!confirmed) throw new InvalidOperationException("CONFIRMATION_REQUIRED");
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        if (interval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        var sent = 0; var skipped = 0; var failed = 0; var failedRecipients = new List<long>();
        foreach (var recipient in recipients)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (recipient.AccountId != accountId || !recipient.HasConsent || recipient.IsOptedOut)
            {
                skipped++;
                continue;
            }
            try
            {
                await new TdLibMessageSendService(databasePath).SendTextAsync(client, recipient, text, true, cancellationToken);
                sent++;
            }
            catch
            {
                failed++;
                failedRecipients.Add(recipient.TelegramUserId);
                continue;
            }
            if (interval > TimeSpan.Zero) await Task.Delay(interval, cancellationToken);
        }
        return new(sent, skipped, failed, failedRecipients);
    }
}

