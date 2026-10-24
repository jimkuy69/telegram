using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Core.Import;

public sealed record ContactImportResult(IReadOnlyList<Contact> Valid, IReadOnlyList<string> Errors);

public static class ContactCsvImporter
{
    public static ContactImportResult Preview(string csv, long accountId)
    {
        ArgumentNullException.ThrowIfNull(csv);
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        var valid = new List<Contact>();
        var errors = new List<string>();
        var seenUserIds = new HashSet<long>();
        var lines = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || !lines[0].Equals("telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out", StringComparison.OrdinalIgnoreCase))
            return new([], ["HEADER_INVALID"]);
        for (var index = 1; index < lines.Length; index++)
        {
            var fields = lines[index].Split(',');
            if (fields.Length != 7 || !long.TryParse(fields[0], out var userId) || userId <= 0)
            {
                errors.Add($"ROW_{index + 1}_INVALID");
                continue;
            }
            if (!seenUserIds.Add(userId))
            {
                errors.Add($"ROW_{index + 1}_DUPLICATE");
                continue;
            }
            if (!bool.TryParse(fields[5], out var consent) || !bool.TryParse(fields[6], out var optedOut))
            {
                errors.Add($"ROW_{index + 1}_BOOLEAN_INVALID");
                continue;
            }
            var phone = string.IsNullOrWhiteSpace(fields[4]) ? null : fields[4].Trim();
            if (!ContactFieldPolicy.IsValidPhone(phone))
            {
                errors.Add($"ROW_{index + 1}_PHONE_INVALID");
                continue;
            }
            valid.Add(new Contact(accountId, userId, fields[1].Trim(), fields[2].Trim(), string.IsNullOrWhiteSpace(fields[3]) ? null : fields[3].Trim().TrimStart('@'), phone, consent, optedOut, DateTimeOffset.UtcNow));
        }
        return new(valid, errors);
    }
}

