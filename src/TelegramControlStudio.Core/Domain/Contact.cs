namespace TelegramControlStudio.Core.Domain;

public sealed record Contact(
    long AccountId,
    long TelegramUserId,
    string FirstName,
    string LastName,
    string? Username,
    string? PhoneNumber,
    bool HasConsent,
    bool IsOptedOut,
    DateTimeOffset UpdatedAt);
