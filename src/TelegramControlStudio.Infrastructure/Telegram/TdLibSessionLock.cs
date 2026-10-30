using System.Runtime.Versioning;

namespace TelegramControlStudio.Infrastructure.Telegram;

/// <summary>
/// Cross-process advisory lock for one TDLib session directory. TDLib must never
/// have two clients on the same session database, so only the process holding this
/// mutex may connect that account — the web host in web-first mode, otherwise the
/// desktop app.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TdLibSessionLock : IDisposable
{
    private readonly Mutex _mutex;

    private TdLibSessionLock(Mutex mutex) => _mutex = mutex;

    public static string MutexName(long accountId) => $@"Local\TCS-TDLibSession-{accountId}";

    public static TdLibSessionLock? TryAcquire(long accountId, TimeSpan timeout)
    {
        var mutex = new Mutex(false, MutexName(accountId));
        try
        {
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return null;
            }

            return new TdLibSessionLock(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose() => _mutex.Dispose();
}
