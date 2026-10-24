using System.Text.RegularExpressions;

namespace TelegramControlStudio.Core.Security;

public static partial class SecretRedactor
{
    public static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var redacted = Otp().Replace(value, "[REDACTED_OTP]");
        redacted = Session().Replace(redacted, "$1[REDACTED_SESSION]");
        redacted = ApiHash().Replace(redacted, "$1[REDACTED_API_HASH]");
        redacted = ApiKey().Replace(redacted, "$1[REDACTED_API_KEY]");
        redacted = TwoFactorPassword().Replace(redacted, "$1[REDACTED_2FA_PASSWORD]");
        return DpapiKey().Replace(redacted, "$1[REDACTED_DPAPI_KEY]");
    }

    [GeneratedRegex(@"(?i)(api[_-]?hash\s*[:=]\s*)[^\s,;]+")]
    private static partial Regex ApiHash();

    [GeneratedRegex(@"(?i)(session(?:[_-]?string)?\s*[:=]\s*)[^\s,;]+")]
    private static partial Regex Session();

    [GeneratedRegex(@"(?i)(otp|verification\s*code)\s*[:=]\s*\d{4,8}")]
    private static partial Regex Otp();

    [GeneratedRegex(@"(?i)(api[_-]?key\s*[:=]\s*)[^\s,;]+")]
    private static partial Regex ApiKey();

    [GeneratedRegex(@"(?i)((?:2fa|two[_-]?step|two[_-]?factor)[ _-]?password\s*[:=]\s*)[^\s,;]+")]
    private static partial Regex TwoFactorPassword();

    [GeneratedRegex(@"(?i)(dpapi(?:[_-]?key)?\s*[:=]\s*)[^\s,;]+")]
    private static partial Regex DpapiKey();
}

