using System.Text.Json;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibContactSyncService(ITdLibClient client, string databasePath)
{
    public async Task<IReadOnlyList<Contact>> SyncAsync(
        long accountId,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var response = await client.SendAsync("getContacts", new Dictionary<string, object?>(), cancellationToken);
        var contacts = new List<Contact>();
        var userIds = TdLibContactParser.ParseUserIds(response);
        // One getUser per contact is the only way TDLib exposes names, so a large address book is a
        // long series of round trips and the caller needs to show progress through it.
        var done = 0;
        progress?.Report((done, userIds.Count));
        foreach (var userId in userIds)
        {
            var userJson = await client.SendAsync("getUser", new Dictionary<string, object?> { ["user_id"] = userId }, cancellationToken);
            progress?.Report((++done, userIds.Count));
            try
            {
                var contact = TdLibContactParser.ParseUser(userJson, accountId);
                if (contact is not null) contacts.Add(contact);
            }
            catch (JsonException)
            {
                // Ignore malformed individual updates; a later sync can retry them.
            }
        }
        await new ContactRepository(databasePath).UpsertManyAsync(contacts, cancellationToken);
        await new ActivityLogRepository(databasePath).AppendAsync(new ActivityLog(
            accountId,
            null,
            "Info",
            "ContactsSyncedReadOnly",
            $"Read-only TDLib sync stored {contacts.Count} contact(s).",
            DateTimeOffset.UtcNow), cancellationToken);
        return contacts;
    }
}