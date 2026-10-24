using TelegramControlStudio.Core.Telegram;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibClientLifecycle(
    TdLibJsonClient client,
    TdLibUpdateRouter updateRouter,
    Func<CancellationToken, Task<string?>> receiveAsync,
    Func<string, CancellationToken, Task>? updateHandler = null)
{
    private CancellationTokenSource? _stopSource;
    private Task? _receiveTask;

    public bool IsRunning => _receiveTask is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning) return;
        _stopSource = new CancellationTokenSource();
        _receiveTask = ReceiveLoopAsync(_stopSource.Token);
    }

    public async Task StopAsync()
    {
        if (_stopSource is null) return;
        await _stopSource.CancelAsync();
        if (_receiveTask is not null)
        {
            try { await _receiveTask; }
            catch (OperationCanceledException) when (_stopSource.IsCancellationRequested) { }
        }
        _stopSource.Dispose();
        _stopSource = null;
        _receiveTask = null;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var json = await receiveAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(json)) continue;
            if (!client.HandleResponse(json))
            {
                updateRouter.Route(json);
                if (updateHandler is not null) await updateHandler(json, cancellationToken);
            }
        }
    }
}

