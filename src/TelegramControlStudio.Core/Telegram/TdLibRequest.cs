namespace TelegramControlStudio.Core.Telegram;

public sealed record TdLibRequest(
    string Function,
    IReadOnlyDictionary<string, object?> Parameters,
    string Extra);
