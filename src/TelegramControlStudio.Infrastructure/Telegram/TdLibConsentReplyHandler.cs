using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public enum ConsentReplyOutcome
{
    Ignored,
    Unanswered,
    NotAnsweringARequest,
    UnknownContact,
    Granted,
    Rejected,
    OptedOut,
    AlreadyDecided
}

/// <summary>
/// Turns an inbound Telegram message into a consent decision. A "yes" only counts when it lands in
/// the same chat as a request this account actually sent, because the alternative — treating any
/// stray "ok" as permission — is how an account ends up messaging people who never agreed.
/// An opt-out needs no such link: refusing to be contacted stands on its own.
/// </summary>
public sealed class TdLibConsentReplyHandler(string databasePath, long accountId)
{
    public async Task<ConsentReplyOutcome> HandleAsync(string json, CancellationToken cancellationToken = default)
    {
        var message = ConsentReplyUpdateParser.TryParse(json);
        if (message is null) return ConsentReplyOutcome.Ignored;

        var decision = ConsentReplyPolicy.Classify(message.Text);
        if (decision.Verdict == ConsentReplyVerdict.Unanswered) return ConsentReplyOutcome.Unanswered;

        var requests = new ConsentRequestRepository(databasePath);
        var contacts = new ContactRepository(databasePath);
        var now = DateTimeOffset.UtcNow;

        if (decision.Verdict == ConsentReplyVerdict.OptedOut)
        {
            if (!await contacts.SetConsentAsync(accountId, message.SenderUserId, false, true, cancellationToken))
                return ConsentReplyOutcome.UnknownContact;
            await RecordAsync(requests, ConsentRequestKinds.OptedOut, message, decision.MatchedKeyword, null, now, cancellationToken);
            await LogAsync("ConsentOptedOut", message, $"Contact {message.SenderUserId} opted out via Telegram; sending is blocked for them.");
            return ConsentReplyOutcome.OptedOut;
        }

        var request = await requests.LatestRequestInChatAsync(accountId, message.SenderUserId, message.ChatId, cancellationToken);
        if (request is null) return ConsentReplyOutcome.NotAnsweringARequest;

        if (decision.Verdict == ConsentReplyVerdict.Rejected)
        {
            // Recorded whatever the earlier answer was: a refusal is evidence the operator should be
            // able to see, but only an opt-out withdraws permission automatically.
            await RecordAsync(requests, ConsentRequestKinds.Rejected, message, decision.MatchedKeyword, request.TemplateId, now, cancellationToken);
            await LogAsync("ConsentDeclined", message, $"Contact {message.SenderUserId} declined the opt-in request; consent was not granted.");
            return ConsentReplyOutcome.Rejected;
        }

        // A second "ok" must not duplicate the grant, and an opt-out must not be quietly reversed by
        // an unrelated "ok" in the same chat.
        var latest = await requests.LatestForContactAsync(accountId, message.SenderUserId, cancellationToken);
        if (latest is not null &&
            latest.OccurredAt >= request.OccurredAt &&
            latest.Kind is ConsentRequestKinds.Granted or ConsentRequestKinds.OptedOut)
            return ConsentReplyOutcome.AlreadyDecided;

        if (!await contacts.SetConsentAsync(accountId, message.SenderUserId, true, false, cancellationToken))
            return ConsentReplyOutcome.UnknownContact;
        await RecordAsync(requests, ConsentRequestKinds.Granted, message, decision.MatchedKeyword, request.TemplateId, now, cancellationToken);
        await LogAsync("ConsentGranted", message, $"Contact {message.SenderUserId} accepted the opt-in request; consent recorded.");
        return ConsentReplyOutcome.Granted;
    }

    private Task RecordAsync(
        ConsentRequestRepository requests,
        string kind,
        InboundTelegramMessage message,
        string? matchedKeyword,
        long? templateId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        requests.AppendAsync(new ConsentRequest(
            0,
            accountId,
            message.SenderUserId,
            kind,
            occurredAt,
            message.MessageId,
            message.ChatId,
            templateId,
            null,
            matchedKeyword,
            ConsentRequestSources.TelegramMessage), cancellationToken);

    private Task LogAsync(string eventType, InboundTelegramMessage message, string text) =>
        new ActivityLogRepository(databasePath).AppendAsync(new ActivityLog(
            accountId, null, "Info", eventType,
            $"{text} Message {message.MessageId} in chat {message.ChatId}; only the matched keyword was stored.",
            DateTimeOffset.UtcNow));
}
