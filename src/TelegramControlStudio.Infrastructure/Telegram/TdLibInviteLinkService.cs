using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibInviteLinkService(string databasePath)
{
    public async Task RevokeAsync(ITdLibClient client, long accountId, long chatId, string inviteUrl, bool confirmed, bool canInvite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var request = TdLibInviteRequests.RevokeChatInviteLink(chatId, inviteUrl, confirmed, canInvite);
        var response = await client.SendAsync(request.Function, request.Parameters, cancellationToken);
        if (!response.Contains(""@type":"ok"", StringComparison.Ordinal))
            throw new InvalidOperationException("TDLib did not confirm invite link revocation.");
        if (!await new InviteLinkRepository(databasePath).MarkRevokedAsync(accountId, inviteUrl, DateTimeOffset.UtcNow, cancellationToken))
            throw new InvalidOperationException("Invite link was not found or was already revoked.");
        await new ActivityLogRepository(databasePath).AppendAsync(new ActivityLog(
            accountId, null, "Info", "InviteLinkRevoked", $"Invite link revoked for chat_id={chatId}.", DateTimeOffset.UtcNow), cancellationToken);
    }

    public async Task<InviteLink> CreateAndPersistAsync(
        ITdLibClient client,
        long accountId,
        long chatId,
        DateTimeOffset? expiresAt,
        int? usageLimit,
        bool createsJoinRequest,
        bool confirmed,
        bool canInvite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var request = TdLibInviteRequests.CreateChatInviteLink(chatId, expiresAt, usageLimit, createsJoinRequest, confirmed, canInvite);
        var response = await client.SendAsync(request.Function, request.Parameters, cancellationToken);
        var link = await PersistCreatedLinkAsync(accountId, chatId, response, cancellationToken);
        return link ?? throw new InvalidOperationException("TDLib did not return a valid invite link response.");
    }

    public async Task<InviteLink?> PersistCreatedLinkAsync(long accountId, long chatId, string tdlibResponse, CancellationToken cancellationToken = default)
    {
        var link = TdLibInviteLinkResponseParser.Parse(tdlibResponse, accountId, chatId);
        if (link is null) return null;
        await new InviteLinkRepository(databasePath).CreateAsync(link, cancellationToken);
        return link;
    }
}