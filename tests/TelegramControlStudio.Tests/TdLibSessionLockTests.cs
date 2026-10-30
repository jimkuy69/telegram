using System.Runtime.Versioning;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

[SupportedOSPlatform("windows")]
public sealed class TdLibSessionLockTests
{
    [Fact]
    public void SecondAcquireFromAnotherThreadFailsWhileHeldAndSucceedsAfterRelease()
    {
        const long accountId = 990_001;

        var first = TdLibSessionLock.TryAcquire(accountId, TimeSpan.Zero);
        Assert.NotNull(first);
        try
        {
            // A Mutex is recursive for its owning thread, and the thread pool may reuse that
            // very thread — so the contender must be a dedicated, distinct OS thread.
            TdLibSessionLock? contender = null;
            var thread = new Thread(() => { contender = TdLibSessionLock.TryAcquire(accountId, TimeSpan.Zero); });
            thread.Start();
            thread.Join();
            Assert.Null(contender);
        }
        finally
        {
            first!.Dispose();
        }

        var second = TdLibSessionLock.TryAcquire(accountId, TimeSpan.Zero);
        Assert.NotNull(second);
        second!.Dispose();
    }

    [Fact]
    public void MutexNameIsStablePerAccount()
    {
        Assert.Equal(TdLibSessionLock.MutexName(42), TdLibSessionLock.MutexName(42));
        Assert.NotEqual(TdLibSessionLock.MutexName(42), TdLibSessionLock.MutexName(43));
    }
}
