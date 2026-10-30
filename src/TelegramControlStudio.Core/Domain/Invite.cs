namespace TelegramControlStudio.Core.Domain;

public sealed record Invite(
    string Code,
    AppUserRole Role,
    string Note,
    long? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? UsedAt,
    long? UsedBy,
    bool IsRevoked)
{
    public bool IsRedeemable(DateTimeOffset now) =>
        !IsRevoked && UsedAt is null && (ExpiresAt is null || ExpiresAt > now);
}
