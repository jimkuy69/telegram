using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Security;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibSessionStateBridge
{
    private readonly AccountRepository _accounts;
    private readonly string _sessionDirectory;
    private readonly long _accountId;
    private readonly AuthorizationStateMachine _stateMachine;
    private bool _attached;

    public TdLibSessionStateBridge(string databasePath, long accountId, string sessionDirectory, AuthorizationStateMachine stateMachine)
    {
        if (!SessionPathPolicy.IsWithinSessionRoot(sessionDirectory))
            throw new ArgumentException("TDLib session directory must be inside the per-account local session root.", nameof(sessionDirectory));
        _accounts = new AccountRepository(databasePath); _accountId = accountId; _sessionDirectory = sessionDirectory; _stateMachine = stateMachine;
    }

    public Task ApplyAsync(AuthorizationState state, CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(state, cancellationToken);

    private async Task ApplyCoreAsync(AuthorizationState state, CancellationToken cancellationToken)
    {
        var authorized = state == AuthorizationState.Ready;
        var session = new AccountSession(
            _accountId,
            _sessionDirectory,
            state.ToString(),
            authorized,
            DateTimeOffset.UtcNow);
        await _accounts.SaveSessionAsync(session, cancellationToken);
        await _accounts.UpdateStatusAsync(_accountId, MapAccountStatus(state), cancellationToken);
    }

    public void Attach()
    {
        if (_attached) return;
        _stateMachine.StateChanged += OnStateChanged;
        _attached = true;
    }

    public void Detach()
    {
        if (!_attached) return;
        _stateMachine.StateChanged -= OnStateChanged;
        _attached = false;
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