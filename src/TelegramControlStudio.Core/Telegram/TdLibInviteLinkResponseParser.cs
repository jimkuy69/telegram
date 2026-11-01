using System.Text.Json;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Core.Telegram;

public static class TdLibInviteLinkResponseParser
{
    /// <summary>
    /// Reads the <c>chatInviteLink</c> object TDLib returns from create/edit/revoke calls. The link is
    /// a plain string and the object carries no chat id, so the chat the request was sent for is the
    /// one recorded here.
    /// </summary>
    public static InviteLink? Parse(string json, long accountId, long chatId)
    {
        if (chatId == 0) throw new ArgumentOutOfRangeException(nameof(chatId));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("invite_link", out var link) || link.ValueKind != JsonValueKind.String) return null;
        var url = link.GetString();
        if (string.IsNullOrWhiteSpace(url)) return null;
        int? limit = root.TryGetProperty("member_limit", out var memberLimit) && memberLimit.TryGetInt32(out var parsedLimit) && parsedLimit > 0 ? parsedLimit : null;
        DateTimeOffset? expires = root.TryGetProperty("expiration_date", out var expiration) && expiration.TryGetInt64(out var seconds) && seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        var createsJoinRequest = root.TryGetProperty("creates_join_request", out var join) && join.ValueKind == JsonValueKind.True;
        var isRevoked = root.TryGetProperty("is_revoked", out var revoked) && revoked.ValueKind == JsonValueKind.True;
        return new InviteLink(0, accountId, chatId, url, expires, limit, createsJoinRequest, DateTimeOffset.UtcNow, isRevoked ? DateTimeOffset.UtcNow : null);
    }
}
