using TelegramControlStudio.Core.Operations;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class JobFailureClassifierTests
{
    [Theory]
    [InlineData("429: FLOOD_WAIT_30", JobFailureKind.FloodWait)]
    [InlineData("network timeout", JobFailureKind.RetryableNetwork)]
    [InlineData("CHAT_ADMIN_REQUIRED", JobFailureKind.PermissionDenied)]
    [InlineData("PHONE_CODE_EXPIRED", JobFailureKind.SessionExpired)]
    [InlineData("CONSENT_REQUIRED", JobFailureKind.ConsentDenied)]
    [InlineData("USER_NOT_FOUND", JobFailureKind.Permanent)]
    public void MapsKnownErrorsToSafeFailureKinds(string error, JobFailureKind expected) =>
        Assert.Equal(expected, JobFailureClassifier.Classify(error));
}

