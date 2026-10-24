using System.Text.Json;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibGroupQueryService(ITdLibClient client, string databasePath)
{
    public async Task<IReadOnlyList<TelegramGroup>> SyncMainChatsAsync(long accountId, int limit = 100, CancellationToken cancellationToken = default)
    {
        var response = await client.SendAsync("getChats", new Dictionary<string, object?>
        {
            ["chat_list"] = new Dictionary<string, object?> { ["@type"] = "chatListMain" },
            ["limit"] = Math.Clamp(limit, 1, 1000)
        }, cancellationToken);
        using var document = JsonDocument.Parse(response);
        if (!document.RootElement.TryGetProperty("chat_ids", out var ids) || ids.ValueKind != JsonValueKind.Array) return [];
        var repository = new TelegramGroupRepository(databasePath);
        var groups = new List<TelegramGroup>();
        foreach (var idElement in ids.EnumerateArray())
        {
            if (!idElement.TryGetInt64(out var chatId)) continue;
            var chatJson = await client.SendAsync("getChat", new Dictionary<string, object?> { ["chat_id"] = chatId }, cancellationToken);
            var group = TelegramGroupUpdateParser.Parse(chatJson, accountId);
            if (group is null || group.Kind == "Unknown") continue;
            await repository.UpsertAsync(group, cancellationToken);
            groups.Add(group);
        }
        return groups;
    }
}

