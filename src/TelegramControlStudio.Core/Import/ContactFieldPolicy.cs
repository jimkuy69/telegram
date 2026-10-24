using System.Text.RegularExpressions;

namespace TelegramControlStudio.Core.Import;

public static partial class ContactFieldPolicy
{
    public static bool IsValidPhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) || PhonePattern().IsMatch(phone.Trim());

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")]
    private static partial Regex PhonePattern();
}

