using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TelegramGroupUpdateParserTests
{
    [Fact]
    public void ParsesObservedPermissionsAndPreservesChatId()
    {
        var group = TelegramGroupUpdateParser.Parse("""{"chat":{"id":-100123,"title":"Team","type":{"@type":"chatTypeSupergroup"},"member_count":42,"can_view_members":true,"can_send_messages":true,"can_invite_users":false}}""", 7);
        Assert.NotNull(group); Assert.Equal(-100123, group!.ChatId); Assert.Equal("Channel", group.Kind); Assert.Equal(42, group.MemberCount); Assert.True(group.CanViewMembers); Assert.False(group.CanInviteUsers);
    }

    [Fact]
    public void MissingMemberCountIsUnknownNotZero()
    {
        var group = TelegramGroupUpdateParser.Parse("""{"chat":{"id":1,"title":"Private","type":{"@type":"chatTypeBasicGroup"}}}""", 7);
        Assert.NotNull(group); Assert.Null(group!.MemberCount); Assert.Contains("UNKNOWN", group.ObservedNote);
    }
}

