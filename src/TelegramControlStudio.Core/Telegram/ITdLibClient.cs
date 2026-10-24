namespace TelegramControlStudio.Core.Telegram;

public interface ITdLibClient : IAsyncDisposable
{
    Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default);
}
