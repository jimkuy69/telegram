using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibConsentReplyHandlerTests
{
    private const long ChatId = 6001;

    [Fact]
    public async Task AffirmativeAnswerToOurRequestGrantsConsentAndKeepsTheEvidence()
    {
        var (path, accountId) = await RequestedAsync();
        try
        {
            var outcome = await new TdLibConsentReplyHandler(path, accountId)
                .HandleAsync(Update(40, ChatId, 11, "ตกลงครับ"));

            Assert.Equal(ConsentReplyOutcome.Granted, outcome);
            var contact = (await new ContactRepository(path).ListAsync(accountId)).Single();
            Assert.True(contact.HasConsent);
            Assert.False(contact.IsOptedOut);

            var events = await new ConsentRequestRepository(path).ListAsync(accountId);
            var granted = events.Single(item => item.Kind == ConsentRequestKinds.Granted);
            Assert.Equal(11, granted.MessageId);
            Assert.Equal(ChatId, granted.ChatId);
            Assert.Equal("ตกลง", granted.MatchedKeyword);
            Assert.Equal(ConsentRequestSources.TelegramMessage, granted.Source);
            Assert.Null(granted.BodySha256);

            var logs = await new ActivityLogRepository(path).ListAsync(accountId);
            Assert.Contains(logs, item => item.EventType == "ConsentGranted");
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task AYesInAnotherChatIsNotTreatedAsAnAnswer()
    {
        var (path, accountId) = await RequestedAsync();
        try
        {
            var outcome = await new TdLibConsentReplyHandler(path, accountId)
                .HandleAsync(Update(40, 9999, 12, "ok"));

            Assert.Equal(ConsentReplyOutcome.NotAnsweringARequest, outcome);
            var contact = (await new ContactRepository(path).ListAsync(accountId)).Single();
            Assert.False(contact.HasConsent);
            Assert.DoesNotContain(await new ConsentRequestRepository(path).ListAsync(accountId),
                item => item.Kind == ConsentRequestKinds.Granted);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task RepeatingTheAnswerDoesNotDuplicateTheGrant()
    {
        var (path, accountId) = await RequestedAsync();
        try
        {
            var handler = new TdLibConsentReplyHandler(path, accountId);
            Assert.Equal(ConsentReplyOutcome.Granted, await handler.HandleAsync(Update(40, ChatId, 11, "ok")));
            Assert.Equal(ConsentReplyOutcome.AlreadyDecided, await handler.HandleAsync(Update(40, ChatId, 12, "ok")));

            var events = await new ConsentRequestRepository(path).ListAsync(accountId);
            Assert.Single(events, item => item.Kind == ConsentRequestKinds.Granted);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task RefusalIsRecordedButGrantsNothing()
    {
        var (path, accountId) = await RequestedAsync();
        try
        {
            var outcome = await new TdLibConsentReplyHandler(path, accountId)
                .HandleAsync(Update(40, ChatId, 13, "ไม่สนใจ"));

            Assert.Equal(ConsentReplyOutcome.Rejected, outcome);
            var contact = (await new ContactRepository(path).ListAsync(accountId)).Single();
            Assert.False(contact.HasConsent);
            Assert.False(contact.IsOptedOut);
            Assert.Contains(await new ConsentRequestRepository(path).ListAsync(accountId),
                item => item.Kind == ConsentRequestKinds.Rejected && item.MatchedKeyword == "ไม่สนใจ");
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task OptOutIsHonouredEvenWithoutARequestAndRevokesPermission()
    {
        var path = NewPath("optout");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("OptOut", "+66000000301");
            await new ContactRepository(path).UpsertManyAsync([
                new Contact(accountId, 40, "Asked", "", null, "+66000000040", true, false, DateTimeOffset.UtcNow)]);

            var outcome = await new TdLibConsentReplyHandler(path, accountId)
                .HandleAsync(Update(40, ChatId, 14, "หยุดส่งได้แล้ว"));

            Assert.Equal(ConsentReplyOutcome.OptedOut, outcome);
            var contact = (await new ContactRepository(path).ListAsync(accountId)).Single();
            Assert.False(contact.HasConsent);
            Assert.True(contact.IsOptedOut);
            Assert.DoesNotContain(await new ConsentRequestRepository(path).ListAsync(accountId),
                item => item.Kind == ConsentRequestKinds.Requested);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task AnOptOutCannotBeReversedByALaterYesInTheSameChat()
    {
        var (path, accountId) = await RequestedAsync();
        try
        {
            var handler = new TdLibConsentReplyHandler(path, accountId);
            await handler.HandleAsync(Update(40, ChatId, 15, "ตกลง"));
            await handler.HandleAsync(Update(40, ChatId, 16, "unsubscribe"));

            Assert.Equal(ConsentReplyOutcome.AlreadyDecided, await handler.HandleAsync(Update(40, ChatId, 17, "ok")));
            var contact = (await new ContactRepository(path).ListAsync(accountId)).Single();
            Assert.True(contact.IsOptedOut);
            Assert.False(contact.HasConsent);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task StrangersCannotCreateAConsentRecord()
    {
        var (path, accountId) = await RequestedAsync();
        try
        {
            var outcome = await new TdLibConsentReplyHandler(path, accountId)
                .HandleAsync(Update(4040, ChatId, 18, "หยุดส่ง"));

            Assert.Equal(ConsentReplyOutcome.UnknownContact, outcome);
            Assert.Empty(await new ConsentRequestRepository(path).ListAsync(accountId, ConsentRequestKinds.OptedOut));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task SmallTalkIsIgnoredWithoutLeavingATrace()
    {
        var (path, accountId) = await RequestedAsync();
        try
        {
            Assert.Equal(ConsentReplyOutcome.Unanswered,
                await new TdLibConsentReplyHandler(path, accountId).HandleAsync(Update(40, ChatId, 19, "สวัสดีครับ")));
            Assert.Equal(ConsentReplyOutcome.Ignored,
                await new TdLibConsentReplyHandler(path, accountId).HandleAsync("{not an update"));

            var events = await new ConsentRequestRepository(path).ListAsync(accountId);
            Assert.Single(events);
            Assert.Equal(ConsentRequestKinds.Requested, events[0].Kind);
        }
        finally { Cleanup(path); }
    }

    private static async Task<(string Path, long AccountId)> RequestedAsync()
    {
        var path = NewPath("handler");
        await new SqliteDatabase(path).InitializeAsync();
        var accountId = await new AccountRepository(path).CreateAsync("Reply", "+66000000300");
        await new ContactRepository(path).UpsertManyAsync([
            new Contact(accountId, 40, "Asked", "", null, "+66000000040", false, false, DateTimeOffset.UtcNow)]);
        await new ConsentRequestRepository(path).AppendAsync(new ConsentRequest(
            0, accountId, 40, ConsentRequestKinds.Requested, DateTimeOffset.UtcNow.AddMinutes(-10),
            7001, ChatId, 3, new string('b', 64), null, ConsentRequestSources.JobRunner));
        return (path, accountId);
    }

    private static string NewPath(string label) =>
        Path.Combine(Path.GetTempPath(), $"tg-consent-reply-{label}-{Guid.NewGuid():N}.db");

    private static string Update(long senderId, long chatId, long messageId, string text) =>
        UpdateTemplate
            .Replace("$MESSAGE", messageId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("$SENDER", senderId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("$CHAT", chatId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("$TEXT", text);

    private const string UpdateTemplate = """
        {"@type":"updateNewMessage","message":{"id":$MESSAGE,
         "sender_id":{"@type":"messageSenderUser","user_id":$SENDER},
         "chat_id":$CHAT,"is_outgoing":false,
         "content":{"@type":"messageText","text":{"@type":"formattedText","text":"$TEXT"}}}}
        """;

    private static void Cleanup(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(path)) File.Delete(path);
    }
}
