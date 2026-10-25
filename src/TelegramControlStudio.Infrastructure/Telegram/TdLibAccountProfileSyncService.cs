using System.Text.Json;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibAccountProfileSyncService(ITdLibClient client, AccountRepository accounts)
{
    public async Task<bool> SyncAsync(long accountId, CancellationToken cancellationToken = default)
    {
        var response = await client.SendAsync("getMe", new Dictionary<string, object?>(), cancellationToken);
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        if (root.TryGetProperty("@type", out var type) && string.Equals(type.GetString(), "error", StringComparison.Ordinal))
            throw new InvalidOperationException("TDLIB_PROFILE_SYNC_FAILED");
        var first = root.TryGetProperty("first_name", out var firstName) ? firstName.GetString() : null;
        var last = root.TryGetProperty("last_name", out var lastName) ? lastName.GetString() : null;
        var phone = root.TryGetProperty("phone_number", out var phoneNumber) ? phoneNumber.GetString() : null;
        var displayName = string.Join(' ', new[] { first, last }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(phone)) throw new InvalidOperationException("TDLIB_PROFILE_DATA_INCOMPLETE");
        return await accounts.UpdateProfileAsync(accountId, displayName, phone, cancellationToken);
    }
}
