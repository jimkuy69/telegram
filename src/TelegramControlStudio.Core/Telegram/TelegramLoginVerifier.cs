using System.Security.Cryptography;
using System.Text;

namespace TelegramControlStudio.Core.Telegram;

/// <summary>
/// Verifies the payload Telegram sends to the Login Widget callback (data-check-string signed with SHA256(bot_token)).
/// </summary>
public static class TelegramLoginVerifier
{
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromHours(24);

    public static bool Verify(IReadOnlyDictionary<string, string> fields, string botToken, DateTimeOffset now, TimeSpan? maxAge = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (string.IsNullOrWhiteSpace(botToken)) return false;
        if (!fields.TryGetValue("hash", out var hash) || string.IsNullOrWhiteSpace(hash)) return false;

        var checkString = string.Join("\n", fields
            .Where(pair => pair.Key != "hash" && !string.IsNullOrEmpty(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));

        var secret = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));
        var expected = Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(checkString))).ToLowerInvariant();
        var provided = hash.Trim().ToLowerInvariant();
        if (expected.Length != provided.Length ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(provided)))
            return false;

        if (!fields.TryGetValue("auth_date", out var authDateText) || !long.TryParse(authDateText, out var authDateUnix))
            return false;

        var age = now - DateTimeOffset.FromUnixTimeSeconds(authDateUnix);
        if (age < TimeSpan.FromMinutes(-5) || age > (maxAge ?? DefaultMaxAge)) return false;
        return true;
    }
}
