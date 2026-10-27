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
        return await SendAsync(accountId, request, cancellationToken);
    }

    public async Task<string> SubmitCodeAsync(long accountId, string code, CancellationToken cancellationToken = default)
    {
        var request = Core.Telegram.TdLibAuthorizationRequests.CheckCode(code);
        return await SendAsync(accountId, request, cancellationToken);
    }

    public async Task<string> SubmitPasswordAsync(long accountId, string password, CancellationToken cancellationToken = default)
    {
        var request = Core.Telegram.TdLibAuthorizationRequests.CheckPassword(password);
        return await SendAsync(accountId, request, cancellationToken);
    }

    public Task<string> SubmitEmailAddressAsync(long accountId, string emailAddress, CancellationToken cancellationToken = default) =>
        SendAsync(accountId, Core.Telegram.TdLibAuthorizationRequests.SetEmailAddress(emailAddress), cancellationToken);

    public Task<string> SubmitEmailCodeAsync(long accountId, string code, CancellationToken cancellationToken = default) =>
        SendAsync(accountId, Core.Telegram.TdLibAuthorizationRequests.CheckEmailCode(code), cancellationToken);

    public Task<string> SubmitRegistrationAsync(long accountId, string firstName, string lastName, CancellationToken cancellationToken = default) =>
        SendAsync(accountId, Core.Telegram.TdLibAuthorizationRequests.RegisterUser(firstName, lastName), cancellationToken);

    private async Task<string> SendAsync(long accountId, Core.Telegram.TdLibRequest request, CancellationToken cancellationToken)
    {
        var response = await sessions.SendAsync(accountId, request.Function, request.Parameters, cancellationToken);
        if (IsTdLibError(response))
        {
            await accounts.UpdateStatusAsync(accountId, "Error", cancellationToken);
            throw new InvalidOperationException("TDLib authorization request failed.");
        }

        // AuthorizationState updates are the source of truth for the account status.
        // Do not write a transient success status here: it can race with
        // authorizationStateReady and overwrite the persisted Ready state.
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
