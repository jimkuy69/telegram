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

    public static TdLibRequest SetEmailAddress(string emailAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emailAddress);
        return new TdLibRequest("setAuthenticationEmailAddress", new Dictionary<string, object?> { ["email_address"] = emailAddress }, "auth-email");
    }

    public static TdLibRequest CheckEmailCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new TdLibRequest("checkAuthenticationEmailCode", new Dictionary<string, object?> { ["code"] = code }, "auth-email-code");
    }

    public static TdLibRequest RegisterUser(string firstName, string lastName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        return new TdLibRequest("registerUser", new Dictionary<string, object?> { ["first_name"] = firstName, ["last_name"] = lastName ?? string.Empty }, "auth-register");
    }
}
