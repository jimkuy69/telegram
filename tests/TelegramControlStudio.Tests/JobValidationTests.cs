using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class JobValidationTests
{
    [Fact]
    public void RequiresActiveAccount()
    {
        var result = JobValidation.Validate(null, []);
        Assert.False(result.IsValid);
        Assert.Equal("ACTIVE_ACCOUNT_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public void RejectsOptedOutRecipients()
    {
        var account = new Account(7, "Demo", "+66000000000", "Ready", DateTimeOffset.UtcNow, null);
        var recipients = new[]
        {
            new Contact(7, 10, "A", "", "a", null, true, true, DateTimeOffset.UtcNow)
        };
        var result = JobValidation.Validate(account, recipients);
        Assert.False(result.IsValid);
        Assert.Equal("CONSENT_REQUIRED", result.ErrorCode);
    }
}
