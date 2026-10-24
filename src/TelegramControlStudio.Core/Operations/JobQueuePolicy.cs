using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Core.Operations;

public enum JobFailureKind
{
    None,
    RetryableNetwork,
    FloodWait,
    PermissionDenied,
    ConsentDenied,
    SessionExpired,
    Permanent
}

public sealed record JobExecutionDecision(
    JobStatus NextStatus,
    bool ShouldRetry,
    DateTimeOffset? FloodWaitUntil,
    string? ErrorCode);

public static class JobQueuePolicy
{
    public static JobExecutionDecision Start(Job job, bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!confirmed || job.Status is JobStatus.Draft or JobStatus.WaitingForConfirmation)
            return new(JobStatus.WaitingForConfirmation, false, null, "CONFIRMATION_REQUIRED");
        if (job.Status is JobStatus.Completed or JobStatus.Cancelled)
            return new(job.Status, false, null, "JOB_TERMINAL");
        return new(JobStatus.Running, false, null, null);
    }

    public static JobExecutionDecision HandleFailure(
        Job job,
        JobFailureKind failureKind,
        DateTimeOffset now,
        TimeSpan? floodWait = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (failureKind == JobFailureKind.FloodWait && floodWait is { } wait)
            return new(JobStatus.WaitingForFloodWait, false, now.Add(wait), "FLOOD_WAIT");
        if (failureKind == JobFailureKind.RetryableNetwork && job.RetryCount < 3)
            return new(JobStatus.Queued, true, null, "NETWORK_RETRY");
        if (failureKind == JobFailureKind.PermissionDenied)
            return new(JobStatus.Failed, false, null, "PERMISSION_DENIED");
        if (failureKind == JobFailureKind.ConsentDenied)
            return new(JobStatus.Failed, false, null, "CONSENT_DENIED");
        if (failureKind == JobFailureKind.SessionExpired)
            return new(JobStatus.Failed, false, null, "SESSION_EXPIRED");
        return new(JobStatus.Failed, false, null, "PERMANENT_FAILURE");
    }

    public static bool CanResume(Job job, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Status is JobStatus.Paused or JobStatus.Queued ||
            (job.Status == JobStatus.WaitingForFloodWait && job.FloodWaitUntil <= now);
    }
}

