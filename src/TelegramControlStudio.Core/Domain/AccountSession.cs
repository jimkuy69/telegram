namespace TelegramControlStudio.Core.Domain;

public sealed record AccountSession(
    long AccountId,
    string SessionDirectory,
    string AuthorizationState,
    bool IsAuthorized,
    DateTimeOffset UpdatedAt);
