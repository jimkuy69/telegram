using System.Text.Json;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Core.Telegram;

public static class TdLibInviteLinkResponseParser
{
    public static InviteLink? Parse(string json, long accountId, long expectedChatId)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (!root.TryGetProperty("invite_link", out var link) || !link.TryGetProperty("invite_link", out var url)) return null;
        if (!link.TryGetProperty("chat_id", out var chatId) || chatId.GetInt64() != expectedChatId) return null;
        int? limit = link.TryGetProperty("member_limit", out var memberLimit) && memberLimit.TryGetInt32(out var parsedLimit) ? parsedLimit : null;
        DateTimeOffset? expires = link.TryGetProperty("expiration_date", out var expiration) && expiration.TryGetInt64(out var seconds) && seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        var createsJoinRequest = link.TryGetProperty("creates_join_request", out var join) && join.ValueKind == JsonValueKind.True;
        return new InviteLink(0, accountId, expectedChatId, url.GetString() ?? throw new InvalidDataException("Invite URL is missing."), expires, limit, createsJoinRequest, DateTimeOffset.UtcNow, null);
    }
}

