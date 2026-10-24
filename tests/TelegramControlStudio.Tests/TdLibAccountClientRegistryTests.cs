using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibAccountClientRegistryTests
{
    [Fact]
    public async Task KeepsClientsIsolatedByAccountAndDisposesOnRemoval()
    {
        var registry = new TdLibAccountClientRegistry();
        var first = new FakeClient();
        var second = new FakeClient();
        Assert.True(registry.Register(1, first));
        Assert.True(registry.Register(2, second));
        Assert.False(registry.Register(1, new FakeClient()));
        Assert.True(registry.TryGet(2, out var resolved));
        Assert.Same(second, resolved);
        Assert.True(await registry.RemoveAsync(1));
        Assert.True(first.Disposed);
        Assert.False(registry.TryGet(1, out _));
        await registry.DisposeAllAsync();
        Assert.True(second.Disposed);
    }

    private sealed class FakeClient : ITdLibClient
    {
        public bool Disposed { get; private set; }
        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default) => Task.FromResult("{}");
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}