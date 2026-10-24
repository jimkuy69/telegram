namespace TelegramControlStudio.Core.Operations;

public static class InviteLinkPolicy
{
    public static string? Validate(int? usageLimit, DateTimeOffset? expiresAt, bool createsJoinRequest, bool confirmed, bool canInvite)
    {
        if (!confirmed) return "CONFIRMATION_REQUIRED";
        if (!canInvite) return "INVITE_PERMISSION_REQUIRED";
        if (usageLimit is <= 0) return "USAGE_LIMIT_INVALID";
        if (expiresAt is not null && expiresAt <= DateTimeOffset.UtcNow) return "EXPIRATION_MUST_BE_FUTURE";
        if (!createsJoinRequest) return "JOIN_REQUEST_RECOMMENDED";
        return null;
    }
}

