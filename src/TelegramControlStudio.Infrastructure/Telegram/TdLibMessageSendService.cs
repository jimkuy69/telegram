using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibMessageSendService(string databasePath)
{
    public async Task<string> SendTextAsync(
        ITdLibClient client,
        Contact recipient,
        string text,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(recipient);

        var safety = await new SendSafetyRepository(databasePath).GetAsync(cancellationToken);
        var decision = SendSafetyPolicy.Evaluate(safety, recipient.TelegramUserId, recipient.HasConsent, recipient.IsOptedOut);
        if (!decision.CanSend)
        {
            await LogAsync(recipient.AccountId, decision.Verdict == SendSafetyVerdict.Block ? "Warning" : "Info", "SendBlocked", $"Send blocked before transmission: {decision.ReasonCode}.", cancellationToken);
            throw new SendSafetyBlockedException(decision.ReasonCode, decision.Verdict);
        }

        var request = TdLibMessageRequests.SendText(recipient.TelegramUserId, text, recipient.HasConsent, recipient.IsOptedOut, confirmed);
        var response = await client.SendAsync(request.Function, request.Parameters, cancellationToken);
        await EnsureNoTdLibErrorAsync(response, recipient.AccountId, cancellationToken);
        await LogAsync(recipient.AccountId, "Info", "MessageSent", "Message sent to a consented recipient.", cancellationToken);
        return response;
    }

    /// <summary>
    /// Sends the one message that is allowed to precede consent, and records it as evidence.
    /// Everything here is re-read from the database rather than taken from the caller, because an
    /// unbounded opt-in blast is the fastest way to get an account restricted.
    /// </summary>
    public async Task<string> SendConsentRequestAsync(
        ITdLibClient client,
        Contact recipient,
        string text,
        long? templateId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var safety = await new SendSafetyRepository(databasePath).GetAsync(cancellationToken);
        var limits = await new ConsentRequestSettingsRepository(databasePath).GetAsync(cancellationToken);
        var requests = new ConsentRequestRepository(databasePath);
        var now = DateTimeOffset.UtcNow;
        var latestEvent = await requests.LatestForContactAsync(recipient.AccountId, recipient.TelegramUserId, cancellationToken);
        var eligibility = new ConsentRequestEligibility(
            recipient.TelegramUserId,
            !string.IsNullOrWhiteSpace(recipient.PhoneNumber),
            recipient.HasConsent,
            recipient.IsOptedOut,
            await requests.LastRequestedAtAsync(recipient.AccountId, recipient.TelegramUserId, cancellationToken),
            await requests.CountRequestedSinceAsync(recipient.AccountId, now.AddDays(-1), cancellationToken),
            limits.CooldownDays,
            limits.DailyLimit,
            now,
            latestEvent?.Kind == ConsentRequestKinds.Rejected);

        var decision = SendSafetyPolicy.EvaluateConsentRequest(safety, eligibility);
        if (!decision.CanSend)
        {
            await LogAsync(recipient.AccountId, decision.Verdict == SendSafetyVerdict.Block ? "Warning" : "Info", "SendBlocked", $"Consent request blocked before transmission: {decision.ReasonCode}.", cancellationToken);
            throw new SendSafetyBlockedException(decision.ReasonCode, decision.Verdict);
        }

        var request = TdLibMessageRequests.SendText(
            recipient.TelegramUserId, text, recipient.HasConsent, recipient.IsOptedOut, confirmed: true, SendPurpose.ConsentRequest);
        var response = await client.SendAsync(request.Function, request.Parameters, cancellationToken);
        await EnsureNoTdLibErrorAsync(response, recipient.AccountId, cancellationToken);

        await requests.AppendAsync(new ConsentRequest(
            0,
            recipient.AccountId,
            recipient.TelegramUserId,
            ConsentRequestKinds.Requested,
            now,
            TryMessageId(response),
            TryChatId(response) ?? recipient.TelegramUserId,
            templateId,
            BodyFingerprint(text),
            null,
            ConsentRequestSources.JobRunner), cancellationToken);
        await LogAsync(recipient.AccountId, "Info", "ConsentRequested", "Opt-in request sent and recorded as consent evidence.", cancellationToken);
        return response;
    }

    private async Task EnsureNoTdLibErrorAsync(string response, long accountId, CancellationToken cancellationToken)
    {
        if (!IsTdLibError(response)) return;
        var floodMatch = Regex.Match(response, "FLOOD_WAIT_(?<seconds>\\d+)", RegexOptions.CultureInvariant);
        if (floodMatch.Success && int.TryParse(floodMatch.Groups["seconds"].Value, out var seconds))
        {
            await LogAsync(accountId, "Waiting", "FloodWait", "TDLib requested a FloodWait; secret data was not logged.", cancellationToken);
            throw new TdLibFloodWaitException(seconds);
        }
        await LogAsync(accountId, "Error", "MessageSendFailed", "TDLib returned an error for a recipient that passed the safety gate.", cancellationToken);
        throw new InvalidOperationException("TDLIB_MESSAGE_SEND_FAILED");
    }

    private static long? TryMessageId(string response) => TryReadId(response, "id");

    private static long? TryChatId(string response) => TryReadId(response, "chat_id");

    /// <summary>
    /// Reads an identifier from a TDLib response. Newer TDLib builds wrap ids in an object
    /// ({"@type":"chatId","id":123}), so both the bare number and the wrapped form are accepted.
    /// </summary>
    private static long? TryReadId(string response, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            if (!document.RootElement.TryGetProperty(property, out var value)) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var direct)) return direct;
            return value.TryGetProperty("id", out var wrapped) && wrapped.TryGetInt64(out var inner) ? inner : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BodyFingerprint(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private Task LogAsync(long accountId, string level, string eventType, string message, CancellationToken cancellationToken) =>
        new ActivityLogRepository(databasePath).AppendAsync(new ActivityLog(accountId, null, level, eventType, message, DateTimeOffset.UtcNow), cancellationToken);

    private static bool IsTdLibError(string response)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            return document.RootElement.TryGetProperty("@type", out var type) &&
                   string.Equals(type.GetString(), "error", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
