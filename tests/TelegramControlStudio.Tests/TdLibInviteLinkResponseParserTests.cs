using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibInviteLinkResponseParserTests
{
    [Fact]
    public void ParsesLinkAndOptionalLimits()
    {
        var result = TdLibInviteLinkResponseParser.Parse("""{"invite_link":{"invite_link":"https://t.me/+abc","chat_id":-10,"member_limit":20,"expiration_date":4102444800,"creates_join_request":true}}""", 7, -10);
        Assert.NotNull(result); Assert.Equal("https://t.me/+abc", result!.InviteUrl); Assert.Equal(20, result.UsageLimit); Assert.True(result.CreatesJoinRequest);
    }

    [Fact]
    public void RejectsUnexpectedChat()
    {
        Assert.Null(TdLibInviteLinkResponseParser.Parse("""{"invite_link":{"invite_link":"https://t.me/+abc","chat_id":-11}}""", 7, -10));
    }
}

