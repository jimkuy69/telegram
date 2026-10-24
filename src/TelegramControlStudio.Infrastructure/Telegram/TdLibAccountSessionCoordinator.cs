using TelegramControlStudio.Core.Telegram;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibAccountSessionCoordinator(TdLibAccountClientRegistry clients)
{
    public bool IsConnected(long accountId) => clients.TryGet(accountId, out _);

    public void Attach(long accountId, ITdLibClient client)
    {
        if (!clients.Register(accountId, client))
            throw new InvalidOperationException($"A TDLib client is already attached to account {accountId}.");
    }

    public Task<bool> DetachAsync(long accountId) => clients.RemoveAsync(accountId);

    public async Task<string> SendAsync(long accountId, string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        if (!clients.TryGet(accountId, out var client) || client is null)
            throw new InvalidOperationException($"Account {accountId} has no authenticated TDLib client.");
        return await client.SendAsync(function, parameters, cancellationToken);
    }
}