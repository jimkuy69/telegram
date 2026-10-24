using TelegramControlStudio.Core.Operations;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class JoinRequestDecisionPolicyTests
{
    [Fact]
    public void RequiresExplicitConfirmation() =>
        Assert.Equal("CONFIRMATION_REQUIRED", JoinRequestDecisionPolicy.Validate("Approved", "ตรวจสอบแล้ว", false));

    [Fact]
    public void RequiresReason() =>
        Assert.Equal("REASON_REQUIRED", JoinRequestDecisionPolicy.Validate("Rejected", "", true));

    [Fact]
    public void AcceptsConfirmedDecisionWithReason() =>
        Assert.Empty(JoinRequestDecisionPolicy.Validate("Approved", "ได้รับอนุญาต", true));
}

