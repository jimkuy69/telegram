namespace TelegramControlStudio.Core.Domain;

public sealed record Job(
    Guid Id,
    long AccountId,
    string TargetType,
    int TargetCount,
    JobStatus Status,
    int RetryCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FloodWaitUntil,
    string? LastError,
    string IdempotencyKey,
    string MessageText,
    string? LinkUrl);
