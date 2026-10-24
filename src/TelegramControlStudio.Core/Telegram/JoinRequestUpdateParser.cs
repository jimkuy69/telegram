using System.Text.Json;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Core.Telegram;

public static class JoinRequestUpdateParser
{
    public static JoinRequest? TryParse(string json, long accountId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("@type", out var type) || type.GetString() != "updateChatJoinRequest") return null;
        if (!root.TryGetProperty("chat_id", out var chatId) || !root.TryGetProperty("user_id", out var userId)) return null;
        return new JoinRequest(
            0,
            accountId,
            chatId.GetInt64(),
            userId.GetInt64(),
            "Pending",
            DateTimeOffset.UtcNow,
            null,
            null);
    }
}

