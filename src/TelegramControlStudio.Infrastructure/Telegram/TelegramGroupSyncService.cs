using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TelegramGroupSyncService(string databasePath)
{
    public async Task<TelegramGroup?> SyncChatJsonAsync(long accountId, string chatJson, CancellationToken cancellationToken = default)
    {
        var group = TelegramGroupUpdateParser.Parse(chatJson, accountId);
        if (group is null) return null;
        await new TelegramGroupRepository(databasePath).UpsertAsync(group, cancellationToken);
        return group;
    }
}

