namespace TelegramControlStudio.Core.Telegram;

public static class TdLibInviteRequests
{
    public static TdLibRequest RevokeChatInviteLink(long chatId, string inviteUrl, bool confirmed, bool canInvite)
    {
        if (!confirmed) throw new InvalidOperationException("CONFIRMATION_REQUIRED");
        if (!canInvite) throw new InvalidOperationException("INVITE_PERMISSION_REQUIRED");
        ArgumentException.ThrowIfNullOrWhiteSpace(inviteUrl);
        if (chatId == 0) throw new ArgumentOutOfRangeException(nameof(chatId));
        return new TdLibRequest("revokeChatInviteLink", new Dictionary<string, object?>
        {
            ["chat_id"] = chatId, ["invite_link"] = inviteUrl
        }, "revoke-invite-link");
    }

    public static TdLibRequest CreateChatInviteLink(long chatId, DateTimeOffset? expiresAt, int? usageLimit, bool createsJoinRequest, bool confirmed, bool canInvite)
    {
        var validation = Operations.InviteLinkPolicy.Validate(usageLimit, expiresAt, createsJoinRequest, confirmed, canInvite);
        if (validation is not null && validation != "JOIN_REQUEST_RECOMMENDED") throw new InvalidOperationException(validation);
        if (chatId == 0) throw new ArgumentOutOfRangeException(nameof(chatId));
        var parameters = new Dictionary<string, object?> { ["chat_id"] = chatId, ["name"] = "Telegram Control Studio", ["creates_join_request"] = createsJoinRequest };
        if (expiresAt is not null) parameters["expiration_date"] = expiresAt.Value.ToUnixTimeSeconds();
        if (usageLimit is not null) parameters["member_limit"] = usageLimit.Value;
        return new TdLibRequest("createChatInviteLink", parameters, "invite-link");
    }

    public static TdLibRequest ProcessChatJoinRequest(long chatId, long userId, bool approve, string reason, bool confirmed, bool canInvite)
    {
        if (!confirmed) throw new InvalidOperationException("CONFIRMATION_REQUIRED");
        if (!canInvite) throw new InvalidOperationException("INVITE_PERMISSION_REQUIRED");
        if (chatId == 0 || userId == 0) throw new ArgumentOutOfRangeException(nameof(chatId));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new TdLibRequest("processChatJoinRequest", new Dictionary<string, object?>
        {
            ["chat_id"] = chatId, ["user_id"] = userId, ["approve"] = approve
        }, $"join-request-{(approve ? "approve" : "reject")}");
    }
}

