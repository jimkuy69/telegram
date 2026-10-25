using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibJoinRequestDecisionService(string databasePath)
{
    public async Task DecideAsync(ITdLibClient client, JoinRequest request, string decision, string reason, bool confirmed, bool canInvite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (request.Status != "Pending") throw new InvalidOperationException("REQUEST_NOT_PENDING");
        var approve = string.Equals(decision, "Approved", StringComparison.Ordinal);
        if (!approve && !string.Equals(decision, "Rejected", StringComparison.Ordinal)) throw new ArgumentException("INVALID_DECISION", nameof(decision));
        var tdRequest = TdLibInviteRequests.ProcessChatJoinRequest(request.ChatId, request.TelegramUserId, approve, reason, confirmed, canInvite);
        var response = await client.SendAsync(tdRequest.Function, tdRequest.Parameters, cancellationToken);
        if (!response.Contains("\"@type\":\"ok\"", StringComparison.Ordinal)) throw new InvalidOperationException("TDLIB_JOIN_REQUEST_NOT_CONFIRMED");
        if (!await new JoinRequestRepository(databasePath).DecideAsync(request.AccountId, request.ChatId, request.TelegramUserId, decision, reason, cancellationToken))
            throw new InvalidOperationException("REQUEST_NOT_PENDING");
        await new ActivityLogRepository(databasePath).AppendAsync(new ActivityLog(request.AccountId, null, "Info", $"JoinRequest{decision}", $"Join request decision recorded for chat_id={request.ChatId}.", DateTimeOffset.UtcNow), cancellationToken);
    }
}

