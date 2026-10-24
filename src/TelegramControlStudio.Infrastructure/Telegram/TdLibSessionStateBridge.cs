using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibSessionStateBridge(
    string databasePath,
    long accountId,
    string sessionDirectory,
    AuthorizationStateMachine stateMachine)
{
    private readonly AccountRepository _accounts = new(databasePath);
    private readonly string _sessionDirectory = sessionDirectory;

    public Task ApplyAsync(AuthorizationState state, CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(state, cancellationToken);

    private async Task ApplyCoreAsync(AuthorizationState state, CancellationToken cancellationToken)
    {
        var authorized = state == AuthorizationState.Ready;
        var session = new AccountSession(
            accountId,
            _sessionDirectory,
            state.ToString(),
            authorized,
            DateTimeOffset.UtcNow);
        await _accounts.SaveSessionAsync(session, cancellationToken);
        await _accounts.UpdateStatusAsync(accountId, MapAccountStatus(state), cancellationToken);
    }

    public void Attach()
    {
        stateMachine.StateChanged += OnStateChanged;
    }

    public void Detach()
    {
        stateMachine.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(object? sender, AuthorizationState state)
    {
        _ = ApplyAsync(state);
    }

    private static string MapAccountStatus(AuthorizationState state) => state switch
    {
        AuthorizationState.Ready => "Ready",
        AuthorizationState.Expired => "SessionExpired",
        AuthorizationState.Closed => "Offline",
        AuthorizationState.Error => "Error",
        AuthorizationState.LoggingOut => "LoggingOut",
        _ => "PendingAuthorization"
    };
}

