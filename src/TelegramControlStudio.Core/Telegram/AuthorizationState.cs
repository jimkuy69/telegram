namespace TelegramControlStudio.Core.Telegram;

public enum AuthorizationState
{
    Unknown,
    WaitTdlibParameters,
    WaitPhoneNumber,
    WaitEmailAddress,
    WaitEmailCode,
    WaitCode,
    WaitPassword,
    WaitRegistration,
    Ready,
    LoggingOut,
    Closed,
    Expired,
    Error
}
