namespace TelegramControlStudio.Core.Domain;

/// <summary>
/// One append-only consent event: what was asked, when, and how the person answered.
/// This is the evidence trail behind a contact's consent flag, so rows are never updated.
/// Reply content is deliberately not stored — only the keyword that matched and the message id.
/// <c>ChatId</c> is the chat the event happened in; a reply only answers our request when it
/// arrives in the same chat, so a group comment cannot be mistaken for consent.
/// </summary>
public sealed record ConsentRequest(
    long Id,
    long AccountId,
    long TelegramUserId,
    string Kind,
    DateTimeOffset OccurredAt,
    long? MessageId,
    long? ChatId,
    long? TemplateId,
    string? BodySha256,
    string? MatchedKeyword,
    string Source);

public static class ConsentRequestKinds
{
    public const string Requested = "Requested";
    public const string Granted = "Granted";
    public const string OptedOut = "OptedOut";
    public const string Rejected = "Rejected";
}

public static class ConsentRequestSources
{
    public const string JobRunner = "job-runner";
    public const string TelegramMessage = "tdlib-message";
    public const string OperatorInterface = "operator-ui";
}
