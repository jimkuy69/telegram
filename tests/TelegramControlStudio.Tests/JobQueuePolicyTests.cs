using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class JobQueuePolicyTests
{
    private static Job Job(JobStatus status = JobStatus.Queued, int retries = 0, DateTimeOffset? floodWaitUntil = null) =>
        new(Guid.NewGuid(), 7, "Message", 1, status, retries, DateTimeOffset.UtcNow, null, null, floodWaitUntil, null, Guid.NewGuid().ToString("N"));

    [Fact]
    public void RequiresConfirmationBeforeRunning()
    {
        var decision = JobQueuePolicy.Start(Job(JobStatus.WaitingForConfirmation), false);
        Assert.Equal(JobStatus.WaitingForConfirmation, decision.NextStatus);
        Assert.Equal("CONFIRMATION_REQUIRED", decision.ErrorCode);
    }

    [Fact]
    public void RetriesNetworkFailureUpToThreeTimes()
    {
        var decision = JobQueuePolicy.HandleFailure(Job(retries: 2), JobFailureKind.RetryableNetwork, DateTimeOffset.UtcNow);
        Assert.Equal(JobStatus.Queued, decision.NextStatus);
        Assert.True(decision.ShouldRetry);
    }

    [Fact]
    public void StopsRetryingAfterThreeAttempts()
    {
        var decision = JobQueuePolicy.HandleFailure(Job(retries: 3), JobFailureKind.RetryableNetwork, DateTimeOffset.UtcNow);
        Assert.Equal(JobStatus.Failed, decision.NextStatus);
        Assert.False(decision.ShouldRetry);
    }

    [Fact]
    public void FloodWaitStoresResumeTime()
    {
        var now = DateTimeOffset.UtcNow;
        var decision = JobQueuePolicy.HandleFailure(Job(), JobFailureKind.FloodWait, now, TimeSpan.FromMinutes(2));
        Assert.Equal(JobStatus.WaitingForFloodWait, decision.NextStatus);
        Assert.Equal(now.AddMinutes(2), decision.FloodWaitUntil);
    }

    [Fact]
    public void ResumeRequiresFloodWaitToExpire()
    {
        var future = DateTimeOffset.UtcNow.AddMinutes(1);
        Assert.False(JobQueuePolicy.CanResume(Job(JobStatus.WaitingForFloodWait, floodWaitUntil: future), DateTimeOffset.UtcNow));
        Assert.True(JobQueuePolicy.CanResume(Job(JobStatus.WaitingForFloodWait, floodWaitUntil: future), future.AddTicks(1)));
    }
}

