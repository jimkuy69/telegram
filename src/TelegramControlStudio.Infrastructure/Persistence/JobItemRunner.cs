using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class JobItemRunner(string databasePath)
{
    public async Task RunAsync(
        Job job,
        Func<JobItem, CancellationToken, Task> executeAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(executeAsync);

        var items = await new JobItemRepository(databasePath).ListAsync(job.Id, cancellationToken);
        var itemRepository = new JobItemRepository(databasePath);
        var failureHandler = new JobFailureHandler(databasePath);
        var waitingForFloodWait = false;

        foreach (var item in items.Where(item => item.Status is "Queued" or "Failed"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await itemRepository.UpdateStatusAsync(job.Id, item.TargetId, "Running", cancellationToken: cancellationToken)) continue;

            try
            {
                await executeAsync(item with { Status = "Running" }, cancellationToken);
                await itemRepository.UpdateStatusAsync(job.Id, item.TargetId, "Completed", cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await itemRepository.UpdateStatusAsync(job.Id, item.TargetId, "Queued", "Cancelled by user", cancellationToken: CancellationToken.None);
                throw;
            }
            catch (Exception exception)
            {
                await itemRepository.UpdateStatusAsync(job.Id, item.TargetId, "Failed", exception.Message, incrementRetry: true, cancellationToken: cancellationToken);
                var decision = await failureHandler.HandleAsync(job, exception.Message, cancellationToken);
                waitingForFloodWait = decision.NextStatus == JobStatus.WaitingForFloodWait;
                if (decision.NextStatus == JobStatus.WaitingForFloodWait || !decision.ShouldRetry) break;
            }
        }

        if (!waitingForFloodWait)
            await new JobAggregateSynchronizer(databasePath).SynchronizeAsync(job.Id, cancellationToken);
    }
}

