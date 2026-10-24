using System.Text.RegularExpressions;

namespace TelegramControlStudio.Core.Security;

public static partial class SecretRedactor
{
    public static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        return ApiHash().Replace(Session().Replace(Otp().Replace(value, "[REDACTED_OTP]"), "[REDACTED_SESSION]"), "[REDACTED_API_HASH]");
    }

    [GeneratedRegex(@"(?i)(api[_-]?hash\s*[:=]\s*)[^\s,;]+")]
    private static partial Regex ApiHash();

    [GeneratedRegex(@"(?i)(session(?:[_-]?string)?\s*[:=]\s*)[^\s,;]+")]
    private static partial Regex Session();

    [GeneratedRegex(@"(?i)(otp|verification\s*code)\s*[:=]\s*\d{4,8}")]
    private static partial Regex Otp();
}
