namespace TelegramControlStudio.Core.Telegram;

public sealed class AuthorizationStateMachine
{
    public AuthorizationState Current { get; private set; } = AuthorizationState.Unknown;

    public void Apply(string tdlibState)
    {
        ArgumentException.ThrowIfNullOrEmpty(tdlibState);
        Current = tdlibState switch
        {
            "authorizationStateWaitTdlibParameters" => AuthorizationState.WaitTdlibParameters,
            "authorizationStateWaitPhoneNumber" => AuthorizationState.WaitPhoneNumber,
            "authorizationStateWaitCode" => AuthorizationState.WaitCode,
            "authorizationStateWaitPassword" => AuthorizationState.WaitPassword,
            "authorizationStateReady" => AuthorizationState.Ready,
            "authorizationStateLoggingOut" => AuthorizationState.LoggingOut,
            "authorizationStateClosed" => AuthorizationState.Closed,
            _ => AuthorizationState.Unknown
        };
    }

    public void MarkExpired() => Current = AuthorizationState.Expired;
    public void MarkError() => Current = AuthorizationState.Error;
}
