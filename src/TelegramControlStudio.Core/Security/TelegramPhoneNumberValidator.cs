using System.Text.RegularExpressions;

namespace TelegramControlStudio.Core.Security;

public static partial class TelegramPhoneNumberValidator
{
    public static string Normalize(string phoneNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        var normalized = phoneNumber.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).Replace("(", string.Empty).Replace(")", string.Empty);
        if (!E164Pattern().IsMatch(normalized))
            throw new FormatException("Phone number must use international E.164 format, for example +66812345678.");
        return normalized;
    }

    [GeneratedRegex("^\\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164Pattern();
}

