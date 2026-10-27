using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibAccountAuthorizationCoordinatorTests
{
    [Fact]
    public async Task RoutesAuthRequestsWithoutOverwritingTdLibAuthorizationState()
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
        Assert.Equal("PendingAuthorization", (await accounts.ListAsync()).Single().Status);

        await coordinator.SubmitCodeAsync(accountId, "12345");
        Assert.Equal("checkAuthenticationCode", client.LastFunction);
        Assert.Equal("PendingAuthorization", (await accounts.ListAsync()).Single().Status);

        await coordinator.SubmitPasswordAsync(accountId, "secret");
        Assert.Equal("checkAuthenticationPassword", client.LastFunction);
        Assert.Equal("PendingAuthorization", (await accounts.ListAsync()).Single().Status);
    }

    [Fact]
    public async Task TdLibErrorMarksAccountErrorAndDoesNotAdvanceStatus()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tcs-auth-error-{Guid.NewGuid():N}.db");
        await new SqliteDatabase(path).InitializeAsync();
        var accounts = new AccountRepository(path);
        var accountId = await accounts.CreateAsync("Auth error test", "+10000000003");
        var registry = new TdLibAccountClientRegistry();
        registry.Register(accountId, new FakeClient("""{"@type":"error","code":400,"message":"PHONE_NUMBER_INVALID"}"""));
        var coordinator = new TdLibAccountAuthorizationCoordinator(new TdLibAccountSessionCoordinator(registry), accounts);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SubmitPhoneNumberAsync(accountId, "+10000000003"));
        Assert.Equal("Error", (await accounts.ListAsync()).Single().Status);
    }

    private sealed class FakeClient(string response = "{"@type":"ok"}") : ITdLibClient
    {
        public string? LastFunction { get; private set; }
        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        { LastFunction = function; return Task.FromResult(response); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
