using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class CampaignQueueServiceTests
{
    [Fact]
    public async Task ConfirmedCampaignCreatesQueuedJob()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-queue-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Test", "+66000000000");
            var templateId = await new TemplateRepository(path).CreateAsync(new MessageTemplate(0, accountId, "Welcome", "th", "Hello", null, 1, true, DateTimeOffset.UtcNow));
            var contact = new Contact(accountId, 123, "A", "", "a", null, true, false, DateTimeOffset.UtcNow);
            var campaignId = await new CampaignBuilder(path).CreateFromConsentedContactsAsync(accountId, templateId, "Consent campaign", "Explicit consent", [contact], true);
            var jobId = await new CampaignQueueService(path).QueueCampaignAsync(campaignId, accountId, true);
            var counts = await new JobRepository(path).CountByStatusAsync(accountId);
            var items = await new JobItemRepository(path).ListAsync(jobId);
            Assert.NotEqual(Guid.Empty, jobId); Assert.Equal(1, counts[JobStatus.Queued]); Assert.Single(items); Assert.Equal(123, items[0].TargetId); Assert.Equal("Queued", items[0].Status);
            Assert.True(await new JobItemRepository(path).UpdateStatusAsync(jobId, 123, "Failed", "temporary error", true));
            var failed = (await new JobItemRepository(path).ListAsync(jobId)).Single(); Assert.Equal("Failed", failed.Status); Assert.Equal(1, failed.RetryCount); Assert.Equal("temporary error", failed.LastError);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task UnconfirmedCampaignIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-queue-{Guid.NewGuid():N}.db");
        try { await new SqliteDatabase(path).InitializeAsync(); await Assert.ThrowsAsync<InvalidOperationException>(() => new CampaignQueueService(path).QueueCampaignAsync(Guid.NewGuid(), 1, false)); }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task CampaignIncludesOnlyConsentedNonOptedOutContactsFromAccount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-queue-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Policy", "+66000000013");
            var otherAccountId = await new AccountRepository(path).CreateAsync("Other", "+66000000014");
            var templateId = await new TemplateRepository(path).CreateAsync(new MessageTemplate(0, accountId, "Policy", "th", "Hello", null, 1, true, DateTimeOffset.UtcNow));
            var contacts = new[]
            {
                new Contact(accountId, 1, "Allowed", "", null, null, true, false, DateTimeOffset.UtcNow),
                new Contact(accountId, 2, "Opted", "", null, null, true, true, DateTimeOffset.UtcNow),
                new Contact(accountId, 3, "NoConsent", "", null, null, false, false, DateTimeOffset.UtcNow),
                new Contact(otherAccountId, 4, "Other", "", null, null, true, false, DateTimeOffset.UtcNow)
            };

            var campaignId = await new CampaignBuilder(path).CreateFromConsentedContactsAsync(accountId, templateId, "Policy", "consent", contacts, true);
            var recipients = await new CampaignRepository(path).ListRecipientsAsync(campaignId);
            Assert.Single(recipients);
            Assert.Equal(1, recipients[0].TelegramUserId);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}