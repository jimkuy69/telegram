using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

/// <summary>
/// Attaches one verified native TDLib session to one local account.
/// Secrets are accepted in memory only and are never logged or persisted here.
/// </summary>
public sealed class TdLibSessionBootstrapper(TdLibAccountClientRegistry registry, AccountRepository accounts)
{
    public async Task<TdLibNativeSession> ConnectAsync(
        long accountId,
        string nativeDirectory,
        string expectedSha256,
        int apiId,
        string apiHash,
        string sessionDirectory,
        CancellationToken cancellationToken = default)
    {
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        if (apiId <= 0) throw new ArgumentOutOfRangeException(nameof(apiId));
        ArgumentException.ThrowIfNullOrWhiteSpace(apiHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        Directory.CreateDirectory(sessionDirectory);
        var machine = new AuthorizationStateMachine();
        var router = new TdLibUpdateRouter(machine);
        var bridge = new TdLibSessionStateBridge(accounts.DatabasePath, accountId, sessionDirectory, machine);
        bridge.Attach();
        var parameters = TdLibParametersBuilder.Build(apiId, apiHash, sessionDirectory, Path.Combine(sessionDirectory, "files"));
        try
        {
            var session = await TdLibNativeSession.CreateAsync(nativeDirectory, expectedSha256, parameters, router, null, cancellationToken);
            if (!registry.Register(accountId, session))
            {
                await session.DisposeAsync();
                throw new InvalidOperationException($"A TDLib session is already attached to account {accountId}.");
            }
            return session;
        }
        catch
        {
            bridge.Detach();
            throw;
        }
    }
}

