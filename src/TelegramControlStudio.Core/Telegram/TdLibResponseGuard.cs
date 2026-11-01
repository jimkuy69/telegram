using System.Text.Json;

namespace TelegramControlStudio.Core.Telegram;

/// <summary>
/// A failed TDLib request comes back as an <c>error</c> object carrying the same <c>@extra</c> as a
/// successful result, so it reaches the caller through the normal reply path. Services that only look
/// for their expected fields would report "no valid response" and hide the real Telegram reason
/// (400 CHAT_ADMIN_REQUIRED, 400 CHAT_NOT_FOUND, …) from the operator.
/// </summary>
public static class TdLibResponseGuard
{
    public static bool TryGetError(string response, out string summary)
    {
        summary = string.Empty;
        if (string.IsNullOrWhiteSpace(response)) return false;
        try
        {
            using var document = JsonDocument.Parse(response);
            if (!document.RootElement.TryGetProperty("@type", out var type) ||
                !string.Equals(type.GetString(), "error", StringComparison.Ordinal))
                return false;
            summary = TdLibErrorSummary.Describe(response);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static void EnsureSuccess(string response, string operation)
    {
        if (TryGetError(response, out var summary))
            throw new InvalidOperationException($"TDLib {operation} rejected by Telegram: {summary}");
    }
}
