using TelegramControlStudio.Core.Operations;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class InviteLinkPolicyTests
{
    [Fact]
    public void RequiresPermissionAndConfirmation()
    {
        Assert.Equal("CONFIRMATION_REQUIRED", InviteLinkPolicy.Validate(10, DateTimeOffset.UtcNow.AddHours(1), true, false, true));
        Assert.Equal("INVITE_PERMISSION_REQUIRED", InviteLinkPolicy.Validate(10, DateTimeOffset.UtcNow.AddHours(1), true, true, false));
    }

    [Fact]
    public void RejectsInvalidLimitsAndExpiredLinks()
    {
        Assert.Equal("USAGE_LIMIT_INVALID", InviteLinkPolicy.Validate(0, null, true, true, true));
        Assert.Equal("EXPIRATION_MUST_BE_FUTURE", InviteLinkPolicy.Validate(null, DateTimeOffset.UtcNow.AddMinutes(-1), true, true, true));
    }

    [Fact]
    public void RequiresJoinRequestAsSafeDefault()
    {
        Assert.Equal("JOIN_REQUEST_RECOMMENDED", InviteLinkPolicy.Validate(10, DateTimeOffset.UtcNow.AddHours(1), false, true, true));
        Assert.Null(InviteLinkPolicy.Validate(10, DateTimeOffset.UtcNow.AddHours(1), true, true, true));
    }
}

