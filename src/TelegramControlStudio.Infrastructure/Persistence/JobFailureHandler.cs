using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class JobFailureHandler(string databasePath)
{
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

        return decision;
    }
}

