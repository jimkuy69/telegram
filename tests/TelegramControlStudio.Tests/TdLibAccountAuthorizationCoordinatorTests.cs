using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibAccountAuthorizationCoordinatorTests
{
    [Fact]
    public async Task RoutesAuthRequestsAndUpdatesOnlyThatAccountStatus()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tcs-auth-{Guid.NewGuid():N}.db");
        await new SqliteDatabase(path).InitializeAsync();
        var accounts = new AccountRepository(path);
        var accountId = await accounts.CreateAsync("Auth test", "+10000000002");
        var registry = new TdLibAccountClientRegistry();
        var client = new FakeClient();
        registry.Register(accountId, client);
        var coordinator = new TdLibAccountAuthorizationCoordinator(new TdLibAccountSessionCoordinator(registry), accounts);
        await coordinator.SubmitPhoneNumberAsync(accountId, "+10000000002");
        Assert.Equal("setAuthenticationPhoneNumber", client.LastFunction);
        Assert.Equal("AwaitingCode", (await accounts.ListAsync()).Single().Status);
        await coordinator.SubmitCodeAsync(accountId, "12345");
        Assert.Equal("checkAuthenticationCode", client.LastFunction);
        await coordinator.SubmitPasswordAsync(accountId, "secret");
        Assert.Equal("checkAuthenticationPassword", client.LastFunction);
    }

    private sealed class FakeClient : ITdLibClient
    {
        public string? LastFunction { get; private set; }
        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        { LastFunction = function; return Task.FromResult("{\"@type\":\"ok\"}"); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}