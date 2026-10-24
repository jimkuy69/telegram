using Xunit;
using TelegramControlStudio.Core.Security;

namespace TelegramControlStudio.Tests;

public sealed class SecretRedactorTests
{
    [Fact]
    public void RedactsKnownSecretPatterns()
    {
        var result = SecretRedactor.Redact("api_hash=abc123 otp: 123456 session_string=secret");

        Assert.DoesNotContain("abc123", result);
        Assert.DoesNotContain("123456", result);
        Assert.DoesNotContain("secret", result);
    }
}
