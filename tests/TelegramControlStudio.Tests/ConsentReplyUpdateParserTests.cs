using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ConsentReplyUpdateParserTests
{
    [Fact]
    public void ReadsAnInboundTextMessageFromNestedIds()
    {
        var message = ConsentReplyUpdateParser.TryParse("""
            {"@type":"updateNewMessage","message":{"id":11,
             "sender_id":{"@type":"messageSenderUser","user_id":40},
             "chat_id":{"@type":"chatId","id":6001},"is_outgoing":false,
             "content":{"@type":"messageText","text":{"@type":"formattedText","text":"ตกลงครับ"}}}}
            """);

        Assert.NotNull(message);
        Assert.Equal(40, message!.SenderUserId);
        Assert.Equal(6001, message.ChatId);
        Assert.Equal(11, message.MessageId);
        Assert.Null(message.ReplyToMessageId);
        Assert.Equal("ตกลงครับ", message.Text);
    }

    [Fact]
    public void ReadsBareNumericIdsAndReplyLinks()
    {
        var message = ConsentReplyUpdateParser.TryParse("""
            {"@type":"updateNewMessage","message":{"id":12,"sender_id":{"user_id":41},"chat_id":6002,
             "reply_to_message_id":11,"content":{"@type":"messageText","text":{"text":"ok"}}}}
            """);

        Assert.NotNull(message);
        Assert.Equal(41, message!.SenderUserId);
        Assert.Equal(6002, message.ChatId);
        Assert.Equal(11, message.ReplyToMessageId);
        Assert.Equal("ok", message.Text);
    }

    [Theory]
    [InlineData("""{"@type":"updateNewMessage","message":{"id":1,"sender_id":{"user_id":40},"chat_id":2,"is_outgoing":true,"content":{"@type":"messageText","text":{"text":"ok"}}}}""")]
    [InlineData("""{"@type":"updateNewMessage","message":{"id":1,"sender_id":{"user_id":40},"chat_id":2,"content":{"@type":"messagePhoto","caption":{"text":"ok"}}}}""")]
    [InlineData("""{"@type":"updateNewMessage","message":{"id":1,"sender_id":{"user_id":40},"chat_id":2,"content":{"@type":"messageText","text":{"text":"   "}}}}""")]
    [InlineData("""{"@type":"updateDeleteMessages","messages":[1]}""")]
    [InlineData("""{"@type":"updateNewMessage","message":{"sender_id":{"user_id":40},"chat_id":2,"content":{"@type":"messageText","text":{"text":"ok"}}}}""")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void IgnoresAnythingThatIsNotAnInboundTextMessage(string json) =>
        Assert.Null(ConsentReplyUpdateParser.TryParse(json));
}
