using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibGroupMemberParserTests
{
    [Fact]
    public void ParsesSupergroupIdFromChatType()
    {
        Assert.Equal(77, TdLibGroupMemberParser.ParseSupergroupId("{\"type\":{\"@type\":\"chatTypeSupergroup\",\"supergroup_id\":77}}"));
    }

    [Fact]
    public void ParsesMemberIdsFromTheMessageSenderTDLibReturns()
    {
        var response = """
            {"@type":"chatMemberList","found_count":3,"members":[
              {"@type":"chatMember","member_id":{"@type":"messageSenderUser","user_id":42},"status":{"@type":"chatMemberStatusMember"}},
              {"@type":"chatMember","member_id":{"@type":"messageSenderUser","user_id":42},"status":{"@type":"chatMemberStatusMember"}},
              {"@type":"chatMember","member_id":{"@type":"messageSenderUser","user_id":43},"status":{"@type":"chatMemberStatusAdministrator"}}]}
            """;
        Assert.Equal([42L, 43L], TdLibGroupMemberParser.ParseMemberUserIds(response));
    }

    [Fact]
    public void SkipsSendersThatAreChatsRatherThanUsers()
    {
        var response = """
            {"members":[
              {"@type":"chatMember","member_id":{"@type":"messageSenderChat","chat_id":-1009}},
              {"@type":"chatMember","member_id":{"@type":"messageSenderUser","user_id":44}}]}
            """;
        Assert.Equal([44L], TdLibGroupMemberParser.ParseMemberUserIds(response));
    }

    [Fact]
    public void ReadsFlatUserIdsFromOlderTdlibBuilds()
    {
        Assert.Equal([42L, 43L], TdLibGroupMemberParser.ParseMemberUserIds("{\"members\":[{\"user_id\":42},{\"user_id\":42},{\"user_id\":43}]}"));
    }

    [Fact]
    public void RejectsChatsWithoutSupergroupInformation()
    {
        Assert.Null(TdLibGroupMemberParser.ParseSupergroupId("{\"type\":{\"@type\":\"chatTypeBasicGroup\",\"basic_group_id\":77}}"));
    }
}
