namespace TelegramControlStudio.Infrastructure.Safety;

/// <summary>
/// In-process stop signal so a running send loop can abort between (or during) items.
/// The persisted kill switch remains the durable source of truth; this only shortens
/// the window in which an already-running job keeps sending.
/// </summary>
public static class EmergencyStopSignal
{
    private static readonly object Gate = new();
    private static CancellationTokenSource _source = new();

    public static CancellationToken Token
    {
        get { lock (Gate) return _source.Token; }
    }

    public static void Trigger()
    {
        CancellationTokenSource previous;
        lock (Gate)
        {
            previous = _source;
            _source = new CancellationTokenSource();
            previous.Cancel();
        }
        previous.Dispose();
    }
}
