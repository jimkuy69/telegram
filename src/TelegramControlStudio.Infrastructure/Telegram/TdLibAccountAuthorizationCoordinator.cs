using TelegramControlStudio.Infrastructure.Persistence;
using System.Text.Json;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibAccountAuthorizationCoordinator(
    TdLibAccountSessionCoordinator sessions,
    AccountRepository accounts)
{
    public async Task<string> SubmitPhoneNumberAsync(long accountId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var request = Core.Telegram.TdLibAuthorizationRequests.SetPhoneNumber(phoneNumber);
        return await SendAndUpdateAsync(accountId, request, "AwaitingCode", cancellationToken);
    }

    public async Task<string> SubmitCodeAsync(long accountId, string code, CancellationToken cancellationToken = default)
    {
        var request = Core.Telegram.TdLibAuthorizationRequests.CheckCode(code);
        return await SendAndUpdateAsync(accountId, request, "AwaitingPasswordOrReady", cancellationToken);
    }

    public async Task<string> SubmitPasswordAsync(long accountId, string password, CancellationToken cancellationToken = default)
    {
        var request = Core.Telegram.TdLibAuthorizationRequests.CheckPassword(password);
        return await SendAndUpdateAsync(accountId, request, "AuthenticatedPendingStateUpdate", cancellationToken);
    }

    private async Task<string> SendAndUpdateAsync(long accountId, Core.Telegram.TdLibRequest request, string successStatus, CancellationToken cancellationToken)
    {
        var response = await sessions.SendAsync(accountId, request.Function, request.Parameters, cancellationToken);
        if (IsTdLibError(response))
        {
            await accounts.UpdateStatusAsync(accountId, "Error", cancellationToken);
            throw new InvalidOperationException("TDLib authorization request failed.");
        }
        await accounts.UpdateStatusAsync(accountId, successStatus, cancellationToken);
        return response;
    }

    private static bool IsTdLibError(string response)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            return document.RootElement.TryGetProperty("@type", out var type) && type.GetString() == "error";
        }
        catch (JsonException) { return false; }
    }
}