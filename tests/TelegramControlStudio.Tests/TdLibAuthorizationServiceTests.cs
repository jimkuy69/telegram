using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibAuthorizationServiceTests
{
    [Fact]
    public async Task SendsPhoneCodeAndPasswordToExpectedTdlibFunctions()
    {
        var client = new FakeClient(); var service = new TdLibAuthorizationService(client);
        await service.SubmitPhoneNumberAsync("+66123456789"); await service.SubmitCodeAsync("12345"); await service.SubmitPasswordAsync("secret");
        Assert.Equal(["setAuthenticationPhoneNumber", "checkAuthenticationCode", "checkAuthenticationPassword"], client.Functions);
        Assert.Equal("secret", client.Parameters[2]["password"]);
    }

    private sealed class FakeClient : ITdLibClient
    {
        public List<string> Functions { get; } = []; public List<IReadOnlyDictionary<string, object?>> Parameters { get; } = [];
        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default) { Functions.Add(function); Parameters.Add(parameters); return Task.FromResult("{\"@type\":\"ok\"}"); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

