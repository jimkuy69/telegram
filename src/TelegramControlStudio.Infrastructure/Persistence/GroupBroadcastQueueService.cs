using System.Security.Cryptography;
using System.Text;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed record GroupBroadcastQueueResult(
    Guid JobId,
    int Groups,
    int NotPostableExcluded);

/// <summary>
/// Queues an operator-approved post into several of the account's own groups/channels. There is no
/// consent assertion — posting to your own chat is not a private message — but the kill switch is
/// re-checked by the runner before every post. Groups the last sync marked as not postable are
/// excluded up front and only reported; Telegram stays the final authority and a 403 at send time
/// still lands on the item as a normal failure.
/// </summary>
public sealed class GroupBroadcastQueueService(string databasePath)
{
    public async Task<GroupBroadcastQueueResult> QueueAsync(
        long accountId,
        IReadOnlyCollection<TelegramGroup> selected,
        string messageText,
        string? linkUrl,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        ArgumentException.ThrowIfNullOrWhiteSpace(messageText);
        ArgumentNullException.ThrowIfNull(selected);
        if (!confirmed) throw new InvalidOperationException("Group broadcast confirmation is required.");

        var groups = selected.Where(group => group.AccountId == accountId).ToArray();
        var notPostable = groups.Count(group => !group.CanSendMessages);
        var postable = groups
            .Where(group => group.CanSendMessages)
            .GroupBy(group => group.ChatId)
            .Select(group => group.First())
            .ToArray();
        if (postable.Length == 0)
            throw new InvalidOperationException("No postable group selected.");

        var jobId = await new JobRepository(databasePath).CreateAsync(
            accountId,
            JobTargetTypes.GroupBroadcast,
            postable.Length,
            CreateKey($"group-broadcast:{Guid.NewGuid():D}"),
            messageText.Trim(),
            string.IsNullOrWhiteSpace(linkUrl) ? null : linkUrl.Trim(),
            cancellationToken);
        var items = postable
            .Select(group => new JobItem(
                jobId,
                accountId,
                group.ChatId,
                "Queued",
                0,
                CreateKey($"group-broadcast-item:{jobId:D}:{group.ChatId}"),
                null,
                DateTimeOffset.UtcNow))
            .ToArray();
        await new JobItemRepository(databasePath).CreateAsync(items, cancellationToken);
        await new ActivityLogRepository(databasePath).AppendAsync(new ActivityLog(
            accountId,
            jobId,
            "Info",
            "GroupBroadcastQueued",
            $"Group broadcast queued for {postable.Length} group(s); {notPostable} non-postable row(s) excluded.",
            DateTimeOffset.UtcNow), cancellationToken);
        return new(jobId, postable.Length, notPostable);
    }

    private static string CreateKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
