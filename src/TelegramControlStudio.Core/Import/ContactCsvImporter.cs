using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Core.Import;

public sealed record ContactImportResult(IReadOnlyList<Contact> Valid, IReadOnlyList<string> Errors);

public static class ContactCsvImporter
{
    private const string ExpectedHeader = "telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out";
    internal const string HeaderError = $"หัวตารางไม่ถูกต้อง ต้องเป็น {ExpectedHeader} (เว้นว่างบางคอลัมน์ท้ายไม่ได้)";

    public static ContactImportResult Preview(string csv, long accountId)
    {
        ArgumentNullException.ThrowIfNull(csv);
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        var valid = new List<Contact>();
        var errors = new List<string>();
        var seenUserIds = new HashSet<long>();
        var lines = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || !lines[0].Equals(ExpectedHeader, StringComparison.OrdinalIgnoreCase))
            return new([], [HeaderError]);
        for (var index = 1; index < lines.Length; index++)
        {
            var row = index + 1;
            var fields = ParseLine(lines[index]);
            // Excel leaves blank cells out of the row entirely, so a short row is its own failure: the
            // operator has to be told which columns are missing rather than shown a generic code.
            if (fields.Length != 7)
            {
                errors.Add($"แถว {row}: อ่านได้ {fields.Length} คอลัมน์ ต้องมี 7 ตามหัวตาราง (ห้ามเว้น has_consent และ is_opted_out)");
                continue;
            }
            if (!long.TryParse(fields[0].Trim(), out var userId) || userId <= 0)
            {
                errors.Add($"แถว {row}: telegram_user_id ต้องเป็นเลขจำนวนเต็มบวก (อ่านได้ \"{fields[0].Trim()}\")");
                continue;
            }
            if (!seenUserIds.Add(userId))
            {
                errors.Add($"แถว {row}: telegram_user_id {userId} ซ้ำกับแถวอื่นในไฟล์นี้");
                continue;
            }
            if (!TryParseBoolean(fields[5], out var consent) || !TryParseBoolean(fields[6], out var optedOut))
            {
                errors.Add($"แถว {row}: has_consent และ is_opted_out ต้องเป็น true/false หรือ 1/0 (อ่านได้ \"{fields[5].Trim()}\" และ \"{fields[6].Trim()}\")");
                continue;
            }
            var phone = string.IsNullOrWhiteSpace(fields[4]) ? null : fields[4].Trim();
            if (!ContactFieldPolicy.IsValidPhone(phone))
            {
                errors.Add($"แถว {row}: phone_number ไม่ถูกต้อง ให้เว้นว่างหรือใส่รูปแบบสากลเช่น +66900000000 (อ่านได้ \"{fields[4].Trim()}\")");
                continue;
            }
            valid.Add(new Contact(accountId, userId, fields[1].Trim(), fields[2].Trim(), string.IsNullOrWhiteSpace(fields[3]) ? null : fields[3].Trim().TrimStart('@'), phone, consent, optedOut, DateTimeOffset.UtcNow));
        }
        return new(valid, errors);
    }

    private static string[] ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else current.Append(character);
        }
        fields.Add(current.ToString());
        return [.. fields];
    }

    private static bool TryParseBoolean(string value, out bool result)
    {
        if (bool.TryParse(value.Trim(), out result)) return true;
        if (value.Trim() == "1") { result = true; return true; }
        if (value.Trim() == "0") { result = false; return true; }
        result = false;
        return false;
    }
}

