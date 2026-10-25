using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class DashboardMetricsRepositoryTests
{
    [Fact]
    public async Task LoadsJobAndErrorMetricsForAccountAndDateRange()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-dashboard-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Dashboard", "+66000000016");
            var otherAccountId = await new AccountRepository(path).CreateAsync("Other", "+66000000017");
            var jobs = new JobRepository(path);
            var completed = await jobs.CreateAsync(accountId, "Message", 1, "dashboard-completed");
            await jobs.UpdateStatusAsync(completed, JobStatus.Completed);
            var flood = await jobs.CreateAsync(accountId, "Message", 1, "dashboard-flood");
            await jobs.UpdateStatusAsync(flood, JobStatus.WaitingForFloodWait, floodWaitUntil: DateTimeOffset.UtcNow.AddMinutes(5));
            var failed = await jobs.CreateAsync(accountId, "Message", 1, "dashboard-failed");
            await jobs.UpdateStatusAsync(failed, JobStatus.Failed, "failed");
            var campaignCompleted = await jobs.CreateAsync(accountId, "Campaign:consented", 2, "dashboard-campaign-completed");
            await jobs.UpdateStatusAsync(campaignCompleted, JobStatus.Completed);
            var campaignFailed = await jobs.CreateAsync(accountId, "Campaign:consented", 2, "dashboard-campaign-failed");
            await jobs.UpdateStatusAsync(campaignFailed, JobStatus.Failed, "campaign failed");
            var other = await jobs.CreateAsync(otherAccountId, "Message", 1, "other-job");
            await jobs.UpdateStatusAsync(other, JobStatus.Failed, "other");
            var logs = new ActivityLogRepository(path);
            await logs.AppendAsync(new ActivityLog(accountId, failed, "Error", "PermissionError", "permission denied", DateTimeOffset.UtcNow));
            await logs.AppendAsync(new ActivityLog(accountId, failed, "Error", "PrivacyError", "privacy denied", DateTimeOffset.UtcNow));
            await logs.AppendAsync(new ActivityLog(accountId, failed, "Error", "NetworkError", "network failed", DateTimeOffset.UtcNow));
            await logs.AppendAsync(new ActivityLog(otherAccountId, other, "Error", "NetworkError", "other", DateTimeOffset.UtcNow));

            var metrics = await new DashboardMetricsRepository(path).LoadAsync(accountId, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));

            Assert.Equal(5, metrics.TotalJobs);
            Assert.Equal(2, metrics.CompletedJobs);
            Assert.Equal(2, metrics.FailedJobs);
            Assert.Equal(0, metrics.QueuedJobs);
            Assert.Equal(1, metrics.FloodWaitJobs);
            Assert.Equal(1, metrics.PermissionErrors);
            Assert.Equal(1, metrics.PrivacyErrors);
            Assert.Equal(1, metrics.NetworkErrors);
            Assert.Equal(2, metrics.CampaignJobs);
            Assert.Equal(1, metrics.CampaignCompletedJobs);
            Assert.Equal(1, metrics.CampaignFailedJobs);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

