namespace TelegramControlStudio.Core.Telegram;

public static class TdLibAuthorizationRequests
{
    public static TdLibRequest SetPhoneNumber(string phoneNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        return new TdLibRequest("setAuthenticationPhoneNumber", new Dictionary<string, object?> { ["phone_number"] = phoneNumber, ["settings"] = null }, "auth-phone");
    }

    public static TdLibRequest CheckCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new TdLibRequest("checkAuthenticationCode", new Dictionary<string, object?> { ["code"] = code }, "auth-code");
    }

    public static TdLibRequest CheckPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return new TdLibRequest("checkAuthenticationPassword", new Dictionary<string, object?> { ["password"] = password }, "auth-password");
    }
}

