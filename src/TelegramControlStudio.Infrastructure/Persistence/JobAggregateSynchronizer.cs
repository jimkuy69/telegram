using TelegramControlStudio.Core.Operations;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed class JobAggregateSynchronizer(string databasePath)
{
    public async Task<bool> SynchronizeAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var items = await new JobItemRepository(databasePath).ListAsync(jobId, cancellationToken);
        if (items.Count == 0) return false;
        var status = JobAggregateStatusPolicy.FromItems(items);
        return await new JobRepository(databasePath).UpdateStatusAsync(jobId, status, cancellationToken: cancellationToken);
    }
}

