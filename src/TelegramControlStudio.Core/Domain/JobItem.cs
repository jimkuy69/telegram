namespace TelegramControlStudio.Core.Domain;

public sealed record JobItem(Guid JobId, long AccountId, long TargetId, string Status, int RetryCount, string IdempotencyKey, string? LastError, DateTimeOffset UpdatedAt, DateTimeOffset? SentAt = null);

