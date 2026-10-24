namespace TelegramControlStudio.Core.Domain;

public sealed record MessageTemplate(
    long Id,
    long AccountId,
    string Name,
    string Language,
    string Body,
    string? Link,
    int Version,
    bool IsActive,
    DateTimeOffset UpdatedAt);
