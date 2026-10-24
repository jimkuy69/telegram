using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibAuthorizationRequestsTests
{
    [Fact]
    public void BuildsPhoneRequestWithoutPuttingSecretInExtra()
    {
        var request = TdLibAuthorizationRequests.SetPhoneNumber("+66123456789");
        Assert.Equal("setAuthenticationPhoneNumber", request.Function); Assert.Equal("auth-phone", request.Extra); Assert.Equal("+66123456789", request.Parameters["phone_number"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsEmptyAuthenticationSecrets(string value)
    {
        Assert.Throws<ArgumentException>(() => TdLibAuthorizationRequests.CheckCode(value));
        Assert.Throws<ArgumentException>(() => TdLibAuthorizationRequests.CheckPassword(value));
    }
}

