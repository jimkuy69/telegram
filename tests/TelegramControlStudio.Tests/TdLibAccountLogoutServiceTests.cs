using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibAccountLogoutServiceTests
{
    [Fact]
    public async Task LogsOutOnlyTheRequestedAccount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tcs-logout-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accounts = new AccountRepository(path);
            var first = await accounts.CreateAsync("First", "+66000000041");
            var second = await accounts.CreateAsync("Second", "+66000000042");
            var registry = new TdLibAccountClientRegistry();
            var firstClient = new FakeClient();
            var secondClient = new FakeClient();
            registry.Register(first, firstClient);
            registry.Register(second, secondClient);

            await new TdLibAccountLogoutService(registry, accounts).LogoutAsync(first);

            Assert.Equal("logOut", firstClient.LastFunction);
            Assert.True(firstClient.Disposed);
            Assert.False(registry.IsRegistered(first));
            Assert.True(registry.IsRegistered(second));
            Assert.Equal("Offline", (await accounts.ListAsync()).Single(x => x.Id == first).Status);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class FakeClient : ITdLibClient
    {
        public string? LastFunction { get; private set; }
        public bool Disposed { get; private set; }
        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default) { LastFunction = function; return Task.FromResult("{\"@type\":\"ok\"}"); }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}

