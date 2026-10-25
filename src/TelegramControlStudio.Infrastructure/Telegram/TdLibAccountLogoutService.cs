using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibAccountLogoutService(TdLibAccountClientRegistry registry, AccountRepository accounts)
{
    public async Task LogoutAsync(long accountId, CancellationToken cancellationToken = default)
    {
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        if (!registry.TryGet(accountId, out var client) || client is null)
            throw new InvalidOperationException($"Account {accountId} has no attached TDLib session.");

        var response = await client.SendAsync("logOut", new Dictionary<string, object?>(), cancellationToken);
        if (response.Contains("\"@type\":\"error\"", StringComparison.Ordinal))
            throw new InvalidOperationException("TDLib logout request failed.");

        await accounts.UpdateStatusAsync(accountId, "LoggingOut", cancellationToken);
        await registry.RemoveAsync(accountId);
        await accounts.UpdateStatusAsync(accountId, "Offline", cancellationToken);
    }
}

