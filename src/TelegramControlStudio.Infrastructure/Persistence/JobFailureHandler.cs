using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class JobFailureHandler(string databasePath)
{
    public Task<JobExecutionDecision> HandleAsync(
        Job job,
        string error,
        CancellationToken cancellationToken = default) =>
        HandleAsync(job, JobFailureClassifier.Classify(error), error, cancellationToken);

    public async Task<JobExecutionDecision> HandleAsync(
        Job job,
        JobFailureKind failureKind,
        string error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        var wait = failureKind == JobFailureKind.FloodWait
            ? FloodWaitParser.TryParse(error)
            : null;
        var decision = JobQueuePolicy.HandleFailure(job, failureKind, DateTimeOffset.UtcNow, wait);
        var repository = new JobRepository(databasePath);

        if (decision.ShouldRetry)
            await repository.IncrementRetryAsync(job.Id, error, cancellationToken);
        else
            await repository.UpdateStatusAsync(job.Id, decision.NextStatus, error, decision.FloodWaitUntil, cancellationToken);

        var eventType = decision.ErrorCode switch
        {
            "FLOOD_WAIT" => "FloodWait",
            "NETWORK_RETRY" => "NetworkRetry",
            "PERMISSION_DENIED" => "PermissionError",
            "CONSENT_DENIED" => "ConsentDenied",
            "SESSION_EXPIRED" => "SessionExpired",
            _ => "JobFailed"
        };
        await new ActivityLogRepository(databasePath).AppendAsync(new ActivityLog(
            job.AccountId,
            job.Id,
            decision.ShouldRetry ? "Info" : "Warning",
            eventType,
            $"Job failure classified as {decision.ErrorCode ?? "UNKNOWN"}: {error}",
            DateTimeOffset.UtcNow), cancellationToken);

        return decision;
    }
}

