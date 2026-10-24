using System.Text.Json;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Core.Telegram;

public static class TdLibContactParser
{
    public static Contact? ParseUser(string json, long accountId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var userId) || userId <= 0) return null;
        var firstName = root.TryGetProperty("first_name", out var first) ? first.GetString() ?? string.Empty : string.Empty;
        var lastName = root.TryGetProperty("last_name", out var last) ? last.GetString() ?? string.Empty : string.Empty;
        var username = root.TryGetProperty("usernames", out var usernames) && usernames.TryGetProperty("editable_username", out var editable)
            ? editable.GetString()
            : root.TryGetProperty("username", out var legacy) ? legacy.GetString() : null;
        var phone = root.TryGetProperty("phone_number", out var phoneElement) ? phoneElement.GetString() : null;
        return new Contact(accountId, userId, firstName, lastName, username, string.IsNullOrWhiteSpace(phone) ? null : phone, false, false, DateTimeOffset.UtcNow);
    }

    public static IReadOnlyList<long> ParseUserIds(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("user_ids", out var ids) || ids.ValueKind != JsonValueKind.Array) return [];
        return ids.EnumerateArray().Where(item => item.TryGetInt64(out var id) && id > 0).Select(item => item.GetInt64()).Distinct().ToArray();
    }
}