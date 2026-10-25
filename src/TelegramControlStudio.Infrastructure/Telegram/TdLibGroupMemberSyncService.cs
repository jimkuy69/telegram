using System.Text.Json;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibGroupMemberSyncService(ITdLibClient client, string databasePath)
{
    public async Task<int> SyncAsync(long accountId, long chatId, int offset = 0, int limit = 200, CancellationToken cancellationToken = default)
    {
        var chatJson = await client.SendAsync("getChat", new Dictionary<string, object?> { ["chat_id"] = chatId }, cancellationToken);
        var supergroupId = TdLibGroupMemberParser.ParseSupergroupId(chatJson)
            ?? throw new InvalidOperationException("TELEGRAM_MEMBER_LIST_NOT_AVAILABLE");
        var response = await client.SendAsync("getSupergroupMembers", new Dictionary<string, object?>
        {
            ["supergroup_id"] = supergroupId,
            ["filter"] = new Dictionary<string, object?> { ["@type"] = "supergroupMembersFilterRecent" },
            ["offset"] = Math.Max(0, offset),
            ["limit"] = Math.Clamp(limit, 1, 200)
        }, cancellationToken);
        var ids = TdLibGroupMemberParser.ParseMemberUserIds(response);
        var syncedAt = DateTimeOffset.UtcNow;
        var repository = new GroupMemberRepository(databasePath);
        var count = 0;
        foreach (var userId in ids)
        {
            var userJson = await client.SendAsync("getUser", new Dictionary<string, object?> { ["user_id"] = userId }, cancellationToken);
            var member = TdLibGroupMemberParser.ParseUser(userJson, accountId, chatId, syncedAt);
            if (member is null) continue;
            await repository.UpsertAsync(member, cancellationToken);
            count++;
        }
        return count;
    }
}
