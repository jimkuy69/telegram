using System.Text.Json;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Core.Telegram;

public static class TelegramGroupUpdateParser
{
    public static TelegramGroup? Parse(string json, long accountId, DateTimeOffset? syncedAt = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var id)) return null;
        var type = chat.TryGetProperty("type", out var typeElement) ? typeElement.GetProperty("@type").GetString() ?? "unknown" : "unknown";
        var kind = type switch { "chatTypeBasicGroup" => "Group", "chatTypeSupergroup" => "Channel", _ => "Unknown" };
        var title = chat.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? "(ไม่มีชื่อ)" : "(ไม่มีชื่อ)";
        int? members = chat.TryGetProperty("member_count", out var count) && count.TryGetInt32(out var value) ? value : null;
        var canView = ReadBool(chat, "can_view_members");
        var canSend = ReadBool(chat, "can_send_messages");
        var canInvite = ReadBool(chat, "can_invite_users");
        var note = members is null ? "UNKNOWN: Telegram ไม่ได้ส่งจำนวนสมาชิก" : "OBSERVED: ข้อมูลจาก Telegram chat object";
        return new TelegramGroup(accountId, id.GetInt64(), title, kind, members, canView, canSend, canInvite, syncedAt ?? DateTimeOffset.UtcNow, note);
    }

    private static bool ReadBool(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

