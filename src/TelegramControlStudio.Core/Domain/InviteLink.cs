namespace TelegramControlStudio.Core.Domain;

public sealed record InviteLink(
    long Id,
    long AccountId,
    long ChatId,
    string InviteUrl,
    DateTimeOffset? ExpiresAt,
    int? UsageLimit,
    bool CreatesJoinRequest,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);

