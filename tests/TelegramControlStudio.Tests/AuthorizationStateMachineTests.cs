using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class AuthorizationStateMachineTests
{
    [Theory]
    [InlineData("authorizationStateWaitPhoneNumber", AuthorizationState.WaitPhoneNumber)]
    [InlineData("authorizationStateWaitCode", AuthorizationState.WaitCode)]
    [InlineData("authorizationStateWaitPassword", AuthorizationState.WaitPassword)]
    [InlineData("authorizationStateReady", AuthorizationState.Ready)]
    public void MapsTdlibAuthorizationStates(string tdlibState, AuthorizationState expected)
    {
        var machine = new AuthorizationStateMachine();
        machine.Apply(tdlibState);
        Assert.Equal(expected, machine.Current);
    }

    [Fact]
    public void EmitsStateChangedWithoutExposingCredentials()
    {
        var machine = new AuthorizationStateMachine();
        AuthorizationState? observed = null;
        machine.StateChanged += (_, state) => observed = state;

        machine.Apply("authorizationStateWaitCode");

        Assert.Equal(AuthorizationState.WaitCode, observed);
    }
}

