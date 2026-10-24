namespace TelegramControlStudio.Core.Domain;

public sealed record ActivityLog(
    long AccountId,
    Guid? JobId,
    string Level,
    string EventType,
    string Message,
    DateTimeOffset CreatedAt);
