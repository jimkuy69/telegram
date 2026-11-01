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

    // TDLib describes a member with a MessageSender, so the id lives under member_id; the flat
    // user_id is what older tdjson builds returned. Reading only the flat field silently yields an
    // empty list, which looked like "this group has no members" instead of a parsing gap.
    public static IReadOnlyList<long> ParseMemberUserIds(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("members", out var members) || members.ValueKind != JsonValueKind.Array) return [];
        return members.EnumerateArray()
            .Select(ReadUserId)
            .Where(id => id > 0).Distinct().ToArray();
    }

    private static long ReadUserId(JsonElement member)
    {
        if (member.TryGetProperty("member_id", out var sender) && sender.ValueKind == JsonValueKind.Object)
        {
            // A channel posting as itself is not a user, and getUser would only answer with an error.
            if (sender.TryGetProperty("@type", out var senderType) && senderType.ValueKind == JsonValueKind.String &&
                !string.Equals(senderType.GetString(), "messageSenderUser", StringComparison.Ordinal))
                return 0;
            return sender.TryGetProperty("user_id", out var senderUserId) && senderUserId.TryGetInt64(out var senderValue)
                ? senderValue
                : 0;
        }
        return member.TryGetProperty("user_id", out var userId) && userId.TryGetInt64(out var value) ? value : 0;
    }

    public static GroupMember? ParseUser(string json, long accountId, long chatId, DateTimeOffset syncedAt)
    {
        var contact = TdLibContactParser.ParseUser(json, accountId);
        if (contact is null) return null;
        var displayName = string.Join(' ', new[] { contact.FirstName, contact.LastName }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        return new GroupMember(accountId, chatId, contact.TelegramUserId, displayName, contact.Username, "Observed", syncedAt, "TDLib getSupergroupMembers");
    }
}
