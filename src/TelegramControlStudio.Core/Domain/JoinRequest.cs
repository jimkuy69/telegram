namespace TelegramControlStudio.Core.Domain;

public sealed record JoinRequest(
    long Id,
    long AccountId,
    long ChatId,
    long TelegramUserId,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecisionAt,
    string? DecisionReason);

