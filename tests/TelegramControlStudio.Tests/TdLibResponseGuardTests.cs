using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibResponseGuardTests
{
    [Fact]
    public void ThrowsWithTheTelegramReasonForAnErrorResponse()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            TdLibResponseGuard.EnsureSuccess("""{"@type":"error","code":403,"message":"CHAT_ADMIN_REQUIRED","@extra":"12"}""", "getSupergroupMembers"));
        Assert.Equal("TDLib getSupergroupMembers rejected by Telegram: 403: CHAT_ADMIN_REQUIRED", exception.Message);
    }

    [Theory]
    [InlineData("""{"@type":"chatMemberList","members":[]}""")]
    [InlineData("""{"@type":"ok"}""")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void LeavesSuccessfulAndUnparseableResponsesAlone(string response) =>
        TdLibResponseGuard.EnsureSuccess(response, "getSupergroupMembers");

    [Fact]
    public void SummarisesErrorsForCallersThatSkipARequest()
    {
        Assert.True(TdLibResponseGuard.TryGetError("""{"@type":"error","code":400,"message":"USER_ID_INVALID"}""", out var summary));
        Assert.Equal("400: USER_ID_INVALID", summary);
        Assert.False(TdLibResponseGuard.TryGetError("""{"@type":"user","id":5}""", out _));
    }
}
