using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Core.Reporting;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ResultReportRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

    [Fact]
    public async Task CountsOnlyRecipientActivityInsideTheRequestedWindow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-result-report-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Report", "+66000000020");
            var jobs = new JobRepository(path);
            var campaign = await jobs.CreateAsync(accountId, "Campaign:consented", 3, "result-report-campaign");
            var consentJob = await jobs.CreateAsync(accountId, JobTargetTypes.ConsentRequestTargetType(7), 1, "result-report-consent");

            await new JobItemRepository(path).CreateAsync(
            [
                new JobItem(campaign, accountId, 301, "Completed", 0, "rr-301", null, Now.AddHours(-2), Now.AddHours(-1)),
                new JobItem(campaign, accountId, 302, "Failed", 0, "rr-302", "TDLIB_MESSAGE_SEND_FAILED", Now.AddHours(-1)),
                new JobItem(consentJob, accountId, 303, "Completed", 0, "rr-303", null, Now.AddHours(-3), Now.AddHours(-2)),
                new JobItem(campaign, accountId, 304, "Completed", 0, "rr-304", null, Now.AddDays(-9), Now.AddDays(-8)),
                new JobItem(campaign, accountId, 305, "WaitingForFloodWait", 0, "rr-305", "FLOOD_WAIT_120", Now.AddMinutes(-20))
            ]);

            var consent = new ConsentRequestRepository(path);
            await consent.AppendAsync(Event(accountId, 301, ConsentRequestKinds.Requested, "hash-a"));
            await consent.AppendAsync(Event(accountId, 303, ConsentRequestKinds.Requested, "hash-b"));
            await consent.AppendAsync(Granted(accountId, 301));
            await consent.AppendAsync(Event(accountId, 399, ConsentRequestKinds.Requested, "hash-old", Now.AddDays(-9)));

            var report = await new ResultReportRepository(path)
                .LoadAsync(accountId, Now.AddDays(-1), Now, Zone);

            Assert.Equal(4, report.TotalItems);
            Assert.Equal(2, report.Sent);
            Assert.Equal(1, report.Failed);
            Assert.Equal(1, report.Pending);
            Assert.Equal(1, report.ConsentRequestsSent);
            Assert.Equal(2d / 3, report.SuccessRate, 6);
            Assert.Equal(1, report.CampaignJobs);
            Assert.Equal(1, report.ConsentJobs);
            Assert.Equal(4, report.Days.Sum(day => day.Sent + day.Failed + day.Pending + day.Skipped + day.Cancelled));

            var failure = Assert.Single(report.Failures);
            Assert.Equal("TDLIB_MESSAGE_SEND_FAILED", failure.Reason);

            Assert.Equal(2, report.Consent.Requested);
            Assert.Equal(1, report.Consent.Granted);
            Assert.Equal(1, report.Consent.AwaitingAnswer);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static ConsentRequest Event(long accountId, long userId, string kind, string bodyHash, DateTimeOffset? occurredAt = null) =>
        new(0, accountId, userId, kind, occurredAt ?? Now.AddMinutes(-30), 5001, 6001, 7, bodyHash, null, ConsentRequestSources.JobRunner);

    private static ConsentRequest Granted(long accountId, long userId) =>
        new(0, accountId, userId, ConsentRequestKinds.Granted, Now.AddMinutes(-10), 5002, 6001, 7, null, "ok", ConsentRequestSources.TelegramMessage);
}
