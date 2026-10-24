using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibInviteLinkService(string databasePath)
{
    public async Task<InviteLink?> PersistCreatedLinkAsync(long accountId, long chatId, string tdlibResponse, CancellationToken cancellationToken = default)
    {
        var link = TdLibInviteLinkResponseParser.Parse(tdlibResponse, accountId, chatId);
        if (link is null) return null;
        await new InviteLinkRepository(databasePath).CreateAsync(link, cancellationToken);
        return link;
    }
}

