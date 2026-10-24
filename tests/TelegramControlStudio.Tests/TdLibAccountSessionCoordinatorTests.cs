using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibAccountSessionCoordinatorTests
{
    [Fact]
    public async Task RoutesRequestsOnlyToAttachedAccountClient()
    {
        var registry = new TdLibAccountClientRegistry();
        var coordinator = new TdLibAccountSessionCoordinator(registry);
        var client = new FakeClient("ok");
        coordinator.Attach(7, client);
        Assert.True(coordinator.IsConnected(7));
        Assert.Equal("ok", await coordinator.SendAsync(7, "getMe", new Dictionary<string, object?>()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SendAsync(8, "getMe", new Dictionary<string, object?>()));
        Assert.True(await coordinator.DetachAsync(7));
        Assert.False(coordinator.IsConnected(7));
        Assert.True(client.Disposed);
    }

    private sealed class FakeClient(string response) : ITdLibClient
    {
        public bool Disposed { get; private set; }
        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default) => Task.FromResult(response);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}