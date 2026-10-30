using System.Threading.Channels;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

/// <summary>
/// Moves inbound updates onto a worker thread so the TDLib receive loop never waits on SQLite.
/// The callback that feeds this pump runs inside that loop, and a slow disk there would stall
/// authorization updates for the whole session. Overflow is dropped, not queued without limit:
/// a missed consent reply is recoverable, a frozen session is not.
/// </summary>
public sealed class ConsentReplyPump : IAsyncDisposable
{
    private const int Capacity = 1024;
    private readonly Channel<string> _channel;
    private readonly TdLibConsentReplyHandler _handler;
    private readonly Task _loop;
    private long _dropped;

    public ConsentReplyPump(string databasePath, long accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        _handler = new TdLibConsentReplyHandler(databasePath, accountId);
        _loop = Task.Run(() => ProcessAsync(databasePath, accountId));
    }

    public long DroppedCount => Interlocked.Read(ref _dropped);

    public void Enqueue(string json)
    {
        if (!_channel.Writer.TryWrite(json)) Interlocked.Increment(ref _dropped);
    }

    private async Task ProcessAsync(string databasePath, long accountId)
    {
        try
        {
            await foreach (var json in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    await _handler.HandleAsync(json, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // One bad update must not stop the rest of the trail; the message content is
                    // never logged because it can hold anything the recipient typed.
                    await SafeLogFailureAsync(databasePath, accountId);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static Task SafeLogFailureAsync(string databasePath, long accountId) =>
        new ActivityLogRepository(databasePath).AppendAsync(new Core.Domain.ActivityLog(
            accountId, null, "Warning", "ConsentReplyHandlingFailed",
            "An inbound update could not be processed as a consent reply.", DateTimeOffset.UtcNow));

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _loop.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
