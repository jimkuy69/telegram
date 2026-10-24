namespace TelegramControlStudio.Core.Telegram;

public static class TdLibInviteRequests
{
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
}

