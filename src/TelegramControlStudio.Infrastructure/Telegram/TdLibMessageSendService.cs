using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using System.Text.RegularExpressions;

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
        var request = TdLibMessageRequests.SendText(recipient.TelegramUserId, text, recipient.HasConsent, recipient.IsOptedOut, confirmed);
        var response = await client.SendAsync(request.Function, request.Parameters, cancellationToken);
        if (response.Contains("\"@type\":\"error\"", StringComparison.Ordinal))
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
}
