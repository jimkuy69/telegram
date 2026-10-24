using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibAccountAuthorizationCoordinator(
    TdLibAccountSessionCoordinator sessions,
    AccountRepository accounts)
{
    public async Task<string> SubmitPhoneNumberAsync(long accountId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var request = Core.Telegram.TdLibAuthorizationRequests.SetPhoneNumber(phoneNumber);
        var response = await sessions.SendAsync(accountId, request.Function, request.Parameters, cancellationToken);
        await accounts.UpdateStatusAsync(accountId, "AwaitingCode", cancellationToken);
        return response;
    }

    public async Task<string> SubmitCodeAsync(long accountId, string code, CancellationToken cancellationToken = default)
    {
        var request = Core.Telegram.TdLibAuthorizationRequests.CheckCode(code);
        var response = await sessions.SendAsync(accountId, request.Function, request.Parameters, cancellationToken);
        await accounts.UpdateStatusAsync(accountId, "AwaitingPasswordOrReady", cancellationToken);
        return response;
    }

    public async Task<string> SubmitPasswordAsync(long accountId, string password, CancellationToken cancellationToken = default)
    {
        var request = Core.Telegram.TdLibAuthorizationRequests.CheckPassword(password);
        var response = await sessions.SendAsync(accountId, request.Function, request.Parameters, cancellationToken);
        await accounts.UpdateStatusAsync(accountId, "AuthenticatedPendingStateUpdate", cancellationToken);
        return response;
    }
}