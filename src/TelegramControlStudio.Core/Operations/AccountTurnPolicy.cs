using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Core.Operations;

/// <summary>
/// Shares the auto runner between the accounts that are signed in at the same time. The
/// runner serves accounts in a rotating order and takes one job per account per pass, so a
/// long queue on one account cannot starve the others and no two accounts transmit together.
/// </summary>
public static class AccountTurnPolicy
{
    public const int JobsPerAccountPerPass = 1;

    public static IReadOnlyList<long> RotateForFairness(IReadOnlyCollection<long> accountIds, long? lastServedAccountId)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        var ordered = accountIds.Distinct().Order().ToArray();
        var servedIndex = lastServedAccountId is { } id ? Array.IndexOf(ordered, id) : -1;
        return Enumerable.Range(0, ordered.Length)
            .Select(offset => ordered[(servedIndex + 1 + offset) % ordered.Length])
            .ToArray();
    }

    public static IReadOnlyList<Job> SelectForPass(IReadOnlyList<Job> runnableJobs, DateTimeOffset now, int limit = JobsPerAccountPerPass)
    {
        ArgumentNullException.ThrowIfNull(runnableJobs);
        if (limit <= 0) return [];
        return runnableJobs
            .Where(job => AutoRunPolicy.IsEligible(job, now))
            .OrderBy(job => job.CreatedAt)
            .ThenBy(job => job.Id)
            .Take(limit)
            .ToArray();
    }
}
