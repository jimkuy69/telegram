using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Core.Reporting;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ResultReportRepositoryTests
{
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
            var now = DateTimeOffset.UtcNow;

            await new JobItemRepository(path).CreateAsync(
            [
                new JobItem(campaign, accountId, 301, "Completed", 0, "rr-301", null, now.AddHours(-2), now.AddHours(-1)),
                new JobItem(campaign, accountId, 302, "Failed", 0, "rr-302", "TDLIB_MESSAGE_SEND_FAILED", now.AddHours(-1)),
                new JobItem(consentJob, accountId, 303, "Completed", 0, "rr-303", null, now.AddHours(-3), now.AddHours(-2)),
                new JobItem(campaign, accountId, 304, "Completed", 0, "rr-304", null, now.AddDays(-9), now.AddDays(-8)),
                new JobItem(campaign, accountId, 305, "WaitingForFloodWait", 0, "rr-305", "FLOOD_WAIT_120", now.AddMinutes(-20))
            ]);

            var consent = new ConsentRequestRepository(path);
            await consent.AppendAsync(Event(accountId, 301, ConsentRequestKinds.Requested, now, "hash-a"));
            await consent.AppendAsync(Event(accountId, 303, ConsentRequestKinds.Requested, now, "hash-b"));
            await consent.AppendAsync(Granted(accountId, 301, now));
            await consent.AppendAsync(Event(accountId, 399, ConsentRequestKinds.Requested, now, "hash-old", now.AddDays(-9)));

            var report = await new ResultReportRepository(path)
                .LoadAsync(accountId, now.AddDays(-1), now, Zone);

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

    [Fact]
    public async Task GroupBroadcastJobsStayOutOfTheRecipientReport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-result-report-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("ReportGroups", "+66000000021");
            var jobs = new JobRepository(path);
            var campaign = await jobs.CreateAsync(accountId, "Campaign:consented", 1, "rr-gb-campaign");
            var broadcast = await jobs.CreateAsync(accountId, JobTargetTypes.GroupBroadcast, 2, "rr-gb-broadcast");
            var now = DateTimeOffset.UtcNow;

            await new JobItemRepository(path).CreateAsync(
            [
                new JobItem(campaign, accountId, 301, "Completed", 0, "rr-gb-301", null, now.AddHours(-2), now.AddHours(-1)),
                new JobItem(broadcast, accountId, -1001, "Completed", 0, "rr-gb-1001", null, now.AddHours(-1), now.AddMinutes(-30)),
                new JobItem(broadcast, accountId, -1002, "Failed", 0, "rr-gb-1002", "TDLIB_MESSAGE_SEND_FAILED 403: CHAT_ADMIN_REQUIRED", now.AddMinutes(-40))
            ]);

            var report = await new ResultReportRepository(path)
                .LoadAsync(accountId, now.AddDays(-1), now, Zone);

            Assert.Equal(1, report.TotalItems);
            Assert.Equal(1, report.Sent);
            Assert.Equal(1, report.CampaignJobs);
            Assert.Equal(0, report.ConsentJobs);
            Assert.Empty(report.Failures);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static ConsentRequest Event(long accountId, long userId, string kind, DateTimeOffset now, string bodyHash, DateTimeOffset? occurredAt = null) =>
        new(0, accountId, userId, kind, occurredAt ?? now.AddMinutes(-30), 5001, 6001, 7, bodyHash, null, ConsentRequestSources.JobRunner);

    private static ConsentRequest Granted(long accountId, long userId, DateTimeOffset now) =>
        new(0, accountId, userId, ConsentRequestKinds.Granted, now.AddMinutes(-10), 5002, 6001, 7, null, "ok", ConsentRequestSources.TelegramMessage);
}
