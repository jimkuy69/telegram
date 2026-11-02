using System.Text.Json;

namespace TelegramControlStudio.Core.Telegram;

public sealed record ImportedContactsResult(
    string? Error,
    IReadOnlyList<long> UserIds);

/// <summary>
/// Reads the <c>importedContacts</c> reply of <c>importContacts</c>. TDLib answers with one entry per
/// contact of the request, in the same order, and 0 for a number that is not a registered user, so the
/// caller maps a user id back to a row through its position and nothing else.
/// </summary>
public static class TdLibImportedContactsParser
{
    public static ImportedContactsResult Parse(string json)
    {
        if (TdLibResponseGuard.TryGetError(json, out var error))
            return Failed($"TDLib importContacts rejected by Telegram: {error}");
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Failed("TDLib importContacts ตอบกลับเป็น JSON ที่อ่านไม่ได้");
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Failed("TDLib importContacts ตอบกลับไม่ใช่ object");
            var announced = root.TryGetProperty("@type", out var type) && type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : "(ไม่มี @type)";
            if (announced != "importedContacts")
                return Failed($"TDLib importContacts ตอบเป็น {announced} แทนที่จะเป็น importedContacts");
            if (!root.TryGetProperty("user_ids", out var ids) || ids.ValueKind != JsonValueKind.Array)
                return Failed("TDLib importContacts ไม่คืน user_ids ของผู้รับกลับมา");
            var users = new List<long>();
            foreach (var item in ids.EnumerateArray())
                users.Add(item.TryGetInt64(out var id) && id > 0 ? id : 0);
            return new ImportedContactsResult(null, users);
        }
    }

    private static ImportedContactsResult Failed(string error) => new(error, []);
}
