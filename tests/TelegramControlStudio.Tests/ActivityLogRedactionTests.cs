using TelegramControlStudio.Core.Security;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ActivityLogRedactionTests
{
    [Fact]
    public void RedactsSecretsBeforeLogPersistence()
    {
        var message = SecretRedactor.Redact("api_hash=abc otp: 123456 session_string=private");

        Assert.DoesNotContain("abc", message);
        Assert.DoesNotContain("123456", message);
        Assert.DoesNotContain("private", message);
        Assert.Contains("[REDACTED_", message);
    }
}
