using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibMessageRequestsTests
{
    [Fact]
    public void BuildsTextMessageOnlyForConfirmedConsentedRecipient()
    {
        var request = TdLibMessageRequests.SendText(123, "ยินดีต้อนรับ", true, false, true);
        Assert.Equal("sendMessage", request.Function);
        Assert.Equal(123L, request.Parameters["chat_id"]);
        var content = Assert.IsType<Dictionary<string, object?>>(request.Parameters["input_message_content"]);
        Assert.Equal("inputMessageText", content["@type"]);
    }

    [Theory]
    [InlineData(false, false, true, "CONSENT_REQUIRED")]
    [InlineData(true, true, true, "OPT_OUT_RECIPIENT")]
    [InlineData(true, false, false, "CONFIRMATION_REQUIRED")]
    public void RejectsUnsafeMessageSend(bool consent, bool optOut, bool confirmed, string error)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => TdLibMessageRequests.SendText(123, "hello", consent, optOut, confirmed));
        Assert.Equal(error, exception.Message);
    }
}

