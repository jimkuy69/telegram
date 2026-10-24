namespace TelegramControlStudio.Core.Domain;

public sealed record CampaignRecipient(
    Guid CampaignId,
    long AccountId,
    long TelegramUserId,
    string Status,
    int RetryCount,
    string IdempotencyKey,
    string? LastError);
