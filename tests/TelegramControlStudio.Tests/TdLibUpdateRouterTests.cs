using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibUpdateRouterTests
{
    [Fact]
    public void RoutesAuthorizationUpdateToStateMachine()
    {
        var machine = new AuthorizationStateMachine();
        var router = new TdLibUpdateRouter(machine);
        router.Route("""{"@type":"updateAuthorizationState","authorization_state":{"@type":"authorizationStateWaitCode"}}""");

        Assert.Equal(AuthorizationState.WaitCode, machine.Current);
    }

    [Fact]
    public void NotifiesStateConsumerAfterRouting()
    {
        var machine = new AuthorizationStateMachine();
        AuthorizationState? observed = null;
        var router = new TdLibUpdateRouter(machine, state => observed = state);
        router.Route("""{"@type":"updateAuthorizationState","authorization_state":{"@type":"authorizationStateReady"}}""");

        Assert.Equal(AuthorizationState.Ready, observed);
    }

    [Fact]
    public void IgnoresMalformedUpdateWithoutThrowing()
    {
        var router = new TdLibUpdateRouter(new AuthorizationStateMachine());
        router.Route("not-json");
    }
}

