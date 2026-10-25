using System.Text.Json;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Core.Telegram;

public static class TdLibGroupMemberParser
{
    public static long? ParseSupergroupId(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("type", out var type) ||
            !type.TryGetProperty("supergroup_id", out var id) ||
            !id.TryGetInt64(out var value) || value <= 0) return null;
        return value;
    }

    public static IReadOnlyList<long> ParseMemberUserIds(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("members", out var members) || members.ValueKind != JsonValueKind.Array) return [];
        return members.EnumerateArray()
            .Select(member => member.TryGetProperty("user_id", out var id) && id.TryGetInt64(out var value) ? value : 0)
            .Where(id => id > 0).Distinct().ToArray();
    }

    public static GroupMember? ParseUser(string json, long accountId, long chatId, DateTimeOffset syncedAt)
    {
        var contact = TdLibContactParser.ParseUser(json, accountId);
        if (contact is null) return null;
        var displayName = string.Join(' ', new[] { contact.FirstName, contact.LastName }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        return new GroupMember(accountId, chatId, contact.TelegramUserId, displayName, contact.Username, "Observed", syncedAt, "TDLib getSupergroupMembers");
    }
}
