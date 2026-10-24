namespace TelegramControlStudio.Core.Telegram;

public enum AuthorizationState
{
    Unknown,
    WaitTdlibParameters,
    WaitPhoneNumber,
    WaitCode,
    WaitPassword,
    Ready,
    LoggingOut,
    Closed,
    Expired,
    Error
}
