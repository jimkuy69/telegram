using System.Text.Json;

namespace TelegramControlStudio.Core.Telegram;

public sealed record ImportedContactsResult(
    string? Error,
    IReadOnlyDictionary<int, long> UserIdsByClientId,
    IReadOnlyList<int> AlreadyImportedClientIds,
    IReadOnlyList<int> FailedClientIds,
    IReadOnlyList<int> ClientIds);

/// <summary>
/// Reads the <c>importedContacts</c> reply of <c>importContacts</c>. The reply carries no phone numbers,
/// only the <c>client_id</c> TDLib assigned to each contact of the request, so the caller maps a user id
/// back to a row through that number and nothing else.
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
            var users = ReadPairs(root);
            var already = ReadIds(root, "unimported_ids");
            var failed = ReadIds(root, "failed_ids");
            return new ImportedContactsResult(null, users, already, failed,
                users.Keys.Concat(already).Concat(failed).Distinct().ToArray());
        }
    }

    private static ImportedContactsResult Failed(string error) =>
        new(error, new Dictionary<int, long>(), [], [], []);

    private static Dictionary<int, long> ReadPairs(JsonElement root)
    {
        var users = new Dictionary<int, long>();
        if (root.TryGetProperty("imported", out var imported) && imported.ValueKind == JsonValueKind.Array)
            foreach (var item in imported.EnumerateArray())
                if (item.TryGetProperty("client_id", out var clientId) && clientId.TryGetInt32(out var id) &&
                    item.TryGetProperty("user_id", out var userId) && userId.TryGetInt64(out var user) && user > 0)
                    users[id] = user;
        return users;
    }

    private static IReadOnlyList<int> ReadIds(JsonElement root, string property)
    {
        var ids = new List<int>();
        if (root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                if (item.TryGetInt32(out var id))
                    ids.Add(id);
        return ids;
    }
}
