using System.Security.Cryptography;
using System.Text;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed record QuickSendQueueResult(
    Guid JobId,
    int Recipients,
    int ConsentAsserted,
    int OptedOutExcluded);

/// <summary>
/// Queues an operator-approved one-shot send. The operator selects the exact recipients and confirms
/// the count once; that confirmation is also the consent assertion for any selected contact that has
/// not replied to an opt-in request yet, so the assertion is written through the contact repository
/// and logged with the job instead of being applied silently at send time.
/// </summary>
public sealed class QuickSendQueueService(string databasePath)
{
    public const string TargetType = "QuickSend";

    public async Task<QuickSendQueueResult> QueueAsync(
        long accountId,
        string messageText,
        string? linkUrl,
        IReadOnlyCollection<Contact> selected,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        ArgumentException.ThrowIfNullOrWhiteSpace(messageText);
        ArgumentNullException.ThrowIfNull(selected);
        if (!confirmed) throw new InvalidOperationException("Quick send confirmation is required.");

        var contacts = selected.Where(contact => contact.AccountId == accountId).ToArray();
        var optedOut = contacts.Count(contact => contact.IsOptedOut);
        var recipients = contacts
            .Where(contact => !contact.IsOptedOut)
            .GroupBy(contact => contact.TelegramUserId)
            .Select(group => group.First())
            .ToArray();
        if (recipients.Length == 0)
            throw new InvalidOperationException("No selected recipient can be messaged.");

        var contactsRepository = new ContactRepository(databasePath);
        var consentAsserted = 0;
        foreach (var recipient in recipients.Where(contact => !contact.HasConsent))
            if (await contactsRepository.SetConsentAsync(accountId, recipient.TelegramUserId, true, cancellationToken))
                consentAsserted++;

        var jobId = await new JobRepository(databasePath).CreateAsync(
            accountId,
            TargetType,
            recipients.Length,
            CreateKey($"quick-send:{Guid.NewGuid():D}"),
            messageText.Trim(),
            string.IsNullOrWhiteSpace(linkUrl) ? null : linkUrl.Trim(),
            cancellationToken);
        var items = recipients
            .Select(recipient => new JobItem(
                jobId,
                accountId,
                recipient.TelegramUserId,
                "Queued",
                0,
                CreateKey($"quick-send-item:{jobId:D}:{recipient.TelegramUserId}"),
                null,
                DateTimeOffset.UtcNow))
            .ToArray();
        await new JobItemRepository(databasePath).CreateAsync(items, cancellationToken);
        await new ActivityLogRepository(databasePath).AppendAsync(new ActivityLog(
            accountId,
            jobId,
            "Info",
            "QuickSendQueued",
            $"Quick send queued {recipients.Length} recipient(s); operator confirmation asserted consent for {consentAsserted}; {optedOut} opted-out row(s) excluded.",
            DateTimeOffset.UtcNow), cancellationToken);
        return new(jobId, recipients.Length, consentAsserted, optedOut);
    }

    private static string CreateKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
