using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Core.Operations;

public static class JobAggregateStatusPolicy
{
    public static JobStatus FromItems(IReadOnlyCollection<JobItem> items)
    {
        if (items.Count == 0) return JobStatus.Queued;

        var completed = items.Count(item => item.Status == "Completed");
        var failed = items.Count(item => item.Status == "Failed");
        var cancelled = items.Count(item => item.Status == "Cancelled");

        if (completed == items.Count) return JobStatus.Completed;
        if (completed > 0 && completed + failed + cancelled == items.Count)
            return JobStatus.PartiallyCompleted;
        if (failed + cancelled == items.Count) return JobStatus.Failed;
        if (items.Any(item => item.Status == "Running")) return JobStatus.Running;
        return JobStatus.Queued;
    }
}
