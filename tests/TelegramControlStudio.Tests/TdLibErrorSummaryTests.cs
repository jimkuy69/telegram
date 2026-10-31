using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibErrorSummaryTests
{
    [Fact]
    public void ReadsCodeAndMessageFromTdLibError()
    {
        var summary = TdLibErrorSummary.Describe("""{"@type":"error","code":403,"message":"USER_PRIVACY_RESTRICTED"}""");

        Assert.Equal("403: USER_PRIVACY_RESTRICTED", summary);
    }

    [Fact]
    public void CollapsesWhitespaceSoTheLogStaysOneLine()
    {
        var summary = TdLibErrorSummary.Describe("""{"@type":"error","code":400,"message":"Bad Request:\n chat   not found"}""");

        Assert.Equal("400: Bad Request: chat not found", summary);
    }

    [Fact]
    public void FallsBackToUnknownWhenMessageIsMissing()
    {
        Assert.Equal("400: UNKNOWN", TdLibErrorSummary.Describe("""{"@type":"error","code":400}"""));
        Assert.Equal("0: UNKNOWN", TdLibErrorSummary.Describe("not json"));
        Assert.Equal("0: UNKNOWN", TdLibErrorSummary.Describe(null));
    }

    [Fact]
    public void TruncatesLongMessages()
    {
        var summary = TdLibErrorSummary.Describe(
            """{"@type":"error","code":400,"message":"AAAAAAAAAAAAAAAAAAAA"}""", maxLength: 10);

        Assert.Equal("400: AAAAAAAAAA", summary);
    }

    [Fact]
    public void SendFailureReasonsKeepTheirDetail()
    {
        var reason = $"{TdLibErrorSummary.SendFailedCode} 403: USER_PRIVACY_RESTRICTED";

        Assert.True(TdLibErrorSummary.IsSendFailure(reason));
        Assert.Equal("403: USER_PRIVACY_RESTRICTED", TdLibErrorSummary.SendFailureDetail(reason));
        Assert.Null(TdLibErrorSummary.SendFailureDetail(TdLibErrorSummary.SendFailedCode));
        Assert.False(TdLibErrorSummary.IsSendFailure("CONSENT_REQUIRED"));
        Assert.Null(TdLibErrorSummary.SendFailureDetail(null));
    }
}
