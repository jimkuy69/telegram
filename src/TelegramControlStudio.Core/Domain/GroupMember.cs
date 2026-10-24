namespace TelegramControlStudio.Core.Domain;

public sealed record GroupMember(
    long AccountId,
    long ChatId,
    long TelegramUserId,
    string DisplayName,
    string? Username,
    string Status,
    DateTimeOffset SyncedAt,
    string SourceNote);

