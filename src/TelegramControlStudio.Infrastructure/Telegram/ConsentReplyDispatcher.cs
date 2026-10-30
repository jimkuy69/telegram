using System.Collections.Concurrent;

namespace TelegramControlStudio.Infrastructure.Telegram;

/// <summary>
/// Owns the inbound-update pumps for the accounts that are currently connected. The session hands
/// every update it does not recognise to <see cref="SinkFor"/>, which returns immediately; the
/// consent work happens on the pump's own thread and stops with the account.
/// </summary>
public sealed class ConsentReplyDispatcher(string databasePath) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<long, ConsentReplyPump> _pumps = new();

    public Action<string> SinkFor(long accountId)
    {
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        return json => _pumps.GetOrAdd(accountId, id => new ConsentReplyPump(databasePath, id)).Enqueue(json);
    }

    public async Task StopAsync(long accountId)
    {
        if (!_pumps.TryRemove(accountId, out var pump)) return;
        await pump.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var accountId in _pumps.Keys.ToArray())
            await StopAsync(accountId);
    }
}
