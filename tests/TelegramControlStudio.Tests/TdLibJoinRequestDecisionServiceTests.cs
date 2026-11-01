using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibJoinRequestDecisionServiceTests
{
    [Fact]
    public async Task ApprovesWhenTelegramAnswersWithTheInviteLink()
    {
        var (path, accountId, chatId, userId) = await PendingAsync("+10000000005", -10050, 777);
        var client = new FixedClient("""{"@type":"chatInviteLink","invite_link":"https://t.me/+abc","pending_join_request_count":0}""");

        await new TdLibJoinRequestDecisionService(path).DecideAsync(
            client, new JoinRequest(0, accountId, chatId, userId, "Pending", DateTimeOffset.UtcNow, null, null), "Approved", "ตรวจสอบแล้วว่าเหมาะสม", true, true);

        Assert.Empty(await new JoinRequestRepository(path).ListPendingAsync(accountId));
    }

    [Fact]
    public async Task KeepsTheRequestPendingAndQuotesTelegramWhenItRefuses()
    {
        var (path, accountId, chatId, userId) = await PendingAsync("+10000000006", -10051, 778);
        var client = new FixedClient("""{"@type":"error","code":400,"message":"USER_NOT_PENDING"}""");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TdLibJoinRequestDecisionService(path).DecideAsync(
                client, new JoinRequest(0, accountId, chatId, userId, "Pending", DateTimeOffset.UtcNow, null, null), "Rejected", "ไม่อนุญาต", true, true));

        Assert.Contains("processChatJoinRequest", exception.Message);
        Assert.Contains("400: USER_NOT_PENDING", exception.Message);
        Assert.Single(await new JoinRequestRepository(path).ListPendingAsync(accountId));
    }

    private static async Task<(string Path, long AccountId, long ChatId, long UserId)> PendingAsync(string phone, long chatId, long userId)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tcs-join-{Guid.NewGuid():N}.db");
        await new SqliteDatabase(path).InitializeAsync();
        var accountId = await new AccountRepository(path).CreateAsync("Join test", phone);
        await new JoinRequestRepository(path).UpsertAsync(new JoinRequest(0, accountId, chatId, userId, "Pending", DateTimeOffset.UtcNow, null, null));
        return (path, accountId, chatId, userId);
    }

    private sealed class FixedClient(string response) : ITdLibClient
    {
        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default) => Task.FromResult(response);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
