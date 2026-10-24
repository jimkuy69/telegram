namespace TelegramControlStudio.Core.Domain;

public sealed record Account(
    long Id,
    string DisplayName,
    string PhoneNumber,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSyncAt);
