using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class AutoRunPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(JobStatus.Queued, false, true)]
    [InlineData(JobStatus.WaitingForFloodWait, true, true)]
    [InlineData(JobStatus.WaitingForFloodWait, false, false)]
    [InlineData(JobStatus.Paused, false, false)]
    [InlineData(JobStatus.Running, false, false)]
    [InlineData(JobStatus.WaitingForConfirmation, false, false)]
    [InlineData(JobStatus.Completed, false, false)]
    [InlineData(JobStatus.Cancelled, false, false)]
    public void EligibilityFollowsStatusAndFloodWaitDeadline(JobStatus status, bool cooldownElapsed, bool expected)
    {
        var job = Job(status, cooldownElapsed ? Now.AddSeconds(-1) : Now.AddSeconds(60));
        Assert.Equal(expected, AutoRunPolicy.IsEligible(job, Now));
    }

    [Fact]
    public void JobWithoutMessageTextIsNeverAutoSent()
    {
        var blank = Job(JobStatus.Queued, null, messageText: "   ");
        Assert.False(AutoRunPolicy.IsEligible(blank, Now));
    }

    private static Job Job(JobStatus status, DateTimeOffset? floodWaitUntil, string messageText = "สวัสดี") =>
        new(Guid.NewGuid(), 1, "ConsentMessaging", 1, status, 0, Now, null, null, floodWaitUntil, null, "key", messageText, null);
}

public sealed class JobInFlightGateTests
{
    [Fact]
    public void SecondBeginForTheSameJobIsRejectedUntilItEnds()
    {
        var gate = new JobInFlightGate();
        var jobId = Guid.NewGuid();
        Assert.True(gate.TryBegin(jobId));
        Assert.False(gate.TryBegin(jobId));
        Assert.True(gate.IsRunning(jobId));
        Assert.Equal(1, gate.Count);
        gate.End(jobId);
        Assert.False(gate.IsRunning(jobId));
        Assert.True(gate.TryBegin(jobId));
    }
}
