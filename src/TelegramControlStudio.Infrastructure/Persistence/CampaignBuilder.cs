using System.Security.Cryptography;
using System.Text;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class CampaignBuilder(string databasePath)
{
    public async Task<Guid> CreateFromConsentedContactsAsync(
        long accountId,
        long templateId,
        string name,
        string consentPolicy,
        IReadOnlyCollection<Contact> contacts,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed) throw new InvalidOperationException("Campaign confirmation is required.");
        var eligible = contacts.Where(contact => contact.AccountId == accountId && contact.HasConsent && !contact.IsOptedOut).ToArray();
        if (eligible.Length == 0) throw new InvalidOperationException("No consented recipients are eligible.");
        var campaignId = Guid.NewGuid();
        var recipients = eligible.Select(contact => new CampaignRecipient(campaignId, accountId, contact.TelegramUserId, "Pending", 0, CreateKey(campaignId, contact.TelegramUserId), null)).ToArray();
        await new CampaignRepository(databasePath).CreateAsync(new Campaign(campaignId, accountId, templateId, name, consentPolicy, DateTimeOffset.UtcNow), recipients, cancellationToken);
        return campaignId;
    }

    private static string CreateKey(Guid campaignId, long userId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{campaignId:D}:{userId}"))).ToLowerInvariant();
}

