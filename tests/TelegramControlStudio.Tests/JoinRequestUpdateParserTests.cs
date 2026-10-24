using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class JoinRequestUpdateParserTests
{
    [Fact]
    public void ParsesChatJoinRequestUpdate()
    {
        var request = JoinRequestUpdateParser.TryParse("""{"@type":"updateChatJoinRequest","chat_id":-100123,"user_id":456}""", 7);
        Assert.NotNull(request);
        Assert.Equal(7, request.AccountId);
        Assert.Equal(-100123, request.ChatId);
        Assert.Equal(456, request.TelegramUserId);
        Assert.Equal("Pending", request.Status);
    }

    [Fact]
    public void IgnoresOtherUpdates() =>
        Assert.Null(JoinRequestUpdateParser.TryParse("""{"@type":"updateUser"}""", 7));
}

