using TelegramControlStudio.Core.Telegram;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibAuthorizationService(ITdLibClient client)
{
    public Task<string> SubmitPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default) => SendAsync(TdLibAuthorizationRequests.SetPhoneNumber(phoneNumber), cancellationToken);
    public Task<string> SubmitCodeAsync(string code, CancellationToken cancellationToken = default) => SendAsync(TdLibAuthorizationRequests.CheckCode(code), cancellationToken);
    public Task<string> SubmitPasswordAsync(string password, CancellationToken cancellationToken = default) => SendAsync(TdLibAuthorizationRequests.CheckPassword(password), cancellationToken);

    private async Task<string> SendAsync(TdLibRequest request, CancellationToken cancellationToken)
    {
        // Secret values are passed only to TDLib and are never copied into logs or diagnostics.
        return await client.SendAsync(request.Function, request.Parameters, cancellationToken);
    }
}

