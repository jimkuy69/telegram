using System.IO;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibClientLifecycleTests
{
    [Fact]
    public async Task RoutesUpdateAndStopsReceiveLoop()
    {
        var observed = new TaskCompletionSource<AuthorizationState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var machine = new AuthorizationStateMachine();
        var router = new TdLibUpdateRouter(machine, state => observed.TrySetResult(state));
        await using var jsonClient = new TdLibJsonClient(new StringWriter());
        var first = true;
        async Task<string?> Receive(CancellationToken token)
        {
            if (first)
            {
                first = false;
                return """{"@type":"updateAuthorizationState","authorization_state":{"@type":"authorizationStateReady"}}""";
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        }

        var lifecycle = new TdLibClientLifecycle(jsonClient, router, Receive);
        lifecycle.Start();
        Assert.Equal(AuthorizationState.Ready, await observed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        await lifecycle.StopAsync();
        Assert.False(lifecycle.IsRunning);
    }
}

