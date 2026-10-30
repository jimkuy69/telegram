using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

/// <summary>
/// The sink is called from the TDLib receive loop, so these tests hold it to its two promises:
/// answering an opt-in request reaches the database, and handing work over never blocks or throws.
/// </summary>
public sealed class ConsentReplyDispatcherTests
{
    private const long ChatId = 6001;

    [Fact]
    public async Task AnAnswerHandedOverByTheSessionGrantsConsentWithoutBlockingTheCaller()
    {
        var (path, accountId) = await RequestedAsync("grant");
        try
        {
            await using var dispatcher = new ConsentReplyDispatcher(path);
            var sink = dispatcher.SinkFor(accountId);

            var handover = System.Diagnostics.Stopwatch.StartNew();
            sink(Update(40, ChatId, 11, "ok"));
            handover.Stop();

            var contact = await WaitForConsentAsync(path, accountId, 40, item => item.HasConsent);
            Assert.True(contact.HasConsent);
            Assert.False(contact.IsOptedOut);
            Assert.True(handover.Elapsed < TimeSpan.FromSeconds(1), $"Handing the update over took {handover.ElapsedMilliseconds} ms.");
            var events = await new ConsentRequestRepository(path).ListAsync(accountId, ConsentRequestKinds.Granted);
            Assert.Equal(ConsentRequestSources.TelegramMessage, Assert.Single(events).Source);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task AnOptOutHandedOverByTheSessionRevokesConsent()
    {
        var (path, accountId) = await RequestedAsync("optout");
        try
        {
            await using var dispatcher = new ConsentReplyDispatcher(path);
            dispatcher.SinkFor(accountId)(Update(40, ChatId, 12, "หยุดส่งข้อความ"));

            var contact = await WaitForConsentAsync(path, accountId, 40, item => item.IsOptedOut);
            Assert.True(contact.IsOptedOut);
            Assert.False(contact.HasConsent);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task GarbageUpdatesNeverThrowBackIntoTheReceiveLoopAndDoNotStopTheTrail()
    {
        var (path, accountId) = await RequestedAsync("garbage");
        try
        {
            await using var dispatcher = new ConsentReplyDispatcher(path);
            var sink = dispatcher.SinkFor(accountId);

            sink("");
            sink("not json at all");
            sink("""{"@type":"updateNewMessage","message":{"id":1,"chat_id":2}}""");
            sink(Update(40, ChatId, 14, "ok"));

            var contact = await WaitForConsentAsync(path, accountId, 40, item => item.HasConsent);
            Assert.True(contact.HasConsent);
            var granted = await new ConsentRequestRepository(path).ListAsync(accountId, ConsentRequestKinds.Granted);
            Assert.Single(granted);
        }
        finally { Cleanup(path); }
    }

    private static async Task<Contact> WaitForConsentAsync(
        string path, long accountId, long telegramUserId, Func<Contact, bool> settled)
    {
        var contacts = new ContactRepository(path);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var contact = (await contacts.ListAsync(accountId)).Single(item => item.TelegramUserId == telegramUserId);
            if (settled(contact)) return contact;
            await Task.Delay(25);
        }
        throw new TimeoutException("The consent reply was not processed by the pump.");
    }

    private static async Task<(string Path, long AccountId)> RequestedAsync(string label)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-consent-pump-{label}-{Guid.NewGuid():N}.db");
        await new SqliteDatabase(path).InitializeAsync();
        var accountId = await new AccountRepository(path).CreateAsync("Pump", "+66000000400");
        await new ContactRepository(path).UpsertManyAsync([
            new Contact(accountId, 40, "Asked", "", null, "+66000000040", false, false, DateTimeOffset.UtcNow)]);
        await new ConsentRequestRepository(path).AppendAsync(new ConsentRequest(
            0, accountId, 40, ConsentRequestKinds.Requested, DateTimeOffset.UtcNow.AddMinutes(-10),
            7001, ChatId, 3, new string('b', 64), null, ConsentRequestSources.JobRunner));
        return (path, accountId);
    }

    private static string Update(long senderId, long chatId, long messageId, string text) =>
        """
        {"@type":"updateNewMessage","message":{"id":$MESSAGE,
         "sender_id":{"@type":"messageSenderUser","user_id":$SENDER},
         "chat_id":$CHAT,"is_outgoing":false,
         "content":{"@type":"messageText","text":{"@type":"formattedText","text":"$TEXT"}}}}
        """.Replace("$MESSAGE", messageId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("$SENDER", senderId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("$CHAT", chatId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("$TEXT", text);

    private static void Cleanup(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(path)) File.Delete(path);
    }
}
