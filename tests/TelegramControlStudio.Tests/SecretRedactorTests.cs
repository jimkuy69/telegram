using Xunit;
using TelegramControlStudio.Core.Security;

namespace TelegramControlStudio.Tests;

public sealed class SecretRedactorTests
{
    [Fact]
    public void RedactsKnownSecretPatterns()
    {
        var result = SecretRedactor.Redact("api_hash=abc123 api_key=key123 otp: 123456 session_string=secret 2fa_password=hunter2 dpapi_key=dpapi-secret");

        Assert.DoesNotContain("abc123", result);
        Assert.DoesNotContain("123456", result);
        Assert.DoesNotContain("secret", result);
        Assert.DoesNotContain("key123", result);
        Assert.DoesNotContain("hunter2", result);
        Assert.DoesNotContain("dpapi-secret", result);
    }
}

