using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibGroupMemberParserTests
{
    [Fact]
    public void ParsesSupergroupAndMemberIds()
    {
        Assert.Equal(77, TdLibGroupMemberParser.ParseSupergroupId("{\"type\":{\"@type\":\"chatTypeSupergroup\",\"supergroup_id\":77}}"));
        Assert.Equal([42L, 43L], TdLibGroupMemberParser.ParseMemberUserIds("{\"members\":[{\"user_id\":42},{\"user_id\":42},{\"user_id\":43}]}"));
    }

    [Fact]
    public void RejectsChatsWithoutSupergroupInformation()
    {
        Assert.Null(TdLibGroupMemberParser.ParseSupergroupId("{\"type\":{\"@type\":\"chatTypeBasicGroup\",\"basic_group_id\":77}}"));
    }
}
