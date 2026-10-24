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
            Assert.NotEqual(Guid.Empty, jobId); Assert.Equal(1, counts[JobStatus.Queued]);
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
}

