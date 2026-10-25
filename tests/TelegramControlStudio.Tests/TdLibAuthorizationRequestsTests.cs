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

    [Fact]
    public void BuildsEmailAndRegistrationRequests()
    {
        var email = TdLibAuthorizationRequests.SetEmailAddress("user@example.com");
        var emailCode = TdLibAuthorizationRequests.CheckEmailCode("123456");
        var registration = TdLibAuthorizationRequests.RegisterUser("First", "Last");

        Assert.Equal("setAuthenticationEmailAddress", email.Function);
        Assert.Equal("checkAuthenticationEmailCode", emailCode.Function);
        Assert.Equal("registerUser", registration.Function);
        Assert.Equal("user@example.com", email.Parameters["email_address"]);
        Assert.Equal("First", registration.Parameters["first_name"]);
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
