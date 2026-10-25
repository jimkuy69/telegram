using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class AuthorizationWizardStateTests
{
    [Fact]
    public void ExposesOnlyTheInputAllowedByCurrentState()
    {
        var machine = new AuthorizationStateMachine();
        machine.Apply("authorizationStateWaitPhoneNumber");
        Assert.True(machine.CanSubmitPhoneNumber);
        Assert.False(machine.CanSubmitCode);
        Assert.False(machine.CanSubmitPassword);
        Assert.Equal("กรอกเบอร์โทรศัพท์", machine.UserFacingStep);

        machine.Apply("authorizationStateWaitCode");
        Assert.False(machine.CanSubmitPhoneNumber);
        Assert.True(machine.CanSubmitCode);
        Assert.Equal("กรอกรหัสยืนยันจาก Telegram", machine.UserFacingStep);

        machine.Apply("authorizationStateWaitPassword");
        Assert.True(machine.CanSubmitPassword);
    }

    [Fact]
    public void ReadyStateIsExplicitAndDoesNotAcceptCredentialInput()
    {
        var machine = new AuthorizationStateMachine();
        machine.Apply("authorizationStateReady");
        Assert.True(machine.IsReady);
        Assert.False(machine.CanSubmitPhoneNumber);
        Assert.False(machine.CanSubmitCode);
        Assert.False(machine.CanSubmitPassword);
        Assert.Equal("เชื่อมต่อแล้ว", machine.UserFacingStep);
    }
}

