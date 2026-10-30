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
        if (IsTdLibError(response))
        {
            var floodMatch = Regex.Match(response, "FLOOD_WAIT_(?<seconds>\\d+)", RegexOptions.CultureInvariant);
            if (floodMatch.Success && int.TryParse(floodMatch.Groups["seconds"].Value, out var seconds))
            {
                await LogAsync(recipient.AccountId, "Waiting", "FloodWait", "TDLib requested a FloodWait; secret data was not logged.", cancellationToken);
                throw new TdLibFloodWaitException(seconds);
            }
            await LogAsync(recipient.AccountId, "Error", "MessageSendFailed", "TDLib returned an error for a consented recipient.", cancellationToken);
            throw new InvalidOperationException("TDLIB_MESSAGE_SEND_FAILED");
        }
        await LogAsync(recipient.AccountId, "Info", "MessageSent", "Message sent to a consented recipient.", cancellationToken);
        return response;
    }

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
