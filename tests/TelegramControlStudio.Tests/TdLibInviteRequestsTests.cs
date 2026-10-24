using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibInviteRequestsTests
{
    [Fact]
    public void BuildsJoinRequestInviteWithLimits()
    {
        var request = TdLibInviteRequests.CreateChatInviteLink(-100, DateTimeOffset.UtcNow.AddHours(1), 25, true, true, true);
        Assert.Equal("createChatInviteLink", request.Function); Assert.Equal(-100L, request.Parameters["chat_id"]); Assert.Equal(25, request.Parameters["member_limit"]); Assert.Equal(true, request.Parameters["creates_join_request"]);
    }

    [Fact]
    public void RejectsWithoutPermissionOrConfirmation()
    {
        Assert.Throws<InvalidOperationException>(() => TdLibInviteRequests.CreateChatInviteLink(1, null, 10, true, false, true));
        Assert.Throws<InvalidOperationException>(() => TdLibInviteRequests.CreateChatInviteLink(1, null, 10, true, true, false));
    }
}

