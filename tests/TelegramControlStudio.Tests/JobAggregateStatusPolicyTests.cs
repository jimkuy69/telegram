using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class JobAggregateStatusPolicyTests
{
    private static JobItem Item(string status, long id) =>
        new(Guid.NewGuid(), 1, id, status, 0, $"key-{id}", null, DateTimeOffset.UtcNow);

    [Fact]
    public void AllCompletedIsCompleted() =>
        Assert.Equal(JobStatus.Completed, JobAggregateStatusPolicy.FromItems(
            [Item("Completed", 1), Item("Completed", 2)]));

    [Fact]
    public void MixedTerminalResultsArePartial() =>
        Assert.Equal(JobStatus.PartiallyCompleted, JobAggregateStatusPolicy.FromItems(
            [Item("Completed", 1), Item("Failed", 2)]));

    [Fact]
    public void AllFailedOrCancelledIsFailed() =>
        Assert.Equal(JobStatus.Failed, JobAggregateStatusPolicy.FromItems(
            [Item("Failed", 1), Item("Cancelled", 2)]));

    [Fact]
    public void RunningItemKeepsJobRunning() =>
        Assert.Equal(JobStatus.Running, JobAggregateStatusPolicy.FromItems(
            [Item("Completed", 1), Item("Running", 2)]));
}
