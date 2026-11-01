using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibInviteLinkResponseParserTests
{
    [Fact]
    public void ParsesTheFlatChatInviteLinkTDLibReturns()
    {
        var result = TdLibInviteLinkResponseParser.Parse(
            """{"@type":"chatInviteLink","invite_link":"https://t.me/+abc","name":"Studio","member_limit":20,"expiration_date":4102444800,"creates_join_request":true}""",
            7,
            -10);
        Assert.NotNull(result);
        Assert.Equal("https://t.me/+abc", result!.InviteUrl);
        Assert.Equal(20, result.UsageLimit);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(4102444800), result.ExpiresAt);
        Assert.True(result.CreatesJoinRequest);
        Assert.Equal(-10, result.ChatId);
        Assert.Null(result.RevokedAt);
    }

    [Fact]
    public void UnlimitedLinkStaysUnlimitedAndOptionalFieldsMayBeAbsent()
    {
        var result = TdLibInviteLinkResponseParser.Parse("""{"@type":"chatInviteLink","invite_link":"https://t.me/+abc"}""", 7, -10);
        Assert.NotNull(result);
        Assert.Null(result!.UsageLimit);
        Assert.Null(result.ExpiresAt);
        Assert.False(result.CreatesJoinRequest);
    }

    [Fact]
    public void RevokedLinkIsStoredAsRevoked()
    {
        var result = TdLibInviteLinkResponseParser.Parse("""{"@type":"chatInviteLink","invite_link":"https://t.me/+abc","is_revoked":true}""", 7, -10);
        Assert.NotNull(result);
        Assert.NotNull(result!.RevokedAt);
    }

    [Fact]
    public void RejectsResponsesWithoutAUsableLink()
    {
        Assert.Null(TdLibInviteLinkResponseParser.Parse("""{"@type":"chatInviteLink","invite_link":""}""", 7, -10));
        Assert.Null(TdLibInviteLinkResponseParser.Parse("""{"@type":"chatInviteLink","link":"https://t.me/+abc"}""", 7, -10));
    }

    [Fact]
    public void RefusesToAttributeALinkToTheUnknownChat()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TdLibInviteLinkResponseParser.Parse("""{"invite_link":"https://t.me/+abc"}""", 7, 0));
    }
}
