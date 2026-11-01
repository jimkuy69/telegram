using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibGroupMemberSyncServiceTests
{
    [Fact]
    public async Task StoresMembersIdentifiedThroughMessageSender()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tcs-members-{Guid.NewGuid():N}.db");
        await new SqliteDatabase(path).InitializeAsync();
        var accountId = await new AccountRepository(path).CreateAsync("Member test", "+10000000002");
        await new TelegramGroupRepository(path).UpsertAsync(new TelegramGroup(accountId, -10055, "์NEW GAME", "Group", null, false, false, false, DateTimeOffset.UtcNow, "seeded by the groups sync"));
        var client = new RoutingClient(new()
        {
            ["getChat"] = """{"@type":"chat","id":-10055,"title":"์NEW GAME","type":{"@type":"chatTypeSupergroup","supergroup_id":55,"is_channel":false}}""",
            ["getSupergroupMembers"] = """
                {"@type":"chatMemberList","found_count":2,"members":[
                  {"@type":"chatMember","member_id":{"@type":"messageSenderUser","user_id":901}},
                  {"@type":"chatMember","member_id":{"@type":"messageSenderUser","user_id":902}}]}
                """,
            ["getUser"] = """{"@type":"user","id":0,"first_name":"สมาชิก","usernames":{"editable_username":"member901"},"type":{"@type":"userTypeGeneral"}}"""
        });

        var count = await new TdLibGroupMemberSyncService(client, path).SyncAsync(accountId, -10055);

        Assert.Equal(2, count);
        var stored = await new GroupMemberRepository(path).ListAsync(accountId, -10055);
        Assert.Equal([901L, 902L], stored.Select(member => member.TelegramUserId).ToArray());
    }

    [Fact]
    public async Task SaysWhyTelegramRefusedTheMemberList()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tcs-members-denied-{Guid.NewGuid():N}.db");
        await new SqliteDatabase(path).InitializeAsync();
        var accountId = await new AccountRepository(path).CreateAsync("Member test", "+10000000003");
        var client = new RoutingClient(new()
        {
            ["getChat"] = """{"@type":"chat","id":-10056,"title":"Channel","type":{"@type":"chatTypeSupergroup","supergroup_id":56,"is_channel":true}}""",
            ["getSupergroupMembers"] = """{"@type":"error","code":403,"message":"CHAT_ADMIN_REQUIRED"}"""
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TdLibGroupMemberSyncService(client, path).SyncAsync(accountId, -10056));

        Assert.Contains("403: CHAT_ADMIN_REQUIRED", exception.Message);
        Assert.Empty(await new GroupMemberRepository(path).ListAsync(accountId, -10056));
    }

    [Fact]
    public async Task SkipsMembersTelegramWillNotDescribeYet()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tcs-members-partial-{Guid.NewGuid():N}.db");
        await new SqliteDatabase(path).InitializeAsync();
        var accountId = await new AccountRepository(path).CreateAsync("Member test", "+10000000004");
        var client = new RoutingClient(new()
        {
            ["getChat"] = """{"@type":"chat","id":-10057,"title":"Group","type":{"@type":"chatTypeSupergroup","supergroup_id":57}}""",
            ["getSupergroupMembers"] = """
                {"@type":"chatMemberList","members":[
                  {"@type":"chatMember","member_id":{"@type":"messageSenderUser","user_id":903}},
                  {"@type":"chatMember","member_id":{"@type":"messageSenderUser","user_id":904}}]}
                """,
            ["getUser"] = """{"@type":"error","code":400,"message":"USER_ID_INVALID"}"""
        });

        Assert.Equal(0, await new TdLibGroupMemberSyncService(client, path).SyncAsync(accountId, -10057));
    }

    private sealed class RoutingClient(Dictionary<string, string> responses) : ITdLibClient
    {
        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            if (!responses.TryGetValue(function, out var response))
                throw new InvalidOperationException($"Unexpected TDLib call {function}.");
            if (function == "getUser" && parameters["user_id"] is long userId)
                response = response.Replace("\"id\":0", $"\"id\":{userId}", StringComparison.Ordinal);
            return Task.FromResult(response);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
