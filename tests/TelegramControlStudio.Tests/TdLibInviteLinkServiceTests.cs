using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibInviteLinkServiceTests
{
    [Fact]
    public async Task SendsValidatedRequestAndPersistsReturnedLink()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tcs-invite-{Guid.NewGuid():N}.db");
        await new SqliteDatabase(path).InitializeAsync();
        var accountId = await new AccountRepository(path).CreateAsync("Invite test", "+10000000000");
        var client = new FakeClient("""{"invite_link":{"invite_link":"https://t.me/+abc","chat_id":-100,"member_limit":5,"creates_join_request":true}}""");
        var link = await new TdLibInviteLinkService(path).CreateAndPersistAsync(client, accountId, -100, DateTimeOffset.UtcNow.AddHours(1), 5, true, true, true);
        Assert.Equal("https://t.me/+abc", link.InviteUrl);
        Assert.Equal("createChatInviteLink", client.Function);
        Assert.Single(await new InviteLinkRepository(path).ListAsync(accountId));
    }

    [Fact]
    public async Task RevokeRequiresTdLibOkAndMarksLinkRevoked()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tcs-revoke-{Guid.NewGuid():N}.db");
        await new SqliteDatabase(path).InitializeAsync();
        var accountId = await new AccountRepository(path).CreateAsync("Revoke test", "+10000000001");
        var url = "https://t.me/+revoke";
        await new InviteLinkRepository(path).CreateAsync(new TelegramControlStudio.Core.Domain.InviteLink(0, accountId, -100, url, null, 5, true, DateTimeOffset.UtcNow, null));
        var client = new FakeClient("""{"@type":"ok"}""");
        await new TdLibInviteLinkService(path).RevokeAsync(client, accountId, -100, url, true, true);
        var saved = Assert.Single(await new InviteLinkRepository(path).ListAsync(accountId));
        Assert.NotNull(saved.RevokedAt);
        Assert.Equal("revokeChatInviteLink", client.Function);
        var logs = await new ActivityLogRepository(path).ListAsync(accountId);
        Assert.Contains(logs, item => item.EventType == "InviteLinkRevoked");
    }

    private sealed class FakeClient(string response) : ITdLibClient
    {
        public string? Function { get; private set; }
        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        { Function = function; return Task.FromResult(response); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}