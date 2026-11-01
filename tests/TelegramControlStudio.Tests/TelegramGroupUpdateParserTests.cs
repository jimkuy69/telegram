using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TelegramGroupUpdateParserTests
{
    [Fact]
    public void DistinguishesChannelsFromSupergroups()
    {
        var channel = TelegramGroupUpdateParser.Parse("""{"chat":{"id":-100123,"title":"News","type":{"@type":"chatTypeSupergroup","supergroup_id":123,"is_channel":true},"member_count":42}}""", 7);
        var group = TelegramGroupUpdateParser.Parse("""{"chat":{"id":-100124,"title":"Team","type":{"@type":"chatTypeSupergroup","supergroup_id":124,"is_channel":false}}}""", 7);
        Assert.Equal("Channel", channel!.Kind);
        Assert.Equal("Group", group!.Kind);
        Assert.Equal(42, channel.MemberCount);
    }

    [Fact]
    public void ReadsPermissionFlagsWhenTheChatCarriesThem()
    {
        var group = TelegramGroupUpdateParser.Parse("""{"chat":{"id":-100125,"title":"Team","type":{"@type":"chatTypeSupergroup","supergroup_id":125},"can_view_members":true,"can_send_messages":true,"can_invite_users":false}}""", 7);
        Assert.NotNull(group); Assert.True(group!.CanViewMembers); Assert.True(group.CanSendMessages); Assert.False(group.CanInviteUsers);
    }

    [Fact]
    public void MissingMemberCountIsUnknownNotZero()
    {
        var group = TelegramGroupUpdateParser.Parse("""{"chat":{"id":1,"title":"Private","type":{"@type":"chatTypeBasicGroup"}}}""", 7);
        Assert.NotNull(group); Assert.Null(group!.MemberCount); Assert.Contains("UNKNOWN", group.ObservedNote);
    }
}
