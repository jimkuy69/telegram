namespace TelegramControlStudio.Core.Domain;

public sealed record Campaign(
    Guid Id,
    long AccountId,
    long TemplateId,
    string Name,
    string ConsentPolicy,
    DateTimeOffset CreatedAt);
