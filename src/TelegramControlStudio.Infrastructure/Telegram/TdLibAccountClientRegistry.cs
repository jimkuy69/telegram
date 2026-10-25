using System.Collections.Concurrent;
using TelegramControlStudio.Core.Telegram;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibAccountClientRegistry
{
    private readonly ConcurrentDictionary<long, ITdLibClient> _clients = new();
    public int Count => _clients.Count;

    public bool Register(long accountId, ITdLibClient client)
    {
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        ArgumentNullException.ThrowIfNull(client);
        return _clients.TryAdd(accountId, client);
    }

    public bool TryGet(long accountId, out ITdLibClient? client) => _clients.TryGetValue(accountId, out client);
    public bool IsRegistered(long accountId) => _clients.ContainsKey(accountId);

    public async Task<bool> RemoveAsync(long accountId)
    {
        if (!_clients.TryRemove(accountId, out var client)) return false;
        await client.DisposeAsync();
        return true;
    }

    public async Task DisposeAllAsync()
    {
        foreach (var accountId in _clients.Keys.ToArray())
            await RemoveAsync(accountId);
    }
}
