using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibJoinRequestSyncHandler(string databasePath, long accountId)
{
    private readonly JoinRequestRepository _repository = new(databasePath);

    public async Task<bool> HandleAsync(string json, CancellationToken cancellationToken = default)
    {
        var request = JoinRequestUpdateParser.TryParse(json, accountId);
        if (request is null) return false;
        await _repository.UpsertAsync(request, cancellationToken);
        return true;
    }
}

